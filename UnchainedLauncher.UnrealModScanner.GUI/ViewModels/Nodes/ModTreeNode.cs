using UnchainedLauncher.UnrealModScanner.Models.Chivalry2;
using UnchainedLauncher.UnrealModScanner.ViewModels.Nodes;

namespace UnchainedLauncher.UnrealModScanner.GUI.ViewModels.Nodes {


    public sealed class ModTreeNode : PakChildNode {
        public BlueprintModInfo Blueprint { get; }

        public ModTreeNode(BlueprintModInfo blueprint, bool isExpanded = true) {
            Blueprint = blueprint;
            IsExpanded = isExpanded;
        }

        public string DisplayName => Blueprint.ModName;
    }

}