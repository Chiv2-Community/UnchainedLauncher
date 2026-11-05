using System.Xml.Linq;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

public class ModCardExpansionTests {
    [Fact]
    public void CollapsingDetailsKeepsEnableAndVersionControlsVisible() {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Views", "Mod.xaml"));
        var body = Assert.Single(document.Descendants().Where(element =>
            (string?)element.Attribute("Visibility") == "{Binding IsExpanded, Converter={StaticResource BoolToVisibilityConverter}}"));
        Assert.Contains(body.Descendants(), element => (string?)element.Attribute("Text") == "{Binding Description, Mode=OneWay}");
        Assert.DoesNotContain(body.Descendants(), element => element.Attribute("Command")?.Value == "{Binding EnableOrDisableCommand}");
        Assert.DoesNotContain(body.Descendants(), element => element.Attribute("SelectedItem")?.Value == "{Binding EnabledRelease, Mode=TwoWay}");
        Assert.Contains(document.Descendants(), element => element.Attribute("IsChecked")?.Value == "{Binding IsExpanded, Mode=TwoWay}");
    }
}
