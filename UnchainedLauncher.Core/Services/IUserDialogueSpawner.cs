using UnchainedLauncher.Core.Services.Mods;

namespace UnchainedLauncher.Core.Utilities {


    public interface IUserDialogueSpawner {
        public void DisplayMessage(string message);
        public UserDialogueChoice DisplayYesNoMessage(string message, string caption);

        public UserDialogueChoice? DisplayUpdateMessage(string titleText, string messageText, string yesButtonText,
            string noButtonText, string? cancelButtonText, IEnumerable<DependencyUpdate> updates);

        public UserDialogueChoice? DisplayUpdateMessage(string titleText, string messageText, string yesButtonText, string noButtonText, string? cancelButtonText, DependencyUpdate firstUpdate, params DependencyUpdate[] updates) {
            var allUpdates = new List<DependencyUpdate>();
            allUpdates.Add(firstUpdate);
            allUpdates.AddRange(updates);

            return DisplayUpdateMessage(titleText, messageText, yesButtonText, noButtonText, cancelButtonText, allUpdates);
        }
    }

    public enum UserDialogueChoice {
        Yes,
        No,
        Cancel
    }

    public record DependencyUpdate(string Name, string? CurrentVersion, string LatestVersion, string ReleaseUrl, string Reason) {
        public static DependencyUpdate FromUpdateCandidate(UpdateCandidate modUpdate) {
            return new DependencyUpdate(modUpdate.CurrentlyEnabled.Manifest.Name, modUpdate.CurrentlyEnabled.Tag, modUpdate.AvailableUpdate.Tag, modUpdate.AvailableUpdate.ReleaseUrl, "");
        }
    };

}