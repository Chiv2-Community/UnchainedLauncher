using Xunit;

namespace UnchainedLauncher.CLI.Tests;

public class ServerTabsTests {
    [Fact]
    public void ArrowNavigationWrapsAndClickSelectsVisibleTabs() {
        var tabs = new ServerTabs(["one", "two", "three"]);
        Assert.Equal(">one< [two] [three] ", tabs.Layout(80));
        tabs.Move(-1);
        Assert.Equal(2, tabs.Selected);
        tabs.Move(1);
        Assert.Equal(0, tabs.Selected);
        Assert.True(tabs.Click(8, 1));
        Assert.Equal(1, tabs.Selected);
        Assert.False(tabs.Click(8, 2));
        Assert.False(tabs.Click(80, 1));
    }

    [Fact]
    public void NarrowTabBarKeepsSelectedTabVisibleAndRebuildsClickTargets() {
        var tabs = new ServerTabs(["one", "two", "three"]);
        Assert.Equal(">one<", tabs.Layout(5));
        tabs.Move(2);
        Assert.Equal(">thre", tabs.Layout(5));
        Assert.True(tabs.Click(1, 1));
        Assert.Equal(2, tabs.Selected);
        tabs.Move(1);
        Assert.Equal(">one<", tabs.Layout(5));
    }

    [Theory]
    [InlineData("\u001b[<0;8;1M", true)]
    [InlineData("\u001b[<0;8;1m", false)]
    [InlineData("\u001b[<2;8;1M", false)]
    [InlineData("\u001b[<32;8;1M", false)]
    [InlineData("\u001b[<0;0;1M", false)]
    [InlineData("\u001b[<0;99999999999999999;1M", false)]
    public void MouseParserAcceptsOnlyPrimaryPressesWithValidCoordinates(string report, bool expected) {
        var parser = new TerminalMouseParser();
        (int Column, int Row)? result = null;
        foreach (var value in report) {
            Assert.True(parser.Feed(value, out var click));
            result = click;
        }
        Assert.Equal(expected, result.HasValue);
        if (expected) Assert.Equal((8, 1), result!.Value);
    }

    [Fact]
    public void IncompleteOrMalformedMouseInputDoesNotSwallowQuit() {
        var parser = new TerminalMouseParser();
        parser.Feed('\x1b', out _);
        Assert.False(parser.Feed('q', out _));
        Assert.False(parser.Feed('q', out _));
        foreach (var value in "\x1b[<" + new string('1', 70)) parser.Feed(value, out _);
        Assert.False(parser.Feed('q', out _));
    }
}