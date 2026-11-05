using System.Xml.Linq;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

public class CardTextRenderingTests {
    [Fact]
    public void ShadowIsSeparateFromCardContent() {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Views", "CollapsibleCard.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var shadow = Assert.Single(document.Descendants(wpf + "Border")
            .Where(element => (string?)element.Attribute("IsHitTestVisible") == "False"));
        Assert.Empty(shadow.Elements());
        var content = Assert.Single(shadow.Parent!.Elements(wpf + "Border").Where(element => element != shadow));
        Assert.Equal("{x:Null}", (string?)content.Attribute("Effect"));
        Assert.Contains(content.Descendants(), element => element.Name == wpf + "ContentPresenter");
    }
}
