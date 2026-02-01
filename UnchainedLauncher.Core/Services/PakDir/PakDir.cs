using LanguageExt;
using LanguageExt.Common;
using log4net;
using System.Text;
using System.Text.RegularExpressions;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.PakDir {
    using static LanguageExt.Prelude;

    public class PakDirCodec() : DerivedJsonCodec<PakDirMetadata, PakDir>(
        pakDir => new PakDirMetadata(pakDir.DirPath, pakDir.ManagedPaks),
        metadata => new PakDir(metadata.DirPath, metadata.ManagedPaks)
    );

    public record PakDirMetadata(string DirPath, IEnumerable<ManagedPak> ManagedPaks);


    public class PakDir : IPakDir {
        public readonly string DirPath;

        private const string BasePakFileName = "pakchunk0-WindowsNoEditor.pak";
        private const string BaseSigFileName = "pakchunk0-WindowsNoEditor.sig";

        private static readonly ILog Logger = LogManager.GetLogger(nameof(PakDir));

        public IEnumerable<ManagedPak> ManagedPaks => _managedPaks;
        private List<ManagedPak> _managedPaks { get; }

        public PakDir(string dirPath, IEnumerable<ManagedPak> managedPaks) {
            DirPath = dirPath;
            _managedPaks = managedPaks.ToList();
            SynchronizeWithDir();
        }

        private IEnumerable<string> GetSigFiles() {
            return Directory.EnumerateFiles(DirPath, "*.sig");
        }

        private IEnumerable<string> GetModPakFiles() {
            return Directory.EnumerateFiles(DirPath, "*.pak")
                .Filter(f => !f.EndsWith(BasePakFileName));
        }

        private IEnumerable<string> GetModdedSigFiles() {
            var sigFiles = GetSigFiles();
            return sigFiles.Filter(f => !f.EndsWith(BaseSigFileName));
        }

        /// <summary>
        /// Helper for deleting files using try
        /// </summary>
        /// <param name="path">file name to delete</param>
        /// <returns></returns>
        private Either<Error, Unit> _deleteFile(string path) {
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
        private Either<Error, Unit> _copyFile(string path, string destPath) {
            return PrimitiveExtensions
                .TryVoid(() => File.Copy(path, destPath))
                .Invoke()
                .Match<Either<Error, Unit>>(
                    _ => Right(Unit.Default),
                    e => Error.New($"Failed to copy file: '{path}' -> '{destPath}'", (Exception)e)
                );
        }

        public Either<Error, Unit> Uninstall(ModIdentifier coords) =>
            GetManagedPakFile(coords)
                .Match(
                    Some: pak => Right<Error, ManagedPak>(pak)
                        .BindTap(x => _unsignFile(pakNameToPakPath(x.PakFileName)))
                        .Bind(x => _deleteFile(pakNameToPakPath(x.PakFileName))),
                    None: () => {
                        Logger.Debug($"No pak file found for {coords}. Ignoring Uninstall request");
                        return Right<Error, Unit>(Unit.Default);
                    }
                );

        private EitherAsync<Error, string> _writePak(IPakDir.MakeFileWriter mkFileWriter, Option<IProgress<double>> progress, string suggestedFileName) {
            var unManagedPaks = GetUnmanagedPaks().ToHashSet();
            var actualName = Path.GetFileNameWithoutExtension(suggestedFileName);
            var extension = Path.GetExtension(suggestedFileName);
            var fullActualName = actualName + extension;
            while (_managedPaks.Exists(x => pakNameToPakPath(x.PakFileName) == fullActualName) || unManagedPaks.Contains(pakNameToPakPath(fullActualName))) {
                actualName = Successors.TextualSuccessor(actualName);
                fullActualName = actualName + extension;
            }

            Logger.Info($"Downloading pak to {fullActualName}");
            return mkFileWriter(pakNameToPakPath(fullActualName))
                .Bind(fileWriter =>
                    // TODO: allow passing of a cancellation token here
                    fileWriter.WriteAsync(progress, CancellationToken.None)
                    .Map(_ => fullActualName)
                );
        }

        public async IAsyncEnumerable<Either<Error, ManagedPak>> InstallModSet(
                IEnumerable<ModInstallRequest> installs,
                Option<AccumulatedMemoryProgress> progress) {

            // Input is trusted to be pre-sorted by dependency order
            var installsList = installs.ToList();

            // Collect all pak file names, adding org prefix for duplicate module names
            var moduleNameCounts = installsList
                .GroupBy(i => i.Coordinates.ModuleName)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet();

            var pakFileNames = installsList
                .Select(install => {
                    var moduleName = install.Coordinates.ModuleName;
                    var needsOrgPrefix = moduleNameCounts.Contains(moduleName);
                    var baseName = needsOrgPrefix
                        ? $"{install.Coordinates.Org}_{moduleName}"
                        : moduleName;
                    return baseName + ".pak";
                })
                .ToList();

            // Apply sorted lexicographically to get the final names
            var sortedPakNames = ApplySortedLexicographically(pakFileNames).ToList();

            // Build map of existing managed paks by coordinates (for mods in install set)
            var existingByCoords = _managedPaks
                .Filter(p => installsList.Exists(s => s.Coordinates == p.Coordinates))
                .ToDictionary(p => p.Coordinates, p => p);

            // Categorize each install: Skip (already correct), Move (relocate), or Download (new)
            var categorized = installsList
                .Zip(sortedPakNames, (install, finalPakName) => (Install: install, FinalPakName: finalPakName))
                .Select((item, index) => {
                    var existing = existingByCoords.TryGetValue(item.Install.Coordinates, out var e) ? Some(e) : None;
                    return existing.Match(
                        Some: pak => pak.PakFileName == item.FinalPakName
                            ? new InstallAction.Skip(item.Install, index, pak)
                            : new InstallAction.Move(item.Install, index, pak, item.FinalPakName) as InstallAction,
                        None: () => new InstallAction.Download(item.Install, index, item.FinalPakName)
                    );
                })
                .ToList();

            // Process skips (no I/O, already in correct position)
            foreach (var result in categorized.OfType<InstallAction.Skip>().Select(ProcessSkip)) {
                yield return result;
            }

            // Process moves before downloads - names are unique (org prefix added for duplicates)
            foreach (var result in categorized.OfType<InstallAction.Move>().Select(ProcessMove)) {
                yield return result;
            }

            // Process downloads for mods not yet present
            foreach (var download in categorized.OfType<InstallAction.Download>()) {
                yield return await ProcessDownload(download, progress);
            }
        }

        private abstract record InstallAction {
            public record Skip(ModInstallRequest Install, int Index, ManagedPak Existing) : InstallAction;
            public record Move(ModInstallRequest Install, int Index, ManagedPak Existing, string TargetPakName) : InstallAction;
            public record Download(ModInstallRequest Install, int Index, string FinalPakName) : InstallAction;
        }

        private Either<Error, ManagedPak> ProcessSkip(InstallAction.Skip skip) {
            var desired = new ManagedPak(skip.Install.Coordinates, skip.Existing.PakFileName, skip.Index);

            // Update priority in _managedPaks if changed
            if (skip.Existing.Priority != skip.Index) {
                var idx = _managedPaks.FindIndex(p => p.Coordinates.Matches(skip.Install.Coordinates));
                if (idx >= 0) _managedPaks[idx] = desired;
            }

            Logger.Debug($"Skipping {skip.Install.Coordinates.ModuleName} - already at {skip.Existing.PakFileName}");
            return Right<Error, ManagedPak>(desired);
        }

        private Either<Error, ManagedPak> ProcessMove(InstallAction.Move move) {
            var sourcePath = pakNameToPakPath(move.Existing.PakFileName);
            var targetPath = pakNameToPakPath(move.TargetPakName);

            return _moveManagedPak(sourcePath, targetPath)
                .Map(_ => {
                    var managedPak = new ManagedPak(move.Install.Coordinates, move.TargetPakName, move.Index);
                    _managedPaks.RemoveAll(p => p.Coordinates.Matches(move.Install.Coordinates));
                    _managedPaks.Add(managedPak);
                    Logger.Info($"Relocated {move.Install.Coordinates.ModuleName} to {move.TargetPakName}");
                    return managedPak;
                });
        }

        private async Task<Either<Error, ManagedPak>> ProcessDownload(InstallAction.Download download, Option<AccumulatedMemoryProgress> progress) {
            var taskProgress = progress.Map(p => {
                var mp = new MemoryProgress($"Installing {download.Install.Coordinates.ModuleName}");
                p.AlsoTrack(mp);
                return (IProgress<double>)mp;
            });

            return (await _writePak(download.Install.Writer, taskProgress, download.FinalPakName))
                .Map(pakFileName => {
                    var managedPak = new ManagedPak(download.Install.Coordinates, pakFileName, download.Index);
                    _managedPaks.Add(managedPak);
                    _signFile(pakNameToPakPath(managedPak.PakFileName))
                        .IfLeft(e => Logger.Error($"Failed to sign {managedPak.PakFileName}", e));
                    return managedPak;
                });
        }

        /// <summary>
        /// Moves a managed pak (and its .sig if present) from source to destination.
        /// </summary>
        private Either<Error, Unit> _moveManagedPak(string sourcePakPath, string destPakPath) {
            var sourceSigPath = Path.ChangeExtension(sourcePakPath, ".sig");
            var destSigPath = Path.ChangeExtension(destPakPath, ".sig");

            return _moveFile(sourcePakPath, destPakPath)
                .Bind(_ => File.Exists(sourceSigPath)
                    ? _moveFile(sourceSigPath, destSigPath)
                    : Right(Unit.Default));
        }

        private Either<Error, Unit> _moveFile(string source, string dest) {
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
            var sigPath = Path.Join(DirPath, BaseSigFileName);
            return (File.Exists(sigPath)
                ? Either<Error, string>.Right(sigPath)
                : Error.New($"Default sig file '{sigPath}' not found"))
                .Bind(defaultPath => _copyFile(defaultPath, signedName));
        }

        private Either<Error, Unit> _unsignFile(string path) {
            var signedName = Path.ChangeExtension(path, ".sig");
            return _deleteFile(signedName);
        }

        /// <summary>
        /// Delete any ReleaseMap entries that don't
        /// actually have a corresponding file on disk anymore.
        /// NOTE: this should only actually do anything because the *user*
        /// messed something up
        /// </summary>
        private void SynchronizeWithDir() {
            var missing = _managedPaks
                .Filter(p => !File.Exists(pakNameToPakPath(p.PakFileName)))
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
                .Filter(p => p.Coordinates.Matches(coords))
                .ToOption();
        }

        public Option<string> GetManagedPakFilePath(ReleaseCoordinates coords) {
            return GetManagedPakFile(coords)
                .Map(pak => pakNameToPakPath(pak.PakFileName));
        }

        private string pakNameToPakPath(string fileName) {
            return Path.Join(DirPath, fileName);
        }

        private IEnumerable<string> GetUnmanagedPaks() {
            return GetModPakFiles()
                .Filter(p => !_managedPaks.Exists(pak => pak.PakFileName.EndsWith(Path.GetFileName(p))));
        }

        private IEnumerable<string> GetUnmanagedSigs() {
            return GetModdedSigFiles()
                .Filter(p => !_managedPaks.Exists(pak =>
                    pak.PakFileName.EndsWith(Path.GetFileName(Path.ChangeExtension(p, ".pak")))
                ));
        }

        public Either<IEnumerable<Error>, Unit> SignAll() {
            return _managedPaks
                .Map(p => pakNameToPakPath(p.PakFileName))
                .Map(_signFile)
                .BindLefts();
        }

        public Either<IEnumerable<Error>, Unit> UnSignAll() {
            return _managedPaks
                .Map(p => pakNameToPakPath(p.PakFileName))
                .Map(_unsignFile)
                .BindLefts();
        }

        public Either<IEnumerable<Error>, Unit> Reset() {
            return Directory.EnumerateFiles(DirPath)
                .Filter(p => !(p.EndsWith(BasePakFileName) || p.EndsWith(BaseSigFileName)))
                .Map(_deleteFile)
                .BindLefts();
        }

        public Either<IEnumerable<Error>, Unit> DeleteOrphanedSigs() {
            return GetSigFiles()
                .Filter(p => !File.Exists(Path.ChangeExtension(p, ".pak")))
                .Map(_deleteFile)
                .BindLefts();
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
            const string forcedSortDivider = "__-__";
            var inputsList = inputs.ToList();

            // We reverse the alphabet because we actually want to load dependencies backwards
            var alphabet = new string("abcdefghijklmnopqrstuvwxyz".Reverse().ToArray());

            var requiredSymbols = (int)Math.Ceiling(Math.Log(inputsList.Count, alphabet.Length));
            return inputsList.Map((i, n) =>
                $"q{RepUsingAlphabet(alphabet, i).PadLeft(requiredSymbols, alphabet[0])}" +
                forcedSortDivider +
                $"{Regex.Replace(n, $".*{forcedSortDivider}", "")}"
            );
        }
    }
}