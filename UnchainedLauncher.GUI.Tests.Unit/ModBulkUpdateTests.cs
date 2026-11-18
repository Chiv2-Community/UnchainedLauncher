using Moq;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.ViewModels;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

public class ModBulkUpdateTests {
    [Fact]
    public async Task ConfirmationKeepsItsSnapshotWhenDisplayedCardsChange() {
        var fixture = new Fixture();
        fixture.OnPrompt = updates => {
            var confirmed = updates.ToArray();
            Assert.Equal(new[] { "Alpha", "Beta" }, confirmed.Select(update => update.Name));
            fixture.List.DisplayMods.Clear();
            Assert.Equal(confirmed, updates.ToArray());
            return UserDialogueChoice.Yes;
        };

        await fixture.List.UpdateModsCommand.ExecuteAsync(null);

        fixture.AssertEachUpdatedOnce();
        Assert.Equal(new[] { "Mods updated successfully" }, fixture.Messages);
    }

    [Fact]
    public async Task UpdatingOneCandidateCannotInvalidateTheRemainingCandidates() {
        var fixture = new Fixture();
        fixture.OnEnable = _ => {
            fixture.List.SearchTerm = "no matching mods";
            return true;
        };

        await fixture.List.UpdateModsCommand.ExecuteAsync(null);

        Assert.Empty(fixture.List.DisplayMods);
        fixture.AssertEachUpdatedOnce();
        Assert.Equal(new[] { "Mods updated successfully" }, fixture.Messages);
    }

    [Fact]
    public async Task FailureReportingDoesNotRepeatUpdatesOrLoseResultsAfterMutation() {
        var fixture = new Fixture();
        fixture.OnEnable = coordinates => {
            fixture.List.DisplayMods.Clear();
            return coordinates == fixture.Latest[1];
        };

        await fixture.List.UpdateModsCommand.ExecuteAsync(null);

        fixture.AssertEachUpdatedOnce();
        Assert.Equal(new[] {
            "Failed to enable mods: Alpha\n\nCheck the logs for more details.",
            "Mods updated successfully"
        }, fixture.Messages);
    }

    [Theory]
    [InlineData(UserDialogueChoice.No)]
    [InlineData(UserDialogueChoice.Cancel)]
    public async Task DecliningDoesNotUpdateMods(UserDialogueChoice choice) {
        var fixture = new Fixture();
        fixture.OnPrompt = updates => {
            Assert.Equal(2, updates.Count());
            return choice;
        };

        await fixture.List.UpdateModsCommand.ExecuteAsync(null);

        Assert.Empty(fixture.Attempts);
        Assert.Equal(new[] { "Mods not updated" }, fixture.Messages);
    }

    [Fact]
    public async Task NoUpdatesUsesDialogueServiceWithoutEnablingAnything() {
        var fixture = new Fixture(alreadyUpdated: true);
        fixture.OnPrompt = updates => {
            Assert.Empty(updates);
            return null;
        };

        await fixture.List.UpdateModsCommand.ExecuteAsync(null);

        Assert.Empty(fixture.Attempts);
        Assert.Equal(new[] { "No updates available." }, fixture.Messages);
    }

    [Fact]
    public async Task UpdateExceptionIsReportedThroughDialogueService() {
        var fixture = new Fixture();
        fixture.OnEnable = _ => throw new IOException("Cannot enable release");

        await fixture.List.UpdateModsCommand.ExecuteAsync(null);

        Assert.Single(fixture.Attempts);
        Assert.Equal(new[] { "Failed to check for updates. Check the logs for details." }, fixture.Messages);
    }

    [Fact]
    public async Task SuccessPreservesFiltersAndExpansionAcrossRefresh() {
        var fixture = new Fixture();
        fixture.List.ShowEnabledOnly = true;
        fixture.List.SearchTerm = "Alpha";
        fixture.List.CollapseAllCommand.Execute(null);

        await fixture.List.UpdateModsCommand.ExecuteAsync(null);

        Assert.True(fixture.List.ShowEnabledOnly);
        Assert.Equal("Alpha", fixture.List.SearchTerm);
        Assert.False(Assert.Single(fixture.List.DisplayMods).IsExpanded);
        Assert.Equal(new[] { fixture.Latest[0] }, fixture.Attempts);
        fixture.List.SearchTerm = null;
        Assert.All(fixture.List.DisplayMods, card => Assert.False(card.IsExpanded));
        Assert.Equal(new[] { "Mods updated successfully" }, fixture.Messages);
    }

    private sealed class Fixture {
        public ModListVM List { get; }
        public ReleaseCoordinates[] Latest { get; }
        public List<ReleaseCoordinates> Attempts { get; } = [];
        public List<string> Messages { get; } = [];
        public Func<IEnumerable<DependencyUpdate>, UserDialogueChoice?> OnPrompt { get; set; } =
            _ => UserDialogueChoice.Yes;
        public Func<ReleaseCoordinates, bool> OnEnable { get; set; } = _ => true;

        public Fixture(bool alreadyUpdated = false) {
            var mods = new[] { CreateMod("Alpha"), CreateMod("Beta") };
            Latest = mods.Select(mod => ReleaseCoordinates.FromRelease(mod.Releases[0])).ToArray();
            var manager = new Mock<IModManager>();
            manager.SetupGet(m => m.Mods).Returns(mods);
            manager.SetupGet(m => m.EnabledModReleaseCoordinates).Returns(mods
                .Select(mod => ReleaseCoordinates.FromRelease(mod.Releases[alreadyUpdated ? 0 : 1])).ToArray());
            manager.Setup(m => m.UpdateModsList()).ReturnsAsync(new GetAllModsResult([], mods));
            manager.Setup(m => m.EnableModRelease(It.IsAny<ReleaseCoordinates>()))
                .Returns((ReleaseCoordinates coordinates) => {
                    Attempts.Add(coordinates);
                    return OnEnable(coordinates);
                });
            var dialogue = new Mock<IUserDialogueSpawner>();
            dialogue.Setup(d => d.DisplayMessage(It.IsAny<string>()))
                .Callback((string message) => Messages.Add(message));
            dialogue.Setup(d => d.DisplayUpdateMessage(It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                    It.IsAny<IEnumerable<DependencyUpdate>>()))
                .Returns((string _, string _, string _, string _, string? _, IEnumerable<DependencyUpdate> updates) =>
                    OnPrompt(updates));
            List = new ModListVM(manager.Object, dialogue.Object);
        }

        public void AssertEachUpdatedOnce() => Assert.Equal(Latest, Attempts);

        private static Mod CreateMod(string name) {
            var info = new ModInfo($"https://github.com/test/{name}", name, "Description", null, null, [], []);
            var latest = new Release("v2.0.0", "hash", "mod.pak", new DateTime(2026, 1, 2),
                info, new([], [], [], [], []), null);
            return new Mod(info, [latest, latest with { Tag = "v1.0.0", ReleaseDate = new DateTime(2026, 1, 1) }]);
        }
    }
}
