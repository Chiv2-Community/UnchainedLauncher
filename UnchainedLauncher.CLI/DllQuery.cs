using Semver;
using System.Globalization;
using UnchainedLauncher.Core.Services.Mods.Registry;
using static UnchainedLauncher.CLI.ModQuery;

namespace UnchainedLauncher.CLI;

public static class DllQuery {
    public static async Task<int> RunAsync(ModQueryOptions options, AggregateModRegistry registry, TextWriter output, TextWriter error, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        var catalog = await DllCatalog.ReadAsync(registry, token);
        foreach (var problem in catalog.Errors) await error.WriteLineAsync(problem);
        if (options.Operation == ModQueryOperation.List) {
            await output.WriteLineAsync("DLL ID\tNAME\tLATEST VERSION");
            foreach (var package in catalog.Packages)
                await output.WriteLineAsync($"{Id(package.Identifier)}\t{Line(package.LatestReleaseInfo?.Name ?? package.Releases[0].Name)}\t{Line(package.LatestRelease?.Tag ?? package.LatestRelease?.Version ?? "none")}");
            return catalog.Errors.Count == 0 ? 0 : 1;
        }
        var found = catalog.Packages.FirstOrDefault(package => options.Mod!.Matches(package.Identifier));
        if (found is null) {
            await error.WriteLineAsync($"DLL not found or registry unavailable: {Id(options.Mod!)}");
            return 1;
        }
        if (options.Operation == ModQueryOperation.Versions) {
            await output.WriteLineAsync("VERSION\tRELEASE DATE\tTARGET");
            foreach (var release in found.Releases.OrderByDescending(release => SemVersion.Parse(release.Version, SemVersionStyles.Strict)).ThenByDescending(release => release.ReleaseDate))
                await output.WriteLineAsync($"{Line(release.Tag ?? release.Version)}\t{Date(release.ReleaseDate)}\t{Line(release.Info?.Target ?? "shared")}");
        }
        else {
            var selected = options.Version is null ? found.LatestRelease : found.Releases.FirstOrDefault(release => (release.Tag ?? release.Version) == options.Version);
            if (selected is null) {
                await error.WriteLineAsync($"DLL release not found: {Id(options.Mod!)}:{options.Version ?? "latest"}");
                return 1;
            }
            await output.WriteLineAsync($"DLL ID: {Id(options.Mod!)}\nName: {Line(selected.Info?.Name ?? selected.Name)}\nVersion: {Line(selected.Tag ?? selected.Version)}\nDescription: {Line(selected.Info?.Description ?? "Metadata unavailable for this historical release.")}\nTarget: {Line(selected.Info?.Target ?? "shared")}");
            await output.WriteLineAsync($"Authors: {string.Join(", ", (selected.Info?.Authors ?? []).Select(Line))}\nRepository: {Line("https://github.com/" + selected.Id)}\nRelease date: {Date(selected.ReleaseDate)}");
            await output.WriteLineAsync($"DLL file: {Line(selected.EffectiveFileName)}\nHash (SHA-256): {Line(selected.Sha256)}\nSize: {selected.Size.ToString(CultureInfo.InvariantCulture)} bytes\nDownload URL: {Line(selected.DownloadUrl)}");
            var plugins = selected.Info?.RequiredPlugins ?? [];
            var mods = selected.Info?.RequiredMods ?? [];
            await output.WriteLineAsync($"Plugin dependencies ({plugins.Count}):");
            foreach (var dependency in plugins)
                await output.WriteLineAsync($"  {Id(ModIdentifier.FromDependency(dependency))}: {Line(dependency.Version)}");
            await output.WriteLineAsync($"Mod dependencies ({mods.Count}):");
            foreach (var dependency in mods)
                await output.WriteLineAsync($"  {Id(ModIdentifier.FromDependency(dependency))}: {Line(dependency.Version)}");
            await output.WriteLineAsync($"Release notes: {Line(selected.ReleaseNotesMarkdown ?? "none")}");
        }
        return catalog.Errors.Count == 0 ? 0 : 1;
    }

    private static string Date(DateTimeOffset? date) => (date ?? DateTimeOffset.MinValue).ToString("O", CultureInfo.InvariantCulture);
}