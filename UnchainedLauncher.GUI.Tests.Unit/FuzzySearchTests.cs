using UnchainedLauncher.GUI.Services;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit;

public class FuzzySearchTests {
    [Theory]
    [InlineData("TDM_Wardenglade", "td ward", true)]
    [InlineData("Epic Games Store", "egs", true)]
    [InlineData("Steam", "STM", true)]
    [InlineData("Version 1.2.3", "123", true)]
    [InlineData("Steam", "", true)]
    [InlineData("", "", true)]
    [InlineData("", " -_ ", true)]
    [InlineData("", "a", false)]
    [InlineData("Steam", "egs", false)]
    [InlineData("Steam", "ma", false)]
    [InlineData("Steam", "ss", false)]
    [InlineData("Latest New Releases First", "lnrf", true)]
    [InlineData("Newest Plugins First", "plugins", true)]
    public void MatchesOrderedCharactersIgnoringCaseAndSeparators(string text, string query, bool expected) =>
        Assert.Equal(expected, FuzzySearch.Matches(text, query));
}
