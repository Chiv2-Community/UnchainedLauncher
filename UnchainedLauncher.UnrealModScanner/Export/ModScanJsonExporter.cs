using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.ObjectModel;
using UnchainedLauncher.UnrealModScanner.JsonModels;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.Export;

public static class ModScanJsonExporter {
    private static readonly JsonSerializerOptions Options = new() {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void ExportToFile(
        PakDirManifest scanResults,
        string outputPath) {
        var json = JsonSerializer.Serialize(scanResults, Options);
        File.WriteAllText(outputPath, json);
    }

    public static string ExportToString(
        ObservableCollection<PakScanResult> scanResults) {
        var doc = new ModScanDocument {
            Results = scanResults
        };

        return JsonSerializer.Serialize(doc, Options);
    }

    public static void ExportToFile(
        ObservableCollection<PakScanResult> scanResults,
        string outputPath) {
        var json = ExportToString(scanResults);
        File.WriteAllText(outputPath, json);
    }

    public static string ExportToString(
        ModScanResult scanResults) {
        var doc = new ModScanDocument {
            ScanResults = scanResults
        };

        return JsonSerializer.Serialize(doc, Options);
    }

    public static void ExportToFile(
        ModScanResult scanResults,
        string outputPath) {
        var json = ExportToString(scanResults);
        File.WriteAllText(outputPath, json);
    }
}