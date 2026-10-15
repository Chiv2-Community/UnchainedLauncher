using System.Xml.Linq;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

public sealed class PluginReleaseNotesCardTests {
    [Fact]
    public void ReleaseNotesUseSharedCollapsibleCardAndRetainBindings() {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.xaml"));
        XNamespace controls = "clr-namespace:UnchainedLauncher.GUI.Views.Controls";
        var card = Assert.Single(document.Descendants(controls + "CollapsibleCard")
            .Where(element => (string?)element.Attribute("Header") == "Release notes"));
        Assert.Equal("False", (string?)card.Attribute("IsExpanded"));
        var body = Assert.Single(card.Elements(controls + "CollapsibleCard.Body"));
        Assert.Contains(body.Descendants(), element => (string?)element.Attribute("Text") == "{Binding ReleaseNotes}");
        Assert.Contains(body.Descendants(), element => (string?)element.Attribute("Command") == "{Binding OpenGithubReleaseCommand}");
    }
}
