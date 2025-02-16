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

        private OptionAsync<string> GetHashResult(EitherAsync<HashFailure, Option<string>> result) {
            return result.TapLeft<HashFailure, Option<string>, object>(e => 
                _logger.Error($"Failed to hash file", e)
                )
                .ToOption()
                .Bind(o => o.Match(OptionAsync<string>.Some, OptionAsync<string>.None));
        }

        private OptionAsync<string> GetLocalHash(ReleaseCoordinates coords) {
            return _pakDir.GetInstalledPakFile(coords)
                .Match(
                    path => GetHashResult(FileHelpers.Sha512Async(path)),
                    () => OptionAsync<string>.None);

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

            var hashValidationTasks = metas
                .Map(m => 
                    GetLocalHash(ReleaseCoordinates.FromRelease(m))
                        .Map(h => (m, h, m.ReleaseHash))
                        // select only those whose hashes do not match
                        .Filter(o => !o.Item2.Equals(o.Item3))
                        .IfSome(b => {
                            var coords = ReleaseCoordinates.FromRelease(b.Item1);
                            _logger.Warn($"Hash mismatch: actual hash '{b.Item2}' does not " +
                                         $"match release hash '{b.Item3}' for '{coords}'.");
                            
                            var redownloadResponse = _userDialogueSpawner.DisplayYesNoMessage(
                                $"The hash for release {coords} is not correct. " +
                                $"This indicates that the file on disk does not match " +
                                $"what is approved by the mod registry. This could be due to " +
                                $"simple file corruption, but could also indicate that the version " +
                                $"you have is malicious.\n\n" +
                                $"Expected: {b.Item3}\n" +
                                $"Actual: {b.Item2}\n\n" +
                                $"Would you like to re-download the pak? (recommended)", "Refresh pak file?");

                            if (redownloadResponse == UserDialogueChoice.Yes) {
                                _pakDir.Uninstall(coords)
                                    .IfLeft(e =>
                                        _logger.Error($"Failed to uninstall release '{coords}' with invalid hash", e));
                            }
                            
                            return Unit.Default;
                        })
                );
            
            await Task.WhenAll(hashValidationTasks);
                

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