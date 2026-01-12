using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnchainedLauncher.UnrealModScanner.Models;
//using UnrealModScanner.Models;

namespace UnrealModScanner.Export;

public static class ModScanJsonExporter {
    private static readonly JsonSerializerOptions Options = new() {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    //public static string ExportToString(
    //    IReadOnlyList<PakScanResult> scanResults) {
    //    var doc = new ModScanDocument {
    //        Paks = scanResults
    //    };

    //    return JsonSerializer.Serialize(doc, Options);
    //}

    //public static void ExportToFile(
    //    IReadOnlyList<PakScanResult> scanResults,
    //    string outputPath) {
    //    var json = ExportToString(scanResults);
    //    File.WriteAllText(outputPath, json);
    //}
    public static string ExportToString(
    TechnicalManifest scanResults) {
        var doc = new ModScanDocument {
            Manifest = scanResults
        };

        return JsonSerializer.Serialize(doc, Options);
    }

    public static void ExportToFile(
        TechnicalManifest scanResults,
        string outputPath) {
        var json = ExportToString(scanResults);
        File.WriteAllText(outputPath, json);
    }
}
