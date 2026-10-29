using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class ServerProfileTests : IDisposable {
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "server-profile-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose() {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private CliLaunchOptions Request(string command) => CliLaunchOptions.Parse([
        command, "--chivalry2-path", _directory, "--platform", "Steam", "--saved-dir-suffix", "ServerProfileTest",
        .. command == "server" ? new[] { "--local-ip", "127.0.0.1" } : []
    ]);

    [Fact]
    public void ServerCreatesStartupSettingsInTheSelectedProfile() {
        ServerLauncher.InitializeServerProfile(Request("server"), suffix => {
            Assert.Equal("ServerProfileTest", suffix);
            return _directory;
        });

        var ini = File.ReadAllText(Path.Combine(_directory, "Game.ini"));
        Assert.Contains("[/Script/TBL.TBLTitleScreen]", ini);
        Assert.Contains("bSavedHasAgreedToTOS=True", ini);
        Assert.Contains("[/Script/TBL.TBLGameInstance]", ini);
        Assert.Contains("FirstLoadCompleted=True", ini);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void ServerInitializesTheGameObservedHyphenStrippedProfile() {
        var request = CliLaunchOptions.Parse(["server", "--chivalry2-path", _directory, "--platform", "Steam",
            "--saved-dir-suffix", "Host-court-yard", "--local-ip", "127.0.0.1"]);
        ServerLauncher.InitializeServerProfile(request, suffix => {
            Assert.Equal("Hostcourtyard", suffix);
            return _directory;
        });
        var ini = File.ReadAllText(Path.Combine(_directory, "Game.ini"));
        Assert.Contains("bSavedHasAgreedToTOS=True", ini);
        Assert.Contains("FirstLoadCompleted=True", ini);
    }

    [Fact]
    public void HyphenOnlyServerProfileIsRejected() {
        Assert.Throws<ArgumentException>(() => CliLaunchOptions.Parse(["server", "--chivalry2-path", _directory,
            "--platform", "Steam", "--saved-dir-suffix", "---", "--local-ip", "127.0.0.1"]));
    }

    [Fact]
    public void ServerUpdatesStartupSettingsWithoutChangingOtherSettingsAndIsIdempotent() {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "Game.ini");
        File.WriteAllText(path, """
            ; custom server settings
            [/Script/TBL.TBLGameMode]
            ServerName=Keep me
            MapList=FFA_Courtyard
            MapList=FFA_Wardenglade
            [/Script/TBL.TBLTitleScreen]
            bSavedHasAgreedToTOS=False
            CustomTitleSetting=42
            [/Script/TBL.TBLGameInstance]
            FirstLoadCompleted=False
            CustomInstanceSetting=Keep this too
            """);
        File.WriteAllText(Path.Combine(_directory, "Engine.ini"), "engine untouched");
        File.WriteAllText(Path.Combine(_directory, "GameUserSettings.ini"), "player untouched");

        ServerLauncher.InitializeServerProfile(Request("server"), _ => _directory);
        var ini = File.ReadAllText(path);
        Assert.Contains("bSavedHasAgreedToTOS=True", ini);
        Assert.Contains("FirstLoadCompleted=True", ini);
        Assert.DoesNotContain("=False", ini);
        foreach (var setting in new[] { "; custom server settings", "ServerName=Keep me", "MapList=FFA_Courtyard",
            "MapList=FFA_Wardenglade", "CustomTitleSetting=42", "CustomInstanceSetting=Keep this too" })
            Assert.Contains(setting, ini);
        Assert.Equal("engine untouched", File.ReadAllText(Path.Combine(_directory, "Engine.ini")));
        Assert.Equal("player untouched", File.ReadAllText(Path.Combine(_directory, "GameUserSettings.ini")));

        ServerLauncher.InitializeServerProfile(Request("server"), _ => _directory);
        Assert.Equal(ini, File.ReadAllText(path));
    }

    [Fact]
    public void ClientDoesNotResolveOrCreateAProfile() {
        ServerLauncher.InitializeServerProfile(Request("client"), _ => throw new InvalidOperationException("Client must not initialize a profile."));
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void ServerPropagatesProfileWriteFailure() {
        Directory.CreateDirectory(_directory);
        var blocked = Path.Combine(_directory, "blocked");
        File.WriteAllText(blocked, "not a directory");
        Assert.ThrowsAny<IOException>(() => ServerLauncher.InitializeServerProfile(Request("server"), _ => blocked));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComposedPatchesPreserveDefaultsNeighborsAndArraySemantics(bool rotation) {
        Directory.CreateDirectory(_directory);
        var config = Path.Combine(_directory, "launch.yaml");
        File.WriteAllText(config, """
            chiv2-path: .
            platform: Steam
            server:
              local-ip: 127.0.0.1
              ini:
                game:
                  /script/tbl.tbltitlescreen:
                    bSavedHasAgreedToTOS: false
                  /Script/TBL.TBLGameInstance:
                    Custom: {type: String, value: selected}
                  /Script/TBL.TBLGameMode:
                    ServerName: selected name
                    Empty: {type: 'String[]', value: []}
                    Repeats: {type: 'String[]', value: [A, A, B]}
                    RawOnly: {type: raw, value: secret}
                Engine:
                  Custom:
                    Selected: {type: Int, value: 7}
            """ + (rotation ? "\n  map-rotation:\n    - {map-name: FFA_New, name: First round}\n    - {map-name: FFA_New, name: Second round}\n" : ""));
        File.WriteAllText(Path.Combine(_directory, "Game.ini"), """
            [/Script/TBL.TBLTitleScreen]
            Neighbor=title
            [/Script/TBL.TBLGameInstance]
            Neighbor=instance
            [/Script/TBL.TBLGameMode]
            Neighbor=mode
            .Empty=old
            +Repeats=old
            MapList=FFA_Old
            MapListIndex=3
            """);
        File.WriteAllText(Path.Combine(_directory, "Engine.ini"), "[Custom]\nNeighbor=engine\nSelected=1\n");
        File.WriteAllText(Path.Combine(_directory, "GameUserSettings.ini"), "untouched");
        var request = CliLaunchOptions.Parse(["server", "--config-path", config]);

        ServerLauncher.InitializeServerProfile(request, _ => _directory);

        var game = File.ReadAllText(Path.Combine(_directory, "Game.ini"));
        Assert.Contains("bSavedHasAgreedToTOS=False", game);
        Assert.DoesNotContain("bSavedHasAgreedToTOS=True", game);
        Assert.Contains("FirstLoadCompleted=True", game);
        Assert.Contains("Custom=selected", game);
        foreach (var neighbor in new[] { "title", "instance", "mode" }) Assert.Contains("Neighbor=" + neighbor, game);
        Assert.Contains("ServerName=selected name", game);
        Assert.Contains("!Empty=ClearArray", game);
        Assert.DoesNotContain(".Empty=", game);
        Assert.Contains("!Repeats=ClearArray", game);
        Assert.DoesNotContain("+Repeats=old", game);
        Assert.Equal(new[] { ".Repeats=A", ".Repeats=A", ".Repeats=B" },
            game.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.StartsWith(".Repeats=")));
        Assert.DoesNotContain("RawOnly", game);
        if (rotation) {
            Assert.Contains("!MapList=ClearArray", game);
            Assert.Equal(2, game.Split('\n').Count(line => line.TrimEnd('\r') == ".MapList=FFA_New"));
            Assert.Contains("MapListIndex=0", game);
            Assert.DoesNotContain("FFA_Old", game);
        }
        else {
            Assert.Contains("MapList=FFA_Old", game);
            Assert.Contains("MapListIndex=3", game);
        }
        var engine = File.ReadAllText(Path.Combine(_directory, "Engine.ini"));
        Assert.Contains("Neighbor=engine", engine);
        Assert.Contains("Selected=7", engine);
        Assert.Equal("untouched", File.ReadAllText(Path.Combine(_directory, "GameUserSettings.ini")));
        ServerLauncher.InitializeServerProfile(request, _ => _directory);
        Assert.Equal(game, File.ReadAllText(Path.Combine(_directory, "Game.ini")));
    }
}