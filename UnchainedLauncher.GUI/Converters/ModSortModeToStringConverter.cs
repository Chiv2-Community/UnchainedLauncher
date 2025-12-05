using System;
using System.Globalization;
using System.Windows.Data;
using UnchainedLauncher.GUI.ViewModels;

namespace UnchainedLauncher.GUI.Converters {
    public class ModSortModeToStringConverter : IValueConverter {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            return value switch {
                ModSortMode.EnabledFirst => "Enabled first",
                ModSortMode.Alphabetical => "Alphabetical",
                ModSortMode.LatestReleaseDateFirst => "Latest New Releases First",
                ModSortMode.NewestModsFirst => "Newest Mods First",
                _ => value?.ToString() ?? string.Empty
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            // Not used. ComboBox binds SelectedItem to enum directly; this converter is for display only.
            return Binding.DoNothing;
        }
    }
}
