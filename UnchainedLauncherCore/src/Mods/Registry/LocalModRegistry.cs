using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncherCore.Mods.Registry {
    public class LocalModRegistry : ModRegistry {
        public string RegistryPath { get; }
        public LocalModRegistry(string registryPath) {
            RegistryPath = registryPath;
        }

        public override DownloadTask<IEnumerable<DownloadTask<Mod>>> GetAllMods() {
            // List all json files found in the registry path
            var modPaths = Directory.EnumerateFiles(RegistryPath, "*.json", System.IO.SearchOption.AllDirectories);
            return new DownloadTask<IEnumerable<DownloadTask<Mod>>>(
                Task.FromResult(modPaths.Select(GetModMetadata)),
                new DownloadTarget($"file://{RegistryPath}", null)
            );
        }

        public override DownloadTask<string> GetModMetadataString(string modPath) {
            var path = Path.Combine(RegistryPath, modPath);
            return new DownloadTask<string>(
                Task.FromResult(File.ReadAllText(path)), 
                new DownloadTarget($"file://{path}", null)
            );
        }
    }
}
