using System.Security.Cryptography;
using System.Text.Json;
using UnchainedLauncher.Core.Services.Processes;

namespace UnchainedLauncher.ProtonHelper;

internal static class Program {
    private static async Task<int> Main(string[] args) {
        if (!OperatingSystem.IsWindows() || args.Length != 2) return 2;
        if (args[0] == "local-app-data") {
            await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
            return 0;
        }
        if (args[0] != "launch") return 2;
        var directory = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
        var files = new List<FileStream>();
        System.Diagnostics.Process? game = null;
        ProcessOutputCapture? capture = null;
        Task drain = Task.CompletedTask;
        try {
            var request = JsonSerializer.Deserialize<ProtonLaunchRequest>(await File.ReadAllTextAsync(args[1]))
                ?? throw new InvalidDataException();
            File.Delete(args[1]);
            foreach (var dll in request.Dlls) {
                var stream = new FileStream(dll.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                files.Add(stream);
                if (stream.Length != dll.Size || !Convert.ToHexString(SHA256.HashData(stream)).Equals(dll.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException();
            }
            var injector = DllInjector.FromVerifiedPaths(request.Dlls.Select(dll => dll.Path).ToArray());
            if (request.Server) {
                capture = new ProcessOutputCapture();
                drain = Task.WhenAll(capture.StandardOutput.CopyToAsync(Console.OpenStandardOutput()),
                    capture.StandardError.CopyToAsync(Console.OpenStandardError()));
            }
            var result = new ProcessLauncher(request.Executable, output: capture).Launch(request.WorkingDirectory,
                request.Arguments, process => injector.Inject(process));
            game = result.Match(Right: process => process, Left: _ => throw new InvalidOperationException());
            await File.WriteAllTextAsync(Path.Combine(directory, "ready"), game.Id.ToString());
            while (!game.HasExited) {
                // Only this launch is owned here. Never terminate a prefix-wide wineserver.
                if (File.Exists(Path.Combine(directory, "stop")) || request.Server &&
                    DateTime.UtcNow - File.GetLastWriteTimeUtc(Path.Combine(directory, "heartbeat")) > TimeSpan.FromSeconds(20)) {
                    game.Kill(entireProcessTree: true);
                    await game.WaitForExitAsync();
                    return 0;
                }
                await Task.Delay(250);
            }
            return game.ExitCode;
        }
        catch {
            // Neither launch arguments nor exception text may expose authentication secrets.
            if (game is { HasExited: false }) game.Kill(entireProcessTree: true);
            return 1;
        }
        finally {
            try { await drain.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception error) when (error is IOException or ObjectDisposedException or TimeoutException) { }
            finally { capture?.Dispose(); }
            foreach (var file in files) file.Dispose();
            game?.Dispose();
            // The request can contain credentials; do not leave it on disk after launch.
            try {
                File.Delete(args[1]);
                foreach (var name in new[] { "ready", "stop", "heartbeat" }) File.Delete(Path.Combine(directory, name));
                Directory.Delete(directory);
            }
            catch (IOException) { /* The native launcher may still be removing its IPC directory. */ }
        }
    }

}
