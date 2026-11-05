using System.ComponentModel;
using System.Xml.Linq;
using Moq;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.GUI.ViewModels;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

public class ModCardExpansionTests {
    [Fact]
    public void CardsHaveRightSpacingInsideTheScrollViewer() {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Views", "ModList.xaml"));
        var cards = Assert.Single(document.Descendants().Where(element =>
            (string?)element.Attribute("ItemsSource") == "{Binding DisplayMods}"));
        Assert.Equal("ScrollViewer", cards.Parent!.Name.LocalName);
        Assert.Equal("0,0,12,0", (string?)cards.Attribute("Margin"));
    }

    [Fact]
    public void IndividualExpansionNotifiesBindingsWithoutChangingOtherCardsOrEnabledRelease() {
        var (list, manager) = CreateList();
        var first = list.DisplayMods[0];
        var second = list.DisplayMods[1];
        var release = first.EnabledRelease;
        var changes = new List<string?>();
        ((INotifyPropertyChanged)first).PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        Assert.True(first.IsExpanded);
        first.IsExpanded = false;
        Assert.False(first.IsExpanded);
        Assert.True(second.IsExpanded);
        first.IsExpanded = true;
        first.IsExpanded = true;

        Assert.Equal(new[] { nameof(ModVM.IsExpanded), nameof(ModVM.IsExpanded) }, changes);
        Assert.Same(release, first.EnabledRelease);
        manager.Verify(m => m.EnableModRelease(It.IsAny<ReleaseCoordinates>()), Times.Never);
        manager.Verify(m => m.DisableMod(It.IsAny<ModIdentifier>()), Times.Never);
    }

    [Theory]
    [InlineData("Alpha")]
    [InlineData("no matching mods")]
    public void BulkCommandsIncludeFilteredCardsAndCanBeRepeated(string search) {
        var (list, manager) = CreateList();
        var cards = list.DisplayMods.ToArray();
        cards[0].IsExpanded = false;
        var changes = new List<ModVM>();
        foreach (var card in cards)
            ((INotifyPropertyChanged)card).PropertyChanged += (_, args) => {
                if (args.PropertyName == nameof(ModVM.IsExpanded)) changes.Add(card);
            };
        list.SearchTerm = search;

        list.CollapseAllCommand.Execute(null);
        list.CollapseAllCommand.Execute(null);
        Assert.All(cards, card => Assert.False(card.IsExpanded));
        list.ExpandAllCommand.Execute(null);
        list.ExpandAllCommand.Execute(null);
        Assert.All(cards, card => Assert.True(card.IsExpanded));
        Assert.Equal(new[] { cards[1], cards[0], cards[1] }, changes);
        manager.Verify(m => m.EnableModRelease(It.IsAny<ReleaseCoordinates>()), Times.Never);
        manager.Verify(m => m.DisableMod(It.IsAny<ModIdentifier>()), Times.Never);
        Assert.Equal(search, list.SearchTerm);
        list.SearchTerm = null;
        Assert.Equal(cards, list.DisplayMods);
    }

    [Fact]
    public void FilteringAndSortingPreserveIndividualExpansion() {
        var (list, _) = CreateList();
        var enabled = list.DisplayMods[0];
        var disabled = list.DisplayMods[1];
        disabled.IsExpanded = false;

        list.ShowEnabledOnly = true;
        Assert.Same(enabled, Assert.Single(list.DisplayMods));
        list.ShowEnabledOnly = false;
        list.SearchTerm = "Beta";
        Assert.Same(disabled, Assert.Single(list.DisplayMods));
        list.SelectedSortMode = ModSortMode.LatestReleaseDateFirst;
        list.SearchTerm = null;

        Assert.Same(disabled, list.DisplayMods[0]);
        Assert.False(disabled.IsExpanded);
        Assert.Same(enabled, list.DisplayMods[1]);
        Assert.True(enabled.IsExpanded);
    }

    [Fact]
    public async Task RefreshPreservesHiddenAndVisibleStatesByIdentityAndExpandsNewMods() {
        var (list, manager) = CreateList();
        list.CollapseAllCommand.Execute(null);
        list.DisplayMods[0].IsExpanded = true;
        list.SearchTerm = "Alpha";
        list.SelectedSortMode = ModSortMode.Alphabetical;
        var refreshed = new[] { CreateMod("Beta", "Renamed Beta", 3), CreateMod("Alpha", "Alpha", 4), CreateMod("Gamma", "Gamma", 5) };
        manager.Setup(m => m.UpdateModsList()).ReturnsAsync(() => {
            manager.SetupGet(m => m.Mods).Returns(refreshed);
            return new GetAllModsResult([], refreshed);
        });

        await list.RefreshModListCommand.ExecuteAsync(null);

        Assert.Equal("Alpha", list.SearchTerm);
        Assert.Equal(ModSortMode.Alphabetical, list.SelectedSortMode);
        Assert.True(Assert.Single(list.DisplayMods).IsExpanded);
        list.SearchTerm = null;
        Assert.True(list.DisplayMods.Single(card => card.Mod.LatestReleaseInfo.Name == "Alpha").IsExpanded);
        Assert.False(list.DisplayMods.Single(card => card.Mod.LatestReleaseInfo.Name == "Renamed Beta").IsExpanded);
        Assert.True(list.DisplayMods.Single(card => card.Mod.LatestReleaseInfo.Name == "Gamma").IsExpanded);
    }

    [Fact]
    public async Task FailedRefreshLeavesExpansionUntouched() {
        var (list, manager) = CreateList();
        list.CollapseAllCommand.Execute(null);
        var cards = list.DisplayMods.ToArray();
        manager.Setup(m => m.UpdateModsList()).ThrowsAsync(new IOException("Offline"));

        await list.RefreshModListCommand.ExecuteAsync(null);

        Assert.Equal(cards, list.DisplayMods);
        Assert.All(list.DisplayMods, card => Assert.False(card.IsExpanded));
    }

    [Fact]
    public void EmptyListSupportsBothBulkCommands() {
        var manager = new Mock<IModManager>();
        manager.SetupGet(m => m.Mods).Returns(Array.Empty<Mod>());
        var list = new ModListVM(manager.Object, new RecordingDialogue());
        list.CollapseAllCommand.Execute(null);
        list.ExpandAllCommand.Execute(null);
        Assert.Empty(list.DisplayMods);
    }

    private static (ModListVM List, Mock<IModManager> Manager) CreateList() {
        var mods = new[] { CreateMod("Alpha", "Alpha", 1), CreateMod("Beta", "Beta", 2) };
        var manager = new Mock<IModManager>();
        manager.SetupGet(m => m.Mods).Returns(mods);
        manager.SetupGet(m => m.EnabledModReleaseCoordinates).Returns([ReleaseCoordinates.FromRelease(mods[0].Releases[0])]);
        return (new ModListVM(manager.Object, new RecordingDialogue()), manager);
    }

    private static Mod CreateMod(string repository, string name, int releaseDay) {
        var info = new ModInfo($"https://github.com/test/{repository}", name, "Description", null, null, [], []);
        var release = new Release("v1.0.0", "hash", "mod.pak", new DateTime(2026, 1, releaseDay), info, new([], [], [], [], []), null);
        return new Mod(info, [release]);
    }

    [Fact]
    public void CollapsingDetailsKeepsEnableAndVersionControlsVisible() {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Views", "Mod.xaml"));
        var card = Assert.Single(document.Descendants().Where(element =>
            element.Name.LocalName == "CollapsibleCard" &&
            (string?)element.Attribute("IsExpanded") == "{Binding IsExpanded, Mode=TwoWay}"));
        Assert.Equal("False", (string?)card.Attribute("ShowExpansionIndicator"));
        Assert.Equal("True", (string?)card.Attribute("HighlightOnHover"));
        Assert.Equal("True", (string?)card.Attribute("ShowHeaderSeparator"));
        var body = Assert.Single(card.Elements().Where(element => element.Name.LocalName == "CollapsibleCard.Body"));
        var header = Assert.Single(card.Elements().Where(element => element.Name.LocalName == "CollapsibleCard.Header"));
        Assert.Contains(header.Descendants(), element => (string?)element.Attribute("Source") == "{Binding IconUrl}");
        Assert.Contains(header.Descendants(), element => (string?)element.Attribute("Command") == "{Binding ViewOnGithubCommand}");
        Assert.Contains(header.Descendants(), element => (string?)element.Attribute("Command") == "{Binding EnableOrDisableCommand}");
        Assert.Contains(header.Descendants(), element => (string?)element.Attribute("SelectedItem") == "{Binding EnabledRelease, Mode=TwoWay}");
        Assert.Contains(body.Descendants(), element => (string?)element.Attribute("Text") == "{Binding Description, Mode=OneWay}");
        Assert.Contains(body.Descendants(), element => (string?)element.Attribute("Header") == "Contents");
        Assert.Contains(body.Descendants(), element => (string?)element.Attribute("Header") == "Dependencies");
        Assert.DoesNotContain(body.Descendants(), element => element.Attribute("Command")?.Value == "{Binding EnableOrDisableCommand}");
        Assert.DoesNotContain(body.Descendants(), element => element.Attribute("SelectedItem")?.Value == "{Binding EnabledRelease, Mode=TwoWay}");
        Assert.DoesNotContain(header.Descendants(), element => element.Name.LocalName == "ToggleButton");
    }
}
