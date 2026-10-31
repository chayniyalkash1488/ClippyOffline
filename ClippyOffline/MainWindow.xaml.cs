using System.ComponentModel;
using System.Diagnostics;
using System.IO;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace ClippyOffline;

public partial class MainWindow : Window
{
    private const int FrameWidth = 124, FrameHeight = 93;
    // Серия кликов: 1-й обычный, 2-й «двойной», 3-й и 4-й обычные, 5-й злость. Без кликов дольше ChainGap счётчик сбрасывается.
    private static readonly TimeSpan ChainGap = TimeSpan.FromMinutes(1);
    private const int RageClicks = 5;
    private readonly Random _random = new();
    private readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromMilliseconds(1200) };
    private readonly DispatcherTimer _frameTimer = new();
    private readonly DispatcherTimer _bubbleTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<string, MediaPlayer> _soundCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, AnimationData> _animations = new(StringComparer.OrdinalIgnoreCase);
    private BitmapImage? _currentStrip;
    private AnimationData? _currentAnimation;
    private int _frameIndex;
    private IntPtr _lastForegroundWindow;
    private string _lastProcessName = "";
    private DateTime _nextRandomQuip = DateTime.Now.AddSeconds(100);
    private DateTime _lastSpoke = DateTime.MinValue;
    private DateTime _lastClickAt = DateTime.MinValue;
    private int _clickChain;
    private bool _hiddenForFullscreen;
    private bool _userHidden;
    private bool _bubblePinned;
    private bool _sleeping;
    private bool _manualSleep;
    private bool _loopAnimation;
    private bool _sleepLoopActive;
    private const int GoodbyeLastFrame = 33;
    // IDLESNOOZE (88 кадров): 0-12 стоит, 13-26 ложится, 27-80 лежит/спит, 81-87 просыпается и встаёт.
    private const int SleepLoopStart = 27, SleepLoopEnd = 80, SleepWakeFrame = 81;
    private bool _settingsLoaded;
    private bool _pendingHide;
    private bool _pendingExit;
    private bool _allowClose;
    private bool _mousePressed;
    private bool _dragging;
    private NativeMethods.POINT _pressCursor;
    private double _pressLeft, _pressTop;
    private AppSettings _settings = new();
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ToolStripMenuItem? _soundMenuItem;
    private Forms.ToolStripMenuItem? _startupMenuItem;
    private Forms.ToolStripMenuItem? _sizeMenuItem;
    private Forms.ToolStripMenuItem? _sleepMenuItem;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += Window_Loaded;
        Closing += Window_Closing;
        _watchTimer.Tick += WatchTimer_Tick;
        _frameTimer.Tick += FrameTimer_Tick;
        _bubbleTimer.Tick += (_, _) => { _bubbleTimer.Stop(); if (!_bubblePinned) SpeechBubble.Visibility = Visibility.Collapsed; };
        _idleTimer.Tick += IdleTimer_Tick;
        _watchTimer.Start(); _idleTimer.Start();
        CreateTrayIcon();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LoadSettings();
        ApplySizes(save: false);
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 24; Top = work.Bottom - Height - 20;
        try
        {
            var manifestPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Animations", "manifest.json");
            await using var stream = File.OpenRead(manifestPath);
            var manifest = await JsonSerializer.DeserializeAsync<AnimationManifest>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest != null)
                foreach (var a in manifest.Animations) _animations[a.Name.Replace('-', '_')] = a;
        }
        catch
        {
            Say("Не нашёл локальные анимации. Проверь папку Assets.", "startup", pin: true);
            return;
        }
        Play("GREETING");
        Say(Choose(Quips.Active("startup")), "startup");
    }

    private void LoadSettings()
    {
        try
        {
            var path = GetSettingsPath();
            if (File.Exists(path)) _settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch { _settings = new AppSettings(); }
        _settings.CharacterSizePercent = Math.Clamp(_settings.CharacterSizePercent, 60, 200);
        _settings.BubbleSizePercent = Math.Clamp(_settings.BubbleSizePercent, 60, 200);
        _settings.SoundFiles ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _settings.SoundVolumes ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        _settings.Phrases ??= new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        Quips.Custom = _settings.Phrases;
        _settingsLoaded = true;
        ApplyAppearance();
        UpdateTrayMenuChecks();
        TrimMemoryWhenIdle();
    }

    private static string GetSettingsPath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClippyOffline-by-Chayniy_Alkash");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    private void SaveSettings()
    {
        try { File.WriteAllText(GetSettingsPath(), JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true })); }
        catch { /* Settings failure should not crash the character. */ }
    }

    private void CreateTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "clippy.ico");
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "ClippyOffline",
            Icon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath, Forms.SystemInformation.SmallIconSize) : System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _trayIcon.DoubleClick += (_, _) => ToggleVisibility();
    }

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Показать / спрятать", null, (_, _) => ToggleVisibility());
        menu.Items.Add("Сказать что-нибудь", null, (_, _) => { if (!_manualSleep) { Say(Choose(Quips.Active("click")), "click"); Play("WAVE"); } });
        menu.Items.Add("Настройки…", null, (_, _) => OpenSettings());
        _sleepMenuItem = new Forms.ToolStripMenuItem("Режим сна") { CheckOnClick = true };
        _sleepMenuItem.Click += (_, _) => ToggleManualSleep(); menu.Items.Add(_sleepMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        _sizeMenuItem = new Forms.ToolStripMenuItem("Размер скрепки");
        foreach (int size in new[] { 75, 90, 100, 125, 150, 175, 200 })
        {
            var item = new Forms.ToolStripMenuItem($"{size}%") { Tag = size };
            item.Click += (_, _) => { _settings.CharacterSizePercent = (int)item.Tag!; ApplySizes(true); };
            _sizeMenuItem.DropDownItems.Add(item);
        }
        menu.Items.Add(_sizeMenuItem);
        _soundMenuItem = new Forms.ToolStripMenuItem("Звуки включены") { CheckOnClick = true };
        _soundMenuItem.Click += (_, _) => { _settings.SoundsEnabled = _soundMenuItem.Checked; SaveSettings(); };
        menu.Items.Add(_soundMenuItem);
        _startupMenuItem = new Forms.ToolStripMenuItem("Запускать вместе с Windows") { CheckOnClick = true };
        _startupMenuItem.Click += (_, _) =>
        {
            bool wanted = _startupMenuItem.Checked;
            if (SetStartup(wanted)) { _settings.StartWithWindows = wanted; SaveSettings(); }
            else { _startupMenuItem.Checked = IsStartupEnabled(); System.Windows.MessageBox.Show("Не удалось изменить автозапуск. Попробуй опубликованную .exe-версию.", "ClippyOffline", MessageBoxButton.OK, MessageBoxImage.Information); }
        };
        menu.Items.Add(_startupMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => BeginExit());
        menu.Renderer = MakeTrayRenderer();
        menu.Opening += (_, _) => { UpdateTrayMenuChecks(); menu.Renderer = MakeTrayRenderer(); menu.Font = _settings.RetroXp ? new System.Drawing.Font("Tahoma", 8.25f) : Forms.Control.DefaultFont; };
        return menu;
    }

    private Forms.ToolStripRenderer MakeTrayRenderer()
    {
        if (_settings.RetroXp) return new XpTrayRenderer();
        if (_settings.DarkMenus) return new DarkTrayRenderer();
        return new Forms.ToolStripProfessionalRenderer();
    }

    private void ApplyAppearance()
    {
        bool xp = _settings.RetroXp;
        SpeechBubble.Background = new System.Windows.Media.SolidColorBrush(xp ? System.Windows.Media.Color.FromRgb(255, 255, 225) : System.Windows.Media.Color.FromRgb(255, 253, 248));
        SpeechBubble.BorderBrush = new System.Windows.Media.SolidColorBrush(xp ? System.Windows.Media.Color.FromRgb(0, 0, 0) : System.Windows.Media.Color.FromRgb(85, 85, 85));
        SpeechBubble.CornerRadius = new CornerRadius(xp ? 3 : 12);
        SpeechBubble.BorderThickness = new Thickness(xp ? 1 : 1.5);
        SpeechText.FontFamily = new System.Windows.Media.FontFamily(xp ? "Tahoma" : "Segoe UI");
    }

    private void UpdateTrayMenuChecks()
    {
        if (_soundMenuItem != null) _soundMenuItem.Checked = _settings.SoundsEnabled;
        if (_startupMenuItem != null) _startupMenuItem.Checked = IsStartupEnabled();
        if (_sleepMenuItem != null) _sleepMenuItem.Checked = _manualSleep;
        if (_sizeMenuItem != null)
            foreach (Forms.ToolStripMenuItem item in _sizeMenuItem.DropDownItems)
                item.Checked = (int)item.Tag! == _settings.CharacterSizePercent;
    }

    private static bool IsStartupEnabled()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false); return key?.GetValue("ClippyOffline_by_Chayniy_Alkash") is string; }
        catch { return false; }
    }

    private static bool SetStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)
                ?? Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (key == null) return false;
            if (!enabled) { key.DeleteValue("ClippyOffline_by_Chayniy_Alkash", false); return true; }
            string? processPath = Environment.ProcessPath;
            string command;
            if (!string.IsNullOrWhiteSpace(processPath) && !Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) command = $"\"{processPath}\"";
            else
            {
                string dll = Path.Combine(AppContext.BaseDirectory, (Assembly.GetEntryAssembly()?.GetName().Name ?? "ClippyOffline") + ".dll");
                if (string.IsNullOrWhiteSpace(dll) || !File.Exists(dll)) return false;
                command = $"\"{processPath ?? "dotnet"}\" \"{dll}\"";
            }
            key.SetValue("ClippyOffline_by_Chayniy_Alkash", command); return true;
        }
        catch { return false; }
    }

    private void ApplySizes(bool save)
    {
        double charFactor = _settings.CharacterSizePercent / 100.0;
        double bubbleFactor = _settings.BubbleSizePercent / 100.0;
        double bubbleWidth = 262 * bubbleFactor;
        double charWidth = 240 * charFactor;
        double charHeight = 180 * charFactor;
        double characterTop = 96 * bubbleFactor;
        double canvasWidth = Math.Max(bubbleWidth + 8, charWidth + 30);
        double canvasHeight = characterTop + charHeight + 20;
        SpeechBubble.Width = bubbleWidth;
        SpeechBubble.SetValue(System.Windows.Controls.Canvas.LeftProperty, (canvasWidth - bubbleWidth) / 2);
        SpeechText.FontSize = 13 * bubbleFactor;
        SpeechText.MaxHeight = 72 * bubbleFactor;
        CharacterImage.Width = charWidth;
        CharacterImage.Height = charHeight;
        CharacterImage.SetValue(System.Windows.Controls.Canvas.LeftProperty, (canvasWidth - charWidth) / 2);
        CharacterImage.SetValue(System.Windows.Controls.Canvas.TopProperty, characterTop);
        RootCanvas.Width = canvasWidth; RootCanvas.Height = canvasHeight;
        Width = canvasWidth; Height = canvasHeight;
        if (save && _settingsLoaded) SaveSettings();
        UpdateTrayMenuChecks();
    }

    private void OpenSettings()
    {
        var dialog = new SettingsWindow(_settings, SaveSettings) { Owner = this };
        if (dialog.ShowDialog() == true) { ApplySizes(false); ApplyAppearance(); SaveSettings(); UpdateTrayMenuChecks(); }
    }

    private void ToggleVisibility()
    {
        if (_pendingExit) return;
        if (IsVisible) BeginAnimatedHide();
        else { _userHidden = false; Show(); Topmost = true; Activate(); if (!_hiddenForFullscreen && !_manualSleep) { Play("SHOW"); PlaySound("show"); } }
    }

    private void ToggleManualSleep()
    {
        _manualSleep = !_manualSleep;
        if (_manualSleep)
        {
            _sleeping = true; _bubbleTimer.Stop(); SpeechBubble.Visibility = Visibility.Collapsed;
            StartSleepAnimation(); PlaySound("sleep");
        }
        else
        {
            WakeFromSleep(); Say(Choose(Quips.Active("wake")), "wake");
        }
        UpdateTrayMenuChecks();
    }

    private void BeginAnimatedHide()
    {
        if (_pendingHide || _pendingExit || !IsVisible) return;
        _userHidden = true;
        _pendingHide = true; _sleeping = false; _manualSleep = false; _sleepLoopActive = false; _bubbleTimer.Stop(); SpeechBubble.Visibility = Visibility.Collapsed; PlaySound("hide");
        Play("HIDE");
        if (_currentAnimation == null) { _pendingHide = false; Hide(); }
    }

    private void BeginExit()
    {
        if (_pendingExit) return;
        _pendingExit = true; _pendingHide = false; _sleeping = false; _manualSleep = false; _sleepLoopActive = false; _bubbleTimer.Stop(); SpeechBubble.Visibility = Visibility.Collapsed; PlaySound("exit");
        _watchTimer.Stop(); _idleTimer.Stop();
        if (!IsVisible) { _allowClose = true; Close(); return; }
        Play("GOODBYE");
        if (_currentAnimation == null) { Play("HIDE"); if (_currentAnimation == null) { _allowClose = true; Close(); } }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            BeginExit();
            return;
        }
        _watchTimer.Stop(); _frameTimer.Stop(); _bubbleTimer.Stop(); _idleTimer.Stop();
        foreach (var player in _soundCache.Values) { player.Stop(); player.Close(); }
        if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); }
    }

    /// <summary>Один раз после простоя: собрать мусор и отдать системе неиспользуемые страницы (только свой процесс).</summary>
    private void TrimMemoryWhenIdle() => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(NativeMethods.TrimMemory));

    /// <summary>Ложится спать и дальше крутит только «лежачую» часть анимации.</summary>
    private void StartSleepAnimation()
    {
        Play("IDLESNOOZE");
        _sleepLoopActive = _currentAnimation?.Name.Equals("IDLESNOOZE", StringComparison.OrdinalIgnoreCase) == true;
        TrimMemoryWhenIdle();
    }

    /// <summary>Если сон был прерван другой анимацией — возвращаем её сразу в лежачий цикл, без повторного «ложусь».</summary>
    private void ResumeSleepLoop()
    {
        Play("IDLESNOOZE");
        if (_currentAnimation?.Name.Equals("IDLESNOOZE", StringComparison.OrdinalIgnoreCase) != true) { _frameTimer.Stop(); return; }
        _sleepLoopActive = true; _frameIndex = SleepLoopStart; ShowFrame();
    }

    /// <summary>Выключает цикл сна: доигрывает «просыпается и встаёт», затем обычный IDLE.</summary>
    private void WakeFromSleep()
    {
        _sleeping = false;
        bool lying = _sleepLoopActive && _frameIndex >= SleepLoopStart
            && _currentAnimation?.Name.Equals("IDLESNOOZE", StringComparison.OrdinalIgnoreCase) == true;
        _sleepLoopActive = false;
        if (lying) { _loopAnimation = false; _frameIndex = SleepWakeFrame; ShowFrame(); } // по окончании тик запустит IDLE1_1
        else Play("IDLE1_1", true);
    }

    private void Play(string name, bool loop = false)
    {
        if (_pendingExit && name != "GOODBYE" && name != "HIDE") return;
        if (!_animations.TryGetValue(name.Replace('-', '_'), out var animation))
        {
            var alias = name.Replace('_', '-');
            animation = _animations.Values.FirstOrDefault(a => a.Name.Equals(alias, StringComparison.OrdinalIgnoreCase))
                ?? _animations.Values.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        if (animation == null) return;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Animations", animation.File);
            var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(path); bmp.EndInit(); bmp.Freeze();
            _currentStrip = bmp; _currentAnimation = animation; _frameIndex = 0; _loopAnimation = loop; _sleepLoopActive = false; ShowFrame();
        }
        catch { _currentAnimation = null; }
    }

    private void ShowFrame()
    {
        if (_currentStrip == null || _currentAnimation == null) return;
        int count = Math.Max(1, _currentAnimation.FrameCount);
        _frameIndex = Math.Clamp(_frameIndex, 0, count - 1);
        int x = _frameIndex * FrameWidth;
        if (x + FrameWidth > _currentStrip.PixelWidth) { _frameIndex = 0; x = 0; }
        CharacterImage.Source = new CroppedBitmap(_currentStrip, new Int32Rect(x, 0, Math.Min(FrameWidth, _currentStrip.PixelWidth - x), Math.Min(FrameHeight, _currentStrip.PixelHeight)));
        int duration = _currentAnimation.DurationsMs != null && _frameIndex < _currentAnimation.DurationsMs.Length
            ? _currentAnimation.DurationsMs[_frameIndex] : Math.Max(50, _currentAnimation.DurationMs / count);
        _frameTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(duration, 30, 3000));
        _frameTimer.Stop(); _frameTimer.Start();
    }

    private void FrameTimer_Tick(object? sender, EventArgs e)
    {
        _frameTimer.Stop();
        if (_currentAnimation == null) return;
        if (_pendingExit && _currentAnimation.Name.Equals("GOODBYE", StringComparison.OrdinalIgnoreCase) && _frameIndex >= GoodbyeLastFrame) { _allowClose = true; Close(); return; }
        if (_sleepLoopActive && _frameIndex >= SleepLoopEnd) _frameIndex = SleepLoopStart; // спит по кругу, не вставая
        else if (_frameIndex + 1 < _currentAnimation.FrameCount) _frameIndex++;
        else if (_loopAnimation) _frameIndex = 0;
        else
        {
            if (_pendingExit) { _allowClose = true; Close(); return; }   // GOODBYE доигран — просто закрываемся
            if (_pendingHide) { _pendingHide = false; Hide(); TrimMemoryWhenIdle(); return; }
            if (_sleeping || _manualSleep) { ResumeSleepLoop(); return; }
            Play("IDLE1_1", true); return;
        }
        ShowFrame();
    }

    private string PickIdleAnimation()
    {
        // Every bundled animation except dedicated lifecycle/alert/sleep animations can appear as rare ambient behavior.
        string[] ambient = { "GESTURELEFT", "GESTUREDOWN", "GESTURERIGHT", "GESTUREUP", "IDLEFINGERTAP", "IDLESIDETOSIDE", "IDLEEYEBROWRAISE", "GETARTSY", "LOOKDOWNRIGHT", "LOOKDOWN", "LOOKDOWNLEFT", "LOOKRIGHT", "LOOKLEFT", "HEARING_1", "LOOKUPRIGHT", "LOOKUP", "LOOKUPLEFT", "WAVE", "SAVE", "CONGRATULATE", "PROCESSING", "PRINT", "CHECKINGSOMETHING", "EMPTYTRASH", "IDLEROPEPILE", "IDLEHEADSCRATCH", "SENDMAIL", "SEARCHING", "THINKING", "WRITING", "EXPLAIN", "RESTPOSE", "GETTECHY", "GETWIZARDY", "IDLEATOM", "IDLE1_1" };
        return ambient[_random.Next(ambient.Length)];
    }

    private void WatchTimer_Tick(object? sender, EventArgs e)
    {
        if (_pendingExit || _userHidden) return;
        var foreground = NativeMethods.GetForegroundWindow();
        var own = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (foreground == IntPtr.Zero || foreground == own) return;
        if (NativeMethods.IsFullscreen(foreground, own))
        {
            if (!_hiddenForFullscreen) { _hiddenForFullscreen = true; Hide(); }
            return;
        }
        if (_hiddenForFullscreen) { _hiddenForFullscreen = false; if (!_manualSleep) { Show(); Topmost = true; Play("SHOW"); } }
        if (_manualSleep || _sleeping || !IsVisible) return;
        if (foreground != _lastForegroundWindow)
        {
            _lastForegroundWindow = foreground;
            try
            {
                uint pid = NativeMethods.GetWindowProcessId(foreground);
                using var process = Process.GetProcessById((int)pid);
                string name = process.ProcessName;
                if (!string.IsNullOrWhiteSpace(_lastProcessName) && !name.Equals(_lastProcessName, StringComparison.OrdinalIgnoreCase) && DateTime.Now - _lastSpoke > TimeSpan.FromSeconds(24) && _random.NextDouble() < .28)
                {
                    if (name.Contains("chrome", StringComparison.OrdinalIgnoreCase) || name.Contains("firefox", StringComparison.OrdinalIgnoreCase) || name.Contains("msedge", StringComparison.OrdinalIgnoreCase)) { Say(Choose(Quips.Active("browser")), "appSwitch"); Play("LOOKRIGHT"); }
                    else if (name.Contains("explorer", StringComparison.OrdinalIgnoreCase)) { Say(Choose(Quips.Active("explorer")), "appSwitch"); Play("LOOKDOWN"); }
                    else { Say(Choose(Quips.Active("appSwitch")), "appSwitch"); Play("GESTURERIGHT"); }
                }
                _lastProcessName = name;
            }
            catch { }
        }
        if (!_bubblePinned && DateTime.Now >= _nextRandomQuip && DateTime.Now - _lastSpoke > TimeSpan.FromSeconds(60))
        {
            Say(Choose(Quips.Active("idle")), "idle"); Play(PickIdleAnimation()); ScheduleNextQuip();
        }
    }

    private void IdleTimer_Tick(object? sender, EventArgs e)
    {
        if (_pendingExit) return;
        if (_manualSleep) return;
        if (NativeMethods.GetIdleSeconds() < 300)
        {
            if (_sleeping) { WakeFromSleep(); if (_settings.AutoSleepSounds) PlaySound("wake"); }
            return;
        }
        if (_sleeping || _hiddenForFullscreen || !IsVisible) return;
        _sleeping = true; StartSleepAnimation();
        if (DateTime.Now - _lastSpoke > TimeSpan.FromMinutes(4)) Say(Choose(Quips.Active("sleep")), _settings.AutoSleepSounds ? "sleep" : "");
        else if (_settings.AutoSleepSounds) PlaySound("sleep");
    }

    private static string Choose(string[] items) => items.Length == 0 ? "" : items[Random.Shared.Next(items.Length)];
    private void ScheduleNextQuip() => _nextRandomQuip = DateTime.Now.AddSeconds(_random.Next(115, 240));

    private void Say(string message, string soundState, bool pin = false)
    {
        if (string.IsNullOrWhiteSpace(message)) { PlaySound(soundState); ScheduleNextQuip(); return; }
        // Одно и то же облачко: текст просто заменяется, таймер скрытия перезапускается — наслоений нет.
        SpeechText.Text = message; SpeechBubble.Visibility = Visibility.Visible; _bubblePinned = pin; _lastSpoke = DateTime.Now;
        _bubbleTimer.Stop(); if (!pin) _bubbleTimer.Start(); PlaySound(soundState); ScheduleNextQuip();
    }

    /// <summary>Случайная реплика, не совпадающая с той, что сейчас в облачке (чтобы каждый клик был заметен).</summary>
    private string ChooseFresh(string[] items)
    {
        string pick = Choose(items);
        for (int i = 0; i < 6 && items.Length > 1 && pick == SpeechText.Text; i++) pick = Choose(items);
        return pick;
    }

    private void PlaySound(string state)
    {
        if (!_settings.SoundsEnabled) return;
        if (_settings.SoundFiles.TryGetValue(state, out var path) && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                if (_soundCache.TryGetValue(state, out var previous)) { previous.Stop(); previous.Close(); }
                var player = new MediaPlayer { Volume = Math.Clamp(_settings.SoundVolumes.TryGetValue(state, out var volume) ? volume / 100.0 : 1.0, 0.0, 1.0) };
                player.MediaOpened += (_, _) => { player.Position = TimeSpan.Zero; player.Play(); };
                void Release() { player.Close(); if (_soundCache.TryGetValue(state, out var cached) && ReferenceEquals(cached, player)) _soundCache.Remove(state); }
                player.MediaEnded += (_, _) => Release();
                player.MediaFailed += (_, _) => Release();
                player.Open(new Uri(path, UriKind.Absolute));
                _soundCache[state] = player;
                return;
            }
            catch { }
        }
        // No configured file means silence. Never fall back to Windows system sounds.
    }

    private void CharacterImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_pendingHide || _pendingExit || (_manualSleep && !_settings.AllowDragInSleep)) { e.Handled = true; return; }
        _mousePressed = true; _dragging = false;
        _pressCursor = NativeMethods.GetCursorPosition(); _pressLeft = Left; _pressTop = Top;
        CharacterImage.CaptureMouse(); e.Handled = true;
    }

    private void CharacterImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_mousePressed || e.LeftButton != MouseButtonState.Pressed) return;
        var cursor = NativeMethods.GetCursorPosition();
        double dx = cursor.X - _pressCursor.X, dy = cursor.Y - _pressCursor.Y;
        if (!_dragging && Math.Abs(dx) + Math.Abs(dy) >= 9) _dragging = true;
        if (_dragging) { Left = _pressLeft + dx; Top = _pressTop + dy; }
    }

    private void CharacterImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mousePressed) return;
        _mousePressed = false; CharacterImage.ReleaseMouseCapture();
        if (_dragging) { _dragging = false; e.Handled = true; return; }
        if (_manualSleep) { e.Handled = true; return; }
        RegisterClick(); e.Handled = true;
    }

    private void RegisterClick()
    {
        var now = DateTime.Now;
        var gap = now - _lastClickAt;
        if (gap < TimeSpan.FromMilliseconds(35)) return;      // дребезг контактов мыши
        _clickChain = gap <= ChainGap ? _clickChain + 1 : 1;
        _lastClickAt = now;

        if (_clickChain >= RageClicks)
        {
            _clickChain = 0;
            Say(ChooseFresh(Quips.Active("rage")), "rage"); Play("ALERT"); return;
        }

        if (_clickChain == 2)
        {
            Say(ChooseFresh(Quips.Active("doubleClick")), "doubleClick"); Play("GETATTENTION"); return;
        }

        Say(ChooseFresh(Quips.Active("click")), "click");
        Play(PickClickAnimation());
    }

    private string PickClickAnimation()
    {
        // Varied short reactions; ALERT is deliberately reserved for repeated clicking.
        string[] reactions = { "WAVE", "GESTURELEFT", "GESTURERIGHT", "GESTUREUP", "GESTUREDOWN", "LOOKUP", "LOOKRIGHT", "LOOKLEFT", "LOOKDOWN", "IDLEEYEBROWRAISE", "GETARTSY", "IDLEFINGERTAP", "HEARING_1", "LOOKUPLEFT", "LOOKUPRIGHT" };
        return reactions[_random.Next(reactions.Length)];
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        AppTheme.Attach(menu.Resources, _settings.DarkMenus, _settings.RetroXp);
        AppTheme.ApplyFont(menu, _settings.RetroXp);
        var talk = new MenuItem { Header = "Скажи что-нибудь" };
        talk.Click += (_, _) => { if (!_manualSleep) { Say(Choose(Quips.Active("click")), "click"); Play("WAVE"); } };
        var settings = new MenuItem { Header = "Настройки…" }; settings.Click += (_, _) => OpenSettings();
        var sleep = new MenuItem { Header = _manualSleep ? "Разбудить" : "Режим сна", IsCheckable = true, IsChecked = _manualSleep };
        sleep.Click += (_, _) => ToggleManualSleep();
        var hide = new MenuItem { Header = "Спрятаться" }; hide.Click += (_, _) => BeginAnimatedHide();
        var quit = new MenuItem { Header = "Выход" }; quit.Click += (_, _) => BeginExit();
        menu.Items.Add(talk); menu.Items.Add(settings); menu.Items.Add(sleep); menu.Items.Add(new Separator()); menu.Items.Add(hide); menu.Items.Add(new Separator()); menu.Items.Add(quit); menu.IsOpen = true; e.Handled = true;
    }

    private sealed class AnimationManifest { public List<AnimationData> Animations { get; set; } = new(); }
    private sealed class AnimationData
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("file")] public string File { get; set; } = "";
        [JsonPropertyName("frame_count")] public int FrameCount { get; set; }
        [JsonPropertyName("duration_ms")] public int DurationMs { get; set; }
        [JsonPropertyName("durations_ms")] public int[]? DurationsMs { get; set; }
    }
}
