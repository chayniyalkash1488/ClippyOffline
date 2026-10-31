using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Forms;
namespace ClippyOffline;
internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    /// <summary>Поменяны ли местами левая и правая кнопки мыши в системе (SM_SWAPBUTTON).</summary>
    internal static bool MouseButtonsSwapped() => GetSystemMetrics(23) != 0;
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    internal static void SetCaptionTheme(IntPtr hwnd, bool xp)
    {
        try
        {
            int none = unchecked((int)0xFFFFFFFF);
            int caption = xp ? 0x00E35400 : none;
            int text = xp ? 0x00FFFFFF : none;
            int border = xp ? 0x00E35400 : none;
            int corner = xp ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
            DwmSetWindowAttribute(hwnd, 36, ref text, 4);
            DwmSetWindowAttribute(hwnd, 34, ref border, 4);
            DwmSetWindowAttribute(hwnd, 33, ref corner, 4);
        }
        catch { }
    }
    internal static POINT GetCursorPosition() { GetCursorPos(out var p); return p; }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
    internal static double GetIdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return 0;
        uint elapsed = unchecked((uint)Environment.TickCount - info.dwTime);
        return elapsed / 1000.0;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    internal static uint GetWindowProcessId(IntPtr hWnd) { GetWindowThreadProcessId(hWnd, out uint pid); return pid; }
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

    // Окна оболочки Windows (рабочий стол, панель задач, трей). Они всегда «на весь экран» по размеру,
    // но это не полноэкранное приложение — прятаться из-за них не нужно.
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow"
    };

    private static string GetWindowClass(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(256);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    internal static bool IsShellWindow(IntPtr hwnd) => ShellClasses.Contains(GetWindowClass(hwnd));

    internal static bool IsFullscreen(IntPtr hwnd, IntPtr ownHandle)
    {
        if (hwnd == IntPtr.Zero || hwnd == ownHandle || !IsWindowVisible(hwnd) || IsIconic(hwnd) || IsShellWindow(hwnd)) return false;
        if (!GetWindowRect(hwnd, out var r)) return false;
        var b = Screen.FromHandle(hwnd).Bounds; const int t = 3;
        return r.Left <= b.Left+t && r.Top <= b.Top+t && r.Right >= b.Right-t && r.Bottom >= b.Bottom-t;
    }

    [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    /// <summary>Сборка мусора + trim рабочего набора собственного процесса (страницы вернутся по требованию).</summary>
    internal static void TrimMemory()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            SetProcessWorkingSetSize(self.Handle, new IntPtr(-1), new IntPtr(-1));
        }
        catch { /* оптимизация не критична */ }
    }
}
