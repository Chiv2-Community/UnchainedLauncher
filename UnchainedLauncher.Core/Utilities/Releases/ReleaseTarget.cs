using Semver;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;

namespace UnchainedLauncher.Core.Utilities.Releases
{
    public record ReleaseTarget(
        string Name,
        string PageUrl,
        string DescriptionMarkdown,
        string ReleaseTag,
        SemVersion Version,
        IEnumerable<ReleaseAsset> Assets,
        DateTimeOffset CreatedDate,
        bool IsLatestStable,
        bool IsPrerelease) {
        public ReleaseTarget AsLatestStable() => this with { IsLatestStable = true };
        public string DisplayText => $"{ReleaseTag} ({CreatedDate:d})" + (IsLatestStable ? " Recommended" : "");
    }
    
    public record ReleaseAsset(string Name, string DownloadUrl);
}