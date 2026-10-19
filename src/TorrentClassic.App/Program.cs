using System.IO.Pipes;
using System.Text.Json;
using MonoTorrent;
using TorrentClassic.Core;

namespace TorrentClassic.App;

internal static class Program
{
    const string InstanceName = "TorrentClassic-649D8374-0DB1-46F2-AB0E-7B59B4922B68";
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var smoke = args.Length == 2 && args[0] == "--smoke-test";
        using var mutex = new Mutex(true, @"Local\" + InstanceName, out var first);
        if (!first && !smoke)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", InstanceName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(3000);
                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.WriteLine(JsonSerializer.Serialize(args));
                return 0;
            }
            catch { MessageBox.Show("O Torrent Classic já está aberto. Aguarda alguns segundos e tenta novamente.", "Torrent Classic"); return 1; }
        }
        var root = smoke ? Path.Combine(Path.GetTempPath(), "TorrentClassic-Smoke-" + Guid.NewGuid().ToString("N")) : Path.Combine(AppContext.BaseDirectory, "Data");
        Application.ThreadException += (_, e) =>
        {
            File.AppendAllText(Path.Combine(root, "errors.log"), DateTime.Now + " " + e.Exception + Environment.NewLine);
            MessageBox.Show(e.Exception.Message, "Torrent Classic", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        try
        {
            Directory.CreateDirectory(root);
            var preferences = smoke ? new Preferences { EnableDht = false, EnableLocalDiscovery = false, EnablePeerExchange = false, EnablePortForwarding = false, Port = 0 } : null;
            var service = new TorrentService(root, preferences);
            using var window = new MainForm(service);
            using var cancellation = new CancellationTokenSource();
            Task server = smoke ? Task.CompletedTask : ListenAsync(window, cancellation.Token);
            window.Shown += async (_, _) =>
            {
                try
                {
                    await window.Ready;
                    if (smoke) await SmokeAsync(window, args[1], root);
                    else if (args.Length > 0) window.HandleArguments(args);
                }
                catch (Exception error)
                {
                    File.WriteAllText(Path.Combine(smoke ? args[1] : root, "failure.txt"), error.ToString());
                    Environment.ExitCode = 1;
                    window.RequestExit();
                }
            };
            Application.Run(window);
            cancellation.Cancel();
            server.GetAwaiter().GetResult();
            if (smoke) Directory.Delete(root, true);
            return Environment.ExitCode;
        }
        catch (Exception e)
        {
            try { File.WriteAllText(Path.Combine(root, "startup-error.log"), e.ToString()); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (!smoke) MessageBox.Show("Não foi possível iniciar o Torrent Classic.\n\n" + e.Message + "\n\nExtrai a aplicação para uma pasta onde possas escrever (por exemplo, o Ambiente de Trabalho ou uma pen USB).", "Torrent Classic", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
    static async Task ListenAsync(MainForm form, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(InstanceName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var reader = new StreamReader(pipe);
                // A torrent path/magnet fits in 32 KiB; bound input before deserializing.
                var buffer = new char[32768];
                int length = 0;
                while (length < buffer.Length)
                {
                    var read = await reader.ReadAsync(buffer.AsMemory(length, 1), timeout.Token);
                    if (read == 0 || buffer[length] == '\n') break;
                    length += read;
                }
                if (length == buffer.Length) continue;
                var args = JsonSerializer.Deserialize<string[]>(buffer.AsSpan(0, length));
                if (args is not null && !form.IsDisposed) form.BeginInvoke(() => form.HandleArguments(args));
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (JsonException) { }
            catch (InvalidOperationException) when (form.IsDisposed || token.IsCancellationRequested) { }
        }
    }
    static async Task SmokeAsync(MainForm form, string output, string root)
    {
        Directory.CreateDirectory(output);
        foreach (var name in new[] { "Exemplo de distribuição Linux.iso", "Fotografias de exemplo.zip", "Documentação livre.pdf" })
        {
            var source = Path.Combine(root, name);
            await File.WriteAllBytesAsync(source, System.Text.Encoding.UTF8.GetBytes("Torrent Classic · " + name));
            var creator = new TorrentCreator { CreatedBy = "Torrent Classic smoke test", PieceLength = 16384 };
            var torrent = source + ".torrent";
            await creator.CreateAsync(new TorrentFileSource(source), torrent);
            await form.Service.AddAsync(torrent, Path.Combine(root, "downloads"), false);
        }
        foreach (var theme in new[] { "Claro", "Escuro" })
        {
            await form.Service.SavePreferencesAsync(form.Service.Preferences with { Theme = theme });
            form.ApplyTheme();
            await Task.Delay(1200);
            form.Refresh();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
            bitmap.Save(Path.Combine(output, theme.ToLowerInvariant() + ".png"));
            using var dialog = new PreferencesDialog(form.Service.Preferences, Palette.For(theme));
            dialog.Show(form);
            dialog.Refresh();
            using var dialogBitmap = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(dialogBitmap, new Rectangle(0, 0, dialog.Width, dialog.Height));
            dialogBitmap.Save(Path.Combine(output, "preferencias-" + theme.ToLowerInvariant() + ".png"));
            dialog.Close();
        }
        File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: main window, three real torrent entries, preferences and both themes rendered on Windows.");
        form.RequestExit();
    }
}
