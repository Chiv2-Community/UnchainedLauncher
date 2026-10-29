using System.Diagnostics;
using System.Text.Json;
using UnchainedLauncher.Core.JsonModels;
using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public sealed class EpicLaunchArgumentsTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "epic-auth-tests-" + Guid.NewGuid().ToString("N"));
    private const string Response = """{"egl_parameters":["-AUTH_LOGIN=unused","-AUTH_PASSWORD=private-token","-AUTH_TYPE=exchangecode","-epicapp=Peppermint","-epicusername=A Name With Spaces"]}""";

    [Fact]
    public void OnlyAutomaticallyAuthenticatesEpicWithoutExplicitCredentials() {
        Assert.True(EpicLaunchArguments.Required(InstallationType.EpicGamesStore, ["-epicapp=Peppermint"]));
        Assert.False(EpicLaunchArguments.Required(InstallationType.Steam, []));
        Assert.False(EpicLaunchArguments.Required(InstallationType.EpicGamesStore, ["-auth_password=explicit"]));
        Assert.False(EpicLaunchArguments.Required(InstallationType.EpicGamesStore, ["-AUTH_TYPE=exchangecode"]));
    }

    [Fact]
    public void PreservesArgumentBoundariesAndSpaces() {
        Assert.Equal("-epicusername=A Name With Spaces", EpicLaunchArguments.Parse(Response)[4]);
    }

    [Theory]
    [InlineData("private-token")]
    [InlineData("{\"egl_parameters\":[null]}")]
    [InlineData("{\"egl_parameters\":[123]}")]
    [InlineData("{\"egl_parameters\":[]}")]
    [InlineData("{\"egl_parameters\":[\"-AUTH_PASSWORD=private-token\",\"-epicapp=OtherGame\"]}")]
    [InlineData("{\"egl_parameters\":[\"-AUTH_PASSWORD=private-token\",\"-epicapp=Peppermint\",\"bad\\nvalue\"]}")]
    public void RejectsInvalidResponsesWithoutLeakingOutput(string json) {
        var error = Assert.Throws<InvalidOperationException>(() => EpicLaunchArguments.Parse(json));
        Assert.DoesNotContain("private-token", error.ToString());
        Assert.Contains("after --", error.Message);
    }

    private string InstallHeroic(bool flatpak) {
        var root = Path.Combine(_root, flatpak ? ".var/app/com.heroicgameslauncher.hgl/config/heroic" : ".config/heroic");
        var game = Path.Combine(_root, "game");
        Directory.CreateDirectory(game);
        Directory.CreateDirectory(Path.Combine(root, "legendaryConfig/legendary"));
        File.WriteAllText(Path.Combine(root, "legendaryConfig/legendary/installed.json"),
            JsonSerializer.Serialize(new { Peppermint = new { install_path = game } }));
        return root;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusesTheMatchingHeroicLogin(bool flatpak) {
        var heroic = InstallHeroic(flatpak);
        var info = EpicLaunchArguments.StartInfo(Path.Combine(_root, "game"), _ => null, _root);
        Assert.Equal(flatpak ? "flatpak" : "legendary", info.FileName);
        Assert.Equal(Path.Combine(heroic, "legendaryConfig/legendary"), info.Environment["LEGENDARY_CONFIG_PATH"]);
        Assert.Equal(new[] { "launch", "Peppermint", "--json", "--skip-version-check" }, info.ArgumentList.TakeLast(4));
        Assert.True(info.RedirectStandardInput && info.RedirectStandardOutput && info.RedirectStandardError);
        if (flatpak) Assert.Contains("--env=LEGENDARY_CONFIG_PATH=" + info.Environment["LEGENDARY_CONFIG_PATH"], info.ArgumentList);
    }

    [Fact]
    public void ExplicitLegendaryConfigOverridesHeroicLogin() {
        InstallHeroic(true);
        var info = EpicLaunchArguments.StartInfo(Path.Combine(_root, "game"),
            name => name == "LEGENDARY_CONFIG_PATH" ? "/explicit/config" : null, _root);
        Assert.Equal("/explicit/config", info.Environment["LEGENDARY_CONFIG_PATH"]);
        Assert.Contains("--env=LEGENDARY_CONFIG_PATH=/explicit/config", info.ArgumentList);
    }

    [Fact]
    public void DoesNotBorrowLoginFromAnUnrelatedHeroicInstallation() {
        InstallHeroic(true);
        Directory.CreateDirectory(Path.Combine(_root, "other-game"));
        var info = EpicLaunchArguments.StartInfo(Path.Combine(_root, "other-game"), _ => null, _root);
        Assert.Equal("legendary", info.FileName);
    }

    private static ProcessStartInfo Shell(string script) {
        var info = new ProcessStartInfo("/bin/sh") {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(script);
        return info;
    }

    [Fact]
    public async Task CapturesFreshArgumentsForEveryLaunchWithoutPrintingDiagnostics() {
        if (!OperatingSystem.IsLinux()) return;
        var info = Shell("printf '%s' '" + Response + "'; printf private-token >&2");
        Assert.Equal(EpicLaunchArguments.Parse(Response), await EpicLaunchArguments.RunAsync(info, CancellationToken.None));
        Assert.Equal(EpicLaunchArguments.Parse(Response), await EpicLaunchArguments.RunAsync(info, CancellationToken.None));
    }

    [Fact]
    public async Task AuthenticationFailureDoesNotLeakEitherStream() {
        if (!OperatingSystem.IsLinux()) return;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => EpicLaunchArguments.RunAsync(
            Shell("printf private-token; printf private-token >&2; exit 7"), CancellationToken.None));
        Assert.Contains("exit 7", error.Message);
        Assert.DoesNotContain("private-token", error.ToString());
    }

    [Fact]
    public async Task CancellationStopsTheAuthenticationProcess() {
        if (!OperatingSystem.IsLinux()) return;
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            EpicLaunchArguments.RunAsync(Shell("exec sleep 30"), stop.Token));
    }

    [Fact]
    public async Task MissingToolHasActionableCredentialFreeError() {
        var info = Shell("");
        info.FileName = Path.Combine(_root, "missing-legendary");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EpicLaunchArguments.RunAsync(info, CancellationToken.None));
        Assert.Contains("install Legendary", error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task OversizedOutputFailsWithoutWaitingForMoreOutput() {
        if (!OperatingSystem.IsLinux()) return;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => EpicLaunchArguments.RunAsync(
            Shell("head -c 1048577 /dev/zero; exec sleep 30"), stop.Token));
        Assert.Contains("size limit", error.Message);
    }

    [Fact]
    public async Task AuthenticationCannotPromptOnTheTuiInput() {
        if (!OperatingSystem.IsLinux()) return;
        var result = await EpicLaunchArguments.RunAsync(
            Shell("read ignored; printf '%s' '" + Response + "'"), CancellationToken.None);
        Assert.Equal(EpicLaunchArguments.Parse(Response), result);
    }

    public void Dispose() {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
