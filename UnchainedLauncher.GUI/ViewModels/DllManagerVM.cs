using CommunityToolkit.Mvvm.Input;
using PropertyChanged;
using Semver;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using UnchainedLauncher.Core.Services.Dlls;
using UnchainedLauncher.Core.Services;

namespace UnchainedLauncher.GUI.ViewModels {
    public interface IPluginSettings {
        bool EnablePluginAutomaticUpdates { get; set; }
        bool AllowUnstablePluginReleases { get; set; }
    }

    [AddINotifyPropertyChangedInterface]
    public partial class DllManagerVM {
        private readonly DllManager manager;
        private readonly IPluginSettings? _settings;
        private readonly IUserDialogueSpawner? _dialogue;
        public ObservableCollection<DllPluginVM> Plugins { get; } = [];
        private string? _searchTerm;
        public string? SearchTerm {
            get => _searchTerm;
            set {
                if (_searchTerm == value) return;
                _searchTerm = value;
                Populate();
            }
        }
        private ModSortMode _selectedSortMode = ModSortMode.EnabledFirst;
        public ModSortMode SelectedSortMode {
            get => _selectedSortMode;
            set {
                if (_selectedSortMode == value) return;
                _selectedSortMode = value;
                Populate();
            }
        }
        public IReadOnlyList<ModSortMode> SortModes { get; } = [ModSortMode.EnabledFirst, ModSortMode.Alphabetical,
            ModSortMode.LatestReleaseDateFirst, ModSortMode.NewestModsFirst];
        private bool _allowUnstablePluginReleases;
        private bool _enablePluginAutomaticUpdates = true;
        public bool EnablePluginAutomaticUpdates {
            get => _settings?.EnablePluginAutomaticUpdates ?? _enablePluginAutomaticUpdates;
            set {
                if (EnablePluginAutomaticUpdates == value) return;
                _enablePluginAutomaticUpdates = value;
                if (_settings != null) _settings.EnablePluginAutomaticUpdates = value;
            }
        }
        public bool AllowUnstablePluginReleases {
            get => _settings?.AllowUnstablePluginReleases ?? _allowUnstablePluginReleases;
            set {
                if (AllowUnstablePluginReleases == value) return;
                _allowUnstablePluginReleases = value;
                if (_settings != null) _settings.AllowUnstablePluginReleases = value;
                Populate();
            }
        }

        public DllManagerVM(DllManager manager, IPluginSettings? settings = null, IUserDialogueSpawner? dialogue = null) {
            this.manager = manager;
            _settings = settings;
            _dialogue = dialogue;
            manager.SelectionChanged += (_, _) => Populate();
            if (settings is INotifyPropertyChanged observable)
                observable.PropertyChanged += (_, args) => {
                    if (args.PropertyName is nameof(IPluginSettings.AllowUnstablePluginReleases) or null or "") Populate();
                };
        }
        public string Status { get; set; } = "Only enabled, registry-validated DLLs are loaded. Trust the publisher before enabling native code.";

        private void Populate() {
            IEnumerable<DllPluginVM> query = manager.Releases.GroupBy(release => release.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => new DllPluginVM(manager, group.ToArray(), AllowUnstablePluginReleases, message => Status = message, ReportError))
                .Where(plugin => plugin.AvailableVersions.Count != 0 || plugin.IsEnabled);
            var term = (SearchTerm ?? "").Trim();
            if (term.Length != 0)
                query = query.Where(plugin => new[] { plugin.Name, plugin.Organization + "/" + plugin.RepositoryName, plugin.Description, plugin.Authors }
                    .Any(text => text.Contains(term, StringComparison.OrdinalIgnoreCase)));
            var ordered = SelectedSortMode switch {
                ModSortMode.EnabledFirst => query.OrderByDescending(plugin => plugin.IsEnabled).ThenBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase),
                ModSortMode.LatestReleaseDateFirst => query.OrderByDescending(plugin => plugin.LatestReleaseDate).ThenBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase),
                ModSortMode.NewestModsFirst => query.OrderByDescending(plugin => plugin.FirstReleaseDate).ThenBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase),
                _ => query.OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            };
            var displayed = ordered.ThenBy(plugin => plugin.Organization + "/" + plugin.RepositoryName,
                StringComparer.OrdinalIgnoreCase).ToArray();
            Plugins.Clear();
            foreach (var plugin in displayed) Plugins.Add(plugin);
        }

        [RelayCommand]
        public async Task Refresh() {
            try {
                await manager.RefreshAsync();
                Status = "Plugin list refreshed. Automatic updates are checked at launch when enabled.";
            }
            catch (Exception ex) { ReportError($"Could not refresh plugin list (using cached metadata if available): {ex.Message}"); }
            Populate();
        }

        [RelayCommand]
        public async Task UpdatePlugins() {
            try {
                await manager.RefreshAsync();
                Populate();
                var displayed = Plugins.Select(plugin => plugin.Organization + "/" + plugin.RepositoryName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var updates = manager.GetUpdateCandidates(server: null, allowUnstable: AllowUnstablePluginReleases)
                    .Where(update => displayed.Contains(update.Current.Id)).ToArray();
                if (updates.Length == 0) {
                    Status = "No plugin updates available.";
                    return;
                }
                var details = updates.Select(update => new DependencyUpdate(update.Latest.Info?.Name ?? update.Latest.Id,
                    update.Current.Version, update.Latest.Version,
                    $"https://github.com/{update.Latest.Id}/releases/tag/{update.Latest.Tag ?? update.Latest.Version}",
                    update.Latest.Info?.Description ?? "Native plugin")).ToArray();
                if (_dialogue?.DisplayUpdateMessage("Update Plugins?", "Plugin updates available.", "Yes", "No", null, details) != UserDialogueChoice.Yes) {
                    Status = "Plugins not updated.";
                    return;
                }
                foreach (var update in updates) manager.SetEnabled(update.Latest, true);
                Status = "Plugins updated. Registered DLLs will be validated at launch; downloads follow your settings.";
            }
            catch (Exception ex) { ReportError($"Could not update plugins: {ex.Message}"); }
            Populate();
        }

        private void ReportError(string message) {
            Status = message;
            _dialogue?.DisplayMessage(message);
        }

    }

    public partial class DllPluginVM : INotifyPropertyChanged {
        public event PropertyChangedEventHandler? PropertyChanged;
        private readonly DllManager _manager;
        private readonly Action<string> _status;
        private readonly Action<string> _error;
        private readonly Action<string> _openUrl;
        private DllRow? _selectedVersion;
        public string Organization { get; }
        public string RepositoryName { get; }
        private PluginInfo? Info => DisplayRelease?.Info;
        private DllRelease? DisplayRelease => SelectedVersion?.Release ?? EnabledRelease ?? AvailableVersions.FirstOrDefault()?.Release;
        public string Target => DisplayRelease?.Info?.Target ?? "shared";
        public string FileName => DisplayRelease?.DllFileName ?? RepositoryName + ".dll";
        public string ReleaseTag => DisplayRelease?.Tag ?? DisplayRelease?.Version ?? "";
        public string ReleaseDate => DisplayRelease?.ReleaseDate?.ToString("u") ?? "Unknown";
        public string ReleaseNotes => DisplayRelease?.ReleaseNotesMarkdown ?? "No release notes.";
        public string RepositoryUrl => $"https://github.com/{Organization}/{RepositoryName}";
        public string GithubReleaseUrl => DisplayRelease is { } release
            ? $"{RepositoryUrl}/releases/tag/{Uri.EscapeDataString(release.Tag ?? release.Version)}" : RepositoryUrl;
        public string Name => Info?.Name ?? RepositoryName;
        public string Description => Info?.Description ?? "Metadata unavailable for this legacy release.";
        public string Authors => Info == null ? "" : string.Join(", ", Info.Authors);
        public string IconUrl => Info?.IconUrl ?? "pack://application:,,,/UnchainedLauncher;component/assets/chiv2-unchained-logo.png";
        public IReadOnlyList<string> ImageUrls => Info?.ImageUrls ?? [];
        public string Dependencies {
            get {
                if (Info == null) return "None";
                var text = string.Join("\n", Info.RequiredMods.Select(dependency => $"Mod: {dependency.RepoUrl} {dependency.Version}")
                    .Concat(Info.RequiredPlugins.Select(dependency => $"Plugin: {dependency.RepoUrl} {dependency.Version}")));
                return text.Length == 0 ? "None" : text;
            }
        }
        public IReadOnlyList<DllRow> AvailableVersions { get; }
        public DateTimeOffset LatestReleaseDate => AvailableVersions.Select(row => row.Release.ReleaseDate ?? DateTimeOffset.MinValue)
            .DefaultIfEmpty(EnabledRelease?.ReleaseDate ?? DateTimeOffset.MinValue).Max();
        public DateTimeOffset FirstReleaseDate { get; }
        public DllRelease? EnabledRelease { get; private set; }
        public bool IsEnabled => EnabledRelease != null;
        public string ButtonText => IsEnabled ? "Disable" : "Enable";
        public bool HasHiddenEnabledVersion => IsEnabled && !AvailableVersions.Any(row => row.Release == EnabledRelease);
        public DllRow? SelectedVersion {
            get => _selectedVersion;
            set {
                if (value == _selectedVersion || value == null) return;
                if (!AvailableVersions.Contains(value)) throw new InvalidOperationException("Unavailable DLL version.");
                try {
                    _manager.SetEnabled(value.Release, true);
                    _selectedVersion = value;
                    EnabledRelease = value.Release;
                    NotifySelectionChanged();
                    _status("Enabled. The DLL will be validated at next Unchained launch; downloads follow your settings.");
                }
                catch (Exception ex) { _error($"Could not save DLL selection: {ex.Message}"); }
            }
        }

        public DllPluginVM(DllManager manager, IReadOnlyList<DllRelease> releases, bool showPrereleases, Action<string> status,
            Action<string>? error = null, Action<string>? openUrl = null) {
            _manager = manager;
            _status = status;
            _error = error ?? status;
            _openUrl = openUrl ?? (url => Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }));
            Organization = releases[0].Organization;
            RepositoryName = releases[0].Name;
            EnabledRelease = releases.FirstOrDefault(manager.IsEnabled);
            FirstReleaseDate = releases.Where(release => release.ReleaseDate.HasValue).Select(release => release.ReleaseDate!.Value)
                .DefaultIfEmpty(DateTimeOffset.MinValue).Min();
            AvailableVersions = releases.Select(release => new DllRow(release))
                .Where(row => showPrereleases || !row.IsPrerelease)
                .OrderByDescending(row => row.SemanticVersion,
                    Comparer<SemVersion>.Create((a, b) => a.ComparePrecedenceTo(b))).ToArray();
            _selectedVersion = AvailableVersions.FirstOrDefault(row => row.Release == EnabledRelease);
        }

        [RelayCommand]
        public void ViewOnGithub() => OpenLink(RepositoryUrl);

        [RelayCommand]
        public void OpenGithubRelease() => OpenLink(GithubReleaseUrl);

        private void OpenLink(string url) {
            try { _openUrl(url); }
            catch (Exception ex) { _error($"Could not open GitHub link: {ex.Message}"); }
        }

        [RelayCommand]
        public void EnableOrDisable() {
            try {
                if (EnabledRelease != null) {
                    _manager.SetEnabled(EnabledRelease, false);
                    EnabledRelease = null;
                    _selectedVersion = null;
                    NotifySelectionChanged();
                    _status("Disabled. The DLL will not be injected.");
                }
                else if (AvailableVersions.Count != 0) SelectedVersion = AvailableVersions[0];
            }
            catch (Exception ex) { _error($"Could not save DLL selection: {ex.Message}"); }
        }

        private void NotifySelectionChanged() {
            foreach (var property in new[] { nameof(SelectedVersion), nameof(EnabledRelease), nameof(IsEnabled), nameof(ButtonText), nameof(HasHiddenEnabledVersion),
                                             nameof(Name), nameof(Description), nameof(Authors), nameof(Dependencies), nameof(IconUrl), nameof(ImageUrls),
                                             nameof(Target), nameof(FileName), nameof(ReleaseTag), nameof(ReleaseDate), nameof(ReleaseNotes), nameof(GithubReleaseUrl) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }

    public sealed record DllRow(DllRelease Release) {
        public SemVersion SemanticVersion { get; } = SemVersion.Parse(Release.Version, SemVersionStyles.Strict);
        public bool IsPrerelease => SemanticVersion.IsPrerelease;
        public string Organization => Release.Organization;
        public string Name => Release.Name;
        public string Version => Release.Version;
        public long Size => Release.Size;
        public string Source => Release.DownloadUrl;
        public string Sha256 => Release.Sha256;
    }
}
