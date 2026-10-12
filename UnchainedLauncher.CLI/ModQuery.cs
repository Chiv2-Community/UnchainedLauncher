using System.Globalization;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.CLI;

public static class ModQuery {
    public static async Task<int> RunAsync(ModQueryOptions options, IModRegistry registry, TextWriter output, TextWriter error, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (options.Operation == ModQueryOperation.List) {
            var catalog = await registry.GetAllMods().WaitAsync(cancellationToken);
            await output.WriteLineAsync("MOD ID\tNAME\tLATEST VERSION");
            foreach (var mod in catalog.Mods.OrderBy(mod => Id(ModIdentifier.FromMod(mod)), StringComparer.Ordinal)) {
                var latest = Latest(mod);
                await output.WriteLineAsync($"{Id(ModIdentifier.FromMod(mod))}\t{Line(mod.LatestReleaseInfo.Name)}\t{Line(latest?.Tag ?? "none")}");
            }
            foreach (var problem in catalog.Errors) await error.WriteLineAsync(problem.Message);
            return catalog.HasErrors ? 1 : 0;
        }
        var result = await registry.GetMod(options.Mod!).ToEither().WaitAsync(cancellationToken);
        if (result.IsLeft) {
            await error.WriteLineAsync(result.Match(_ => "", problem => problem.Message));
            return 1;
        }
        var found = result.Match(mod => mod, _ => throw new InvalidOperationException());
        if (options.Operation == ModQueryOperation.Versions) {
            await output.WriteLineAsync("VERSION\tRELEASE DATE\tCHANNEL");
            foreach (var release in found.Releases.OrderByDescending(release => release.Version).ThenByDescending(release => release.ReleaseDate))
                await output.WriteLineAsync($"{Line(release.Tag)}\t{release.ReleaseDate.ToString("O", CultureInfo.InvariantCulture)}\t{(release.Version?.IsRelease == true ? "stable" : "prerelease/non-semver")}");
            return 0;
        }
        var selected = options.Version is null ? Latest(found) : found.Releases.Find(release => release.Tag == options.Version);
        if (selected is null) {
            await error.WriteLineAsync($"Release not found: {Id(options.Mod!)}:{options.Version ?? "latest"}");
            return 1;
        }
        var size = await registry.GetPakSize(selected, cancellationToken);
        await output.WriteLineAsync($"Mod ID: {Id(options.Mod!)}\nName: {Line(selected.Info.Name)}\nVersion: {Line(selected.Tag)}\nDescription: {Line(selected.Info.Description)}");
        await output.WriteLineAsync($"Authors: {string.Join(", ", selected.Info.Authors.Select(Line))}\nRepository: {Line(selected.Info.RepoUrl)}\nHomepage: {Line(selected.Info.HomePage ?? "none")}\nRelease date: {selected.ReleaseDate.ToString("O", CultureInfo.InvariantCulture)}");
        await output.WriteLineAsync($"Pak file: {Line(selected.PakFileName)}\nPak hash (SHA-512): {Line(selected.ReleaseHash)}\nSize: {(size is null ? "unknown (not available from registry/file)" : size.Value.ToString(CultureInfo.InvariantCulture) + " bytes")}");
        await output.WriteLineAsync($"Dependencies ({selected.Info.Dependencies.Count}):");
        foreach (var dependency in selected.Info.Dependencies)
            await output.WriteLineAsync($"  {Id(ModIdentifier.FromDependency(dependency))}: {Line(dependency.Version)}");
        await output.WriteLineAsync($"Maps ({selected.Manifest.Maps.Count}):");
        foreach (var map in selected.Manifest.Maps)
            await output.WriteLineAsync($"  {Line(map.MapName ?? map.Path)} | {Line(map.Path)} | mode: {Line(map.GameMode ?? map.GamemodeType ?? "unknown")}");
        await output.WriteLineAsync($"Blueprints ({selected.Manifest.Blueprints.Count}):");
        foreach (var blueprint in selected.Manifest.Blueprints)
            await output.WriteLineAsync($"  {Line(blueprint.ModName ?? blueprint.Path)} | {Line(blueprint.ClassPath ?? blueprint.Path)} | client-side: {blueprint.bClientside?.ToString() ?? "unknown"}");
        await output.WriteLineAsync($"Markers: {selected.Manifest.Markers.Count}\nReplacements: {selected.Manifest.Replacements.Count}\nOther assets: {selected.Manifest.Arbitrary.Count}\nRelease notes: {Line(selected.ReleaseNotesMarkdown ?? "none")}");
        return 0;
    }

    private static Release? Latest(Mod mod) {
        var stable = mod.LatestRelease;
        return stable.IsSome ? stable.IfNone(() => throw new InvalidOperationException())
            : mod.Releases.OrderByDescending(release => release.ReleaseDate).FirstOrDefault();
    }
    internal static string Id(ModIdentifier id) => Line(id.Org + "/" + id.ModuleName);
    internal static string Line(string value) => new(value.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
}
