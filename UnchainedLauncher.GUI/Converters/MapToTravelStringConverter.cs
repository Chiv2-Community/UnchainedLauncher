using System;
using System.Globalization;
using System.Windows.Data;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.GUI.Converters {
    [ValueConversion(typeof(MapDto), typeof(string))]
    public class MapToTravelStringConverter : IValueConverter {
        public static MapToTravelStringConverter Instance = new MapToTravelStringConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is MapDto map) {
                return map.TravelToMapString();
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}
