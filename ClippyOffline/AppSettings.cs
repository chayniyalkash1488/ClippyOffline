namespace ClippyOffline;

internal sealed class AppSettings
{
    public int CharacterSizePercent { get; set; } = 100;
    public int BubbleSizePercent { get; set; } = 100;
    public bool SoundsEnabled { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;
    public bool DarkMenus { get; set; } = false;
    public bool RetroXp { get; set; } = false;
    public bool AllowDragInSleep { get; set; } = true;
    public bool AutoSleepSounds { get; set; } = true;
    public Dictionary<string, string[]> Phrases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> SoundVolumes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> SoundFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
