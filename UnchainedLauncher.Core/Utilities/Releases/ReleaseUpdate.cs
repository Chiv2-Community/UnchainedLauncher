using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using System.Diagnostics;
using System.Windows.Input;
using UnchainedLauncher.Core.Mods;

namespace UnchainedLauncher.Core.Utilities.Releases
{
    public record ReleaseUpdate(ReleaseTarget Target, Option<string> CurrentVersion, string LatestVersion, string Reason) {
        public string CurrentVersionString => CurrentVersion.IfNone("None");
        public string VersionString => CurrentVersion == null ? LatestVersion : $"{CurrentVersionString} -> {LatestVersion}";
        
        // TODO: Remove from this record class. It shouldn't know anything about how to open a web page.
        public ICommand HyperlinkCommand => new RelayCommand(OpenReleasePage);

        public void OpenReleasePage() {
            Process.Start(new ProcessStartInfo { FileName = Target.PageUrl, UseShellExecute = true });
        }

        public static ReleaseUpdate FromUpdateCandidate(UpdateCandidate modUpdate) {
            return new ReleaseUpdate(modUpdate.CurrentlyEnabled.Manifest.Name, modUpdate.CurrentlyEnabled.Tag, modUpdate.AvailableUpdate.Tag, modUpdate.AvailableUpdate.ReleaseUrl, "");
        }
    };
}