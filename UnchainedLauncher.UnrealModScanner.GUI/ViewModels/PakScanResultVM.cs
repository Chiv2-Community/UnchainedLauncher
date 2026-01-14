using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UnchainedLauncher.UnrealModScanner.ViewModels.Nodes;
using UnrealModScanner.Models;

// TODO: implemen. is this a viewmodel?
namespace UnchainedLauncher.UnrealModScanner.ViewModels {
    public class PakScanResultVM {

        public PakScanResultVM(ObservableCollection<PakScanResult> results) {
            Results = results;
            Children = InitChildren(); 
        }

        private ObservableCollection<PakChildNode> InitChildren() {
            var children = new ObservableCollection<PakChildNode>();

            return children;
        }
        
        [JsonIgnore]
        public ObservableCollection<PakChildNode> Children { get; init; } = new();
        
        public ObservableCollection<PakScanResult> Results { get; } = new();
    
        private bool _isChecked;
        /// <summary>
        /// GUI View state
        /// </summary>
        private bool _isExpanded;
        /// <summary>
        /// Whether this category is expanded
        /// </summary>
        [JsonIgnore]
        public string PakPathExpanded { get; set; }
    
        [JsonIgnore]
        public bool IsExpanded {
            get => _isExpanded;
            set {
                if (_isExpanded != value) {
                    _isExpanded = value;
                    if (_isExpanded && Children.Count == 1) {
                        Children.First().IsExpanded = true;
                    }
                    OnPropertyChanged();
                }
            }
        }
    
        [JsonIgnore]
        public bool IsChecked {
            get => _isChecked;
            set {
                if (_isChecked != value) {
                    _isChecked = value;
                    OnPropertyChanged();
                }
            }
        }
    
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}