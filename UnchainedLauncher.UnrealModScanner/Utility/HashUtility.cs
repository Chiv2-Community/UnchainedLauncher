using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace UnchainedLauncher.UnrealModScanner.Utility {
    internal static class HashUtility {
        // Entry identity includes its owning container; weak keys expire with the provider.
        private static readonly ConditionalWeakTable<GameFile, Lazy<string>> Hashes = new();

        public static string GetAssetHash(GameFile file) => Hashes.GetValue(file,
            entry => new Lazy<string>(() => Convert.ToHexString(SHA512.HashData(entry.Read())).ToLowerInvariant())).Value;

        public static string CalculatePakHash(string filePath) {
            if (!File.Exists(filePath)) return "FILE_NOT_FOUND";
            using var sha512 = SHA512.Create();
            using var stream = File.OpenRead(filePath);

            byte[] hashBytes = sha512.ComputeHash(stream);

            // Convert bytes to hex string (e.g., "a3f2c1...")
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        public static string GetAssetHash(IFileProvider provider, string path, object fallback) {
            return provider.TryGetGameFile(path, out var file) ? GetAssetHash(file) : fallback.GetHashCode().ToString();
        }
    }
}
