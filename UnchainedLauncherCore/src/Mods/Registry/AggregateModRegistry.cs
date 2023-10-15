using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncherCore.Mods.Registry {
    public class AggregateModRegistry : ModRegistry {
        
        private List<ModRegistry> Registries;

        public AggregateModRegistry(List<ModRegistry> registries) {
            this.Registries = registries;
        }

        public override DownloadTask<IEnumerable<DownloadTask<Mod>>> GetAllMods() {
            var registryResults = Registries.Select(r => r.GetAllMods());
            var downloadTargetPath = "aggregate://" + string.Join(";", registryResults.Select(r => r.Target.Url));

            var aggregatedResults = registryResults.Select(async r => await r.Task);
            var aggregatedTask = aggregatedResults.Aggregate(async (a, b) => (await a).Concat(await b));

            return new DownloadTask<IEnumerable<DownloadTask<Mod>>>(
                aggregatedTask,
                new DownloadTarget(downloadTargetPath, null)
            );
        }

        public override DownloadTask<string> GetModMetadataString(string modPath) {
            DownloadTask<string>? currentTask = null;

            // Try them in order
            foreach(ModRegistry registry in Registries) {
                if(currentTask == null)
                    currentTask = registry.GetModMetadataString(modPath);
                else
                    currentTask = currentTask.RecoverWith(_ => registry.GetModMetadataString(modPath));
            };

            if (currentTask == null)
                throw new Exception("No registries to try");

            return currentTask;
        }
    }
}
