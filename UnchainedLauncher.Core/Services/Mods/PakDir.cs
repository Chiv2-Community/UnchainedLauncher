using LanguageExt;
using LanguageExt.Common;
using log4net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.JsonModels.ModMetadata;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Mods {
    using static LanguageExt.Prelude;

    public class PakDir : IPakDir {
        public string DirPath { get; }

        private const string BasePakFileName = "pakchunk0-WindowsNoEditor.pak";
        private const string BaseSigFileName = "pakchunk0-WindowsNoEditor.sig";
        private const string ForcedSortDivider = "__-__";

        private static readonly ILog Logger = LogManager.GetLogger(nameof(PakDir));

        public IEnumerable<ManagedPak> ManagedPaks => _managedPaks;
        private readonly List<ManagedPak> _managedPaks;
        private readonly PakInventory _inventory;

        public PakDir(string dirPath, IEnumerable<ManagedPak> managedPaks)
            : this(dirPath, managedPaks, TimeProvider.System) { }

        internal PakDir(string dirPath, IEnumerable<ManagedPak> managedPaks, TimeProvider timeProvider) {
            DirPath = dirPath;
            _managedPaks = managedPaks.ToList();
            _inventory = new PakInventory(dirPath, timeProvider);
            SynchronizeWithDir();
        }

        private IEnumerable<string> GetSigFiles() {
            return Directory.EnumerateFiles(DirPath, "*.sig");
        }

        private IEnumerable<string> GetModPakFiles() {
            return Directory.EnumerateFiles(DirPath, "*.pak")
                .Filter(f => !f.EndsWith(BasePakFileName));
        }

        /// <summary>
        /// Helper for deleting files using try
        /// </summary>
        /// <param name="path">file name to delete</param>
        /// <returns></returns>
        private static Either<Error, Unit> _deleteFile(string path) {
            return PrimitiveExtensions
                .TryVoid(() => File.Delete(path))
                .Invoke()
                .Match<Either<Error, Unit>>(
                    _ => Right(Unit.Default),
                    e => Error.New($"Failed to delete file: '{path}'", (Exception)e)
                    );
        }

        /// <summary>
        /// Helper for copying files
        /// </summary>
        /// <param name="path"></param>
        /// <param name="destPath"></param>
        /// <returns></returns>
        private static Either<Error, Unit> _copyFile(string path, string destPath) {
            return PrimitiveExtensions
                .TryVoid(() => File.Copy(path, destPath))
                .Invoke()
                .Match<Either<Error, Unit>>(
                    _ => Right(Unit.Default),
                    e => Error.New($"Failed to copy file: '{path}' -> '{destPath}'", (Exception)e)
                );
        }

        public async Task<Either<Error, Unit>> Uninstall(ModIdentifier coords) {
            try {
                using var inventoryLock = await _inventory.Lock();
                ReloadInventory();
                _inventory.Save(_managedPaks);
                _inventory.BeginChange();
                var result = UninstallCore(coords);
                SynchronizeWithDir();
                _inventory.Save(_managedPaks);
                return result;
            }
            catch (Exception ex) {
                return Error.New("Failed to update managed pak inventory", ex);
            }
        }

        private Either<Error, Unit> UninstallCore(ModIdentifier coords) =>
            GetManagedPakFile(coords)
                .Match(
                    Some: pak => Right<Error, ManagedPak>(pak)
                        .Bind(x => _unsignFile(PakNameToPakPath(x.PakFileName))
                            .Bind(_ => _deleteFile(PakNameToPakPath(x.PakFileName)))),
                    None: () => {
                        Logger.Debug($"No pak file found for {coords}. Ignoring Uninstall request");
                        return Right<Error, Unit>(Unit.Default);
                    }
                );

        private string AvailablePakName(string suggestedFileName) {
            var unManagedPaks = GetUnmanagedPaks().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actualName = Path.GetFileNameWithoutExtension(suggestedFileName);
            var extension = Path.GetExtension(suggestedFileName);
            var fullActualName = actualName + extension;
            while (_managedPaks.Exists(x => x.PakFileName.Equals(fullActualName, StringComparison.OrdinalIgnoreCase))
                   || unManagedPaks.Contains(PakNameToPakPath(fullActualName))) {
                actualName = Successors.TextualSuccessor(actualName);
                fullActualName = actualName + extension;
            }
            return fullActualName;
        }

        private async Task<Either<Error, string>> _writePak(IPakDir.MakeFileWriter mkFileWriter, Option<IProgress<double>> progress, string suggestedFileName) {
            var fullActualName = AvailablePakName(suggestedFileName);
            var stagingPath = PakNameToPakPath(fullActualName + ".partial");
            Logger.Info($"Downloading pak to {fullActualName}");
            // Reserve a new file before invoking a writer that can overwrite its destination.
            FileStream reservation;
            try {
                reservation = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException
                || ex is IOException && (ex.HResult & 0xffff) is 80 or 183
                || ex is IOException && !OperatingSystem.IsWindows() && (ex.HResult & 0xffff) == 17) {
                // Creation was denied or the name exists: no pak changed. Other I/O failures remain uncertain.
                return Error.New($"Failed to reserve pak file '{fullActualName}'", ex);
            }
            using (reservation) { }
            var completed = false;
            try {
                var result = await mkFileWriter(stagingPath)
                .Bind(fileWriter =>
                    // TODO: allow passing of a cancellation token here
                    fileWriter.WriteAsync(progress, CancellationToken.None)
                    .Map(_ => {
                        File.Move(stagingPath, PakNameToPakPath(fullActualName));
                        return fullActualName;
                    })
                );
                completed = result.IsRight;
                return result;
            }
            catch (Exception ex) {
                return Error.New($"Failed to download pak '{fullActualName}'", ex);
            }
            finally {
                if (!completed) {
                    _deleteFile(stagingPath)
                        .IfLeft(e => throw new IOException($"Failed to remove incomplete pak download: {e}"));
                }
            }
        }

        public IAsyncEnumerable<Either<Error, ManagedPak>> InstallModSet(
                IEnumerable<ModInstallRequest> installs,
                Option<AccumulatedMemoryProgress> progress) => InstallModSetLocked(installs, progress);

        private async IAsyncEnumerable<Either<Error, ManagedPak>> InstallModSetLocked(
                IEnumerable<ModInstallRequest> installs,
                Option<AccumulatedMemoryProgress> progress,
                [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            FileStream? inventoryLock = null;
            Error? inventoryError = null;
            try {
                inventoryLock = await _inventory.Lock(cancellationToken);
                ReloadInventory();
                // Publish migration and check writability before changing any pak files.
                _inventory.Save(_managedPaks);
            }
            catch (OperationCanceledException) {
                inventoryLock?.Dispose();
                throw;
            }
            catch (Exception ex) {
                inventoryError = Error.New("Failed to load managed pak inventory", ex);
            }
            using (inventoryLock) {
                if (inventoryError != null) {
                    yield return inventoryError;
                    yield break;
                }
                // Publish each result before yielding, including successful partial preparations.
                await using var results = InstallModSetCore(installs, progress).GetAsyncEnumerator();
                while (true) {
                    cancellationToken.ThrowIfCancellationRequested();
                    inventoryError = null;
                    var hasResult = false;
                    try {
                        hasResult = await results.MoveNextAsync();
                        _inventory.Save(_managedPaks);
                    }
                    catch (Exception ex) {
                        inventoryError = Error.New("Failed to prepare managed paks", ex);
                    }
                    if (inventoryError != null) {
                        yield return inventoryError;
                        yield break;
                    }
                    if (!hasResult) yield break;
                    yield return results.Current;
                }
            }
        }

        private void ReloadInventory() {
            var latest = _inventory.Load(_managedPaks);
            _managedPaks.Clear();
            _managedPaks.AddRange(latest);
            SynchronizeWithDir();
        }

        public async Task<bool> RecoverReleases(IReadOnlyList<Release> requested, IEnumerable<Release> known, IUserDialogueSpawner dialogue,
            ICollection<Release>? adopted = null, bool alwaysKeepUnmanagedPaks = false) {
            try {
                using var inventoryLock = await _inventory.Lock();
                ReloadInventory();
                _inventory.Save(_managedPaks);
                var recovered = await PakReleaseRecovery.Recover(this, requested, known, dialogue, _inventory.BeginChange, adopted ?? new List<Release>(), alwaysKeepUnmanagedPaks);
                if (recovered) {
                    SynchronizeWithDir();
                    _inventory.Save(_managedPaks);
                }
                // Leave pending intact if a mutation failed after it began.
                return recovered;
            }
            catch (Exception ex) {
                Logger.Error("Failed to recover managed pak inventory", ex);
                dialogue.DisplayMessage($"Failed to recover managed pak inventory: {ex.Message}");
                return false;
            }
        }

        internal void TrackPak(ManagedPak pak) {
            _managedPaks.RemoveAll(existing => existing.Coordinates == pak.Coordinates
                || existing.PakFileName.Equals(pak.PakFileName, StringComparison.OrdinalIgnoreCase));
            _managedPaks.Add(pak);
        }

        internal static IEnumerable<string> GetInstallFileNames(IEnumerable<ReleaseCoordinates> releases) {
            var coordinates = releases.ToList();
            var duplicates = coordinates.GroupBy(c => c.ModuleName).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
            return ApplySortedLexicographically(coordinates.Select(c =>
                (duplicates.Contains(c.ModuleName) ? $"{c.Org}_{c.ModuleName}" : c.ModuleName) + ".pak"));
        }

        private async IAsyncEnumerable<Either<Error, ManagedPak>> InstallModSetCore(
                IEnumerable<ModInstallRequest> installs,
                Option<AccumulatedMemoryProgress> progress) {

            // Input is trusted to be pre-sorted by dependency order
            var installsList = installs.ToList();
            var installCoordinatesSet = installsList.Select(i => i.Coordinates).ToHashSet();

            var sortedPakNames = GetInstallFileNames(installsList.Select(i => i.Coordinates)).ToList();

            // Build map of existing managed paks by coordinates (for mods in install set)
            var existingByCoords = _managedPaks
                .Filter(p => installCoordinatesSet.Contains(p.Coordinates))
                .ToDictionary(p => p.Coordinates, p => p);
            var unmanagedPaths = GetUnmanagedPaks().ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Categorize each install: Skip (already correct), Move (relocate), or Download (new)
            var installActions = installsList
                .Zip(sortedPakNames, (install, finalPakName) => (Install: install, FinalPakName: finalPakName))
                .Select((item, index) => {
                    var existing = existingByCoords.TryGetValue(item.Install.Coordinates, out var e) ? Some(e) : None;
                    return existing.Match(
                        // A collision-safe name is reusable only while its load-order prefix is correct.
                        Some: pak => pak.PakFileName.Equals(item.FinalPakName, StringComparison.OrdinalIgnoreCase)
                            || (unmanagedPaths.Contains(PakNameToPakPath(item.FinalPakName))
                                && pak.PakFileName.StartsWith(
                                    item.FinalPakName.Substring(0, item.FinalPakName.IndexOf(ForcedSortDivider, StringComparison.Ordinal) + ForcedSortDivider.Length),
                                    StringComparison.OrdinalIgnoreCase))
                            ? new InstallAction.Skip(item.Install, index, pak)
                            : new InstallAction.Move(item.Install, index, pak, item.FinalPakName) as InstallAction,
                        None: () => new InstallAction.Download(item.Install, index, item.FinalPakName)
                    );
                })
                .ToList();

            // Categorize managed paks not in install set as Remove actions
            installActions.AddRange(
                _managedPaks
                    .Filter(p => !installCoordinatesSet.Contains(p.Coordinates))
                    .Select(p => new InstallAction.Remove(p))
                    .ToList()
            );

            var removeActions = installActions.OfType<InstallAction.Remove>().ToList();
            var skipActions = installActions.OfType<InstallAction.Skip>().ToList();
            var moveActions = installActions.OfType<InstallAction.Move>().ToList();
            var downloadActions = installActions.OfType<InstallAction.Download>().ToList();

            var reorderCount = skipActions.Count + moveActions.Count;

            // Set up all progress tracking upfront so users see the full scope of work
            var removeProgress = removeActions.Any()
                ? progress.Map(aggregateProgress => {
                    var prog = new MemoryProgress($"Removing {removeActions.Count} paks");
                    aggregateProgress.AlsoTrack(prog);
                    return prog;
                })
                : Option<MemoryProgress>.None;

            var reorderProgress = reorderCount > 0
                ? progress.Map(aggregateProgress => {
                    var prog = new MemoryProgress($"Reordering {reorderCount} paks");
                    aggregateProgress.AlsoTrack(prog);
                    return prog;
                })
                : Option<MemoryProgress>.None;

            var downloadProgressMap = downloadActions
                .Select(d => {
                    var prog = progress.Map(aggregateProgress => {
                        var mp = new MemoryProgress($"Installing {d.Install.Coordinates.ModuleName}");
                        aggregateProgress.AlsoTrack(mp);
                        return mp;
                    });
                    return (Download: d, Progress: prog);
                })
                .ToDictionary(x => x.Download, x => x.Progress);

            // Process removes
            for (var i = 0; i < removeActions.Count; i++) {
                _inventory.BeginChange();
                var removed = ProcessRemove(removeActions[i]);
                _inventory.Save(_managedPaks);
                if (removed.IsLeft) yield return removed.Map(_ => removeActions[i].Existing);
                removeProgress.IfSome(p => p.Report(100d * (i + 1) / removeActions.Count));
            }

            // Process skips and moves together as "reordering"
            var reorderIndex = 0;
            foreach (var result in skipActions.Select(ProcessSkip)) {
                yield return result;
                reorderIndex++;
                reorderProgress.IfSome(p => p.Report(100d * reorderIndex / reorderCount));
            }

            foreach (var result in moveActions.Select(ProcessMove)) {
                yield return result;
                reorderIndex++;
                reorderProgress.IfSome(p => p.Report(100d * reorderIndex / reorderCount));
            }

            // Process downloads for mods not yet present
            foreach (var download in downloadActions) {
                var taskProgress = downloadProgressMap[download];
                yield return await ProcessDownload(download, taskProgress);
            }
        }

        private abstract record InstallAction {
            public record Skip(ModInstallRequest Install, int Index, ManagedPak Existing) : InstallAction;
            public record Move(ModInstallRequest Install, int Index, ManagedPak Existing, string TargetPakName) : InstallAction;
            public record Download(ModInstallRequest Install, int Index, string FinalPakName) : InstallAction;
            public record Remove(ManagedPak Existing) : InstallAction;
        }

        private Either<Error, ManagedPak> ProcessSkip(InstallAction.Skip skip) {
            var desired = new ManagedPak(skip.Install.Coordinates, skip.Existing.PakFileName, skip.Index);

            // Update priority in _managedPaks if changed
            if (skip.Existing.Priority != skip.Index) {
                var idx = _managedPaks.FindIndex(p => p.Coordinates.Matches(skip.Install.Coordinates));
                if (idx >= 0) _managedPaks[idx] = desired;
            }

            Logger.Debug($"Skipping {skip.Install.Coordinates.ModuleName} - already at {skip.Existing.PakFileName}");
            return Right(desired);
        }

        private Either<Error, ManagedPak> ProcessMove(InstallAction.Move move) {
            _inventory.BeginChange();
            var sourcePath = PakNameToPakPath(move.Existing.PakFileName);
            var targetName = AvailablePakName(move.TargetPakName);
            var targetPath = PakNameToPakPath(targetName);

            return _moveManagedPak(sourcePath, targetPath)
                .Map(_ => {
                    var managedPak = new ManagedPak(move.Install.Coordinates, targetName, move.Index);
                    _managedPaks.RemoveAll(p => p.Coordinates.Matches(move.Install.Coordinates));
                    _managedPaks.Add(managedPak);
                    Logger.Info($"Relocated {move.Install.Coordinates.ModuleName} to {targetName}");
                    return managedPak;
                });
        }

        private async Task<Either<Error, ManagedPak>> ProcessDownload(InstallAction.Download download, Option<MemoryProgress> taskProgress) {
            _inventory.BeginChange();
            return (await _writePak(download.Install.Writer, taskProgress.Map(p => (IProgress<double>)p), download.FinalPakName))
                .Map(pakFileName => {
                    var managedPak = new ManagedPak(download.Install.Coordinates, pakFileName, download.Index);
                    _managedPaks.Add(managedPak);
                    _signFile(PakNameToPakPath(managedPak.PakFileName))
                        .IfLeft(e => Logger.Error($"Failed to sign {managedPak.PakFileName}: {e.Message}"));
                    return managedPak;
                });
        }

        private Either<Error, Unit> ProcessRemove(InstallAction.Remove remove) {
            Logger.Info($"Removing pak: {remove.Existing.PakFileName} ({remove.Existing.Coordinates.ModuleName} version {remove.Existing.Coordinates.Version})");
            var uninstallResult = UninstallCore(remove.Existing.Coordinates);
            uninstallResult.IfRight(_ => _managedPaks.RemoveAll(p => p.Coordinates == remove.Existing.Coordinates));
            uninstallResult.IfLeft(e => Logger.Error($"Failed to remove pak {remove.Existing.PakFileName}: {e}"));
            return uninstallResult;
        }

        /// <summary>
        /// Moves a managed pak (and its .sig if present) from source to destination.
        /// </summary>
        private static Either<Error, Unit> _moveManagedPak(string sourcePakPath, string destPakPath) {
            var sourceSigPath = Path.ChangeExtension(sourcePakPath, ".sig");
            var destSigPath = Path.ChangeExtension(destPakPath, ".sig");

            return PrimitiveExtensions.TryVoid(() => ValidateMoveSignatures(sourcePakPath, destPakPath)).Invoke()
                .Match<Either<Error, Unit>>(
                    _ => Right(Unit.Default),
                    e => Error.New($"Failed to move pak: '{sourcePakPath}' -> '{destPakPath}'", (Exception)e))
                .Bind(_ => _moveFile(sourcePakPath, destPakPath))
                .Bind(_ => {
                    var result = File.Exists(sourceSigPath)
                        ? File.Exists(destSigPath) ? _deleteFile(sourceSigPath) : _moveFile(sourceSigPath, destSigPath)
                        : Right<Error, Unit>(Unit.Default);
                    if (result.IsLeft) {
                        _moveFile(destPakPath, sourcePakPath).IfLeft(e =>
                            throw new IOException($"Failed to roll back pak move: {e}"));
                    }
                    return result;
                });
        }

        internal static void ValidateMoveSignatures(string sourcePakPath, string destPakPath) {
            var sourceSigPath = Path.ChangeExtension(sourcePakPath, ".sig");
            var destSigPath = Path.ChangeExtension(destPakPath, ".sig");
            if (File.Exists(sourceSigPath) && File.Exists(destSigPath)
                && !File.ReadAllBytes(sourceSigPath).SequenceEqual(File.ReadAllBytes(destSigPath)))
                throw new IOException($"Conflicting destination signature; files were preserved: {destSigPath}");
        }

        private static Either<Error, Unit> _moveFile(string source, string dest) {
            return PrimitiveExtensions
                .TryVoid(() => File.Move(source, dest))
                .Invoke()
                .Match<Either<Error, Unit>>(
                    _ => Right(Unit.Default),
                    e => Error.New($"Failed to move file: '{source}' -> '{dest}'", (Exception)e)
                );
        }

        /// <summary>
        /// Does nothing if the sign file already exists
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private Either<Error, Unit> _signFile(string path) {
            var signedName = Path.ChangeExtension(path, ".sig");
            if (File.Exists(signedName)) {
                return Right(Unit.Default);
            }
            var sigPath = Path.Join(FilePaths.PakDir, BaseSigFileName);
            return (File.Exists(sigPath)
                ? Either<Error, string>.Right(sigPath)
                : Error.New($"Default sig file '{sigPath}' not found"))
                .Bind(defaultPath => _copyFile(defaultPath, signedName));
        }

        private static Either<Error, Unit> _unsignFile(string path) {
            var signedName = Path.ChangeExtension(path, ".sig");
            return _deleteFile(signedName);
        }

        /// <summary>
        /// Delete any ReleaseMap entries that don't have a corresponding file on disk anymore.
        /// NOTE: this should only do anything when the *user* messed something up
        /// </summary>
        private void SynchronizeWithDir() {
            var missing = _managedPaks
                .Filter(p => !File.Exists(PakNameToPakPath(p.PakFileName)))
                .ToHashSet();

            if (missing.Count == 0) return;

            Logger.LogListWarn(
                "The following files were missing from the pak dir when they were expected to exist:",
                missing
            );

            _managedPaks.RemoveAll(missing.Contains);
        }

        private Option<ManagedPak> GetManagedPakFile(ModIdentifier coords) {
            return _managedPaks
                .Filter(p => coords is ReleaseCoordinates release ? p.Coordinates == release : p.Coordinates.Matches(coords))
                .ToOption();
        }

        public async Task<ManagedPak[]> GetManagedPaks() {
            using var inventoryLock = await _inventory.Lock();
            ReloadInventory();
            return _managedPaks.ToArray();
        }

        public async Task<Option<string>> GetManagedPakFilePath(ReleaseCoordinates coords) {
            try {
                using var inventoryLock = await _inventory.Lock();
                ReloadInventory();
                return GetManagedPakFile(coords)
                    .Map(pak => PakNameToPakPath(pak.PakFileName));
            }
            catch (Exception ex) {
                // Installation will surface the inventory error and cancel the launch.
                Logger.Error("Failed to read managed pak inventory", ex);
                return None;
            }
        }

        private string PakNameToPakPath(string fileName) {
            return Path.Join(DirPath, fileName);
        }

        private IEnumerable<string> GetUnmanagedPaks() {
            return GetModPakFiles()
                .Filter(p => !_managedPaks.Exists(pak => pak.PakFileName.Equals(Path.GetFileName(p), StringComparison.OrdinalIgnoreCase)));
        }

        public Task<Either<IEnumerable<Error>, Unit>> SignAll() {
            return WithDirectoryLock(() => GetModPakFiles()
                .Map(_signFile)
                .BindLefts());
        }

        public Task<Either<IEnumerable<Error>, Unit>> UnSignAll() {
            return WithDirectoryLock(() => GetModPakFiles()
                .Map(_unsignFile)
                .BindLefts());
        }

        private async Task<Either<IEnumerable<Error>, Unit>> WithDirectoryLock(Func<Either<IEnumerable<Error>, Unit>> action, string errorMessage = "Failed to update pak signatures") {
            try {
                using var inventoryLock = await _inventory.Lock();
                return action();
            }
            catch (Exception ex) {
                return Left<IEnumerable<Error>, Unit>(new[] { Error.New(errorMessage, ex) });
            }
        }

        public Task<Either<IEnumerable<Error>, Unit>> Reset() {
            return WithDirectoryLock(() => {
                ReloadInventory();
                _inventory.Save(_managedPaks);
                _inventory.BeginChange();
                var result = Directory.EnumerateFiles(DirPath)
                    .Filter(p => !(p.EndsWith(BasePakFileName) || p.EndsWith(BaseSigFileName)))
                    .Map(_deleteFile)
                    .BindLefts();
                SynchronizeWithDir();
                _inventory.Save(_managedPaks);
                return result;
            }, "Failed to reset managed pak inventory");
        }

        public Task<Either<IEnumerable<Error>, Unit>> DeleteOrphanedSigs() {
            return WithDirectoryLock(() => GetSigFiles()
                .Filter(p => !File.Exists(Path.ChangeExtension(p, ".pak")))
                .Map(_deleteFile)
                .BindLefts());
        }

        /// <summary>
        /// represents i using characters from alphabet
        /// </summary>
        /// <param name="alphabet"></param>
        /// <param name="i">must be positive</param>
        /// <returns></returns>
        private static string RepUsingAlphabet(string alphabet, int i) {
            var systemBase = alphabet.Length;
            var sb = new StringBuilder();
            while (i > 0) {
                sb.Append(alphabet[i % systemBase]);
                i /= systemBase;
            }

            return string.Join("", sb.ToString().Reverse());
        }

        /// <summary>
        /// Given a sequence of strings, modify those strings such
        /// that they are sorted lexicographically and return it
        /// </summary>
        /// <param name="inputs"></param>
        /// <returns></returns>
        private static IEnumerable<string> ApplySortedLexicographically(IEnumerable<string> inputs) {
            var inputsList = inputs.ToList();

            // We reverse the alphabet because we actually want to load dependencies backwards
            var alphabet = new string("abcdefghijklmnopqrstuvwxyz".Reverse().ToArray());

            var requiredSymbols = (int)Math.Ceiling(Math.Log(inputsList.Count, alphabet.Length));
            return inputsList.Map((i, n) =>
                $"q{RepUsingAlphabet(alphabet, i).PadLeft(requiredSymbols, alphabet[0])}" +
                ForcedSortDivider +
                $"{Regex.Replace(n, $".*{ForcedSortDivider}", "")}"
            );
        }
    }
}
