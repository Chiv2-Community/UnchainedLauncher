using LanguageExt;
using LanguageExt.Common;
using log4net;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers {
    using static LanguageExt.Prelude;
    public class Chivalry2ModsInstaller : IChivalry2LaunchPreparer<ModdedLaunchOptions> {
        private readonly ILog _logger = LogManager.GetLogger(typeof(Chivalry2ModsInstaller));
        private readonly IUserDialogueSpawner _userDialogueSpawner;
        private IModRegistry _modRegistry { get; }
        public Chivalry2ModsInstaller(IModRegistry registry, IUserDialogueSpawner dialogueSpawner) {
            _modRegistry = registry;
            _userDialogueSpawner = dialogueSpawner;
        }
        
        public async Task<Option<ModdedLaunchOptions>> PrepareLaunch(ModdedLaunchOptions options) {
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
                    ).SplitEithers();

            var failuresCount = failures.Fold(0, (c, e) => {
                _logger.Error($"Failed to get registry metadata: {e.Message}");
                return c + 1;
            });

            if (failuresCount != 0) {
                return None;
            }
            
            var downloadsMap = new Map<ReleaseCoordinates, Release>()
                .AddRange(releases.Zip(metas))
                .Filter(release => {
                    var alreadyExists = File.Exists(Path.Join(FilePaths.PakDir, release.PakFileName));
                    if (alreadyExists) {
                        _logger.Info($"Not overwriting already installed mod {release.PakFileName}");
                    }
                    
                    return !alreadyExists;
                });
            
            var cts = new CancellationTokenSource(6000);
            
            // TODO: display download progress as a popup or something
            var downloads = await Task.WhenAll(
                downloadsMap.Pairs.Map(
                    p => _modRegistry.DownloadPak(p.Item1, Path.Join(FilePaths.PakDir, p.Item2.PakFileName))
                ).Map(
                    e => e.Map(
                        writer => writer.WriteAsync(None, cts.Token)
                        )
                    ).Map(e => e.MatchAsync(
                        r => r.Match(
                                ri => None,
                                Some<Error>
                            ),
                        Some<Error>
                    )
                )
                );

            var downloadFailureCount = downloads
                .Choose(o => o)
                .Fold(0, (s, e) => {
                    _logger.Error($"Failed to download mod: {e.Message}");
                    return s + 1;
                });

            if (downloadFailureCount != 0) {
                return None;
            }

            return options;
        }
    }
}