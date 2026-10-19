using StructuredINI;
using System.Diagnostics;
using System.Net;
using UnchainedLauncher.Core.INIModels.Game;
using UnchainedLauncher.Core.JsonModels;
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
        var basePakPath = Path.Combine(options.Chivalry2Path, FilePaths.PakDir);
        var instance = request.InstancePaths;
        if (!File.Exists(executable)) throw new FileNotFoundException("Chivalry 2 shipping executable was not found. Check --chivalry2-path.", executable);
        if (!Directory.Exists(basePakPath)) throw new DirectoryNotFoundException($"Chivalry 2 Paks directory was not found: {basePakPath}");

        var registry = CreateRegistry(request);

        return await InInstallationDirectoryAsync(request, async () => {
            var cachePath = instance.ModMetadata;
            var codec = new ModManagerCodec(registry);
            // Core reconciles physical ownership across hosts immediately before installing.
            // CLI selection comes only from this request, never from a cached GUI selection.
            var manager = CreateModManager(request, registry, cancellationToken);
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
            var pluginUpdater = CreateDllSession(request, registry, manager, cancellationToken);
            var preparer = CreateLaunchPreparer(request, manager, pluginUpdater, codec, cachePath, cancellationToken);
            var launcher = new UnchainedChivalry2Launcher(preparer, new ProcessLauncher(executable),
                options.Chivalry2Path, pluginUpdater);
            var result = await launcher.Launch(request.ToLaunchOptions());
            return result.Match(
                Right: process => process,
                // Do not print LaunchFailed.Message: it contains raw password/auth arguments.
                Left: failure => throw new InvalidOperationException("Game launch or DLL injection failed. Check the installation, plugin, and mod files.", failure));
        });
    }

    internal static IChivalry2LaunchPreparer<LaunchOptions> CreateLaunchPreparer(CliLaunchOptions request,
        ModManager manager, DllLaunchSession pluginUpdater, ModManagerCodec codec, string cachePath,
        CancellationToken cancellationToken = default) {
        var dialogue = new ConsoleDialogue();
        var modInstaller = new Chivalry2ModsInstaller(manager, dialogue);
        var signing = SigPreparer.Create(manager.PakDir, dialogue);
        // Call preparers sequentially so each receives the previous result.
        // Always save the managed-pak inventory, even after a partial installation.
        return Chivalry2LaunchPreparer.Create<LaunchOptions>(async launch => {
            cancellationToken.ThrowIfCancellationRequested();
            var updated = await pluginUpdater.PrepareLaunch(launch);
            cancellationToken.ThrowIfCancellationRequested();
            if (updated.IsNone) return LanguageExt.Option<LaunchOptions>.None;
            // DLL preparation can enable older pak releases with different dependencies.
            ValidateReleases(manager, request.Shared.Mods);
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
            InitializeServerProfile(request);
            return updated;
        });
    }

    internal static AggregateModRegistry CreateRegistry(CliLaunchOptions options) =>
        options.Shared.RegistryOptions.CreateRegistry();

    internal static ModManager CreateModManager(CliLaunchOptions request, AggregateModRegistry registry, CancellationToken cancellationToken = default) {
        var basePakPath = Path.Combine(request.Shared.Chivalry2Path, FilePaths.PakDir);
        return new ModManager(registry, request.InstancePaths.CreatePakDir(basePakPath, cancellationToken), request.Shared.Mods.Distinct());
    }

    internal static DllLaunchSession CreateDllSession(CliLaunchOptions request, AggregateModRegistry registry,
        IModManager manager, CancellationToken cancellationToken = default, HttpClient? client = null) =>
        new(registry, manager, request.InstancePaths.Plugins,
            request.Shared.Dlls, request.Shared.UnsafeLoadAllDlls, cancellationToken, client, request.Shared.Assets.DllCache);

    internal static void InitializeServerProfile(CliLaunchOptions request, Func<string, string>? configPath = null) {
        if (request is not ServerOptions server) return;

        var directory = (configPath ?? FilePaths.Chiv2ConfigPath)(server.GameSavedDirSuffix);
        // Match the GUI's startup defaults without rewriting unrelated server or player settings.
        var writer = new StructuredINIWriter();
        if (!writer.BufferWrite(new TBLTitleScreen()) || !writer.BufferWrite(new TBLGameInstance()) ||
            !writer.WriteOut(Path.Combine(directory, "Game.ini")))
            throw new IOException("Could not initialize the server INI profile; refusing to launch.");
        server.Ini.Apply(directory);
        server.Settings.Rotation?.Apply(directory);
    }

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
            foreach (var dependency in release.Info.EffectiveModDependencies) {
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