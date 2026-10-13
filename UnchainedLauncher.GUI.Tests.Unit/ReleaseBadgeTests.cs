using System.Xml.Linq;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using Xunit;

namespace UnchainedLauncher.GUI.Tests.Unit {
    public sealed class ReleaseBadgeTests {
        [Theory]
        [InlineData("v1.0.0-RC2", true)]
        [InlineData("v1.0.0-RC1", true)]
        [InlineData("v1.0.0-rc0", true)]
        [InlineData("v1.0.0", false)]
        public void ModBadgeUsesSemverPrereleaseClassification(string tag, bool prerelease) {
            var release = new Release(tag, "hash", "mod.pak", DateTime.UtcNow, null!, null!, null);
            Assert.Equal(prerelease, release.Version!.IsPrerelease);
        }

        [Fact]
        public void BothManagersUseTheSameBadgeTemplate() {
            var mods = Load("Mod.xaml");
            var dlls = Load("MainWindow.xaml");
            var theme = Load("Misc.xaml");
            var modBadges = Badges(mods).ToArray();
            Assert.Equal(2, modBadges.Length); // Version selector and dependency versions.
            Assert.All(modBadges, badge => Assert.Contains("Version.IsPrerelease", badge.Attribute("Visibility")!.Value));
            var dllBadge = Assert.Single(Badges(dlls));
            Assert.Contains("IsPrerelease", dllBadge.Attribute("Visibility")!.Value);
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var style = Assert.Single(theme.Descendants().Where(element => element.Attribute(x + "Key")?.Value == "Release.PrereleaseBadge"));
            Assert.Contains(style.Descendants(), element => element.Attribute("Text")?.Value == "Prerelease");
            Assert.DoesNotContain(mods.Descendants(), element => element.Name.LocalName == "ComboBox" && element.Attribute("DisplayMemberPath") != null);
            Assert.Contains(mods.Descendants(), element => element.Attribute("Style")?.Value == "{StaticResource PluginCard}");
            Assert.Contains(dlls.Descendants(), element => element.Attribute("Style")?.Value == "{StaticResource PluginCard}");
            Assert.Contains(dlls.Descendants(), element => element.Name.LocalName == "ComboBox" && element.Attribute("ItemsSource")?.Value == "{Binding AvailableVersions}");
        }

        private static XDocument Load(string name) => XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Views", name));
        private static IEnumerable<XElement> Badges(XDocument document) => document.Descendants()
            .Where(element => element.Attribute("Style")?.Value == "{StaticResource Release.PrereleaseBadge}");
    }
}
