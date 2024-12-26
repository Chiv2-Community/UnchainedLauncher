using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using log4net;
using Semver;
using System.Diagnostics;
using System.Net.Http.Headers;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Utilities.Releases {

    public enum UpdateResponse {
        Accept,
        Reject,
        Cancel,
        Exit
    }
    
    public interface IUpdateNotifier {
        UpdateResponse Notify(string titleText, string? descriptionText, IEnumerable<ReleaseUpdate> update);

        public UpdateResponse Notify(string titleText, string? descriptionText, ReleaseUpdate update) {
            return Notify(titleText, descriptionText, new[] { update });
        }
        
    }

    public record ReleaseTargetAssetMapping(ReleaseTarget Target, Func<ReleaseAsset, string?> PathMapping);

    public enum ReleaseDownloadResult {
        Updated,
        Rejected,
        Cancelled
    }
    
    public interface IReleaseDownloader {
        /// <summary>
        /// Attempts to synchronize some asset with the target assets if necessary
        /// </summary>
        /// <param name="notificationTitle">The title text that will be displayed with the update notification</param>
        /// <param name="notificationDescription">An optional description that will be displayed with the notification</param>
        /// <param name="target"></param>
        /// <param name="pathMapping">A function returning a local path to an asset, or null if the asset should be ignored.</param>
        /// <param name="progress">An object for reporting download progress to the caller</param>
        /// <returns></returns>
        public Task<bool> Download(string notificationTitle, string? notificationDescription, IEnumerable<ReleaseTargetAssetMapping> targetAssetMappings, IProgress<int>? progress = null) {
    }

    public class FileInfoReleaseDownloader : IReleaseDownloader {
        private static readonly ILog logger = LogManager.GetLogger(nameof(FileInfoReleaseDownloader));

        private string WorkingDirectory;
        public IUpdateNotifier UpdateNotifier { get; set; }

        
        public FileInfoReleaseDownloader(string workingDirectory, IUpdateNotifier updateNotifier) {
            WorkingDirectory = workingDirectory;
            UpdateNotifier = updateNotifier;
        }


        private Option<(ReleaseAsset Asset, Option<SemVersion> CurrentVersion)> CheckIfAssetNeedsUpdate(SemVersion targetVersion, ReleaseAsset asset, string assetLocalPath) {
            var path = Path.Combine(WorkingDirectory, assetLocalPath);
            var exists = File.Exists(path);

            if (!exists) return (asset, None);
        
            var fileInfo = FileVersionInfo.GetVersionInfo(path);

            var versionString = fileInfo.ProductVersion ?? fileInfo.FileVersion;
            logger.Debug("Raw plugin version: " + versionString);
            var splitVersionString = versionString.Split('.');
            versionString = String.Join('.', splitVersionString.Take(3));
            logger.Debug("Cleaned plugin version: " + versionString);

            var successful = SemVersion.TryParse(
                versionString,
                SemVersionStyles.Any,
                out var currentVersion
            );

            // If new version is the same as or less than current, don't download anything
            if (successful && currentVersion.ComparePrecedenceTo(targetVersion) >= 0) return null;
                
            return (asset, Some(currentVersion));

        }


        public async Task<ReleaseDownloadResult> Download(string notificationTitle, string? notificationDescription, IEnumerable<ReleaseTargetAssetMapping> targetAssetMappings, IProgress<int>? progress = null) {
            var updateTargetGroups =
                from mapping in targetAssetMappings
                from asset in mapping.Target.Assets
                let path = mapping.PathMapping(asset)
                let updateInfo =
                    CheckIfAssetNeedsUpdate(mapping.Target.Version, asset, path)
                where updateInfo.IsSome
                let result =
                    (Mapping: mapping, UpdateAsset: updateInfo.Value())
                group result by result.Mapping.Target;

            var notificationEntries =
                from updateGroup in updateTargetGroups
                let names = string.Join(", ", updateGroup.Select(x => x.UpdateAsset.Asset.Name))
                select new ReleaseUpdate(
                    updateGroup.Key,
                    updateGroup.First().UpdateAsset.CurrentVersion.Select(x => x.ToString()),
                    updateGroup.Key.Version.ToString(),
                    $"Missing or outdated files: {names}"
                );

            var userResponse = UpdateNotifier.Notify(
                notificationTitle,
                notificationDescription,
                notificationEntries
            );

            // The cases in here are all for exiting early.
            return userResponse switch {
                UpdateResponse.Cancel => ReleaseDownloadResult.Cancelled,
                UpdateResponse.Exit => ReleaseDownloadResult.Cancelled,
                UpdateResponse.Reject => ReleaseDownloadResult.Rejected,
                UpdateResponse.Accept => {
                    foreach(var updateTargetGroup in updateTargetGroups)
                    {
                        var mapping = updateTargetGroup.First().UpdateAsset;
                        await HttpHelpers.DownloadReleaseTarget();
                    }
                    
                    return ReleaseDownloadResult.Updated;
                }
            };
        }
    }
} 