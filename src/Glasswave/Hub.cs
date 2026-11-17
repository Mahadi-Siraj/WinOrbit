using Glasswave.Core;
using Glasswave.Design;
using Glasswave.Island;
using Glasswave.Rainmeter;
using Glasswave.Services;
using Glasswave.Wallpaper;
using Glasswave.Widgets;

namespace Glasswave;

/// <summary>The application's long-lived services. Created once in <see cref="App"/>, in dependency order.</summary>
public static class Hub
{
    public static bool Ready { get; set; }
    public static Store Store { get; set; } = null!;
    public static AppSettings Settings => Store.Settings;
    public static ShellHost Shell { get; set; } = null!;
    public static WallpaperEngine Wallpaper { get; set; } = null!;
    public static ThemeEngine Theme { get; set; } = null!;
    public static SystemMonitor Monitor { get; set; } = null!;
    public static MediaService Media { get; set; } = null!;
    public static TimerService Timers { get; set; } = null!;
    public static ForegroundWatcher Foreground { get; set; } = null!;
    public static NetworkGate Net { get; set; } = null!;
    public static WeatherService Weather { get; set; } = null!;
    public static IslandController Island { get; set; } = null!;
    public static WidgetManager Widgets { get; set; } = null!;
    public static ProfileService Profiles { get; set; } = null!;
    public static RainmeterService Rainmeter { get; set; } = null!;

    /// <summary>Re-apply everything from settings (after a profile switch or import).</summary>
    public static void ApplyAll()
    {
        var id = Settings.ActiveThemeId;
        if (Theme.Find(id) is null) Settings.ActiveThemeId = id = Presets.All[0].Id;
        Theme.Use(id);
        Island.Apply();
        Widgets.Apply();
        App.Current.RegisterHotkeys();
    }
}
