using LanguageExt;
using LanguageExt.Common;
using log4net;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers {
    using static LanguageExt.Prelude;

    // TODO: turn this into a class that overall represents the pak subdir.
    // Anything that wants to add, remove, or check paks in the paks subdir
    // does so through that object. We have a lot of that behavior happening
    // manually when there is actually a specific set of operations 
    // that we want to model.
    public class LastInstalledPakVersionMeta {
        ILog _logger = LogManager.GetLogger(typeof(LastInstalledPakVersionMeta));
        public IEnumerable<ReleaseCoordinates> Coordinates { get; private set; }
        private readonly string _infoFilePath;
        public LastInstalledPakVersionMeta(string infoFilePath) {
            Coordinates = ReadCoordinates(infoFilePath);
            _infoFilePath = infoFilePath;
        }

        public bool UpdateVersion(ReleaseCoordinates coordinates) {
            if (Coordinates.Any(c => c.Matches(coordinates) && c.Version == coordinates.Version))
                return false;

            Coordinates = Coordinates
                .Filter(c => !c.Matches(coordinates))
                .Append(coordinates);
            return true;
        }

        private IEnumerable<ReleaseCoordinates> ReadCoordinates(string infoFilePath) {
            return Try(() => File.ReadAllText(infoFilePath))()
                .Match(
                    Some,
                    e => {
                        if (e is FileNotFoundException or DirectoryNotFoundException) {
                            return None;
                        }

                        _logger.Error($"Could not read installed paks metadata file {infoFilePath}: {e}");
                        return None;
                    }
                ).Map(
                    contents => JsonHelpers
                        .Deserialize<IEnumerable<ReleaseCoordinates>>(contents)
                        .ToEither()
                        .Match(
                            Some,
                            e => {
                                _logger.Error($"Couldn't parse installed paks metadata file", e);
                                return None;
                            })
                )
                .Flatten()
                .Match(
                    s => s,
                    () => new List<ReleaseCoordinates>()
                );
        }

        public void Save() {
            try {
                var serialized = JsonHelpers.Serialize(Coordinates);
                File.WriteAllText(_infoFilePath, serialized);
            }
            catch (Exception e) {
                _logger.Error("Failed to write installed paks metadata file", e);
            }
        }
    }


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
                    ).Partition();

            var failuresCount = failures.Fold(0, (c, e) => {
                _logger.Error($"Failed to get registry metadata: {e.Message}");
                return c + 1;
            });

            if (failuresCount != 0) {
                return None;
            }

            // delete any paks not mentioned in this launch
            // TODO: do something more clever than just deleting. This weirdness ultimately comes
            // from the fact that launches share a pak dir, and can affect each other.
            // This stuff is only really relevant to servers, and there's probably not much
            // we can do without adding some seriously in-depth hooks in the plugin
            // that allows doing some kind of per-process pak dir isolation
            var modFileNames = metas.Map(m => m.PakFileName);
            foreach (string file in Directory.EnumerateFiles(FilePaths.PakDir)
                     .Filter(f => f.Contains(".pak"))
                     .Filter(f => !f.Contains("pakchunk0-WindowsNoEditor.pak"))
                     .Filter(f => !modFileNames.Any(n => f.Contains(n)))) {
                try {
                    File.Delete(file);
                }
                catch (Exception e) {
                    _logger.Error($"Failed to delete '{file}'", e);
                }
            }

            // tracks versions of installed paks
            var installedFiles = new LastInstalledPakVersionMeta(FilePaths.LastInstalledPakVersionList);
            var downloadsMap = new Map<ReleaseCoordinates, Release>()
                .AddRange(releases.Zip(metas))
                .Filter(release => {
                    var alreadyExists = File.Exists(Path.Join(FilePaths.PakDir, release.PakFileName));
                    var isDifferentVersion = installedFiles.UpdateVersion(ReleaseCoordinates.FromRelease(release));
                    if (isDifferentVersion || !alreadyExists) {
                        return true;
                    }

                    _logger.Info($"Not overwriting already installed and versioned mod {release.PakFileName}");
                    return false;

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
                .Choose(identity)
                .Fold(0, (s, e) => {
                    _logger.Error($"Failed to download mod: {e.Message}");
                    return s + 1;
                });

            if (downloadFailureCount != 0) {
                return None;
            }
            // save new versions of paks in this dir
            installedFiles.Save();

            return options;
        }
    }
}