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
    public void ServerInitializesTheGameObservedHyphenStrippedProfile() {
        var request = CliLaunchOptions.Parse(["server", "--chivalry2-path", _directory, "--platform", "Steam",
            "--saved-dir-suffix", "Host-court-yard", "--local-ip", "127.0.0.1"]);
        ServerLauncher.InitializeServerProfile(request, suffix => {
            Assert.Equal("Hostcourtyard", suffix);
            return _directory;
        });
        Assert.True(File.Exists(Path.Combine(_directory, "Game.ini")));
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
