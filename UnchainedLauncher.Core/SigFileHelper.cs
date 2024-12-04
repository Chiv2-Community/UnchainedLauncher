using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core {
    public static class SigFileHelper {
        private static readonly ILog log = LogManager.GetLogger(typeof(SigFileHelper));

        private static string PakDirectory => FilePaths.PakDir;
        private static string DefaultSigFile => Path.Combine(PakDirectory, "pakchunk0-WindowsNoEditor.sig");

        private static Dictionary<string, (bool HasPak, bool HasSig)>? GetPakAndSigFiles() {
            try {
                if (!Directory.Exists(PakDirectory)) {
                    log.Error($"Directory does not exist: {PakDirectory}");
                    return null;
                }

                var pakFiles = Directory.EnumerateFiles(PakDirectory, "*.pak")
                                        .Select(Path.GetFileNameWithoutExtension)
                                        .OfType<string>()
                                        .ToHashSet();

                HashSet<string> sigFiles = Directory.EnumerateFiles(PakDirectory, "*.sig")
                                        .Select(Path.GetFileNameWithoutExtension)
                                        .OfType<string>()
                                        .ToHashSet();

                var allFiles = pakFiles.Union(sigFiles).Distinct();


                return allFiles.ToDictionary(
                    fileName => fileName,
                    fileName => (pakFiles.Contains(fileName), sigFiles.Contains(fileName))
                );
            }
            catch (Exception ex) {
                log.Error("An error occurred while gathering pak and sig files.", ex);
                return null;
            }
        }

        public static void CheckAndCopySigFiles() {
            try {
                var files = GetPakAndSigFiles();
                if (files == null) return;

                if (!File.Exists(DefaultSigFile)) {
                    log.Error($"Default .sig file not found: {DefaultSigFile}");
                    return;
                }

                foreach (var file in files) {
                    if (file.Value.HasPak && !file.Value.HasSig) {
                        var sigFilePath = Path.Combine(PakDirectory, $"{file.Key}.sig");
                        log.Info($"Creating {file.Key}.sig");
                        File.Copy(DefaultSigFile, sigFilePath);
                    }
                }

                log.Info("Process completed.");
            }
            catch (Exception ex) {
                log.Error("An error occurred during the sig file checking process.", ex);
            }
        }

        public static void DeleteOrphanedSigFiles() {
            try {
                var files = GetPakAndSigFiles();
                if (files == null) return;

                foreach (var file in files) {
                    if (!file.Value.HasPak && file.Value.HasSig) {
                        var sigFilePath = Path.Combine(PakDirectory, $"{file.Key}.sig");
                        log.Info($"Deleting orphaned sig file: {sigFilePath}");
                        File.Delete(sigFilePath);
                    }
                }

                log.Info("Orphaned sig files deletion completed.");
            }
            catch (Exception ex) {
                log.Error("An error occurred during the deletion of orphaned sig files.", ex);
            }
        }

        public static void RemoveAllNonDefaultSigFiles() {
            try {
                var files = GetPakAndSigFiles();
                if (files == null) return;

                foreach (var file in files) {
                    if (file.Key != "pakchunk0-WindowsNoEditor" && file.Value.HasSig) {
                        var sigFilePath = Path.Combine(PakDirectory, $"{file.Key}.sig");
                        log.Info($"Deleting sig file for non-default pak: {sigFilePath}");
                        File.Delete(sigFilePath);
                    }
                }

                log.Info("Non-default sig files deletion completed.");
            }
            catch (Exception ex) {
                log.Error("An error occurred during the deletion of non-default sig files.", ex);
            }
        }
    }

}