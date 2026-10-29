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
                await output.WriteLineAsync($"{Id(package.LatestReleaseInfo.Identifier)}\t{Line(package.LatestReleaseInfo.Name)}\t{Line(package.LatestRelease?.Tag ?? "none")}");
            return catalog.Errors.Count == 0 ? 0 : 1;
        }
        var found = catalog.Packages.FirstOrDefault(package => options.Mod!.Matches(package.LatestReleaseInfo.Identifier));
        if (found is null) {
            await error.WriteLineAsync($"DLL not found or registry unavailable: {Id(options.Mod!)}");
            return 1;
        }
        if (options.Operation == ModQueryOperation.Versions) {
            await output.WriteLineAsync("VERSION\tRELEASE DATE\tTARGET");
            foreach (var release in found.Releases.OrderByDescending(release => release.Version).ThenByDescending(release => release.ReleaseDate))
                await output.WriteLineAsync($"{Line(release.Tag)}\t{release.ReleaseDate.ToString("O", CultureInfo.InvariantCulture)}\t{Line(release.Info.Target)}");
        }
        else {
            var selected = options.Version is null ? found.LatestRelease : found.Releases.Find(release => release.Tag == options.Version);
            if (selected is null) {
                await error.WriteLineAsync($"DLL release not found: {Id(options.Mod!)}:{options.Version ?? "latest"}");
                return 1;
            }
            await output.WriteLineAsync($"DLL ID: {Id(options.Mod!)}\nName: {Line(selected.Info.Name)}\nVersion: {Line(selected.Tag)}\nDescription: {Line(selected.Info.Description)}\nTarget: {Line(selected.Info.Target)}");
            await output.WriteLineAsync($"Authors: {string.Join(", ", selected.Info.Authors.Select(Line))}\nRepository: {Line(selected.Info.RepoUrl)}\nRelease date: {selected.ReleaseDate.ToString("O", CultureInfo.InvariantCulture)}");
            await output.WriteLineAsync($"DLL file: {Line(selected.DllFileName)}\nHash (SHA-256): {Line(selected.Hash)}\nSize: {selected.Size.ToString(CultureInfo.InvariantCulture)} bytes\nDownload URL: {Line(selected.DownloadUrl)}");
            await output.WriteLineAsync($"Plugin dependencies ({selected.Info.PluginDependencies.Count}):");
            foreach (var dependency in selected.Info.PluginDependencies)
                await output.WriteLineAsync($"  {Id(ModIdentifier.FromDependency(dependency))}: {Line(dependency.Version)}");
            await output.WriteLineAsync($"Mod dependencies ({selected.Info.ModDependencies.Count}):");
            foreach (var dependency in selected.Info.ModDependencies)
                await output.WriteLineAsync($"  {Id(ModIdentifier.FromDependency(dependency))}: {Line(dependency.Version)}");
            await output.WriteLineAsync($"Release notes: {Line(selected.ReleaseNotesMarkdown ?? "none")}");
        }
        return catalog.Errors.Count == 0 ? 0 : 1;
    }
}