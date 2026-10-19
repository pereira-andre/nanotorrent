using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TorrentClassic.Core;

namespace TorrentClassic.Desktop;

/// <summary>Recover from read-only/translocated app locations while keeping data in a chosen portable folder.</summary>
internal sealed class WorkspaceWindow : Window
{
    readonly Func<string, Task> open;
    readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed };
    readonly Expander diagnostics = new() { Header = "Technical details", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBox location = Ui.Text();
    readonly Button choose;
    bool opening;

    internal WorkspaceWindow(Exception reason, Func<string, Task> open)
    {
        this.open = open;
        Title = "NanoTorrent — Choose a portable folder";
        Icon = Program.LoadIcon();
        Width = 730; Height = 450; MinWidth = 660; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        location.IsReadOnly = true; location.Text = Program.PortableDirectory;
        choose = Ui.Button("Choose portable folder…", async () =>
        {
            try { if (await Ui.PickFolder(this, "Choose portable folder (containing Data)") is { } folder) await OpenAsync(folder); }
            catch (Exception e) { ShowError(e); }
        });
        var body = Ui.Stack(
            new TextBlock { Text = "Choose where to keep your portable data", FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "NanoTorrent could not write beside the app. " + (OperatingSystem.IsMacOS() ? "macOS may run a downloaded app from a temporary read-only location. A disk image or protected folder can cause the same problem." : "The app folder may be read-only or protected."), TextWrapping = TextWrapping.Wrap },
            new TextBlock { Text = "Select the extracted NanoTorrent folder, or another writable folder. Your session stays in Data; a new session uses Downloads inside the selected folder. To resume an existing session, select the folder containing its Data folder. Existing settings and download folders are preserved.", TextWrapping = TextWrapping.Wrap },
            Ui.Field("Current app folder:", location), error, diagnostics,
            new TextBlock { Text = "For fully portable use, move the complete NanoTorrent folder to a writable location" + (OperatingSystem.IsMacOS() ? " in Finder" : "") + " and open the app there. This dialog may appear again while the app runs from a read-only location.", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var root = new DockPanel { Margin = new Thickness(20) };
        var buttons = Ui.Buttons(Ui.Button("Quit", () => { Close(); return Task.CompletedTask; }), choose);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons); root.Children.Add(new ScrollViewer { Content = body }); Content = root;
        ShowError(reason);
        if (Program.StartupTest)
            Opened += async (_, _) =>
            {
                try
                {
                    Directory.CreateDirectory(Program.Arguments[1]);
                    await MainWindow.Capture(this, Path.Combine(Program.Arguments[1], "read-only-startup.png"));
                    if (Program.Arguments.Length < 3) throw new InvalidOperationException("The startup test needs a writable recovery folder.");
                    await OpenAsync(Program.Arguments[2]);
                }
                catch (Exception e) { Program.ReportTestFailure(e); Close(); }
            };
    }
    void ShowError(Exception reason)
    {
        var log = Program.RecordError(reason);
        error.Text = reason is IOException or UnauthorizedAccessException
            ? "The folder is read-only or access was denied. Choose a folder you can write to."
            : "The app could not open its data folder. You can try another portable folder.";
        diagnostics.Content = new TextBlock { Text = reason.Message + (log is null ? "" : "\nDiagnostic log: " + log), TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    }
    internal async Task OpenAsync(string folder)
    {
        if (opening) return; opening = true; choose.IsEnabled = false;
        try
        {
            var data = PortableWorkspace.Prepare(folder);
            await open(data); Close();
        }
        catch (Exception e)
        {
            ShowError(e);
            if (Program.StartupTest) throw;
        }
        finally { opening = false; choose.IsEnabled = true; }
    }
}
