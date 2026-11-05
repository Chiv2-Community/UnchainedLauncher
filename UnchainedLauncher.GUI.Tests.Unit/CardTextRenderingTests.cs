using System.Xml.Linq;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

public class CardTextRenderingTests {
    [Fact]
    public void ShadowIsSeparateFromCardContent() {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Views", "CollapsibleCard.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var root = document.Root!.Element(wpf + "Grid");
        Assert.NotNull(root);
        var borders = root!.Elements().ToArray();
        Assert.Equal(2, borders.Length);
        Assert.All(borders, border => {
            Assert.Equal(wpf + "Border", border.Name);
            Assert.Equal("{DynamicResource Card}", (string?)border.Attribute("Style"));
        });
        var shadow = borders[0];
        Assert.Equal("False", (string?)shadow.Attribute("IsHitTestVisible"));
        Assert.Equal("0", (string?)shadow.Attribute("Padding"));
        Assert.Empty(shadow.Elements());
        var content = borders[1];
        Assert.Equal("{x:Null}", (string?)content.Attribute("Effect"));
        Assert.Equal("0", (string?)content.Attribute("Padding"));
        Assert.Null(content.Attribute("IsHitTestVisible"));
        var toggle = Assert.Single(content.Descendants(wpf + "ToggleButton"));
        Assert.Equal("{Binding IsExpanded, ElementName=Root, Mode=TwoWay}", (string?)toggle.Attribute("IsChecked"));
        var body = Assert.Single(content.Descendants(wpf + "ContentPresenter")
            .Where(element => (string?)element.Attribute("Content") == "{Binding Body, ElementName=Root}"));
        Assert.Equal("12,0,12,12", (string?)body.Attribute("Margin"));
        Assert.Equal("{Binding IsExpanded, ElementName=Root, Converter={StaticResource BoolToVisibilityConverter}}",
            (string?)body.Attribute("Visibility"));
    }
}
