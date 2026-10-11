using Octokit;
using System.Diagnostics;
using System.Net;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Services.Server.A2S;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.CLI;

public static class ServerLauncher {
    public static async Task<Process> LaunchAsync(CliLaunchOptions request, CancellationToken cancellationToken) {
        var options = request.Shared;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Game launching requires Windows (Core's DLL injector uses kernel32.dll).");
        var executable = Path.Combine(options.Chivalry2Path, FilePaths.GameBinPath);
        var pakPath = Path.Combine(options.Chivalry2Path, FilePaths.PakDir);
        var pluginsPath = Path.Combine(options.Chivalry2Path, FilePaths.PluginDir);
        if (!File.Exists(executable)) throw new FileNotFoundException("Chivalry 2 shipping executable was not found. Check --chivalry2-path.", executable);
        if (!Directory.Exists(pakPath)) throw new DirectoryNotFoundException($"Chivalry 2 Paks directory was not found: {pakPath}");
        if (!options.UpdatePlugin && !File.Exists(Path.Combine(options.Chivalry2Path, FilePaths.UnchainedPluginPath)))
            throw new FileNotFoundException("--no-plugin-update requires an existing UnchainedPlugin.dll in the Plugins directory.");

        var registry = CreateRegistry(request);

        return await InInstallationDirectoryAsync(request, async () => {
            var cachePath = Path.Combine(options.Chivalry2Path, FilePaths.ModCachePath, "cli_mod_manager.json");
            var codec = new ModManagerCodec(registry);
            // Core reconciles physical ownership across hosts immediately before installing.
            // CLI selection comes only from this request, never from a cached GUI selection.
            var manager = new ModManager(registry,
                new PakDir(pakPath, []), options.Mods.Distinct());
            Console.Error.WriteLine("Loading mod registries...");
            var fetched = await manager.UpdateModsList();
            cancellationToken.ThrowIfCancellationRequested();
            if (fetched.HasErrors) {
                foreach (var error in fetched.Errors) Console.Error.WriteLine(error.Message);
                throw new InvalidOperationException("Registry loading failed; refusing to launch with incomplete metadata.");
            }
            ValidateReleases(manager, options.Mods);

            if (options.Platform == InstallationType.Steam) {
                var appIdPath = Path.Combine(options.Chivalry2Path, FilePaths.SteamAppIdPath);
                if (!File.Exists(appIdPath)) File.WriteAllText(appIdPath, "1824220");
            }
            var dialogue = new ConsoleDialogue();
            var pluginUpdater = new UnchainedPluginUpdateChecker(
                new GithubReleaseLocator(new GitHubClient(new ProductHeaderValue("UnchainedLauncher.CLI")),
                    "Chiv2-Community", "UnchainedPlugin"), new FileInfoVersionExtractor(), dialogue);
            var modInstaller = new Chivalry2ModsInstaller(manager, dialogue);
            var signing = SigPreparer.Create(manager.PakDir, dialogue);
            // Call preparers sequentially so each receives the previous result.
            // Always save the managed-pak inventory, even after a partial installation.
            var preparer = Chivalry2LaunchPreparer.Create<LaunchOptions>(async launch => {
                cancellationToken.ThrowIfCancellationRequested();
                var updated = await pluginUpdater.PrepareLaunch(launch);
                cancellationToken.ThrowIfCancellationRequested();
                if (updated.IsNone) return LanguageExt.Option<LaunchOptions>.None;
                if (!File.Exists(Path.Combine(pluginsPath, "UnchainedPlugin.dll")))
                    throw new FileNotFoundException("UnchainedPlugin.dll is required for an Unchained launch.");
                try {
                    updated = await modInstaller.PrepareLaunch(updated.IfNone(launch));
                }
                finally {
                    codec.SerializeFile(cachePath, manager);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (updated.IsNone || (await signing.PrepareLaunch(LanguageExt.Unit.Default)).IsNone)
                    return LanguageExt.Option<LaunchOptions>.None;
                cancellationToken.ThrowIfCancellationRequested();
                return updated;
            });
            var launcher = new UnchainedChivalry2Launcher(preparer, new ProcessLauncher(executable),
                options.Chivalry2Path, new DllInjector(pluginsPath));
            var result = await launcher.Launch(request.ToLaunchOptions());
            return result.Match(
                Right: process => process,
                // Do not print LaunchFailed.Message: it contains raw password/auth arguments.
                Left: failure => throw new InvalidOperationException("Game launch or DLL injection failed. Check the installation, plugin, and mod files.", failure));
        });
    }

    internal static AggregateModRegistry CreateRegistry(CliLaunchOptions options) =>
        new(new IModRegistry[] {
            new GithubModRegistry("Chiv2-Community", "C2ModRegistry")
        }.Concat(options.Shared.Registries.Select(r => r.Create())).ToArray());

    // Several existing Core preparers use installation-relative paths.
    // This standalone CLI sets CWD only for preparation, then restores it.
    internal static async Task<T> InInstallationDirectoryAsync<T>(CliLaunchOptions options, Func<Task<T>> action) {
        var previousDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(options.Shared.Chivalry2Path);
        try {
            return await action();
        }
        finally {
            Directory.SetCurrentDirectory(previousDirectory);
        }
    }

    public static void ValidateReleases(IModManager manager, IEnumerable<ReleaseCoordinates> requested) {
        foreach (var coordinate in requested) {
            if (manager.GetRelease(coordinate).IsNone)
                throw new ArgumentException($"Requested mod release was not found: {coordinate.Org}/{coordinate.ModuleName}:{coordinate.Version}");
        }
        if (manager.GetLatestRelease(CommonMods.UnchainedMods).IsNone)
            throw new InvalidOperationException("The registry must provide a stable Chiv2-Community/Unchained-Mods release.");
        var releases = manager.GetEnabledAndDependencyReleases().ToArray();
        if (releases.GroupBy(r => (r.Info.Organization, r.Info.RepoName)).Any(g => g.Select(r => r.Tag).Distinct().Count() > 1))
            throw new ArgumentException("Dependency resolution selected conflicting versions of a mod. Core currently selects the latest stable dependencies, including Unchained-Mods.");
        foreach (var release in releases) {
            foreach (var dependency in release.Info.Dependencies) {
                if (manager.GetLatestRelease(ModIdentifier.FromDependency(dependency)).IsNone)
                    throw new InvalidOperationException($"Missing stable dependency: {dependency.Organization}/{dependency.RepoName}");
                if (!releases.Any(r => r.Info.Organization == dependency.Organization && r.Info.RepoName == dependency.RepoName))
                    throw new InvalidOperationException($"Core did not resolve dependency {dependency.Organization}/{dependency.RepoName} for the requested release. Its current resolver uses latest-release dependency metadata.");
            }
        }
    }

    public static IA2S CreateQuery(ServerOptions options) {
        var address = options.Settings.LocalIp is null ? IPAddress.Loopback : IPAddress.Parse(options.Settings.LocalIp);
        if (address.Equals(IPAddress.Any)) address = IPAddress.Loopback;
        if (address.Equals(IPAddress.IPv6Any)) address = IPAddress.IPv6Loopback;
        return new A2S(new IPEndPoint(address, options.Settings.QueryPort));
    }
}