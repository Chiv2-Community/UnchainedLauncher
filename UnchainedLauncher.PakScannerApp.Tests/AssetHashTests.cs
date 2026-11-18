using CUE4Parse.Compression;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Readers;
using System.Security.Cryptography;
using UnchainedLauncher.UnrealModScanner.Utility;

namespace UnchainedLauncher.PakScannerApp.Tests;

public class AssetHashTests {
    private sealed class Entry(byte[] bytes) : GameFile("Shared.uasset", bytes.Length) {
        internal int Reads;
        public override bool IsEncrypted => false;
        public override CompressionMethod CompressionMethod => CompressionMethod.None;
        public override byte[] Read() { Interlocked.Increment(ref Reads); return bytes; }
        public override FArchive CreateReader() => throw new NotSupportedException();
    }

    [Fact]
    public void SamePathInDifferentContainersOrProvidersNeverSharesHashes() {
        var original = new Entry([1, 2, 3]);
        var replacement = new Entry([4, 5, 6]);
        var originalHash = HashUtility.GetAssetHash(original);
        var replacementHash = HashUtility.GetAssetHash(replacement);
        Assert.NotEqual(originalHash, replacementHash);
        Assert.Equal(Convert.ToHexString(SHA512.HashData(new byte[] { 4, 5, 6 })).ToLowerInvariant(), replacementHash);
        Assert.Equal(originalHash, HashUtility.GetAssetHash(original));
        Assert.Equal(1, original.Reads);
        Assert.Equal(1, replacement.Reads);
    }

    [Fact]
    public async Task ParallelHashRequestsReadEachEntryOnce() {
        var entry = new Entry([7, 8, 9]);
        var hashes = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => HashUtility.GetAssetHash(entry))));
        Assert.Single(hashes.Distinct());
        Assert.Equal(1, entry.Reads);
    }
}