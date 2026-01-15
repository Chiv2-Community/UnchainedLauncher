using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.Utils;
using Newtonsoft.Json;
using System.Diagnostics;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.AssetSources;
using UnchainedLauncher.UnrealModScanner.Models;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Utility;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
    /// <summary>
    /// Parses .umap assets. Retrieves Game Mode name from WorldSettings
    /// </summary>
    public class MapProcessor : IAssetProcessor {
        public void Process(ScanContext ctx, PakScanResult result) {
            if (!ctx.FilePath.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)) return;

            var gameMode = "";
            var settingsDictionary = new Dictionary<string, Dictionary<string, object?>>();
            if (ctx.Package is Package pkg) {
                // Find WorldSettings (or something that shares name)
                var settings = pkg.ExportMap
                    .Select((exp, idx) => (Export: exp, Index: idx))
                    .Where(x => x.Export.ObjectName.Text.Contains("WorldSettings"))
                    .ToList();

                if (settings.Count > 0) {
                    // var (setting, setting_idx) = settings.First();
                    foreach (var (setting, setting_idx) in settings) {
                        var ws = ctx.Package.GetExport(setting_idx);
                        if (ws == null) continue;
                        
                        var dictInner = new Dictionary<string, object?>();
                        foreach (var kvp in ws.Properties) {
                            // dictInner.Add($"{kvp.PropertyType.ToString()}: {kvp.Name.PlainText}" , kvp.Tag?.GenericValue.ToString());
                            // TODO: Add a helper for pretty property values
                            var value = kvp.Tag?.GenericValue;
                            
                            if (value == null) continue;
                            var rawValue = kvp.Tag.GetValue(kvp.Tag.GetType()) ?? kvp.Tag.GenericValue;
                            // var dumpValue = rawValue switch {
                            //     UObject nestedObj => nestedObj.ToSafeJson(0, 3),
                            //     _ => JsonConvert.SerializeObject(rawValue)
                            // };
                            // dictInner.Add(kvp.Name.PlainText , dumpValue);
                            dictInner.Add(kvp.Name.PlainText , value.IsNumericType() ? value : value.ToString());
                            if (kvp.Name.PlainText == "DefaultGameMode")
                                if (kvp.Tag?.GenericValue is FPackageIndex pidx)
                                    gameMode = pidx.Name;
                        }
                        settingsDictionary.Add(ws.Name, dictInner);
                    }
                }
            }
            // var (mainExport, index) = BaseAsset.GetMainExport(ctx.Package).Value;
            result._Maps.Add(GameMapInfo.FromSource(
                new ScanContextAssetSource(ctx),
                gameMode,
                settingsDictionary));
            
        }
    }
}