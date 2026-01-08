using System;
using System.Linq;
using System.Windows;

namespace UnchainedLauncher.GUI.Services {
    public enum ThemeVariant {
        Dark,
        Light
    }

    public static class ThemeService {
        public static event EventHandler<ThemeVariant>? ThemeChanged;

        private static readonly Uri DarkConstantsUri = new(
            "/UnchainedLauncher;component/Resources/Themes/DarkConstants.xaml",
            UriKind.Relative);

        private static readonly Uri LightConstantsUri = new(
            "/UnchainedLauncher;component/Resources/Themes/LightConstants.xaml",
            UriKind.Relative);

        public static ThemeVariant Current { get; private set; } = ThemeVariant.Dark;

        public static void Apply(ThemeVariant variant) {
            var app = Application.Current;
            if (app == null) return;

            var merged = app.Resources.MergedDictionaries;
            if (merged == null) return;

            var targetUri = variant == ThemeVariant.Light ? LightConstantsUri : DarkConstantsUri;

            var existingIndex = merged
                .Select((d, i) => new { Dict = d, Index = i })
                .FirstOrDefault(x => x.Dict.Source != null &&
                                     (x.Dict.Source.OriginalString.EndsWith("/Resources/Themes/DarkConstants.xaml", StringComparison.OrdinalIgnoreCase) ||
                                      x.Dict.Source.OriginalString.EndsWith("/Resources/Themes/LightConstants.xaml", StringComparison.OrdinalIgnoreCase)))
                ?.Index;

            var newDict = new ResourceDictionary { Source = targetUri };

            if (existingIndex.HasValue) {
                merged[existingIndex.Value] = newDict;
            }
            else {
                merged.Insert(0, newDict);
            }

            if (Current != variant) {
                Current = variant;
                ThemeChanged?.Invoke(null, variant);
            }
        }
    }
}
