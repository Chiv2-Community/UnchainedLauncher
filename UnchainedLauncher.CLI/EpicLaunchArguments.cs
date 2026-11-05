using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using UnchainedLauncher.Core.JsonModels;

namespace UnchainedLauncher.CLI;

internal static class EpicLaunchArguments {
    private const string Guidance = "Sign in to Epic through Heroic/Legendary, or pass fresh Epic launch arguments after --.";

    internal static bool Required(InstallationType platform, IEnumerable<string> arguments) =>
        platform == InstallationType.EpicGamesStore && !arguments.Any(arg =>
            arg.TrimStart('-').StartsWith("AUTH_", StringComparison.OrdinalIgnoreCase));

    internal static ProcessStartInfo StartInfo(string game, Func<string, string?>? environment = null, string? home = null) {
        environment ??= Environment.GetEnvironmentVariable;
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var heroic = ProtonRuntime.FindHeroicInstallation(game, environment, home);
        var flatpakRoot = Path.Combine(home, ".var/app/com.heroicgameslauncher.hgl/config/heroic");
        var flatpak = heroic == flatpakRoot;
        var info = new ProcessStartInfo(flatpak ? "flatpak" : "legendary") {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var config = environment("LEGENDARY_CONFIG_PATH") ??
            (heroic == null ? null : Path.Combine(heroic, "legendaryConfig/legendary"));
        if (config != null) info.Environment["LEGENDARY_CONFIG_PATH"] = config;
        if (flatpak) {
            info.ArgumentList.Add("run");
            info.ArgumentList.Add($"--env=LEGENDARY_CONFIG_PATH={config}");
            info.ArgumentList.Add("--command=/app/bin/heroic/resources/app.asar.unpacked/build/bin/x64/linux/legendary");
            info.ArgumentList.Add("com.heroicgameslauncher.hgl");
        }
        foreach (var arg in new[] { "launch", "Peppermint", "--json", "--skip-version-check" }) info.ArgumentList.Add(arg);
        return info;
    }

    internal static string[] Parse(string json) {
        try {
            using var document = JsonDocument.Parse(json);
            var arguments = document.RootElement.GetProperty("egl_parameters").EnumerateArray()
                .Select(value => value.GetString() ?? throw new InvalidDataException()).ToArray();
            if (arguments.Length == 0 || arguments.Any(arg => !arg.StartsWith('-') || arg.Any(char.IsControl)) ||
                !arguments.Any(arg => arg.StartsWith("-AUTH_PASSWORD=", StringComparison.OrdinalIgnoreCase) && arg.Length > 15) ||
                !arguments.Any(arg => arg.Equals("-epicapp=Peppermint", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException();
            return arguments;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or InvalidDataException) {
            // Never include parser errors or Legendary output: either may contain credentials.
            throw new InvalidOperationException($"Legendary returned invalid Epic launch arguments. {Guidance}");
        }
    }

    internal static async Task<string[]> AcquireAsync(string game, CancellationToken token) =>
        await RunAsync(StartInfo(game), token);

    internal static async Task<string[]> RunAsync(ProcessStartInfo info, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var process = new Process { StartInfo = info };
        try {
            if (!process.Start()) throw new InvalidOperationException($"Could not start Legendary. {Guidance}");
        }
        catch (Win32Exception) {
            throw new InvalidOperationException($"Epic authentication tool was not found; install Legendary (or Flatpak Heroic). {Guidance}");
        }
        process.StandardInput.Close();
        var output = ReadOutputAsync(process.StandardOutput, timeout.Token);
        var errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
        try {
            var json = await output;
            await process.WaitForExitAsync(timeout.Token);
            await errors;
            if (process.ExitCode != 0) throw new InvalidOperationException($"Epic authentication failed (exit {process.ExitCode}). {Guidance}");
            return Parse(json);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) {
            throw new InvalidOperationException($"Epic authentication timed out. {Guidance}");
        }
        finally {
            await timeout.CancelAsync();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            try { await Task.WhenAll(output, errors); }
            catch { /* Output is private; preserve the original error. */ }
        }
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader, CancellationToken token) {
        var output = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) != 0) {
            if (output.Length + count > 1024 * 1024) throw new InvalidOperationException("Epic authentication response exceeded the size limit.");
            output.Append(buffer, 0, count);
        }
        return output.ToString();
    }
}