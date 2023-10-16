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

namespace UnchainedLauncher.Core.Mods.Registry {
    public class LocalModRegistry : ModRegistry {

        public string RegistryPath { get; }
        public LocalModRegistry(string registryPath, ModRegistryDownloader downloader) : base("Local registry at: " + registryPath, downloader) {
            RegistryPath = registryPath;
        }

        public override Task<(IEnumerable<RegistryMetadataException>, IEnumerable<Mod>)> GetAllMods() {
            // List all json files found in the registry path
            var emptyResult =
                new Tuple<ImmutableList<RegistryMetadataException>, ImmutableList<Mod>>(
                    ImmutableList<RegistryMetadataException>.Empty,
                    ImmutableList<Mod>.Empty
                );

            return Task
                .Run(() => Directory.EnumerateFiles(RegistryPath, "*.json", SearchOption.AllDirectories))
                .Bind(paths =>
                    paths.ToImmutableList()
                        .Select(GetModMetadata)
                        .Partition()
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
