using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.AssetSources;
using UnchainedLauncher.UnrealModScanner.Models.Dto;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Utility;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
    /// <summary>
    /// Discovers assets with TMaps that hold references to other assets.
    /// Assumes that the Marker is a (BP) DataAsset and that TMap key is the SoftClassPath to the target. 
    /// <br/>
    /// Mod orchestrator then initiates a second pass looking only for the references
    /// </summary>
    public class ReferenceDiscoveryProcessor : IAssetProcessor {
        /// <summary>
        /// </summary>
        private readonly string _containerClassName; // e.g., "DA_ModMarker_C"
        /// <summary>
        /// 
        /// </summary>
        private readonly string _mapPropertyName;     // e.g., "ModActors"

        // Thread-safe collection for the Orchestrator to aggregate
        public ConcurrentBag<PendingBlueprintReference> DiscoveredReferences { get; } = new();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="containerClass">
        /// (partial) Class Name to check. uses .Contains(). 
        /// e.g. "DA_ModMarker_C" or "DA_ModMarker"
        /// TODO: Convert this to use Regex<br/>
        /// TODO: Make this scan multiple classes in one pass
        /// </param>
        /// <param name="mapProperty">
        /// Property name of the TMap containing target assets
        /// e.g. "ModActors"
        /// </param>
        public ReferenceDiscoveryProcessor(string containerClass, string mapProperty) {
            _containerClassName = containerClass;
            _mapPropertyName = mapProperty;
        }

        public void Process(ScanContext ctx, PakScanResult result) {
            var gameMode = "";
            var settingsDictionary = new Dictionary<string, Dictionary<string, object?>>();

            var (mainExport, index) = BaseAsset.GetMainExport(ctx.Package).Value;
            // TODO: throw
            if (mainExport == null) return;
            if (mainExport.ClassName != _containerClassName) return;
            var mainExportLazy = ctx.Package.GetExport(index);

            var map = mainExportLazy.GetOrDefault<UScriptMap>(_mapPropertyName);

            var properties = new Dictionary<string, object?>();
            var childClassName = String.Empty;
            foreach (var kvp in mainExportLazy.Properties) {
                var value = kvp.Tag?.GenericValue;

                if (value == null) continue;

                try {
                    ;
                    var rawValue = kvp.Tag.GetValue(kvp.Tag.GetType()) ?? kvp.Tag.GenericValue;
                    if (rawValue is UScriptMap _map) {
                        _map.ToString();
                    }
                    properties.Add(kvp.Name.Text, rawValue switch {
                        UObject nestedObj => JToken.Parse(nestedObj.ToSafeJson(0, 3)?.ToString() ?? null),
                        UScriptMap smap => JToken.Parse(JsonConvert.SerializeObject(rawValue)).Children<JObject>()
                            .ToDictionary(
                                obj => obj["Key"]?.ToString() ?? "UnknownKey",
                                obj => obj["Value"]
                            ),
                        _ => JToken.Parse(JsonConvert.SerializeObject(rawValue))
                    });
                }
                catch (Exception e) {
                    Log.Error("Failed to save Marker properties");
                }

                // properties.Add(kvp.Name.Text, rawValue);
            }

            if (map == null) {
                Debug.WriteLine($"Could not find TMap for {_mapPropertyName}");
                return;
            }
            ;

            foreach (var entry in map.Properties) {
                if (entry.Key.GetValue(typeof(FPackageIndex)) is FPackageIndex idx) {

                    var resolved = ctx.Package.ResolvePackageIndex(idx);
                    if (childClassName.Length == 0 && resolved.Super != null)
                        childClassName = resolved.Super.GetPathName();
                    if (resolved != null) {
                        DiscoveredReferences.Add(new PendingBlueprintReference {
                            SourceMarkerPath = ctx.FilePath,
                            SourceMarkerClassName = mainExport.ClassName,
                            TargetBlueprintPath = resolved.GetPathName(),
                            TargetClassName = resolved.Name.Text,
                            SourcePakFile = ctx.PakEntry.PakFileReader.Name,
                        });
                    }
                }
            }

            result.AddGenericMarker(GenericMarkerEntry.FromSource(
                new ScanContextAssetSource(ctx),
                childClassName,
                null,
                properties
            ), mainExport.ClassName);

            // foreach (var kvp in mainExportLazy.Properties) {
            //         // dictInner.Add($"{kvp.PropertyType.ToString()}: {kvp.Name.PlainText}" , kvp.Tag?.GenericValue.ToString());
            //         // TODO: Add a helper for pretty property values
            //         var value = kvp.Tag?.GenericValue;
            //         if (value == null) continue;
            //         
            //         dictInner.Add(kvp.Name.PlainText , value.IsNumericType() ? value : value.ToString());
            //         if (kvp.Name.PlainText == "DefaultGameMode")
            //             if (kvp.Tag?.GenericValue is FPackageIndex pidx)
            //                 gameMode = pidx.Name;
            //     }
            //     foreach (var entry in map.Properties) {
            //         if (entry.Key.GetValue(typeof(FPackageIndex)) is FPackageIndex idx) {
            //             var resolved = ctx.Package.ResolvePackageIndex(idx);
            //             if (resolved != null) {
            //                 DiscoveredReferences.Add(new PendingBlueprintReference {
            //                     SourceMarkerPath = ctx.FilePath,
            //                     SourceMarkerClassName = base_name,
            //                     TargetBlueprintPath = resolved.GetPathName(),
            //                     TargetClassName = resolved.Name.Text,
            //                     SourcePakFile = curPakName,
            //                 });
            //             }
            //         }
            // if (ctx.Package is Package pkg) {
            //     // Find WorldSettings (or something that shares name)
            //     // var settings = pkg.ExportMap
            //     //     .Select((exp, idx) => (Export: exp, Index: idx))
            //     //     .Where(x => x.Export.ObjectName.Text.Contains("WorldSettings"))
            //     //     .ToList();
            //     //
            //     // if (settings.Count > 0) {
            //     //     // var (setting, setting_idx) = settings.First();
            //     //     foreach (var (setting, setting_idx) in settings) {
            //     //         var ws = ctx.Package.GetExport(setting_idx);
            //     //         if (ws == null) continue;
            //     //         
            //     //         var dictInner = new Dictionary<string, object?>();
            //     //         foreach (var kvp in ws.Properties) {
            //     //             // dictInner.Add($"{kvp.PropertyType.ToString()}: {kvp.Name.PlainText}" , kvp.Tag?.GenericValue.ToString());
            //     //             // TODO: Add a helper for pretty property values
            //     //             var value = kvp.Tag?.GenericValue;
            //     //             if (value == null) continue;
            //     //             
            //     //             dictInner.Add(kvp.Name.PlainText , value.IsNumericType() ? value : value.ToString());
            //     //             if (kvp.Name.PlainText == "DefaultGameMode")
            //     //                 if (kvp.Tag?.GenericValue is FPackageIndex pidx)
            //     //                     gameMode = pidx.Name;
            //     //         }
            //     //         settingsDictionary.Add(ws.Name, dictInner);
            //     //     }
            //     // }
            // }
            // result._Maps.Add(GameMapInfo.FromSource(
            //     new ScanContextAssetSource(ctx),
            //     gameMode,
            //     settingsDictionary));
            return;
            // UObject? classExport = null;
            // if (ctx.Package is not Package package) {
            //     Debug.WriteLine($"{ctx.Package.Name} not a package ({ctx.Package.GetType().Name})");
            //     return;
            // }
            // var pkg = ctx.Package as Package;
            // if (ctx.Package is null) {
            //     Debug.WriteLine($"{ctx.Package.Name} not a package ({ctx.Package.GetType().Name})");
            //     return;
            // }
            // var test_main = BaseAsset.FindMainExport(ctx.Package);
            // try {
            //     var (mainExport, index) = BaseAsset.GetMainExport(ctx.Package).GetValueOrDefault();
            //     if (mainExport != null) {
            //         classExport = mainExport.ClassIndex.Load();
            //         if (mainExport.ClassName.EndsWith("BlueprintGeneratedClass"))
            //         {
            //             Debug.WriteLine("Using BlueprintGeneratedClass");
            //         }
            //
            //         if (!classExport.Name.Contains(_containerClassName))
            //             return;
            //         
            //         result.AddGenericMarker(GenericMarkerEntry.FromSource(
            //             new ScanContextAssetSource(ctx),
            //             "" // FIXME: Grab child class name from TMap
            //         ), classExport.Name);
            //     }
            //     else Debug.WriteLine($"Can't find mainExport for {ctx.Package.Name}");
            //
            //     return;
            // }
            // catch (Exception ex) {
            //     Debug.WriteLine(ex.Message);
            // }

            return;


            // var containers = ctx.Package.ExportsLazy
            //     .Where(e => e.Value.Class?.Name.Contains(_containerClassName) == true);
            //
            // var curPakName = ctx.PakEntry.PakFileReader.Name;
            //     
            //         //if (pkg == null || file.Value is not FPakEntry pakEntry) return;
            //
            //         //var context = new ScanContext(provider, pkg, file.Key, pakEntry);
            //         //var pakName = pakEntry.PakFileReader.Nam
            // foreach (var container in containers) {
            //     var map = container.Value.GetOrDefault<UScriptMap>(_mapPropertyName);
            //     if (map == null) continue;
            //     var mainExport = ctx.Package.ExportsLazy.FirstOrDefault().Value;
            //     var base_name = (mainExport.Super ?? mainExport.Template?.Outer)?.GetPathName();
            //     
            //     result.AddGenericMarker(GenericMarkerEntry.FromSource(
            //         new ScanContextAssetSource(ctx),
            //         "" // FIXME: Grab child class name from TMap
            //     ), base_name);
            //
            //     foreach (var entry in map.Properties) {
            //         if (entry.Key.GetValue(typeof(FPackageIndex)) is FPackageIndex idx) {
            //             var resolved = ctx.Package.ResolvePackageIndex(idx);
            //             if (resolved != null) {
            //                 DiscoveredReferences.Add(new PendingBlueprintReference {
            //                     SourceMarkerPath = ctx.FilePath,
            //                     SourceMarkerClassName = base_name,
            //                     TargetBlueprintPath = resolved.GetPathName(),
            //                     TargetClassName = resolved.Name.Text,
            //                     SourcePakFile = curPakName,
            //                 });
            //             }
            //         }
            //     }
            // }
        }
    }
}