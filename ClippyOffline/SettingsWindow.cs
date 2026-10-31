using Panel = System.Windows.Controls.Panel;
using Control = System.Windows.Controls.Control;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClippyOffline;

internal sealed class SettingsWindow : Window
{
    private readonly AppSettings _original;
    private readonly CheckBox _soundsEnabled;
    private readonly CheckBox _darkMenus;
    private readonly CheckBox _retroXp;
    private readonly CheckBox _dragInSleep;
    private readonly CheckBox _autoSleepSounds;
    private readonly Action? _persist;
    private readonly Dictionary<string, List<Action>> _phraseCommits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Slider> _soundVolumes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ComboBox _characterSize;
    private readonly ComboBox _bubbleSize;
    private readonly Dictionary<string, TextBox> _soundPaths = new(StringComparer.OrdinalIgnoreCase);

    private static readonly (string Key, string Label)[] SoundStates =
    {
        ("startup", "Запуск"), ("click", "Обычный клик"), ("doubleClick", "Двойной клик"),
        ("rage", "Спам кликами / злость"), ("appSwitch", "Смена приложения"),
        ("idle", "Фоновая реплика"), ("sleep", "Сон"), ("wake", "Пробуждение"),
        ("hide", "Скрытие"), ("show", "Раскрытие"), ("exit", "Выход")
    };

    public SettingsWindow(AppSettings settings, Action? persist = null)
    {
        _original = settings;
        _persist = persist;
        _original.Phrases ??= new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        Title = "ClippyOffline — настройки";
        Width = 640; Height = 700; MinWidth = 600; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AppTheme.Attach(Resources, settings.DarkMenus, settings.RetroXp);
        AppTheme.ApplyFont(this, settings.RetroXp);
        SetResourceReference(BackgroundProperty, "ThemeBg");
        SetResourceReference(ForegroundProperty, "ThemeFg");

        var root = new DockPanel { Margin = new Thickness(14) };
        Content = root;
        var bottom = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var version = MutedText("Версия " + AppInfo.Version + " " + AppInfo.Channel, new Thickness(0));
        version.VerticalAlignment = VerticalAlignment.Center;
        bottom.Children.Add(version);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        Grid.SetColumn(buttons, 1);
        var save = new Button { Content = "Сохранить", MinWidth = 100, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Отмена", MinWidth = 90, Padding = new Thickness(10, 6, 10, 6), IsCancel = true };
        save.Click += (_, _) => SaveAndClose();
        buttons.Children.Add(save); buttons.Children.Add(cancel);
        bottom.Children.Add(buttons);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);

        var content = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        var left = new StackPanel();
        top.Children.Add(left);
        var preview = CreatePreview();
        if (preview != null) { Grid.SetColumn(preview, 1); top.Children.Add(preview); }
        content.Children.Add(top);
        left.Children.Add(new TextBlock { Text = "Внешний вид", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        _characterSize = AddSizeRow(left, "Размер скрепки", settings.CharacterSizePercent);
        _darkMenus = new CheckBox { Content = "Тёмная тема меню и настроек", IsChecked = settings.DarkMenus, IsEnabled = !settings.RetroXp, Margin = new Thickness(0, 8, 0, 8) };
        left.Children.Add(_darkMenus);
        _bubbleSize = AddSizeRow(left, "Размер облачка", settings.BubbleSizePercent);
        _retroXp = new CheckBox { Content = "Ретро-тема Windows XP (все окна приложения)", IsChecked = settings.RetroXp, Margin = new Thickness(0, 6, 0, 0) };
        _darkMenus.Checked += (_, _) => ApplyTheme(); _darkMenus.Unchecked += (_, _) => ApplyTheme();
        _retroXp.Checked += (_, _) => ApplyTheme(); _retroXp.Unchecked += (_, _) => ApplyTheme();
        content.Children.Add(_retroXp);
        SourceInitialized += (_, _) => { if (_retroXp.IsChecked == true) NativeMethods.SetCaptionTheme(new WindowInteropHelper(this).Handle, true); };
        content.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 12) });
        content.Children.Add(new TextBlock { Text = "Поведение", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        _dragInSleep = new CheckBox { Content = "Разрешить перемещать скрепку курсором в режиме сна", IsChecked = settings.AllowDragInSleep };
        content.Children.Add(_dragInSleep);
        content.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 12) });
        content.Children.Add(new TextBlock { Text = "Звуки по состояниям", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        _soundsEnabled = new CheckBox { Content = "Включить звуки", IsChecked = settings.SoundsEnabled, Margin = new Thickness(0, 0, 0, 6) };
        content.Children.Add(_soundsEnabled);
        _autoSleepSounds = new CheckBox { Content = "Звуки сна и пробуждения, если скрепка уснула сама от бездействия", IsChecked = settings.AutoSleepSounds, Margin = new Thickness(0, 0, 0, 10) };
        content.Children.Add(_autoSleepSounds);
        content.Children.Add(MutedText("Выбирай WAV-файлы. Если путь пустой, событие будет беззвучным. Звуки проигрываются локально.", new Thickness(0, 0, 0, 8)));
        foreach (var state in SoundStates)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.Children.Add(new TextBlock { Text = state.Label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) });
            var pathBox = new TextBox { Text = settings.SoundFiles.TryGetValue(state.Key, out var path) ? path : "", VerticalContentAlignment = VerticalAlignment.Center, MinWidth = 90 };
            Grid.SetColumn(pathBox, 1); row.Children.Add(pathBox); _soundPaths[state.Key] = pathBox;
            var browse = new Button { Content = "Обзор…", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 4, 8, 4), Tag = state.Key };
            browse.Click += (_, _) => BrowseSound((string)browse.Tag, pathBox);
            Grid.SetColumn(browse, 2); row.Children.Add(browse);
            var clear = new Button { Content = "✕", MinWidth = 30, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(0, 4, 0, 4), ToolTip = "Убрать звук", IsEnabled = pathBox.Text.Trim().Length > 0 };
            clear.Click += (_, _) => pathBox.Clear();
            pathBox.TextChanged += (_, _) => clear.IsEnabled = pathBox.Text.Trim().Length > 0;
            Grid.SetColumn(clear, 3); row.Children.Add(clear);
            var volume = new Slider { Minimum = 0, Maximum = 100, Value = settings.SoundVolumes.TryGetValue(state.Key, out var v) ? Math.Clamp(Math.Round(v), 0, 100) : 100, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 5, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Громкость: 0–100%" };
            var volumeText = new TextBlock { Text = ((int)Math.Round(volume.Value)).ToString(), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            volume.ValueChanged += (_, _) => volumeText.Text = ((int)Math.Round(volume.Value)).ToString();
            Grid.SetColumn(volumeText, 5); row.Children.Add(volumeText);
            Grid.SetColumn(volume, 4); row.Children.Add(volume); _soundVolumes[state.Key] = volume; content.Children.Add(row);
        }
        content.Children.Add(MutedText("Примечание: поддерживаются WAV-файлы. Для остальных форматов конвертируй звук в WAV.", new Thickness(0, 8, 0, 0)));

        content.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 12) });
        content.Children.Add(new TextBlock { Text = "Фразы", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        content.Children.Add(MutedText("Раскрой нужную группу и измени фразы. «Сохранить» применяет фразу сразу. Пустая фраза не используется, а если пустая вся группа, скрепка в этом случае молчит.", new Thickness(0, 0, 0, 6)));
        foreach (var category in Quips.Categories) AddPhraseSection(content, category.Key, category.Title, category.Defaults);
    }

    private void AddPhraseSection(Panel parent, string key, string title, string[] defaults)
    {
        var header = new Border { Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, Padding = new Thickness(2, 7, 2, 7), BorderThickness = new Thickness(0, 0, 0, 1) };
        header.SetResourceReference(Border.BorderBrushProperty, "ThemeBorder");
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal };
        var arrow = new TextBlock { Text = "▸", Width = 20, VerticalAlignment = VerticalAlignment.Center };
        headerRow.Children.Add(arrow);
        headerRow.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        header.Child = headerRow;
        var body = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(20, 4, 0, 8) };
        bool filled = false;
        header.MouseLeftButtonUp += (_, _) =>
        {
            bool open = body.Visibility != Visibility.Visible;
            if (open && !filled) { FillPhraseBody(body, key, defaults); filled = true; }
            body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            arrow.Text = open ? "▾" : "▸";
        };
        parent.Children.Add(header);
        parent.Children.Add(body);
    }

    private void FillPhraseBody(StackPanel body, string key, string[] defaults)
    {
        body.Children.Clear();
        _phraseCommits[key] = new List<Action>();
        var current = GetPhrases(key, defaults);
        for (int i = 0; i < current.Length; i++) body.Children.Add(CreatePhraseRow(key, defaults, i, current[i] ?? ""));
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var add = new Button { Content = "Добавить фразу", Padding = new Thickness(10, 4, 10, 4) };
        var reset = new Button { Content = "Вернуть стандартные", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0) };
        add.Click += (_, _) =>
        {
            int index = body.Children.Count - 1;
            body.Children.Insert(index, CreatePhraseRow(key, defaults, index, ""));
        };
        reset.Click += (_, _) =>
        {
            _original.Phrases.Remove(key);
            FillPhraseBody(body, key, defaults);
            _persist?.Invoke();
        };
        tools.Children.Add(add);
        tools.Children.Add(reset);
        body.Children.Add(tools);
    }

    private Grid CreatePhraseRow(string key, string[] defaults, int index, string text)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new TextBox { Text = text, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 28 };
        row.Children.Add(box);
        string saved = text.Trim();
        var save = new Button { Content = "Сохранить", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center, IsEnabled = false };
        var clear = new Button { Content = "Очистить", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center, IsEnabled = text.Length > 0 };
        Grid.SetColumn(save, 1); row.Children.Add(save);
        Grid.SetColumn(clear, 2); row.Children.Add(clear);
        box.TextChanged += (_, _) =>
        {
            save.IsEnabled = box.Text.Trim() != saved;
            clear.IsEnabled = box.Text.Length > 0;
        };
        Action commit = () =>
        {
            var value = box.Text.Trim();
            if (value == saved) return;
            SavePhrase(key, defaults, index, value);
            saved = value;
            save.IsEnabled = false;
        };
        save.Click += (_, _) => commit();
        clear.Click += (_, _) => box.Clear();
        _phraseCommits[key].Add(commit);
        return row;
    }

    private string[] GetPhrases(string key, string[] defaults)
    {
        if (_original.Phrases.TryGetValue(key, out var list) && list != null) return (string[])list.Clone();
        return (string[])defaults.Clone();
    }

    private void SavePhrase(string key, string[] defaults, int index, string value)
    {
        var list = GetPhrases(key, defaults);
        if (index >= list.Length) Array.Resize(ref list, index + 1);
        list[index] = value;
        _original.Phrases[key] = list;
        _persist?.Invoke();
    }

    private static TextBlock MutedText(string text, Thickness margin)
    {
        var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "ThemeMuted");
        return tb;
    }

    private void ApplyTheme()
    {
        bool xp = _retroXp.IsChecked == true;
        _darkMenus.IsEnabled = !xp;
        AppTheme.Fill(Resources, _darkMenus.IsChecked == true, xp);
        AppTheme.ApplyFont(this, xp);
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) NativeMethods.SetCaptionTheme(handle, xp);
    }

    private static System.Windows.Controls.Image? CreatePreview()
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "clippy_preview.png");
            if (!System.IO.File.Exists(path)) return null;
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            var image = new System.Windows.Controls.Image { Source = bmp, Height = 108, Stretch = Stretch.Uniform, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }
        catch { return null; }
    }

    private static ComboBox AddSizeRow(Panel parent, string label, int selected)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var combo = new ComboBox { ItemsSource = new[] { 60, 75, 90, 100, 110, 125, 140, 150, 175, 200 }, SelectedItem = selected, Margin = new Thickness(0, 2, 0, 2) };
        if (combo.SelectedItem == null) combo.SelectedItem = 100;
        Grid.SetColumn(combo, 1); row.Children.Add(combo); parent.Children.Add(row); return combo;
    }

    private void BrowseSound(string key, TextBox target)
    {
        var dialog = new OpenFileDialog { Title = "Выбрать звук для состояния", Filter = "WAV audio (*.wav)|*.wav|Все файлы (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) target.Text = dialog.FileName;
    }

    private void SaveAndClose()
    {
        _original.CharacterSizePercent = (int)(_characterSize.SelectedItem ?? 100);
        _original.BubbleSizePercent = (int)(_bubbleSize.SelectedItem ?? 100);
        _original.SoundsEnabled = _soundsEnabled.IsChecked == true;
        _original.DarkMenus = _darkMenus.IsChecked == true;
        _original.RetroXp = _retroXp.IsChecked == true;
        _original.AllowDragInSleep = _dragInSleep.IsChecked == true;
        _original.AutoSleepSounds = _autoSleepSounds.IsChecked == true;
        foreach (var commits in _phraseCommits.Values.ToList()) foreach (var commit in commits) commit();
        _original.SoundVolumes ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var volume in _soundVolumes) _original.SoundVolumes[volume.Key] = volume.Value.Value;
        foreach (var entry in _soundPaths)
        {
            var path = entry.Value.Text.Trim();
            if (path.Length == 0) _original.SoundFiles.Remove(entry.Key);
            else _original.SoundFiles[entry.Key] = path;
        }
        DialogResult = true;
    }
}
