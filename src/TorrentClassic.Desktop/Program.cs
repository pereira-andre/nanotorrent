using System.Globalization;
using System.IO.Pipes;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Themes.Simple;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using TorrentClassic.Core;

namespace TorrentClassic.Desktop;

internal static class Program
{
    const string InstanceName = "TorrentClassic-Desktop-649D8374-0DB1-46F2-AB0E-7B59B4922B68";
    static readonly CancellationTokenSource lifetime = new();
    internal static Task ListenTask = Task.CompletedTask;
    internal static string[] Arguments = [];
    internal static bool Smoke => Arguments.Length == 2 && Arguments[0] is "--smoke-test" or "--native-smoke-test";
    internal static bool StartupTest => Arguments.Length >= 2 && Arguments[0] == "--startup-smoke-test";
    internal static bool TestRun => Smoke || StartupTest;
    internal static bool WorkspaceRecovered;
    internal static bool Headless => Smoke && Arguments[0] == "--smoke-test";
    internal static string PortableDirectory
    {
        get
        {
            var path = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            if (OperatingSystem.IsMacOS() && Path.GetFileName(path) == "MacOS" && Directory.GetParent(path)?.Name == "Contents")
                return Directory.GetParent(path)!.Parent!.Parent!.FullName;
            return path;
        }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        Arguments = args;
        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(true, InstanceName, out var first);
            if (!first && !TestRun)
            {
                using var pipe = new NamedPipeClientStream(".", InstanceName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                pipe.Connect(3000);
                using var writer = new StreamWriter(pipe) { AutoFlush = true };
                writer.WriteLine(JsonSerializer.Serialize(args)); return 0;
            }
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-GB");
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
            AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception error) RecordError(error); };
            var builder = AppBuilder.Configure<DesktopApp>();
            builder = Headless ? builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }) : builder.UsePlatformDetect();
            var result = builder.StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
            return Environment.ExitCode == 0 ? result : Environment.ExitCode;
        }
        catch (Exception error)
        {
            RecordError(error);
            if (TestRun) ReportTestFailure(error);
            Console.Error.WriteLine(error); return 1;
        }
        finally
        {
            lifetime.Cancel();
            try { ListenTask.GetAwaiter().GetResult(); } catch (Exception error) { RecordError(error); }
            mutex?.Dispose();
        }
    }
    internal static string? RecordError(Exception error) => StartupDiagnostics.Write(error,
        TestRun ? Arguments[1] : Path.Combine(PortableDirectory, "Data"),
        Path.Combine(Path.GetTempPath(), "TorrentClassic", "Diagnostics"));
    internal static void ReportTestFailure(Exception error)
    {
        Environment.ExitCode = 1;
        StartupDiagnostics.Write(error, Arguments[1]);
    }
    internal static async Task ListenAsync(MainWindow window)
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(InstanceName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(lifetime.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var reader = new StreamReader(pipe); var buffer = new char[32768]; int length = 0;
                while (length < buffer.Length) { var read = await reader.ReadAsync(buffer.AsMemory(length, 1), timeout.Token); if (read == 0 || buffer[length] == '\n') break; length += read; }
                if (length == buffer.Length) continue;
                var incoming = JsonSerializer.Deserialize<string[]>(buffer.AsSpan(0, length));
                if (incoming is not null) Dispatcher.UIThread.Post(() => window.HandleArguments(incoming));
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (JsonException) { }
        }
    }
}

public sealed class DesktopApp : Application
{
    public DesktopApp() { Name = "Torrent Classic"; }
    public override void Initialize()
    {
        Styles.Add(new SimpleTheme());
        Styles.Add(new StyleInclude(new Uri("avares://TorrentClassic.Desktop/")) { Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Simple.xaml") });
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                var root = Program.Smoke
                    ? Path.Combine(Path.GetTempPath(), "TorrentClassic-Desktop-Smoke-" + Guid.NewGuid().ToString("N"))
                    : PortableWorkspace.Prepare(Program.PortableDirectory);
                desktop.MainWindow = CreateMainWindow(root);
            }
            catch (Exception reason)
            {
                desktop.MainWindow = new WorkspaceWindow(reason, data =>
                {
                    var window = CreateMainWindow(data);
                    Program.WorkspaceRecovered = true;
                    desktop.MainWindow = window;
                    window.Show();
                    return Task.CompletedTask;
                });
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
    static MainWindow CreateMainWindow(string root)
    {
        var preferences = Program.TestRun ? new Preferences { Port = 0, EnableDht = false, EnablePeerExchange = false, EnableLocalDiscovery = false, EnablePortForwarding = false, DownloadDirectory = Path.Combine(Path.GetDirectoryName(root)!, "Downloads") } : null;
        var runtime = new ClientRuntime(new TorrentService(root, preferences));
        var window = new MainWindow(runtime);
        if (!Program.TestRun) Program.ListenTask = Program.ListenAsync(window);
        if (Current is { } app) app.RequestedThemeVariant = runtime.Service.Preferences.Theme switch { "Dark" => ThemeVariant.Dark, "Light" => ThemeVariant.Light, _ => ThemeVariant.Default };
        return window;
    }

}
