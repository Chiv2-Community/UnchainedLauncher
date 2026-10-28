using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services.Mods;

namespace UnchainedLauncher.CLI;

public sealed class ProtonConfiguration {
    public string? Path { get; set; }
    public string? CompatDataPath { get; set; }
    public string? SteamPath { get; set; }
    public string? HelperPath { get; set; }
}

internal sealed record ProtonRuntime(string Executable, string CompatDataPath, string? SteamPath, string HelperPath,
    bool SteamGame = true) {
    internal const string AppId = "1824220";

    internal static async Task ConfigureAsync(CliLaunchOptions request, CancellationToken token) {
        if (OperatingSystem.IsWindows()) return;
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Game launching supports Windows and Linux with Proton.");
        if (request.Shared.Proton != null) return;
        var runtime = Discover(request.Shared);
        var directory = CreatePrivateDirectory();
        try {
            var output = System.IO.Path.Combine(directory, "local-app-data.json");
            var windowsOutput = await runtime.WinePathAsync(output, windows: true, token);
            await runtime.ToolAsync([runtime.HelperPath, "local-app-data", windowsOutput], token);
            var windowsAppData = JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(output, token))
                ?? throw new InvalidDataException("Wine did not return LocalAppData.");
            if (string.IsNullOrWhiteSpace(windowsAppData)) throw new InvalidDataException("Wine returned an empty LocalAppData path.");
            var localAppData = await runtime.WinePathAsync(windowsAppData, windows: false, token);
            request.Shared.Assets = new ProfileAssetPaths(request.Shared.SavedDirSuffix, localAppData);
            request.Shared.Proton = runtime;
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    internal static ProtonRuntime Discover(SharedLaunchOptions options, Func<string, string?>? environment = null, string? home = null) {
        environment ??= Environment.GetEnvironmentVariable;
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = options.ProtonConfiguration;
        var steamGame = options.Platform == InstallationType.Steam;
        var steam = config.SteamPath ?? environment("STEAM_COMPAT_CLIENT_INSTALL_PATH") ??
            new[] { System.IO.Path.Combine(home, ".steam/root"), System.IO.Path.Combine(home, ".local/share/Steam"),
                System.IO.Path.Combine(home, ".steam/steam"), System.IO.Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam") }
                .FirstOrDefault(path => Directory.Exists(System.IO.Path.Combine(path, "steamapps")));
        if (steam == null && steamGame) throw new InvalidOperationException("Steam was not found; set --steam-path.");
        if (steam != null) steam = System.IO.Path.GetFullPath(steam);
        var libraries = new List<string>();
        if (steamGame && steam != null) libraries.Add(steam);
        var folders = steam == null ? null : System.IO.Path.Combine(steam, "steamapps/libraryfolders.vdf");
        if (steamGame && File.Exists(folders)) libraries.AddRange(SteamKeyValues.Read(folders!).At("libraryfolders")?.Children.Values
            .Select(node => node.At("path")?.Value ?? node.Value).OfType<string>() ?? []);
        libraries = libraries.Select(Canonical).Distinct().ToList();
        var library = libraries.Distinct().FirstOrDefault(path => {
            var manifest = System.IO.Path.Combine(path, $"steamapps/appmanifest_{AppId}.acf");
            if (!File.Exists(manifest)) return false;
            var install = SteamKeyValues.Read(manifest).At("AppState", "installdir")?.Value;
            return install != null && SameDirectory(System.IO.Path.Combine(path, "steamapps/common", install), options.Chivalry2Path);
        });
        var compat = config.CompatDataPath ?? environment("STEAM_COMPAT_DATA_PATH");
        var proton = config.Path ?? environment("STEAM_COMPAT_TOOL_PATHS")?.Split(System.IO.Path.PathSeparator)
            .FirstOrDefault(path => File.Exists(System.IO.Path.Combine(path, "proton")));
        if (compat == null && environment("WINEPREFIX") is { } winePrefix) {
            if (System.IO.Path.GetFileName(winePrefix.TrimEnd('/')) != "pfx")
                throw new InvalidOperationException("Proton requires a compat-data directory containing pfx; set --compat-data-path.");
            compat = System.IO.Path.GetDirectoryName(winePrefix.TrimEnd('/'));
        }
        if (!steamGame && (compat == null || proton == null)) {
            var heroic = FindHeroic(options.Chivalry2Path, environment, home);
            compat ??= heroic?.CompatData;
            proton ??= heroic?.Proton;
        }
        compat ??= library != null ? System.IO.Path.Combine(library, "steamapps/compatdata", AppId)
            : throw new InvalidOperationException(steamGame
                ? "Game Steam library was not found; set --compat-data-path."
                : "EGS requires a Heroic Proton prefix or explicit --compat-data-path containing pfx.");
        compat = System.IO.Path.GetFullPath(compat);
        if (!File.Exists(System.IO.Path.Combine(compat, "pfx/system.reg")))
            throw new InvalidOperationException(steamGame
                ? "Initialize this game's Proton prefix by launching it through Steam once."
                : "Initialize this game's Proton prefix through Heroic first; --compat-data-path must contain pfx/system.reg. Wine-only prefixes are not supported.");

        if (proton == null) {
            if (!steamGame) throw new InvalidOperationException("EGS requires Heroic's selected Proton runner or explicit --proton-path.");
            var tools = FindTools(libraries, steam!).ToArray();
            var steamConfig = System.IO.Path.Combine(steam!, "config/config.vdf");
            var mapping = File.Exists(steamConfig) ? SteamKeyValues.Read(steamConfig)
                .At("InstallConfigStore", "Software", "Valve", "Steam", "CompatToolMapping") : null;
            var selected = mapping?.At(AppId, "name")?.Value ?? mapping?.At("0", "name")?.Value;
            var candidates = tools.Where(tool => selected == null || tool.Names.Contains(selected, StringComparer.OrdinalIgnoreCase))
                .Select(tool => tool.Path).Distinct().ToArray();
            if (candidates.Length != 1) throw new InvalidOperationException("Cannot identify the selected Proton tool; set --proton-path.");
            proton = candidates[0];
        }
        if (Directory.Exists(proton)) proton = System.IO.Path.Combine(proton, "proton");
        proton = System.IO.Path.GetFullPath(proton);
        if (!File.Exists(proton)) throw new FileNotFoundException("Proton executable was not found.", proton);
        var helper = config.HelperPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "proton-helper/UnchainedLauncher.ProtonHelper.exe");
        if (!File.Exists(helper)) throw new FileNotFoundException("Proton helper missing; use a Linux CLI publish containing proton-helper.", helper);
        return new(proton, compat, steam, System.IO.Path.GetFullPath(helper), steamGame);
    }

    private static (string Proton, string CompatData)? FindHeroic(string game, Func<string, string?> environment, string home) {
        static JsonElement ObjectField(JsonElement parent, string key) {
            if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(key, out var value) ||
                value.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
            return value;
        }
        static string TextField(JsonElement parent, string key) {
            if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(key, out var value) ||
                value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) throw new InvalidDataException();
            return value.GetString()!;
        }
        var configHome = environment("XDG_CONFIG_HOME") ?? System.IO.Path.Combine(home, ".config");
        foreach (var root in new[] { System.IO.Path.Combine(configHome, "heroic"),
                     System.IO.Path.Combine(home, ".var/app/com.heroicgameslauncher.hgl/config/heroic") }.Distinct()) {
            var installed = System.IO.Path.Combine(root, "legendaryConfig/legendary/installed.json");
            var settings = System.IO.Path.Combine(root, "GamesConfig/Peppermint.json");
            if (!File.Exists(installed) || !File.Exists(settings)) continue;
            try {
                using var installs = JsonDocument.Parse(File.ReadAllText(installed));
                if (installs.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
                if (!installs.RootElement.TryGetProperty("Peppermint", out var entry)) continue;
                var location = TextField(entry, "install_path");
                if (!System.IO.Path.IsPathFullyQualified(location) || !Directory.Exists(location) || !SameDirectory(location, game)) continue;
                using var document = JsonDocument.Parse(File.ReadAllText(settings));
                var config = ObjectField(document.RootElement, "Peppermint");
                var runner = ObjectField(config, "wineVersion");
                if (TextField(runner, "type") != "proton")
                    throw new InvalidOperationException("Heroic must use a Proton runner for CLI launching; Wine-only runners are not supported. Set --proton-path and --compat-data-path to override.");
                var proton = TextField(runner, "bin");
                var compat = TextField(config, "winePrefix");
                if (!System.IO.Path.IsPathFullyQualified(proton) || !System.IO.Path.IsPathFullyQualified(compat))
                    throw new InvalidDataException();
                return (proton, compat);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException) {
                throw new InvalidOperationException("Cannot read Heroic's game runtime settings; set --proton-path and --compat-data-path explicitly.");
            }
        }
        return null;
    }

    private static IEnumerable<(string Path, string[] Names)> FindTools(IEnumerable<string> libraries, string steam) {
        foreach (var root in libraries.Select(path => System.IO.Path.Combine(path, "steamapps/common"))
                     .Append(System.IO.Path.Combine(steam, "compatibilitytools.d")).Distinct()) {
            if (!Directory.Exists(root)) continue;
            foreach (var tool in Directory.EnumerateDirectories(root)) {
                if (!File.Exists(System.IO.Path.Combine(tool, "proton"))) continue;
                var names = new List<string> { Regex.Replace(System.IO.Path.GetFileName(tool).Replace(" - ", " ").ToLowerInvariant(), @"\s+", "_") };
                var major = Regex.Match(System.IO.Path.GetFileName(tool), @"^Proton (\d+)(?:\.\d+)?$", RegexOptions.IgnoreCase);
                if (major.Success) names.Add("proton_" + major.Groups[1].Value);
                var metadata = System.IO.Path.Combine(tool, "compatibilitytool.vdf");
                if (File.Exists(metadata)) names.AddRange(SteamKeyValues.Read(metadata).At("compatibilitytools", "compat_tools")?.Children.Keys.AsEnumerable() ?? []);
                yield return (System.IO.Path.Combine(tool, "proton"), names.ToArray());
            }
        }
    }

    private static bool SameDirectory(string a, string b) => Canonical(a) == Canonical(b);
    private static string Canonical(string path) {
        path = System.IO.Path.GetFullPath(path);
        var current = System.IO.Path.GetPathRoot(path)!;
        foreach (var part in path[current.Length..].Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)) {
            current = System.IO.Path.Combine(current, part);
            current = new DirectoryInfo(current).ResolveLinkTarget(true)?.FullName ?? current;
        }
        return current;
    }

    internal static void CleanupDirectory(string directory) {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    internal ProcessStartInfo StartInfo(IEnumerable<string> arguments) {
        var info = new ProcessStartInfo(Executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        if (SteamPath != null) info.Environment["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = SteamPath;
        else info.Environment.Remove("STEAM_COMPAT_CLIENT_INSTALL_PATH");
        info.Environment["STEAM_COMPAT_DATA_PATH"] = CompatDataPath;
        info.Environment["WINEPREFIX"] = System.IO.Path.Combine(CompatDataPath, "pfx");
        info.Environment["STEAM_COMPAT_APP_ID"] = SteamGame ? AppId : "0";
        if (SteamGame) {
            info.Environment["SteamAppId"] = AppId;
            info.Environment["SteamGameId"] = AppId;
        }
        else {
            info.Environment.Remove("SteamAppId");
            info.Environment.Remove("SteamGameId");
            info.Environment.Remove("SteamOverlayGameId");
        }
        // Do not let Proton write to the native TUI or enable game logging implicitly.
        info.Environment["WINEDEBUG"] = "-all";
        return info;
    }

    internal async Task<string> ToolAsync(IEnumerable<string> arguments, CancellationToken token) {
        using var process = Process.Start(StartInfo(new[] { "runinprefix" }.Concat(arguments)))
            ?? throw new InvalidOperationException("Could not start Proton.");
        var output = process.StandardOutput.ReadToEndAsync(token);
        var errors = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
        await errors;
        if (process.ExitCode != 0) throw new InvalidOperationException($"Proton path query failed (exit {process.ExitCode}).");
        return (await output).TrimEnd('\r', '\n');
    }

    internal async Task<string> WinePathAsync(string path, bool windows, CancellationToken token) {
        var converted = await ToolAsync(["winepath", windows ? "-w" : "-u", path], token);
        var absolute = windows ? Regex.IsMatch(converted, @"\A(?:[A-Za-z]:[\\/]|\\\\)")
            : System.IO.Path.IsPathFullyQualified(converted);
        if (!absolute || converted.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new InvalidDataException("Wine returned an invalid absolute path.");
        return converted;
    }

    internal static string CreatePrivateDirectory() {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Proton runs on Linux.");
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unchained-proton-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
