using System.Xml.Linq;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

public class ProgressPresentationTests {
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    internal static XDocument Load(string name) => XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Views", name + ".xaml"));

    [Fact]
    public void ProgressUsesThemedTrackAndRotatesVerticalFill() {
        var style = Load("Misc").Descendants(Wpf + "Style").Single(e => (string?)e.Attribute("TargetType") == "ProgressBar");
        foreach (var key in new[] { "Brush.InputBackground", "Brush.Primary", "Brush.InputBorder" })
            Assert.Contains("{DynamicResource " + key + "}", style.ToString());
        Assert.Contains(style.Descendants(Wpf + "Grid"), e => (string?)e.Attribute(X + "Name") == "PART_Track");
        Assert.Contains(style.Descendants(Wpf + "Border"), e => (string?)e.Attribute(X + "Name") == "PART_Indicator");
        Assert.Contains(style.Descendants(Wpf + "RotateTransform"), e => (string?)e.Attribute("Angle") == "-90");
        Assert.Single(style.Descendants(Wpf + "RemoveStoryboard"));
    }

    [Fact]
    public void DisabledButtonsAndSettingsDividerUseThemeResources() {
        var disabled = Load("Buttons").Descendants(Wpf + "Trigger")
            .Single(e => (string?)e.Attribute("Property") == "IsEnabled" && (string?)e.Attribute("Value") == "False");
        Assert.Contains("{DynamicResource Brush.Muted}", disabled.ToString());
        Assert.DoesNotContain("#FFADB5BD", disabled.ToString());
        Assert.Contains(Load("Settings").Descendants(Wpf + "Border"), e =>
            (string?)e.Attribute("BorderThickness") == "0,0,0,1" &&
            (string?)e.Attribute("BorderBrush") == "{DynamicResource Brush.Border}");
    }

    [Fact]
    public void TaskLabelsWrapAndDoNotCaptureThemeBrushes() {
        var display = Load("MemoryProgressDisplay");
        var labels = display.Descendants(Wpf + "TextBlock").Where(e => ((string?)e.Attribute("Text"))?.Contains("TaskName") == true).ToArray();
        Assert.Equal(2, labels.Length);
        Assert.All(labels, e => {
            Assert.Equal("Wrap", (string?)e.Attribute("TextWrapping"));
            Assert.Equal("560", (string?)e.Attribute("MaxWidth"));
            Assert.Equal("{DynamicResource Brush.TextPrimary}", (string?)e.Attribute("Foreground"));
        });
        Assert.DoesNotContain("{StaticResource Brush.", display.ToString());
    }

    [Theory]
    [InlineData("Button.EnableOrDisable")]
    [InlineData("Release.PrereleaseBadge")]
    [InlineData("PluginCard")]
    public void MainOnlyStylesSurvive(string key) =>
        Assert.Contains(Load("Misc").Descendants(Wpf + "Style"), e => (string?)e.Attribute(X + "Key") == key);
}
