using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Semver;
using UnchainedLauncher.Core.JsonModels.ModMetadata;

namespace UnchainedLauncher.Core.Services.Dlls {
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record DllRelease(
        [property: JsonPropertyName("organization")] string Organization,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("download_url")] string DownloadUrl,
        [property: JsonPropertyName("sha256")] string Sha256,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("info"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PluginInfo? Info = null,
        [property: JsonPropertyName("tag"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tag = null,
        [property: JsonPropertyName("release_date"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? ReleaseDate = null,
        [property: JsonPropertyName("release_notes_markdown"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReleaseNotesMarkdown = null,
        [property: JsonPropertyName("dll_file_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DllFileName = null) {
        [JsonIgnore]
        public string Id => $"{Organization}/{Name}";
        [JsonIgnore]
        public string Key => $"{Id}:{Version}";

        public void Validate() {
            Info?.Validate();
            if (Info?.RepoUrl != null && !Info.RepoUrl.Equals($"https://github.com/{Id}", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Plugin repository mismatch.");
            if (Tag != null && SemVersion.Parse(Tag, SemVersionStyles.AllowV).ToString() != Version)
                throw new InvalidDataException("Plugin tag/version mismatch.");
            if (DllFileName != null && (!DllFileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || DllFileName.IndexOfAny(['/', '\\', ':', '<', '>', '"', '|', '?', '*']) >= 0 || DllFileName.Any(char.IsControl)))
                throw new InvalidDataException("Invalid DLL asset filename.");
            if (!Regex.IsMatch(Organization ?? "", "^[A-Za-z0-9_-]+$") ||
                !Regex.IsMatch(Name ?? "", "^[A-Za-z0-9_-]+$") ||
                !SemVersion.TryParse(Version, SemVersionStyles.Strict, out _) ||
                !Uri.TryCreate(DownloadUrl, UriKind.Absolute, out var url) ||
                url.Scheme != Uri.UriSchemeHttps || url.UserInfo.Length != 0 ||
                url.Fragment.Length != 0 || !url.AbsolutePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                !Regex.IsMatch(Sha256 ?? "", "^[a-f0-9]{64}$") || Size <= 0) {
                throw new InvalidDataException("Invalid DLL registry metadata.");
            }
        }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PluginInfo(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("authors")] IReadOnlyList<string> Authors,
        [property: JsonPropertyName("plugin_dependencies"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Dependency>? PluginDependencies = null,
        [property: JsonPropertyName("icon_url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? IconUrl = null,
        [property: JsonPropertyName("image_urls"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? ImageUrls = null,
        [property: JsonPropertyName("mod_dependencies"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Dependency>? ModDependencies = null,
        [property: JsonPropertyName("repo_url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RepoUrl = null,
        [property: JsonPropertyName("target")] string Target = "shared",
        [property: JsonPropertyName("$schema"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Schema = null) {
        [JsonIgnore]
        public IReadOnlyList<Dependency> RequiredMods => ModDependencies ?? [];
        [JsonIgnore]
        public IReadOnlyList<Dependency> RequiredPlugins => PluginDependencies ?? [];
        public void Validate() {
            if (Target is not ("client" or "server" or "shared"))
                throw new InvalidDataException("Plugin target must be client, server or shared.");
            if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Description) || Authors == null ||
                Authors.Count == 0 || Authors.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("Invalid plugin.json metadata.");
            foreach (var imageUrl in (ImageUrls ?? []).Concat(IconUrl == null ? [] : new[] { IconUrl })) {
                if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var image) || image.Scheme != Uri.UriSchemeHttps || image.UserInfo.Length != 0)
                    throw new InvalidDataException("Plugin image/icon URLs must use HTTPS without credentials.");
            }
            var repos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (kind, dependency) in RequiredMods.Select(dependency => ("mod", dependency))
                         .Concat(RequiredPlugins.Select(dependency => ("plugin", dependency)))) {
                var repository = kind == "mod" ? @"(?!\.{1,2}$)[A-Za-z0-9_.-]+" : "[A-Za-z0-9_-]+";
                if (dependency == null || !Uri.TryCreate(dependency.RepoUrl, UriKind.Absolute, out var url) ||
                    url.Scheme != Uri.UriSchemeHttps || url.Host != "github.com" || url.UserInfo.Length != 0 || !url.IsDefaultPort ||
                    !Regex.IsMatch(url.AbsolutePath, "^/[A-Za-z0-9_-]+/" + repository + "$") || url.Query.Length != 0 || url.Fragment.Length != 0 ||
                    !repos.Add(kind + ":" + dependency.RepoUrl))
                    throw new InvalidDataException("Invalid plugin dependency repository.");
                MatchesVersion("1.0.0", dependency.Version);
            }
        }

        public static bool MatchesVersion(string version, string requirement) {
            var candidate = SemVersion.Parse(version, SemVersionStyles.Strict);
            if (string.IsNullOrEmpty(requirement)) throw new InvalidDataException("Missing dependency version.");
            if (requirement.StartsWith('v')) requirement = requirement[1..];
            if (requirement.StartsWith('^')) {
                var lower = SemVersion.Parse(requirement[1..], SemVersionStyles.Strict);
                var upper = lower.Major != 0 ? new SemVersion(checked(lower.Major + 1), 0, 0)
                    : lower.Minor != 0 ? new SemVersion(0, checked(lower.Minor + 1), 0)
                    : new SemVersion(0, 0, checked(lower.Patch + 1));
                return candidate.ComparePrecedenceTo(lower) >= 0 && candidate.ComparePrecedenceTo(upper) < 0;
            }
            var matches = true;
            foreach (var part in requirement.Split(',')) {
                var match = Regex.Match(part.Trim(), "^(>=|<=|==|!=|>|<|=)?(.+)$");
                if (!match.Success) throw new InvalidDataException("Invalid dependency version.");
                var target = SemVersion.Parse(match.Groups[2].Value, SemVersionStyles.Strict);
                var comparison = candidate.ComparePrecedenceTo(target);
                matches &= match.Groups[1].Value switch {
                    ">=" => comparison >= 0, "<=" => comparison <= 0, ">" => comparison > 0, "<" => comparison < 0,
                    "!=" => comparison != 0, "" or "=" or "==" => comparison == 0,
                    _ => throw new InvalidDataException("Invalid dependency version operator.")
                };
            }
            return matches;
        }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PluginPackage(
        [property: JsonPropertyName("latest_release_info"), JsonRequired] PluginInfo LatestReleaseInfo,
        [property: JsonPropertyName("releases"), JsonRequired] IReadOnlyList<PluginRelease> Releases);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record PluginRelease(
        [property: JsonPropertyName("tag"), JsonRequired] string Tag,
        [property: JsonPropertyName("hash"), JsonRequired] string Hash,
        [property: JsonPropertyName("dll_file_name"), JsonRequired] string? DllFileName,
        [property: JsonPropertyName("download_url"), JsonRequired] string DownloadUrl,
        [property: JsonPropertyName("size"), JsonRequired] long Size,
        [property: JsonPropertyName("release_date"), JsonRequired] DateTimeOffset? ReleaseDate,
        [property: JsonPropertyName("info"), JsonRequired] PluginInfo? Info,
        [property: JsonPropertyName("release_notes_markdown"), JsonRequired] string? ReleaseNotesMarkdown) {
        public DllRelease ToDllRelease(string organization, string name) => new(organization, name,
            SemVersion.Parse(Tag, SemVersionStyles.AllowV).ToString(), DownloadUrl, Hash, Size, Info,
            Tag, ReleaseDate, ReleaseNotesMarkdown, DllFileName);
    }
}
