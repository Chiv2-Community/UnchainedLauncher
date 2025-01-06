using LanguageExt;
using UnchainedLauncher.Core.Mods;

namespace UnchainedLauncher.Core.Utilities {


    public interface IUpdateNotifier {
        public UserDialogueChoice? Notify(string titleText, string messageText, string yesButtonText,
            string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdate> updates);

        public UserDialogueChoice? Notify(string titleText, string messageText, string yesButtonText, string noButtonText, string? cancelButtonText, DependencyUpdate firstUpdate, params DependencyUpdate[] updates) {
            var allUpdates = new List<DependencyUpdate>();
            allUpdates.Add(firstUpdate);
            allUpdates.AddRange(updates);

            return Notify(titleText, messageText, yesButtonText, noButtonText, cancelButtonText, allUpdates);
        }
    }

    public record DependencyUpdate(string Name, Option<string> CurrentVersion, string LatestVersion, string ReleaseUrl, string Reason) {
        public string CurrentVersionString => CurrentVersion.IfNone("None");

        public static DependencyUpdate FromUpdateCandidate(UpdateCandidate modUpdate) {
            return new DependencyUpdate(modUpdate.CurrentlyEnabled.Manifest.Name, modUpdate.CurrentlyEnabled.Tag, modUpdate.AvailableUpdate.Tag, modUpdate.AvailableUpdate.ReleaseUrl, "");
        }
    };
}