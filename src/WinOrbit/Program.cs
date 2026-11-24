using System.Runtime;

namespace WinOrbit;

/// <summary>
/// Entry point. <c>--agent</c> is the hook command AI coding tools run on every status change, so it is handled here,
/// before WPF and the app's styles load, and exits in a few milliseconds. Everything else starts the app as usual.
/// </summary>
public static class Program
{
    /// <summary>Multicore JIT profile, in the data folder (not JSON, so backups leave it out).</summary>
    internal const string JitProfileName = "startup.jitprofile";

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Any(a => a.Equals("--agent", StringComparison.OrdinalIgnoreCase)))
        {
            Services.AgentClient.Run(args);
            Environment.Exit(0);   // even if a read of the input is still pending
            return;
        }
        StartJitProfile(args);
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    /// <summary>
    /// .NET's multicore JIT: the running copy records which methods it compiles while it starts, and the next start
    /// compiles those again on a spare core while the UI thread loads the windows. .NET 10's JIT is slower than .NET 8's
    /// and the Windows Runtime projection ships uncompiled, so without this the installed version started about 0.1 s
    /// later than 1.3.2 did; with it, it starts about 0.24 s sooner than without (our test PC). The profile holds method
    /// names only. A missing or outdated one (first start, after an update) is simply recorded again.
    /// On only in framework-dependent builds (the WinOrbit.MulticoreJit switch in WinOrbit.csproj).
    /// </summary>
    private static void StartJitProfile(string[] args)
    {
        try
        {
            if (!AppContext.TryGetSwitch("WinOrbit.MulticoreJit", out var on) || !on) return;
            var otherCopyRunning = Mutex.TryOpenExisting(App.MutexName, out var running);
            running?.Dispose();
            if (!RecordsJitProfile(args, otherCopyRunning)) return;
            ProfileOptimization.SetProfileRoot(Core.Paths.Root);
            ProfileOptimization.StartProfile(JitProfileName);
        }
        catch { }   // only a speed-up; never a reason not to start
    }

    /// <summary>
    /// Only a launch that becomes the running WinOrbit may record. A second copy that just hands over to the running one,
    /// <c>--quit</c>, the uninstaller's <c>--revert-tweaks</c> and the classic-player helper (<c>--wmp-link</c>) exit or
    /// run other code, and would replace the profile with their own few methods.
    /// </summary>
    internal static bool RecordsJitProfile(IReadOnlyList<string> args, bool otherCopyRunning) =>
        !otherCopyRunning && !args.Any(a => a.Equals("--quit", StringComparison.OrdinalIgnoreCase)
                                         || a.Equals("--revert-tweaks", StringComparison.OrdinalIgnoreCase)
                                         || a.Equals("--wmp-link", StringComparison.OrdinalIgnoreCase));
}
