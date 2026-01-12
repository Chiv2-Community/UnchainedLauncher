
using UnchainedLauncher.UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.ViewModels.Nodes {


    public sealed class ModTreeNode : PakChildNode {
        public BlueprintModInfo Blueprint { get; }

        public ModTreeNode(BlueprintModInfo blueprint, bool isExpanded = true) {
            Blueprint = blueprint;
            IsExpanded = isExpanded;
        }

        public string DisplayName => Blueprint.ModName;
    }

}