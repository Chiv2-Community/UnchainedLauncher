using LanguageExt;
using LanguageExt.Common;
using log4net;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.PakDir {
    using static LanguageExt.Prelude;

    public class PakDir : IPakDir {
        private readonly string _dirPath;
        private readonly string _metadataFileName;

        private const string BasePakFileName = "pakchunk0-WindowsNoEditor.pak";
        private const string BaseSigFileName = "pakchunk0-WindowsNoEditor.sig";

        private static readonly ILog Logger = LogManager.GetLogger(nameof(PakDir));

        // TODO: invert this map, because most uses of it are filtering on the Value, not the key.
        private Map<string, ReleaseCoordinates> _releaseMap;

        /// <summary>
        /// Mapping from file name to ReleaseCoordinates. File name is ALWAYS lacking the path/to/pakdir/ prefix
        /// </summary>
        /// 
        private Map<string, ReleaseCoordinates> ReleaseMap {
            get => _releaseMap;
            set {
                _releaseMap = value;
                SaveMetaData() //TODO: make this save less often, maybe only on process close
                    .IfLeft(e => Logger.Error("PakDir autosave", e));
            }
        }
        public PakDir(string dirPath, string metadataFileName = "metadata.json") {
            _dirPath = dirPath;
            _metadataFileName = metadataFileName;
            ReleaseMap = TryLoadMetadata();
        }

        private Map<string, ReleaseCoordinates> TryLoadMetadata() {
            var fullMetadataPath = Path.Join(_dirPath, _metadataFileName);
            return Try(() => File.ReadAllText(fullMetadataPath))
                .Invoke()
                .Match<Either<Error, string>>(
                    s => Right(s),
                    e => Error.New($"Failed to read metadata file '{fullMetadataPath}'", (Exception)e)
                )
                .Bind(s =>
                    JsonHelpers.Deserialize<IEnumerable<(string, ReleaseCoordinates)>>(s)
                        .ToEither()
                        .MapLeft(e => Error.New($"Failed to deserialize metadata file '{fullMetadataPath}'", e))
                        .Map(pairs => new Map<string, ReleaseCoordinates>(pairs))
                    )
                .Match(
                    s => s,
                    e => {
                        if (e.ToException() is FileNotFoundException) {
                            Logger.Info("Metadata file not found. Using empty one instead.");
                            return new Map<string, ReleaseCoordinates>();
                        }
                        Logger.Error("Error while deserializing PakDir metadata. Starting from scratch instead.", e);
                        return new Map<string, ReleaseCoordinates>();
                    }
                    );
        }

        private readonly object _saveLock = new object();
        public Either<Error, Unit> SaveMetaData() {
            lock (_saveLock) {
                var jsonMetaData = JsonHelpers.Serialize(ReleaseMap);
                var fullMetadataPath = Path.Join(_dirPath, _metadataFileName);
                return PrimitiveExtensions.TryVoid(() => {
                        Directory.CreateDirectory(_dirPath);
                        File.WriteAllText(fullMetadataPath, jsonMetaData);
                    })
                    .Invoke()
                    .Match<Either<Error, Unit>>(
                        s => Right(s),
                        e => Error.New("Failed to save metadata file. Starting from scratch instead.", (Exception)e)
                    );
            }
        }

        public IEnumerable<string> GetPakFiles() {
            return Directory.EnumerateFiles(_dirPath, "*.pak");
        }

        public IEnumerable<string> GetSigFiles() {
            return Directory.EnumerateFiles(_dirPath, "*.sig");
        }

        public Either<Error, string> GetDefaultSigFilePath() {
            var sigPath = Path.Join(_dirPath, BaseSigFileName);
            return File.Exists(sigPath)
                ? Either<Error, string>.Right(sigPath)
                : Error.New($"Default sig file '{sigPath}' not found");
        }

        public IEnumerable<string> GetModPakFiles() {
            return GetPakFiles()
                .Filter(f => !f.EndsWith(BasePakFileName));
        }

        public IEnumerable<string> GetModdedSigFiles() {
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="fileToRemove">file to remove</param>
        /// <returns></returns>
        private Either<Error, Unit> _internalUninstall(string fileToRemove) {
            var deleteResult = _deleteFile(fileToRemove);
            if (deleteResult.IsRight) {
                ReleaseMap = ReleaseMap.Remove(Path.GetFileName(fileToRemove));
            }
            return deleteResult;
        }

        public Either<Error, Unit> Uninstall(ReleaseCoordinates coords) {
            return Unsign(coords)
                .Bind(_ =>
                    GetInstalledPakFile(coords)
                        .Match(_internalUninstall, Right(Unit.Default))
                    );
        }

        public Either<Error, Unit> Uninstall(ModIdentifier coords) {
            return Unsign(coords)
                .Bind(_ => GetInstalledPakFile(coords)
                    .Match(_internalUninstall, Right(Unit.Default)));
        }

        private EitherAsync<Error, string> _writePak(IPakDir.MakeFileWriter mkFileWriter, Option<IProgress<double>> progress, string suggestedFileName) {
            var unManagedPaks = GetUnmanagedPaks().ToHashSet();
            var actualName = Path.GetFileNameWithoutExtension(suggestedFileName);
            var extension = Path.GetExtension(suggestedFileName);
            string fullActualName = actualName + extension;
            while (ReleaseMap.ContainsKey(fullActualName) || unManagedPaks.Contains(_fnToProperPath(fullActualName))) {
                actualName = Successors.TextualSuccessor(actualName);
                fullActualName = actualName + extension;
            }

            Logger.Info($"Downloading pak to {fullActualName}");
            return mkFileWriter(_fnToProperPath(fullActualName))
                .Bind(fileWriter =>
                    // TODO: allow passing of a cancellation token here
                    fileWriter.WriteAsync(progress, CancellationToken.None)
                    .Map(_ => fullActualName)
                );
        }

        public EitherAsync<Error, Unit> Install(ReleaseCoordinates coords, IPakDir.MakeFileWriter mkFileWriter, string suggestedFileName, Option<IProgress<double>> progress) {
            return ReleaseMap
                .Filter(c => c.Matches(coords))
                .ToOption()
                .Match(
                    (p) => {
                        if (p.Value.CompareTo(coords) == 0) {
                            // this is already installed; nothing to do
                            return p.Key;
                        }

                        // uninstall the other version, then install the new version
                        return Uninstall(p.Value)
                            .ToEitherAsync()
                            .Bind(
                                _ => _writePak(mkFileWriter, progress, suggestedFileName)
                            );
                    },
                    // no version installed. Install the new version
                    () => _writePak(mkFileWriter, progress, suggestedFileName)
                )
                .Map<Unit>(actualName => {
                    if (!ReleaseMap.ContainsKey(actualName)) {
                        ReleaseMap = ReleaseMap.Add(actualName, coords);
                    }
                    return Unit.Default;
                });

        }

        public EitherAsync<Error, Unit> InstallOnly(
                IEnumerable<(
                    ReleaseCoordinates version, 
                    IPakDir.MakeFileWriter,
                    string suggestedPakName)> installs,
                Option<AccumulatedMemoryProgress> progress) {
            return ReleaseMap
                .Filter(c =>
                    !installs.Any(t =>
                        t.Item1.Matches(c)
                    )
                ) // Find any releases not mentioned in this install
                .Map(p => Uninstall(p.Value)) // uninstall them
                .AggregateBind()
                // attempt to install all mentioned releases (installing an already installed release does nothing)
                // TODO: maybe parallelize this, so all paks get downloaded in parallel. It's fast enough for now though
                .Bind(_ => installs.Map(t => {
                    if (GetInstalledPakFile(t.Item1).IsSome) {
                        // so we don't show already installed paks in the progress
                        return Unit.Default;
                    }
                    var versionProgress = new MemoryProgress($"{t.Item1}");
                    progress.IfSome(p => p.AlsoTrack(versionProgress));
                    return Install(t.Item1, t.Item2, t.Item3, Some((IProgress<double>)versionProgress));
                }))
                .AggregateBind();
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
            return GetDefaultSigFilePath()
                .Bind(defaultPath => _copyFile(defaultPath, signedName));
        }

        private Either<Error, Unit> _unsignFile(string path) {
            var signedName = Path.ChangeExtension(path, ".sig");
            return _deleteFile(signedName);
        }

        public Either<Error, Unit> Sign(ReleaseCoordinates coords) {
            return GetInstalledPakFile(coords)
                .Match(_signFile, () => Right(Unit.Default));
        }

        public Either<Error, Unit> Unsign(ReleaseCoordinates coords) {
            return GetInstalledPakFile(coords)
                .Match(_unsignFile, Right(Unit.Default));
        }

        public Either<Error, Unit> Unsign(ModIdentifier coords) {
            return GetInstalledPakFile(coords)
                .Match(_unsignFile, Right(Unit.Default));
        }

        public Either<Error, Unit> SignOnly(IEnumerable<ReleaseCoordinates> coords) {
            return ReleaseMap
                .Filter(c => coords.All(ci => ci.CompareTo(c) != 0))
                .Map(p => _unsignFile(p.Key))
                .AggregateBind()
                .Bind(_ => coords.Map(Sign))
                .AggregateBind();
        }

        public bool IsSigned(ReleaseCoordinates coords) {
            return GetInstalledPakFile(coords)
                .Map(f => Path.ChangeExtension(f, ".sig"))
                .Filter(GetSigFiles().Contains)
                .IsSome;
        }

        public bool IsSigned(ModIdentifier coords) {
            return GetInstalledPakFile(coords)
                .Map(f => Path.ChangeExtension(f, ".sig"))
                .Filter(GetSigFiles().Contains)
                .IsSome;
        }

        public Option<string> GetInstalledPakFile(ReleaseCoordinates coords) {
            return ReleaseMap
                .Filter(c => c.CompareTo(coords) == 0)
                .Map(p => _fnToProperPath(p.Key))
                .ToOption();
        }

        public Option<string> GetInstalledPakFile(ModIdentifier coords) {
            return ReleaseMap
                .Filter(c => c.Matches(coords))
                .Map(p => _fnToProperPath(p.Key))
                .ToOption();
        }

        private string _fnToProperPath(string fileName) {
            return Path.Join(_dirPath, fileName);
        }

        public IEnumerable<ReleaseCoordinates> GetSignedReleases() {
            return GetInstalledReleases()
                .Filter(IsSigned);
        }

        public IEnumerable<ReleaseCoordinates> GetInstalledReleases() {
            return ReleaseMap.Map(p => p.Value);
        }

        public IEnumerable<string> GetUnmanagedPaks() {
            return GetModPakFiles()
                .Filter(p => !ReleaseMap.ContainsKey(Path.GetFileName(p)));
        }

        public IEnumerable<string> GetUnmanagedSigs() {
            return GetModdedSigFiles()
                .Filter(p =>
                    !ReleaseMap.ContainsKey(
                        Path.GetFileName(Path.ChangeExtension(p, ".pak")))
                    );
        }

        public Either<Error, Unit> SignUnmanaged() {
            return GetUnmanagedPaks()
                .Map(_signFile)
                .AggregateBind();
        }

        public Either<Error, Unit> UnSignUnmanaged() {
            return GetUnmanagedPaks()
                .Map(_unsignFile)
                .AggregateBind();
        }

        public Either<Error, Unit> DeleteUnmanaged() {
            return GetUnmanagedPaks().ConcatFast(GetUnmanagedSigs())
                .Map(_deleteFile)
                .AggregateBind();
        }

        public Either<Error, Unit> CleanUpDir() {
            return Directory.EnumerateFiles(_dirPath)
                .Filter(p => !(p.EndsWith(BasePakFileName) || p.EndsWith(BaseSigFileName)))
                .Map(_deleteFile)
                .AggregateBind()
                .Map(_ => {
                    // bypass auto-save behavior so that the .json file is not re-created
                    _releaseMap = ReleaseMap.Clear();
                    return Unit.Default;
                });
        }

        public Either<Error, Unit> DeleteOrphanedSigs() {
            return GetSigFiles()
                .Filter(p => !File.Exists(Path.ChangeExtension(p, ".pak")))
                .Map(_deleteFile)
                .AggregateBind();
        }
    }
}