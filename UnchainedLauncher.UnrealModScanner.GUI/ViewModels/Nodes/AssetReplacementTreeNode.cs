

using UnchainedLauncher.UnrealModScanner.Models.UnchainedLauncher.UnrealModScanner.Models;
using UnchainedLauncher.UnrealModScanner.ViewModels.Nodes;

namespace UnchainedLauncher.UnrealModScanner.GUI.ViewModels.Nodes {
    public sealed class AssetReplacementTreeNode : PakChildNode {
        public AssetReplacementInfo Replacement { get; }


        public AssetReplacementTreeNode(AssetReplacementInfo replacement, bool isExpanded = true) {
            Replacement = replacement;
            IsExpanded = isExpanded;
        }
        public string DisplayName => Replacement.AssetPath;
    }
}