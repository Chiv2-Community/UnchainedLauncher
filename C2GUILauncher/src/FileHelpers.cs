using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace C2GUILauncher {
    static class FileHelpers {
        public const string USER_LOCK_SUFFIX = ".USER_LOCK";

        public static bool IsFileLocked(string filePath) {
            return File.Exists(Path.Combine(Path.GetDirectoryName(filePath)!, USER_LOCK_SUFFIX)) ||
                   File.Exists(filePath + USER_LOCK_SUFFIX);
        }

        public static bool DeleteFiles(IEnumerable<string> filePaths) {
            // Delete all files in a transaction.
            // If any of them are locked, don't delete any of them.
            if (filePaths.Any(IsFileLocked)) {
                return false;
            }

            foreach (var filePath in filePaths) {
                File.Delete(filePath);
            }

            return true;
        }

        public static bool DeleteFile(string filePath) {
            if (IsFileLocked(filePath)) {
                return false;
            }

            File.Delete(filePath);
            return true;
        }

        // Currently unused, but maybe someday.
        public static void LockFile(string filePath) {
            File.Create(Path.Combine(Path.GetDirectoryName(filePath)!, USER_LOCK_SUFFIX)).Close();
        }

        // Currently unused, but maybe someday.
        public static void UnlockFile(string filePath) {
            File.Delete(Path.Combine(Path.GetDirectoryName(filePath)!, USER_LOCK_SUFFIX));
        }


    }
}
