using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using CUE4Parse.Utils;
using System.IO;

namespace UnchainedLauncher.UnrealModScanner.Scanning {

    public class FilteredFileProvider : DefaultFileProvider {
        private readonly DirectoryInfo _workingDir;
        private readonly SearchOption _sOption;

        // Predicate: Return true to INCLUDE, false to EXCLUDE
        public Predicate<FileInfo>? PakFilter { get; set; }

        public FilteredFileProvider(string directory, SearchOption searchOption, bool isCaseInsensitive = false, VersionContainer? versions = null)
            : base(new DirectoryInfo(directory), searchOption, isCaseInsensitive, versions) {
            _workingDir = new DirectoryInfo(directory);
            _sOption = searchOption;
        }

        public new void Initialize() {
            if (!_workingDir.Exists)
                throw new DirectoryNotFoundException("Game directory not found.");

            // Replicate DefaultFileProvider logic with the filter injection
            var uproject = _workingDir.GetFiles("*.uproject", SearchOption.TopDirectoryOnly).FirstOrDefault();
            var currentOption = uproject != null ? SearchOption.AllDirectories : _sOption;

            foreach (var file in _workingDir.EnumerateFiles("*.*", currentOption)) {
                var ext = file.Extension.SubstringAfter('.').ToUpper();

                // Filter containers (PAK/UTOC)
                if (uproject == null && (ext == "PAK" || ext == "UTOC")) {
                    if (PakFilter != null && !PakFilter(file))
                        continue; // Skip excluded paks

                    // base.RegisterVfs is protected, so we can call it here
                    base.RegisterVfs(file);
                }
            }
        }
    }
}