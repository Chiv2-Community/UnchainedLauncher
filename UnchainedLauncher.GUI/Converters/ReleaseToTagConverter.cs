using System;
using System.Globalization;
using System.Windows.Data;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;

namespace UnchainedLauncher.GUI.Converters
{
    public class ReleaseToTagConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) {
            return value switch {
                Release release => release.Tag,
                null => string.Empty,
                _ => throw new InvalidCastException("ReleaseToTagConverter can only convert from Release. Got " + value.GetType().Name)
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}