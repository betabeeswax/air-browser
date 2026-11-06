using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;

namespace AIRBrowser;

public partial class App : Application
{
    private Window? _window;
    private static int _isLoadingMainWindowXaml;

    public static void SetMainWindowXamlLoading(bool loading) =>
        Interlocked.Exchange(ref _isLoadingMainWindowXaml, loading ? 1 : 0);
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIR Browser", "startup.log");

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public App()
    {
        InitializeComponent();
        RequestedTheme = ApplicationTheme.Dark;
        UnhandledException += (_, e) => WriteDiagnostic("XAML unhandled exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteDiagnostic("AppDomain unhandled exception", e.ExceptionObject as Exception);
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            if (Volatile.Read(ref _isLoadingMainWindowXaml) == 1)
                WriteDiagnostic("First-chance exception while loading MainWindow.xaml", e.Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteDiagnostic("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            WriteDiagnostic("AIR Browser launch started", null);
            _window = new MainWindow();
            _window.Activate();
            WriteDiagnostic("Main window activated", null);
        }
        catch (Exception ex)
        {
            WriteDiagnostic("Fatal startup failure", ex);
            MessageBoxW(IntPtr.Zero,
                "AIR Browser could not start. A diagnostic log was saved here:\n" + LogPath +
                "\n\nError: " + ex.Message,
                "AIR Browser startup error", 0x10);
        }
    }

    private static string FormatExceptionDetails(Exception exception)
    {
        var builder = new System.Text.StringBuilder();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            builder.AppendLine($"Type: {current.GetType().FullName}");
            builder.AppendLine($"HResult: 0x{current.HResult:X8}");
            builder.AppendLine($"Message: {current.Message}");
            foreach (var propertyName in new[] { "LineNumber", "LinePosition", "BaseUri" })
            {
                try
                {
                    var value = current.GetType().GetProperty(propertyName)?.GetValue(current);
                    if (value is not null) builder.AppendLine($"{propertyName}: {value}");
                }
                catch { }
            }
            builder.AppendLine(current.StackTrace);
            builder.AppendLine();
        }
        return builder.ToString();
    }

    public static void WriteDiagnostic(string message, Exception? exception)
    {
        try
        {
            var folder = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(folder);
            File.AppendAllText(LogPath,
                $"[{DateTimeOffset.Now:O}] {message}" +
                (exception is null ? Environment.NewLine : Environment.NewLine + FormatExceptionDetails(exception) + Environment.NewLine));
        }
        catch { }
    }
}
