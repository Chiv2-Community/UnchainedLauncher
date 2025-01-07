using LanguageExt;
using System.Collections.Immutable;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;

namespace UnchainedLauncher.Core.Services.Mods.Registry {
    public class LocalModRegistry : JsonRegistry {
        public override string Name => $"Local registry at {RegistryPath}";
        public override IModRegistryDownloader ModRegistryDownloader { get; }

        public string RegistryPath { get; }
        public LocalModRegistry(string registryPath, IModRegistryDownloader downloader) {
            RegistryPath = registryPath;
            ModRegistryDownloader = downloader;
        }

        public override Task<GetAllModsResult> GetAllMods() {
            return Task
                .Run(() => Directory.EnumerateFiles(RegistryPath, "*.json", SearchOption.AllDirectories))
                .Bind(paths =>
                    paths.ToImmutableList()
                        .Select(GetModMetadata)
                        .Partition()
                        .Select(t => new GetAllModsResult(t.Lefts, t.Rights))
                );
        }

        public override EitherAsync<RegistryMetadataException, string> GetModMetadataString(string modPath) {
            return EitherAsync<RegistryMetadataException, string>
                .Right(Path.Combine(RegistryPath, modPath))
                .BindAsync(path => Task.Run(() => {
                    if (!File.Exists(path))
                        return EitherAsync<RegistryMetadataException, string>
                            .Left(new RegistryMetadataException(modPath, new IOException("File not found")));
                    else
                        return Prelude
                            .TryAsync(Task.Run(() => File.ReadAllText(path)))
                            .ToEither()
                            .MapLeft(e => new RegistryMetadataException(modPath, e));
                }));
        }
    }
}