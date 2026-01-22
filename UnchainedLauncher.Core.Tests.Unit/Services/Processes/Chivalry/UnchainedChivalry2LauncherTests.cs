using FluentAssertions;
using LanguageExt;
using LanguageExt.Common;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.Processes;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers;

namespace UnchainedLauncher.Core.Tests.Unit.Services.Processes.Chivalry;

public class UnchainedChivalry2LauncherTests {
    private sealed class CapturingProcessLauncher : IProcessLauncher {
        public string Executable => "test.exe";
        public string? WorkingDirectory { get; private set; }
        public string? Args { get; private set; }

        public Either<LaunchFailed, System.Diagnostics.Process> Launch(string workingDirectory, string args) {
            WorkingDirectory = workingDirectory;
            Args = args;

            // Return a failure so `UnchainedChivalry2Launcher` doesn't attempt DLL injection (which would require a real process).
            return Prelude.Left(new LaunchFailed(Executable, args, Error.New("test")));
        }
    }

    private sealed class NoOpProcessInjector : IProcessInjector {
        public bool Inject(System.Diagnostics.Process process) => true;
    }

    private static LaunchOptions CreateLaunchOptions(string rawLaunchArgs) {
        var serverLaunchOptions = new ServerLaunchOptions(
            Headless: false,
            Name: "Test Server",
            Description: "Test Description",
            RegisterWithBackend: false,
            Password: Option<string>.None,
            Map: "TestMap",
            GamePort: 7777,
            BeaconPort: 15000,
            QueryPort: 27015,
            RconPort: 9000,
            FFATimeLimit: null,
            FFAScoreLimit: 25,
            TDMTimeLimit: null,
            TDMTicketCount: null,
            PlayerBotCount: null,
            WarmupTime: null,
            LocalIp: Option<string>.None,
            ServerMods: []
        );

        return new LaunchOptions(
            EnabledReleases: Array.Empty<ReleaseCoordinates>(),
            ServerBrowserBackend: Option<string>.None,
            LaunchArgs: rawLaunchArgs,
            CheckForDependencyUpdates: false,
            SavedDirSuffix: Option<string>.None,
            ServerLaunchOptions: Prelude.Some(serverLaunchOptions)
        );
    }

    [Fact]
    public async Task TryLaunch_WhenRawArgsContainsTBL_ShouldAppendMapUriStringToEndOfRawArgs() {
        var preparer = Chivalry2LaunchPreparer.Create<LaunchOptions>(opts => Prelude.Some(opts));
        var capturingLauncher = new CapturingProcessLauncher();
        var launcher = new UnchainedChivalry2Launcher(
            preparer,
            capturingLauncher,
            installationRootDir: "C:\\TestInstall",
            processInjector: new NoOpProcessInjector());

        var opts = CreateLaunchOptions("TBL");
        await launcher.TryLaunch(opts);

        capturingLauncher.Args.Should().NotBeNull();
        capturingLauncher.Args!.Should().Contain("TBL?FFAScoreLimit=25");
    }

    [Fact]
    public async Task TryLaunch_WhenRawArgsDoesNotContainTBL_ShouldPrefixRawArgsWithTBLAndMapUriString() {
        var preparer = Chivalry2LaunchPreparer.Create<LaunchOptions>(opts => Prelude.Some(opts));
        var capturingLauncher = new CapturingProcessLauncher();
        var launcher = new UnchainedChivalry2Launcher(
            preparer,
            capturingLauncher,
            installationRootDir: "C:\\TestInstall",
            processInjector: new NoOpProcessInjector());

        var opts = CreateLaunchOptions("-someOtherArg");
        await launcher.TryLaunch(opts);

        capturingLauncher.Args.Should().NotBeNull();
        capturingLauncher.Args!.Should().Contain("TBL?FFAScoreLimit=25 -someOtherArg");
    }

    [Fact]
    public async Task TryLaunch_WhenRawArgsContainsTBLAndOthers_ShoulSuffixTBLWithMapUriString() {
        var preparer = Chivalry2LaunchPreparer.Create<LaunchOptions>(opts => Prelude.Some(opts));
        var capturingLauncher = new CapturingProcessLauncher();
        var launcher = new UnchainedChivalry2Launcher(
            preparer,
            capturingLauncher,
            installationRootDir: "C:\\TestInstall",
            processInjector: new NoOpProcessInjector());

        var opts = CreateLaunchOptions("--foo TBL -someOtherArg");
        await launcher.TryLaunch(opts);

        capturingLauncher.Args.Should().NotBeNull();
        capturingLauncher.Args!.Should().Contain("--foo TBL?FFAScoreLimit=25 -someOtherArg");
    }
}