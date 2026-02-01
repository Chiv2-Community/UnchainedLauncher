using LanguageExt;
using LanguageExt.Common;
using log4net;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.JsonModels.Metadata.V3;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Services.PakDir;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers {
    using static LanguageExt.Prelude;

    public class Chivalry2ModsInstaller : IChivalry2LaunchPreparer<LaunchOptions> {
        private readonly ILog _logger = LogManager.GetLogger(typeof(Chivalry2ModsInstaller));
        private readonly IUserDialogueSpawner _userDialogueSpawner;
        private readonly IPakDir _pakDir;
        private IModManager _modManager { get; }
        public Chivalry2ModsInstaller(IModManager modManager, IPakDir pakDir, IUserDialogueSpawner dialogueSpawner) {
            _modManager = modManager;
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
            return _pakDir.GetManagedPakFilePath(coords)
                .Match(
                    path => GetHashResult(FileHelpers.Sha512Async(path)),
                    () => OptionAsync<string>.None);
        }

        private Unit IfHashInvalid((Release, string, string) validationResult) {
            var coords = ReleaseCoordinates.FromRelease(validationResult.Item1);
            _logger.Warn($"Hash mismatch: actual hash '{validationResult.Item2}' does not " +
                         $"match release hash '{validationResult.Item3}' for '{coords}'.");

            var redownloadResponse = _userDialogueSpawner.DisplayYesNoMessage(
                $"The hash for release {coords} is not correct. " +
                $"This indicates that the file on disk does not match " +
                $"what is approved by the mod registry. This could be due to " +
                $"simple file corruption, but could also indicate that the version " +
                $"you have is malicious.\n\n" +
                $"Expected: {validationResult.Item3}\n" +
                $"Actual: {validationResult.Item2}\n\n" +
                $"Would you like to re-download the pak? (recommended)", "Refresh pak file?");

            if (redownloadResponse == UserDialogueChoice.Yes) {
                _pakDir.Uninstall(coords)
                    .IfLeft(e =>
                        _logger.Error($"Failed to uninstall release '{coords}' with invalid hash", e));
            }

            return Unit.Default;
        }

        private async Task ValidateHashes(IEnumerable<Release> releases) {
            var hashValidationTasks = releases
                .Map(m =>
                    GetLocalHash(ReleaseCoordinates.FromRelease(m))
                        .Map(h => (m, h, m.ReleaseHash))
                        // select only those whose hashes do not match
                        .Filter(o => !o.Item2.Equals(o.Item3))
                        .IfSome(IfHashInvalid)
                );

            await Task.WhenAll(hashValidationTasks);
        }

        public async Task<Option<LaunchOptions>> PrepareLaunch(LaunchOptions options) {
            IEnumerable<ReleaseCoordinates> releases = options.EnabledReleases;
            var releasesList = new List<ReleaseCoordinates>(releases);
            _logger.LogListInfo("Installing the following releases:", releasesList);
            // get release metadatas
            var (errs, metas) =
                releasesList
                    .Select(x => _modManager.GetRelease(x).ToEither(x))
                    .Partition()
                    .BiMap(x => x.ToList(), x => x.ToList());

            if (errs.Any()) {
                _logger.Error($"Failed to get release metadatas for: {string.Join(", ", errs)}");
                var humanReadableFailedVersions = string.Join(", ", errs.Select(x => $"{x.ModuleName} {x.Version}"));
                var selection = _userDialogueSpawner.DisplayYesNoMessage($"Failed to get release metadatas for: {humanReadableFailedVersions}. Would you like to proceed anyway? The listed mods will not be downloaded.", "Failed to get release metadatas");

                if (selection == UserDialogueChoice.No) return None;
            }

            var enumerable = metas.ToList();
            await ValidateHashes(enumerable);

            // TODO: do something more clever than just deleting. This weirdness ultimately comes
            // from the fact that launches share a pak dir, and can affect each other.
            // This stuff is only really relevant to servers, and there's probably not much
            // we can do without adding some seriously in-depth hooks in the plugin
            // that allows doing some kind of per-process pak dir isolation
            var progresses = new AccumulatedMemoryProgress(taskName: "Installing releases");
            var closeProgressWindow = _userDialogueSpawner.DisplayProgress(progresses);
            var installOnlyResults =
                await _pakDir.InstallModSet(
                    enumerable.Map(m => {
                        var coords = ReleaseCoordinates.FromRelease(m);

                        return new ModInstallRequest(
                            coords,
                            (outputPath) =>
                                _modManager.ModRegistry
                                    .DownloadPak(coords, outputPath)
                                    .MapLeft(e => Error.New(e))
                        );
                    }
                    ), progresses
                ).ToListAsync();

            var (errors, successes) =
                installOnlyResults.Partition()
                    .MapFirst(x => x.ToList())
                    .MapSecond(x => x.ToList());

            successes.ForEach(succ => _logger.Debug($"Successfully installed {succ.Coordinates.ModuleName} version {succ.Coordinates.Version}"));

            if (errors.Count != 0) {
                errors.ToList().ForEach(e => _logger.Error($"Failed to install releases: {e.Message}"));
                var shouldContinue = _userDialogueSpawner.DisplayYesNoMessage(
                    $"There were errors while installing releases:\n " +
                    $"{errors.Aggregate((a, b) => $"{a}\n\n{b}")}\n" +
                    "Would you like to continue anyway?", "Errors encountered"
                    );
                if (shouldContinue == UserDialogueChoice.No) return None;
            }

            closeProgressWindow();

            return Some(options);
        }
    }
}