using CUE4Parse.UE4.Pak.Objects;
using System.Runtime.CompilerServices;

namespace UnchainedLauncher.PakScannerApp.Tests;

internal sealed class ReadablePakEntry : FPakEntry {
    private ReadablePakEntry() : base(null!, "", (CUE4Parse.UE4.Readers.FArchive)null!) { }

    internal static ReadablePakEntry Create(string path = "Synthetic.uasset") {
        // The parser constructor requires a real pak; classification tests only need readable bytes.
        var entry = (ReadablePakEntry)RuntimeHelpers.GetUninitializedObject(typeof(ReadablePakEntry));
        entry.Path = path;
        return entry;
    }

    public override byte[] Read() => [1, 2, 3];
}