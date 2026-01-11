using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;
using UnchainedLauncher.Core.JsonModels.Metadata.V4;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.GUI.Converters {
    [ValueConversion(typeof(object[]), typeof(bool))]
    public class ReleaseEnabledConverter : IMultiValueConverter {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            if (values.Length < 2) return false;

            var release = values[0] as Release;
            var list = values[1] as ObservableCollection<ReleaseCoordinates>;

            if (release == null || list == null) return false;

            var coords = ReleaseCoordinates.FromRelease(release);
            // ObservableCollection.Contains will use value equality of the record type
            return list.Contains(coords);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}