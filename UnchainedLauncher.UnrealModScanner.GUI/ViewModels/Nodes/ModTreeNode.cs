
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.ViewModels.Nodes;

namespace UnchainedLauncher.UnrealModScanner.GUI.ViewModels.Nodes {


    public sealed class ModTreeNode : PakChildNode {
        // public BlueprintModInfo Blueprint { get; }
        //
        // public ModTreeNode(BlueprintModInfo blueprint, bool isExpanded = true) {
        //     Blueprint = blueprint;
        //     IsExpanded = isExpanded;
        // }

        public GenericAssetEntry Blueprint { get; set; }
        public ModTreeNode(GenericAssetEntry blueprint, bool isExpanded = true) {
            Blueprint = blueprint;
            IsExpanded = isExpanded;
        }

        public string DisplayName => Blueprint.ClassName;
    }

}