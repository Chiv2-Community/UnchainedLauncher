using log4net;
using log4net.Repository.Hierarchy;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;

namespace C2GUILauncher {
    static class FileHelpers {
        private static readonly ILog logger = LogManager.GetLogger(nameof(FileHelpers));

        public const string USER_LOCK_SUFFIX = ".USER_LOCK";

        public static bool IsFileLocked(string filePath) {
            return File.Exists(Path.Combine(Path.GetDirectoryName(filePath)!, USER_LOCK_SUFFIX)) ||
                   File.Exists(filePath + USER_LOCK_SUFFIX);
        }

        public static bool DeleteFiles(IEnumerable<string> filePaths) {
            logger.Info("Deleting files: " + string.Join("\n    ", filePaths));
            // Delete all files in a transaction.
            // If any of them are locked, don't delete any of them.
            if (filePaths.Any(IsFileLocked)) {
                var lockedFiles = filePaths.Where(IsFileLocked);
                logger.Warn($"Not deleting files because some of them are locked:\n" + string.Join("\n    ", lockedFiles));
                return false;
            }

            foreach (var filePath in filePaths) {
                File.Delete(filePath);
            }

            return true;
        }

        public static bool DeleteFile(string filePath) {
            if (IsFileLocked(filePath)) {
                logger.Warn($"Not deleting file because it is locked: {filePath}");
                return false;
            }

            if (File.Exists(filePath)) {
                logger.Info("Deleting file: " + filePath);
                File.Delete(filePath);
            }

            return true;
        }

        public static bool DeleteDirectory(string filePath) {
            logger.Info("Deleting directory: " + filePath);
            if(!Directory.Exists(filePath)) {
                logger.Warn($"Not deleting directory because it doesn't exist: {filePath}");
                return false;
            }

            var files = Directory.GetFiles(filePath, "*", SearchOption.AllDirectories);
            var result = DeleteFiles(files);

            if(result)
                Directory.Delete(filePath, true);

            return result;
        }

        // Currently unused, but maybe someday.
        public static void LockFile(string filePath) {
            File.Create(Path.Combine(Path.GetDirectoryName(filePath)!, USER_LOCK_SUFFIX)).Close();
        }

        // Currently unused, but maybe someday.
        public static void UnlockFile(string filePath) {
            File.Delete(Path.Combine(Path.GetDirectoryName(filePath)!, USER_LOCK_SUFFIX));
        }

        public static string Sha512(string filePath) {
            using var sha512 = System.Security.Cryptography.SHA512.Create();
            var bytes = File.ReadAllBytes(filePath);
            return BitConverter.ToString(sha512.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
