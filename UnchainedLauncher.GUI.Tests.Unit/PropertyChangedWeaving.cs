// Existing portable tests use unmodified setters; only the mod-card tests opt
// into the notification weaving used by the WPF application.
[assembly: PropertyChanged.FilterType(@"^UnchainedLauncher\.GUI\.ViewModels\.Mod(List)?VM$")]
