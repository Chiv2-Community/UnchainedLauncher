using System;
using System.Globalization;
using System.Windows.Data;
using UnchainedLauncher.Core.JsonModels;

namespace UnchainedLauncher.GUI.Converters {

    [ValueConversion(typeof(InstallationType), typeof(string))]
    public class InstallationTypeConverter : IValueConverter {
        public static InstallationTypeConverter Instance = new InstallationTypeConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is InstallationType installationType) {
                return installationType.ToFriendlyString();
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}