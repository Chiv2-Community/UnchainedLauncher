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
}
