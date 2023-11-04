using LanguageExt;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Mods.Registry.Resolver;
using UnchainedLauncher.Core.Utilities;
using UnchainedLauncher.Core.Mods.Registry;

namespace UnchainedLauncher.Core.Mods.Registry {
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
                        .Select(t => new GetAllModsResult(t.Item1, t.Item2))
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
