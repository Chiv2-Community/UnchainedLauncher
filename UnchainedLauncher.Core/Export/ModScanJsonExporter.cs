using System.Collections.ObjectModel;
using UnchainedLauncher.Core.JsonModels.Metadata;
using UnrealModScanner.Models;
using Newtonsoft.Json;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;

namespace UnrealModScanner.Export;

public static class ModScanJsonExporter {
    //private static readonly JsonSerializerOptions Options = new() {
    //    WriteIndented = true,
    //    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    //};
    private static readonly JsonSerializerSettings Settings = new() {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
    };
    public static string ExportToString(
    TechnicalManifest scanResults) {
        var doc = new ModScanDocument {
            Manifest = scanResults
        };

        return JsonConvert.SerializeObject(doc, Settings);
    }

    public static void ExportToFile(
        TechnicalManifest scanResults,
        string outputPath) {
        var json = ExportToString(scanResults);
        File.WriteAllText(outputPath, json);
    }

    public static string ExportToString(
        ObservableCollection<PakScanResult> scanResults) {
        var doc = new ModScanDocument {
            Results = scanResults
        };

        return JsonConvert.SerializeObject(doc, Settings);
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

        return JsonConvert.SerializeObject(doc, Settings);
    }

    public static void ExportToFile(
        ModScanResult scanResults,
        string outputPath) {
        var json = ExportToString(scanResults);
        File.WriteAllText(outputPath, json);
    }
}