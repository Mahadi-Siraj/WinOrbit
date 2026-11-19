namespace WinOrbit;

/// <summary>
/// Entry point. <c>--agent</c> is the hook command AI coding tools run on every status change, so it is handled here,
/// before WPF and the app's styles load, and exits in a few milliseconds. Everything else starts the app as usual.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Any(a => a.Equals("--agent", StringComparison.OrdinalIgnoreCase)))
        {
            Services.AgentClient.Run(args);
            Environment.Exit(0);   // even if a read of the input is still pending
            return;
        }
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
