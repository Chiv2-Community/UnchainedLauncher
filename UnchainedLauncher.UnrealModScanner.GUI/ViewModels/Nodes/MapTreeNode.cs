using System.Collections;
using System.Globalization;
using System.Windows.Data;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.ViewModels.Nodes;

namespace UnchainedLauncher.UnrealModScanner.GUI.ViewModels.Nodes {
    public sealed class MapTreeNode : PakChildNode {
        public GameMapInfo Map { get; init; }


        public MapTreeNode(GameMapInfo map, bool isExpanded = true) {
            Map = map;
            IsExpanded = isExpanded;
        }
        public string DisplayName => Map.AssetPath;
    }

    public class PropertyFilterConverter : IValueConverter {
        // Define the list of keys you want to HIDE from the UI
        private static readonly HashSet<string> Blacklist = new()
        {
            "NavigationSystemConfig",
            "BookmarkArray",
            "asset_hash",
            "bEnableNavigationSystem",
            "KillZ"
        };

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            // 1. Handle Generic Dictionaries (Dictionary<string, object>)
            if (value is IEnumerable<KeyValuePair<string, object>> genericDict) {
                return genericDict
                    .Where(kvp => !Blacklist.Contains(kvp.Key))
                    .ToList();
            }

            // 2. Fallback for non-generic dictionaries (just in case)
            if (value is IDictionary dict) {
                var filtered = new List<DictionaryEntry>();
                foreach (DictionaryEntry entry in dict) {
                    if (!Blacklist.Contains(entry.Key.ToString())) {
                        filtered.Add(entry);
                    }
                }
                return filtered;
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}