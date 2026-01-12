

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UnchainedLauncher.UnrealModScanner.ViewModels.Nodes {
    public class PakChildNode {
        //string DisplayName { get; }
        public PakChildNode(bool isExpanded = true) {
            IsExpanded = isExpanded;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool _isExpanded;
        public virtual bool IsExpanded {
            get => _isExpanded;
            set {
                if (_isExpanded != value) {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }
    }
}
