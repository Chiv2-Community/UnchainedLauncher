using System.Xml.Linq;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

public class DropdownPresentationTests {
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Behavior = "clr-namespace:UnchainedLauncher.GUI.Behaviors";
    private static XDocument Load(string name) => ProgressPresentationTests.Load(name);

    [Fact]
    public void SearchResultsPreserveTemplatesAndUseThemeSelection() {
        var inputs = Load("Inputs");
        var results = inputs.Descendants(Wpf + "ListBox").Single(e => (string?)e.Attribute(X + "Name") == "PART_SearchResults");
        foreach (var property in new[] { "ItemTemplate", "ItemTemplateSelector", "DisplayMemberPath" })
            Assert.Equal("{TemplateBinding " + property + "}", (string?)results.Attribute(property));
        var style = inputs.Descendants(Wpf + "Style").Single(e => (string?)e.Attribute(X + "Key") == "SearchResultItemBase");
        Assert.Contains("{DynamicResource Brush.PrimaryTransparent}", style.ToString());
        Assert.Contains("{DynamicResource Brush.TextPrimary}", style.ToString());
        Assert.Contains("InputOptions.IsHoverHighlightEnabled", inputs.ToString());
    }

    [Fact]
    public void PluginAndPakReleaseSearchUsesDisplayedVersionLabelsWithoutLosingBadges() {
        var plugin = Load("MainWindow").Descendants(Wpf + "ComboBox")
            .Single(e => (string?)e.Attribute("ItemsSource") == "{Binding AvailableVersions}");
        Assert.Equal("Version", (string?)plugin.Attribute("TextSearch.TextPath"));
        var pak = Load("Mod").Descendants(Wpf + "ComboBox").Single();
        Assert.Equal("Tag", (string?)pak.Attribute("TextSearch.TextPath"));
        foreach (var selector in new[] { plugin, pak })
            Assert.Contains("Release.PrereleaseBadge", selector.ToString());
    }

    [Fact]
    public void PluginSortSearchUsesItsConverterParameter() {
        var sort = Load("MainWindow").Descendants(Wpf + "ComboBox")
            .Single(e => (string?)e.Attribute("ItemsSource") == "{Binding SortModes}");
        Assert.Equal("{StaticResource ModSortModeToStringConverter}", (string?)sort.Attribute(Behavior + "SearchableComboBoxBehavior.TextConverter"));
        Assert.Equal("Plugins", (string?)sort.Attribute(Behavior + "SearchableComboBoxBehavior.TextConverterParameter"));
    }

    [Theory]
    [InlineData("BaseConfigurationSectionView")]
    [InlineData("Server")]
    public void OnlyMapOptionResultsAreAlphabetized(string view) {
        var document = Load(view);
        var selector = document.Descendants(Wpf + "ComboBox")
            .Single(e => (string?)e.Attribute("ItemsSource") == "{Binding AvailableMaps}");
        Assert.Equal("True", (string?)selector.Attribute(Behavior + "SearchableComboBoxBehavior.SortAlphabetically"));
        Assert.Contains("MapToTravelStringConverter", (string?)selector.Attribute(Behavior + "SearchableComboBoxBehavior.TextConverter"));
        Assert.Single(document.Descendants().Attributes(Behavior + "SearchableComboBoxBehavior.SortAlphabetically"));
    }

    [Fact]
    public void InstallerEmphasisAndConvertedLabelsSurvive() {
        var version = Load("VersionSelectionView");
        var style = version.Descendants(Wpf + "Style").Single(e => (string?)e.Attribute(X + "Key") == "SearchResultItem");
        Assert.Equal("{x:Type ListBoxItem}", (string?)style.Attribute("TargetType"));
        Assert.Contains("IsLatestStable", style.ToString());
        Assert.Contains("Bold", style.ToString());
        Assert.Contains("InstallationTypeConverter", (string?)Load("InstallationTargetView").Descendants(Wpf + "ComboBox")
            .Single().Attribute(Behavior + "SearchableComboBoxBehavior.TextConverter"));
    }
}
