using System.Runtime.CompilerServices;
using System.Text.Json;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Dlls;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.Core.Services.Mods;

/// <summary>A validated, dependency-first snapshot. Selection and metadata changes invalidate reuse.</summary>
public sealed class PakLaunchPlan {
    public IReadOnlyList<Release> Releases { get; }

    private PakLaunchPlan(IEnumerable<Release> releases) => Releases = Array.AsReadOnly(releases.ToArray());

    private sealed record CacheEntry(Release[] Metadata, string Key, PakLaunchPlan Plan);
    private sealed class Cached { public CacheEntry? Value; }

    private static readonly ConditionalWeakTable<IModManager, Cached> Cache = new();

    public static PakLaunchPlan Create(IModManager manager, bool includeDependencies = true,
        IReadOnlyList<Release>? roots = null, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        roots ??= manager.EnabledModReleaseCoordinates.Select(coordinate =>
            manager.GetRelease(coordinate).IfNone(() => throw new ArgumentException($"Selected mod release was not found: {coordinate}"))).ToArray();
        var metadata = manager.Mods.SelectMany(mod => mod.Releases).ToArray();
        var identities = metadata.Concat(roots.OrderBy(release => ReleaseCoordinates.FromRelease(release).ToString(), StringComparer.Ordinal)).ToArray();
        // Metadata records contain mutable dependency lists. Compare their values as well as release identity.
        var key = JsonSerializer.Serialize(new {
            includeDependencies,
            Roots = roots.Select(ReleaseCoordinates.FromRelease).OrderBy(c => c.ToString(), StringComparer.Ordinal),
            Metadata = identities.Select(release => new {
                Coordinates = ReleaseCoordinates.FromRelease(release),
                Mods = release.Info.EffectiveModDependencies,
                Plugins = release.Info.EffectivePluginDependencies
            })
        });
        var cached = Cache.GetOrCreateValue(manager);
        var previous = cached.Value;
        if (previous != null && previous.Key == key && previous.Metadata.SequenceEqual(identities, ReferenceEqualityComparer.Instance))
            return previous.Plan;

        var selected = new Dictionary<ModIdentifier, Release>();
        var pins = roots.GroupBy(ModIdentifier.FromRelease).ToDictionary(group => group.Key, group => {
            if (group.Select(release => release.Tag).Distinct().Count() != 1)
                throw new ArgumentException("Dependency resolution selected conflicting versions of a mod.");
            return group.First();
        });
        var pending = pins.Keys.OrderBy(id => id.ToString(), StringComparer.Ordinal).Select(id => (Id: id, Requirement: "*")).ToList();
        var common = manager.GetLatestRelease(CommonMods.UnchainedMods);
        if (includeDependencies && common.IsSome) {
            var release = common.IfNone(() => throw new InvalidOperationException());
            if (pins.TryGetValue(CommonMods.UnchainedMods, out var pin) && pin.Tag != release.Tag)
                throw new ArgumentException("Dependency resolution selected conflicting versions of Unchained-Mods.");
            pins[CommonMods.UnchainedMods] = release;
            if (!pending.Any(item => item.Id == CommonMods.UnchainedMods)) pending.Insert(0, (CommonMods.UnchainedMods, "*"));
        }
        if (!Search(pending, out var error)) throw new InvalidOperationException(error);
        var ordered = new List<Release>();
        var visited = new HashSet<ModIdentifier>();
        var visiting = new HashSet<ModIdentifier>();
        foreach (var id in selected.Keys.OrderBy(id => id == CommonMods.UnchainedMods ? "" : id.ToString(), StringComparer.Ordinal)) Emit(id);
        var plan = new PakLaunchPlan(ordered);
        cached.Value = new CacheEntry(identities, key, plan);
        return plan;

        bool Accept(Release release, string requirement) => requirement == "*" ||
            release.Version is { } version && PluginInfo.MatchesVersion(version.ToString(), requirement);

        bool Search(IReadOnlyList<(ModIdentifier Id, string Requirement)> remaining, out string? failure) {
            cancellationToken.ThrowIfCancellationRequested();
            failure = null;
            if (remaining.Count == 0) return true;
            var (id, requirement) = remaining[0];
            if (selected.TryGetValue(id, out var existing)) {
                if (Accept(existing, requirement)) return Search(remaining.Skip(1).ToArray(), out failure);
                failure = $"Incompatible pak dependency: {id} {requirement}";
                return false;
            }
            IEnumerable<Release> candidates = pins.TryGetValue(id, out var pinned) ? [pinned] :
                metadata.Where(release => ModIdentifier.FromRelease(release) == id && release.Version?.IsRelease == true)
                    .OrderByDescending(release => release.Version).ThenBy(release => release.Tag, StringComparer.Ordinal);
            failure = metadata.Any(release => ModIdentifier.FromRelease(release) == id)
                ? $"Incompatible stable dependency: {id.Org}/{id.ModuleName} {requirement}"
                : $"Missing stable dependency: {id.Org}/{id.ModuleName}";
            foreach (var candidate in candidates.Where(release => Accept(release, requirement))) {
                selected.Add(id, candidate);
                var next = includeDependencies ? candidate.Info.EffectiveModDependencies
                    .Select(dependency => (ModIdentifier.FromDependency(dependency), dependency.Version)).ToList() : [];
                next.AddRange(remaining.Skip(1));
                if (Search(next, out failure)) return true;
                selected.Remove(id);
            }
            return false;
        }

        void Emit(ModIdentifier id) {
            cancellationToken.ThrowIfCancellationRequested();
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidOperationException($"Mod dependencies contain a cycle: {id}");
            var release = selected[id];
            // With automatic downloads disabled, absent dependencies may be supplied by unmanaged development paks.
            foreach (var dependency in release.Info.EffectiveModDependencies.OrderBy(d => d.RepoUrl, StringComparer.Ordinal)) {
                var dependencyId = ModIdentifier.FromDependency(dependency);
                if (!selected.TryGetValue(dependencyId, out var target)) {
                    if (includeDependencies) throw new InvalidOperationException($"Missing pak dependency: {dependencyId}");
                    continue;
                }
                if (!Accept(target, dependency.Version)) throw new InvalidOperationException($"Incompatible pak dependency: {dependencyId}");
                Emit(dependencyId);
            }
            visiting.Remove(id);
            visited.Add(id);
            ordered.Add(release);
        }
    }
}