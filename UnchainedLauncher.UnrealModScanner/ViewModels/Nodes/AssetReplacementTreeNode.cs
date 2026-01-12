

using UnchainedLauncher.UnrealModScanner.Models.UnchainedLauncher.UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.ViewModels.Nodes {
    public sealed class AssetReplacementTreeNode : PakChildNode {
        public AssetReplacementInfo Replacement { get; }


        public AssetReplacementTreeNode(AssetReplacementInfo replacement, bool isExpanded = true) {
            Replacement = replacement;
            IsExpanded = isExpanded;
        }
        public string DisplayName => Replacement.AssetPath;
    }
}