using UnchainedLauncher.GUI.Services;
using Xunit;

namespace UnchainedLauncher.GUI.Tests {
    public class FuzzySearchTests {
        [Theory]
        [InlineData("FFA_Courtyard", "", true)]
        [InlineData("FFA_Courtyard", "  ", true)]
        [InlineData("FFA_Courtyard", "COURT", true)]
        [InlineData("TDM_Wardenglade", "td ward", true)]
        [InlineData("FFA_Courtyard", "fcrtyd", true)]
        [InlineData("Latest Stable (1.0.2)", "ls102", true)]
        [InlineData("Epic Games Store", "egs", true)]
        [InlineData("Steam", "maets", false)]
        [InlineData("FFA_Courtyard", "courtyardx", false)]
        [InlineData("", "x", false)]
        [InlineData("", "", true)]
        public void MatchesOrderedCharactersIgnoringCaseAndSeparators(string label, string query, bool expected) {
            Assert.Equal(expected, FuzzySearch.Matches(label, query));
        }
    }
}
