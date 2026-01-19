using CUE4Parse.FileProvider;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace UnchainedLauncher.UnrealModScanner.Utility {
    internal static class HashUtility {
        private static readonly ConcurrentDictionary<string, string> _hashStore = new();
        private static readonly ConcurrentDictionary<string, string> _hashCache = new();

        private static string GetFastHash(byte[] data) {
            if (data == null || data.Length == 0) return string.Empty;

            // Only hash the first 16KB to save massive CPU/IO time
            int lengthToHash = Math.Min(data.Length, 16384);

            using var sha1 = SHA1.Create();
            var hashBytes = sha1.ComputeHash(data, 0, lengthToHash);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        public static string CalculatePakHash(string filePath) {
            if (!File.Exists(filePath)) return "FILE_NOT_FOUND";
            using var sha512 = SHA512.Create();
            using var stream = File.OpenRead(filePath);

            byte[] hashBytes = sha512.ComputeHash(stream);

            // Convert bytes to hex string (e.g., "a3f2c1...")
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        public static string GetAssetHash(IFileProvider provider, string path, object fallback) {
            return _hashStore.GetOrAdd(path, _ => {
                if (provider.TrySaveAsset(path, out var data)) {
                    // return GetFastHash(data);
                    byte[] hashBytes = System.Security.Cryptography.SHA512.HashData(data);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                }
                return fallback.GetHashCode().ToString();
            });
        }
    }
}