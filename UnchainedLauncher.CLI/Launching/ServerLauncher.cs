using UnchainedLauncher.CLI.Registries;
using StructuredINI;
using System.Net;
using UnchainedLauncher.CLI.Configuration;
using UnchainedLauncher.CLI.Launching.Proton;
using UnchainedLauncher.CLI.Terminal;
using UnchainedLauncher.Core.INIModels.Game;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services.Dlls;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;
using UnchainedLauncher.Core.Services.Server.A2S;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.CLI.Launching;

public static class ServerLauncher {
    public static async Task<LaunchedGame> LaunchAsync(CliLaunchOptions request, CancellationToken cancellationToken) {
        var options = request.Shared;
        await ProtonRuntime.ConfigureAsync(request, cancellationToken);
        var executable = Path.Combine(options.Chivalry2Path, FilePaths.GameBinPath);
        var basePakPath = Path.Combine(options.Chivalry2Path, FilePaths.PakDir);
        if (!File.Exists(executable)) throw new FileNotFoundException("Chivalry 2 shipping executable was not found. Check --chivalry2-path.", executable);
        if (!Directory.Exists(basePakPath)) throw new DirectoryNotFoundException($"Chivalry 2 Paks directory was not found: {basePakPath}");
        await PluginLogging.PrepareAsync(request, cancellationToken);

        var registry = CreateRegistry(request);

        return await InInstallationDirectoryAsync(request, async () => {
            // Ownership/cache come from this profile; selection comes only from the request.
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
            var preparer = CreateLaunchPreparer(request, manager, pluginUpdater, cancellationToken);
            if (options.Proton != null) {
                var prepared = await preparer.PrepareLaunch(request.ToLaunchOptions());
                if (prepared.IsNone) throw new InvalidOperationException("Launch preparation was cancelled.");
                return await ProtonGameLauncher.LaunchAsync(request, prepared.IfNone(request.ToLaunchOptions()), pluginUpdater, cancellationToken);
            }
            return await NativeGameLauncher.LaunchAsync(executable, options.Chivalry2Path,
                request.ToLaunchOptions(), preparer, pluginUpdater, forwardConsole: request is ServerOptions);
        });
    }

    internal static IChivalry2LaunchPreparer<LaunchOptions> CreateLaunchPreparer(CliLaunchOptions request,
        ModManager manager, DllLaunchSession pluginUpdater,
        CancellationToken cancellationToken = default) {
        var dialogue = new ConsoleDialogue();
        var modInstaller = new Chivalry2ModsInstaller(manager, dialogue, preparePlan: (roots, _) => {
            ValidateRequestedReleases(manager, request.Shared.Mods);
            foreach (var coordinate in request.Shared.Mods) {
                if (!roots.Any(coordinate.Matches))
                    throw new InvalidOperationException($"Requested mod pin changed during preparation: {coordinate}");
            }
            return pluginUpdater.GetPakPlan(roots);
        });
        var signing = SigPreparer.CreateForProfile(manager.PakDir, dialogue);
        // Call preparers sequentially so each receives the previous result.
        // The mutating manager checkpoints ownership, including partial installation.
        return Chivalry2LaunchPreparer.Create<LaunchOptions>(async launch => {
            cancellationToken.ThrowIfCancellationRequested();
            var updated = await pluginUpdater.PrepareLaunch(launch);
            cancellationToken.ThrowIfCancellationRequested();
            if (updated.IsNone) return LanguageExt.Option<LaunchOptions>.None;
            updated = await modInstaller.PrepareLaunch(updated.IfNone(launch));
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
        return LegacyOwnershipMigration.LoadProfile(request.InstancePaths.ModMetadata, registry,
            request.InstancePaths.CreatePakDir(request.Shared.Chivalry2Path, cancellationToken),
            request.Shared.Mods.Distinct()).Manager;
    }

    internal static DllLaunchSession CreateDllSession(CliLaunchOptions request, AggregateModRegistry registry,
        IModManager manager, CancellationToken cancellationToken = default, HttpClient? client = null) =>
        new(registry, manager, request.InstancePaths.Plugins,
            request.Shared.Dlls, request.Shared.UnsafeLoadAllDlls, cancellationToken, client, request.Shared.Assets.DllCache);

    internal static void InitializeServerProfile(CliLaunchOptions request, Func<string, string>? configPath = null) {
        if (request is not ServerOptions server) return;

        var directory = configPath != null ? configPath(server.GameSavedDirSuffix)
            : Path.Combine(Path.GetDirectoryName(request.Shared.Assets.Root)!, "Config", "WindowsNoEditor");
        // Match the GUI's startup defaults without rewriting unrelated server or player settings.
        var writer = new StructuredINIWriter();
        if (!writer.BufferWrite(new TBLTitleScreen()) || !writer.BufferWrite(new TBLGameInstance()))
            throw new IOException("Could not initialize the server INI profile; refusing to launch.");
        var files = new Dictionary<string, StructuredINIWriter>(StringComparer.OrdinalIgnoreCase) { ["Game"] = writer };
        server.Ini.BufferInto(files);
        server.Settings.Rotation?.BufferInto(writer);
        IniPatches.Write(directory, files);
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
        ValidateRequestedReleases(manager, requested);
        PakLaunchPlan.Create(manager);
    }

    private static void ValidateRequestedReleases(IModManager manager, IEnumerable<ReleaseCoordinates> requested) {
        foreach (var coordinate in requested) {
            if (manager.GetRelease(coordinate).IsNone)
                throw new ArgumentException($"Requested mod release was not found: {coordinate.Org}/{coordinate.ModuleName}:{coordinate.Version}");
        }
        if (manager.GetLatestRelease(CommonMods.UnchainedMods).IsNone)
            throw new InvalidOperationException("The registry must provide a stable Chiv2-Community/Unchained-Mods release.");
    }

    public static IA2S CreateQuery(ServerOptions options) {
        var address = options.Settings.LocalIp is null ? IPAddress.Loopback : IPAddress.Parse(options.Settings.LocalIp);
        if (address.Equals(IPAddress.Any)) address = IPAddress.Loopback;
        return new A2S(new IPEndPoint(address, options.Settings.QueryPort));
    }
}