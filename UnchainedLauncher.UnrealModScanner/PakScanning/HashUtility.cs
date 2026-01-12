using CUE4Parse.FileProvider;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace UnchainedLauncher.UnrealModScanner.PakScanning {
    internal static class HashUtility {
        private static readonly ConcurrentDictionary<string, string> _hashStore = new();
        private static readonly ConcurrentDictionary<string, string> _hashCache = new();

        public static string GetFastHash(byte[] data) {
            if (data == null || data.Length == 0) return string.Empty;

            // Only hash the first 16KB to save massive CPU/IO time
            int lengthToHash = Math.Min(data.Length, 16384);

            using var sha1 = SHA1.Create();
            var hashBytes = sha1.ComputeHash(data, 0, lengthToHash);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        public static string GetAssetHash(IFileProvider provider, string path, object fallback) {
            // Use the path as the key to ensure we don't re-hash the same file 
            // across different processors.
            return _hashStore.GetOrAdd(path, _ => {
                if (provider.TrySaveAsset(path, out var data)) {
                    return GetFastHash(data);
                    //byte[] hashBytes = System.Security.Cryptography.SHA1.HashData(data);
                    //return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                }
                return fallback.GetHashCode().ToString();
            });
        }
    }
}