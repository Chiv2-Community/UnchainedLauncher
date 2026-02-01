using LanguageExt;
using LanguageExt.Common;
using LanguageExt.UnsafeValueAccess;
using log4net;
using System.Text;
using System.Text.RegularExpressions;
using UnchainedLauncher.Core.Extensions;
using UnchainedLauncher.Core.Services.Mods;
using UnchainedLauncher.Core.Services.Mods.Registry;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.PakDir {
    using static LanguageExt.Prelude;

    public class PakDir : IPakDir {
        private readonly string _dirPath;

        private const string BasePakFileName = "pakchunk0-WindowsNoEditor.pak";
        private const string BaseSigFileName = "pakchunk0-WindowsNoEditor.sig";

        private static readonly ILog Logger = LogManager.GetLogger(nameof(PakDir));

        public IReadOnlyList<ManagedPak> ManagedPaks => _managedPaks;
        private List<ManagedPak> _managedPaks { get; }
        
        private IModManager _modManager { get; }

        public PakDir(string dirPath, IEnumerable<ManagedPak> managedPaks, IModManager modManager) {
            _dirPath = dirPath;
            _managedPaks = managedPaks.ToList();
            _modManager = modManager;
            SynchronizeWithDir();
        }

        private IEnumerable<string> GetPakFiles() {
            return Directory.EnumerateFiles(_dirPath, "*.pak");
        }

        private IEnumerable<string> GetSigFiles() {
            return Directory.EnumerateFiles(_dirPath, "*.sig");
        }

        private Either<Error, string> GetDefaultSigFilePath() {
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

        public Either<Error, Unit> Uninstall(ModIdentifier coords) =>
            GetManagedPakFile(coords)
                .Match(
                    Some: pak => Right<Error, ManagedPak>(pak)
                        .BindTap(Unsign)
                        .Bind(x => _deleteFile(pakNameToPakPath(x.PakFileName))),
                    None: () => {
                        Logger.Debug($"No pak file found for {coords}. Ignoring Uninstall request");
                        return Right<Error, Unit>(Unit.Default);
                    }
                );

        private Either<Error, Unit> Unsign(ManagedPak pak) {
            return _unsignFile(pakNameToPakPath(pak.PakFileName));
        }

        private EitherAsync<Error, string> _writePak(IPakDir.MakeFileWriter mkFileWriter, Option<IProgress<double>> progress, string suggestedFileName) {
            var unManagedPaks = GetUnmanagedPaks().ToHashSet();
            var actualName = Path.GetFileNameWithoutExtension(suggestedFileName);
            var extension = Path.GetExtension(suggestedFileName);
            string fullActualName = actualName + extension;
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

        public IAsyncEnumerable<Either<Error,ManagedPak>> InstallModSet(
                IEnumerable<ModInstallRequest> installs,
                Option<AccumulatedMemoryProgress> progress) {
            
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

        public IEnumerable<ManagedPak> GetManagedPaks() => _managedPaks;

        public Option<ManagedPak> GetManagedPakFile(ModIdentifier coords) {
            return _managedPaks
                .Filter(p => p.Coordinates.Matches(coords))
                .ToOption();
        }

        private string pakNameToPakPath(string fileName) {
            return Path.Join(_dirPath, fileName);
        }

        public IEnumerable<string> GetUnmanagedPaks() {
            return GetModPakFiles()
                .Filter(p => !_managedPaks.Exists( pak => pak.PakFileName.EndsWith(Path.GetFileName(p))));
        }

        public IEnumerable<string> GetUnmanagedSigs() {
            return GetModdedSigFiles()
                .Filter(p => !_managedPaks.Exists(pak => 
                    pak.PakFileName.EndsWith(Path.GetFileName(Path.ChangeExtension(p, ".pak")))
                ));
        }

        public Either<IEnumerable<Error>, Unit> SignUnmanaged() {
            return GetUnmanagedPaks()
                .Map(_signFile)
                .BindLefts();
        }

        public Either<IEnumerable<Error>, Unit> UnSignUnmanaged() {
            return GetUnmanagedPaks()
                .Map(_unsignFile)
                .BindLefts();
        }

        public Either<IEnumerable<Error>, Unit> DeleteUnmanaged() {
            return GetUnmanagedPaks().ConcatFast(GetUnmanagedSigs())
                .Map(_deleteFile)
                .BindLefts();
        }

        public Either<IEnumerable<Error>, Unit> Reset() {
            return Directory.EnumerateFiles(_dirPath)
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
            const string alphabet = "abcdefghijklmnopqrstuvwxyz";
            var requiredSymbols = (int)Math.Ceiling(Math.Log(inputsList.Count, alphabet.Length));
            return inputsList.Map((i, n) =>
                $"q{RepUsingAlphabet(alphabet, i).PadLeft(requiredSymbols, alphabet[0])}" +
                forcedSortDivider +
                $"{Regex.Replace(n, $".*{forcedSortDivider}", "")}"
            );
        }
    }
}