namespace DailyMacro;

internal static class AppInfo
{
    public const string Name = "XoMacro";

    /// <summary>Bump this on every dist-vXX publish. Shown in the window title.</summary>
    public const int Build = 65;

    public static string Label => "v" + Build;

    public static string WindowTitle => Name;
}
