using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.GUI.ViewModels.ServersTab.IniSections {
    // The travel string is user configuration, not disposable catalog metadata.
    [AddINotifyPropertyChangedInterface]
    public class MapRotationEntry(string travelString) {
        public string TravelString { get; } = travelString;
        public MapDto? Map { get; private set; }

        public void RefreshMetadata(IEnumerable<MapDto> availableMaps) {
            var package = TravelString.Split('?')[0];
            var name = package[(package.LastIndexOf('/') + 1)..];
            var dot = name.IndexOf('.');
            if (dot >= 0) name = name[..dot];
            Map = availableMaps.FirstOrDefault(map =>
                string.Equals(map.TravelToMapString(), name, StringComparison.OrdinalIgnoreCase));
        }

        public override string ToString() => TravelString;
    }
}
