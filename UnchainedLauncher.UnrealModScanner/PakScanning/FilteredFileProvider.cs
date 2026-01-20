using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.FileProvider.Vfs;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Pak.Objects;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Utils;
using UnchainedLauncher.UnrealModScanner.Config;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;

namespace UnchainedLauncher.UnrealModScanner.PakScanning {
    using PakContentFilter = Predicate<KeyValuePair<string, GameFile>>;

    /// <summary>
    /// DefaultFileProvider
    /// </summary>
    public class FilteredFileProvider(
        string directory,
        SearchOption searchOption,
        Predicate<KeyValuePair<string, GameFile>>? pakContentFilter,
        string aesKey,
        VersionContainer? versions = null,
        StringComparer? pathComparer = null)
        : DefaultFileProvider(new DirectoryInfo(directory), [], searchOption, versions, pathComparer) {

        /// <summary>
        /// Smart constructor that uses ScanOptions to fill out the majority of the FilteredFileProvider
        ///
        /// Good chance a lot of this could just replace the base constructor if it was moved to a classic constructor
        /// setup.  Didn't want to mess with Program.cs to make that happen, though.
        /// </summary>
        /// <param name="options"></param>
        /// <param name="mode"></param>
        /// <param name="pakDir"></param>
        /// <param name="extraPakFilter">An additional pak filter to apply after the default filters</param>
        /// <returns></returns>
        public static FilteredFileProvider CreateFromOptions(ScanOptions options, ScanMode mode, string pakDir, PakContentFilter? extraPakFilter = null) {
            IScanFilter? pakFilter = mode switch {
                ScanMode.Mods => new IgnorePakScanFilter(options.VanillaPakNames.ToArray()),
                ScanMode.GameInternal => new SpecificPakScanFilter(options.VanillaPakNames.ToArray()),
                // just return true for the default case. Apply no additional filtering.
                _ => null
            };


            return new FilteredFileProvider(
                pakDir, 
                SearchOption.TopDirectoryOnly, 
                options.ScanFilter.With(pakFilter).CreatePakContentFilter(), 
                options.AesKey
            );
        }

        public IEnumerable<KeyValuePair<string, GameFile>> FilteredFiles {
            get => pakContentFilter != null
                ? Files.Where(kv => pakContentFilter(kv))
                : Files;
        }

        public new void Initialize() {
            var workingDir = new DirectoryInfo(directory);
            if (!workingDir.Exists)
                throw new DirectoryNotFoundException("Game directory not found.");

            // Replicate DefaultFileProvider logic with the filter injection
            var uproject = workingDir.GetFiles("*.uproject", SearchOption.TopDirectoryOnly).FirstOrDefault();
            var currentOption = uproject != null ? SearchOption.AllDirectories : searchOption;

            foreach (var file in workingDir.EnumerateFiles("*.*", currentOption)) {
                var ext = file.Extension.SubstringAfter('.').ToUpper();

                // Filter containers (PAK/UTOC)
                if (uproject == null && ext is "PAK" or "UTOC") {
                    RegisterVfs(file);
                }
            }

            SubmitKey(new FGuid(), new FAesKey(aesKey));
        }
    }
}