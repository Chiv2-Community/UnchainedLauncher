
using System.Collections.ObjectModel;
using UnchainedLauncher.UnrealModScanner.ViewModels.Nodes;
namespace UnchainedLauncher.UnrealModScanner.GUI.ViewModels.Nodes {
    public class PakGroupNode : PakChildNode {
        public PakGroupNode(string title, bool isExpanded = true) {
            Title = title;
            IsExpanded = isExpanded;
        }

        public string Title { get; }

        private bool _isExpanded;
        public override bool IsExpanded {
            get => _isExpanded;
            set {
                if (_isExpanded != value) {
                    _isExpanded = value;
                    if (_isExpanded && Children.Count == 1) {
                        Children.First().IsExpanded = value;
                    }
                    OnPropertyChanged();
                }
            }
        }


        public ObservableCollection<PakChildNode> Children { get; } = new();
    }

}