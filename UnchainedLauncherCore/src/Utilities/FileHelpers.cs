using LanguageExt;
using log4net;
using System.Security.AccessControl;
using LanguageExt.Common;

namespace UnchainedLauncher.Core.Utilities {
    public static class FileHelpers {
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
            if (!Directory.Exists(filePath)) {
                logger.Warn($"Not deleting directory because it doesn't exist: {filePath}");
                return false;
            }

            var files = Directory.GetFiles(filePath, "*", SearchOption.AllDirectories);
            var result = DeleteFiles(files);

            if (result)
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

        public static Task<string> Sha512Async(string filePath) {
            using var sha512 = System.Security.Cryptography.SHA512.Create();
            return File.ReadAllBytesAsync(filePath).ContinueWith(t => {
                var bytes = t.Result;
                return BitConverter.ToString(sha512.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            });
        }
    }


    public class FileWriter {
        public string FilePath { get; }
        public Stream InputStream { get; }

        public FileWriter(string filePath, Stream inputStream) {
            FilePath = filePath;
            InputStream = inputStream;
        }

        public EitherAsync<Error, Unit> WriteAsync(Option<IProgress<double>> progress, CancellationToken cancellationToken) {
            return Prelude.TryAsync(
                async () => {
                    // Ensure the input stream is readable
                    if (!InputStream.CanRead)
                        throw new ArgumentException("The input stream is not readable.", nameof(InputStream));

                    // Create or overwrite the target file
                    using (var outputStream = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true)) {
                        var totalBytes = InputStream.Length; // Total bytes to read (if the input stream supports seeking)
                        var totalBytesWritten = 0L; // Total bytes written to the file
                        var buffer = new byte[8192]; // Buffer to hold data from input stream
                        int bytesRead;

                        // While there's data to read from the input stream, read and write asynchronously
                        while ((bytesRead = await InputStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0) {
                            await outputStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);

                            totalBytesWritten += bytesRead;
                            var percentage = (double)totalBytesWritten / totalBytes * 100;
                            progress.IfSome(p => p.Report(percentage));

                        }
                    }

                    return Unit.Default;
                })
                .ToEither();
        }

    }
}
