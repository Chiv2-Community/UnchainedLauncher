using LanguageExt;
using LanguageExt.Common;
using log4net;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.PakDir;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers {
    using static LanguageExt.Prelude;

    public class Chivalry2ModsInstaller : IChivalry2LaunchPreparer<LaunchOptions> {
        private readonly ILog _logger = LogManager.GetLogger(typeof(Chivalry2ModsInstaller));
        private readonly IUserDialogueSpawner _userDialogueSpawner;
        private readonly IPakDir _pakDir;
        private IModRegistry _modRegistry { get; }
        public Chivalry2ModsInstaller(IModRegistry registry, IPakDir pakDir, IUserDialogueSpawner dialogueSpawner) {
            _modRegistry = registry;
            _pakDir = pakDir;
            _userDialogueSpawner = dialogueSpawner;
        }

        public async Task<Option<LaunchOptions>> PrepareLaunch(LaunchOptions options) {
            IEnumerable<ReleaseCoordinates> releases = options.EnabledReleases;
            var releasesList = new List<ReleaseCoordinates>(releases);
            _logger.LogListInfo("Installing the following releases:", releasesList);
            // get release metadatas
            var (failures, metas) =
                (
                    await Task.WhenAll(
                        releasesList
                        .Map(_modRegistry.GetModRelease)
                        .Map(async t => await t)
                    )
                    ).Partition();

            var failuresCount = failures.Fold(0, (c, e) => {
                _logger.Error($"Failed to get registry metadata: {e.Message}");
                return c + 1;
            });

            if (failuresCount != 0) {
                return None;
            }

            // TODO: do something more clever than just deleting. This weirdness ultimately comes
            // from the fact that launches share a pak dir, and can affect each other.
            // This stuff is only really relevant to servers, and there's probably not much
            // we can do without adding some seriously in-depth hooks in the plugin
            // that allows doing some kind of per-process pak dir isolation
            var progresses = new AccumulatedMemoryProgress(taskName: "Installing releases");
            var installOnlyResult = _pakDir.InstallOnly(
                metas.Map<Release, (ReleaseCoordinates, IPakDir.MakeFileWriter, string)>(m => {
                        var coords = ReleaseCoordinates.FromRelease(m);
                    
                        return (
                            coords,
                            (outputPath) => _modRegistry.DownloadPak(coords, outputPath)
                                .MapLeft(e => Error.New(e)),
                            m.PakFileName
                        );
                    }
                ), progresses
            ).Match(
                _ => Some(options),
                e => {
                    _logger.Error($"Failed to install releases: {e.Message}");
                    _userDialogueSpawner.DisplayMessage($"There was an error while installing releases: {e.Message}");
                    return None;
                });



            var closeProgressWindow = _userDialogueSpawner.DisplayProgress(progresses);
                
            var finalResult = await installOnlyResult;

            closeProgressWindow();
            
            return finalResult;

        }
    }
}