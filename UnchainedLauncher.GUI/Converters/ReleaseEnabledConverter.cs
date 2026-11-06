using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using UnchainedLauncher.GUI.Services;
using UnchainedLauncher.UnrealModScanner.JsonModels;

namespace UnchainedLauncher.GUI.Converters {
    [ValueConversion(typeof(object[]), typeof(bool))]
    public class ReleaseEnabledConverter : IMultiValueConverter {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
            if (values.Length < 2) return false;

            var blueprint = values[0] as BlueprintDto;
            var list = values[1] as ObservableCollection<BlueprintDto>;

            if (blueprint == null || list == null) return false;

            return list.Any(selected => ServerBlueprintIdentity.Same(selected, blueprint));
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}