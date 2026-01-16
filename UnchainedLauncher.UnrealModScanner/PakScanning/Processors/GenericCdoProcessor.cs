using CUE4Parse.UE4.Objects.UObject;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.AssetSources;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
    using CUE4Parse.UE4.Assets.Exports;
    using CUE4Parse.UE4.Assets.Objects;
    using global::UnrealModScanner.Models;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using UnchainedLauncher.UnrealModScanner.Config;
    using UnchainedLauncher.UnrealModScanner.Utility;

    /// <summary>
    /// Uses CDO (Blueprints) or object properties (e.g. for a DataAsset) to retrieve
    /// a property list specified by user.
    /// </summary>
    public class GenericCdoProcessor : IAssetProcessor {
        private readonly string _targetClassName;
        //private readonly List<(string Name, Type Type)> _fieldsToExtract;
        private readonly List<PropertyConfig> _propertyConfigs;

        public GenericCdoProcessor(string targetClassName, /*List<(string, Type)> fields,*/ List<PropertyConfig> propertyConfigs) {
            _targetClassName = targetClassName;
            //_fieldsToExtract = fields;
            _propertyConfigs = propertyConfigs;
        }

        public void Process(ScanContext ctx, PakScanResult result) {
            var (mainExport, index) = BaseAsset.GetMainExport(ctx.Package).Value;
            // TODO: throw
            if (mainExport == null) return;

            // var pathname = (mainExportLazy.Super ?? mainExportLazy.Template?.Outer)?.GetPathName();
            var PathName = mainExport.ClassIndex.ResolvedObject.GetPathName();
            if (PathName.EndsWith("BlueprintGeneratedClass")) {
                if (!mainExport.SuperIndex.IsNull) {
                    try {
                        var resolved = ctx.Package.ResolvePackageIndex(mainExport.SuperIndex);
                        PathName = resolved.GetPathName();
                    }
                    catch (Exception e) {
                        Console.WriteLine(e);
                        throw;
                    }

                }
            }
            if (PathName.EndsWith("_C"))
                PathName = PathName.Split('.')[0];
            if (_targetClassName != "*" && PackageUtility.ToGamePathName(PathName) != _targetClassName)
                return;

            var propertyMap = new Dictionary<string, FPropertyTag>();
            // Why does GetExport crash with 0
            var mainExportLazy = index > 0 ? ctx.Package.GetExport(index) : ctx.Package.ExportsLazy[0].Value;

            if (mainExportLazy is UClass bgc) {
                var cdo = bgc.ClassDefaultObject.Load();
                propertyMap = propertyMap = cdo.Properties.ToDictionary(p => p.Name.Text, p => p);
            }
            else {
                propertyMap = mainExportLazy.Properties.ToDictionary(p => p.Name.Text, p => p);
            }
            var properties = new Dictionary<string, object?>();
            var childClassName = String.Empty;

            var filteredProperties = new Dictionary<string, object?>();
            foreach (var propConfig in _propertyConfigs) {
                if (!propertyMap.TryGetValue(propConfig.Name, out var propTag) || propTag.Tag == null)
                    continue;

                var rawValue = propTag.Tag.GetValue(propTag.Tag.GetType()) ?? propTag.Tag.GenericValue;

                // if (rawValue != null)
                //     Console.WriteLine($"Actually got val {rawValue}");
                if (rawValue == null) continue;

                filteredProperties[propConfig.Name] = propConfig.Mode switch {
                    EExtractionMode.Json => rawValue switch {
                        UObject nestedObj => JToken.Parse(nestedObj.ToSafeJson(0, propConfig.MaxDepth)?.ToString() ?? ""),

                        UScriptMap map => JToken.Parse(JsonConvert.SerializeObject(rawValue)).Children<JObject>().ToDictionary(
                                                    obj => obj["Key"]?.ToString() ?? "UnknownKey",
                                                    obj => obj["Value"]
                                                ),
                        _ => JToken.Parse(JsonConvert.SerializeObject(rawValue))
                    },
                    EExtractionMode.String => rawValue?.ToString() ?? "null",
                    EExtractionMode.Raw => rawValue,
                    EExtractionMode.StringJson => JsonConvert.DeserializeObject(rawValue.ToString()), // Whats the right way to do this?
                    _ => throw new ArgumentOutOfRangeException()
                };
            }
            var className = mainExport.ClassName;
            if (mainExportLazy is null)
                return;
            if (mainExportLazy.ExportType.EndsWith("BlueprintGeneratedClass")) {
                className = mainExportLazy.Class.Name;
                if (mainExportLazy is UStruct ustruct) {
                    if (ustruct.SuperStruct != null) {
                        try {
                            var resolved = ctx.Package.ResolvePackageIndex(ustruct.SuperStruct);
                            className = PackageUtility.ToGamePathName(resolved.GetPathName()); // Use pathname to resolve orphans
                        }
                        catch (Exception e) {
                            className = ustruct.SuperStruct.Name;
                            Console.WriteLine(e);
                            throw;
                        }
                    }
                }
                // Debug.WriteLine("Using BlueprintGeneratedClass");
            }
            var entry = GenericAssetEntry.FromSource(
                new ScanContextAssetSource(ctx),
                filteredProperties);
            // var base_name = (mainExportLazy.Super ?? mainExportLazy.Template?.Outer)?.GetPathName();
            result.AddGenericEntry(entry, className ?? "CDO");
            // result.RemoveArbitraryAsset(entry);

            return;
            // UObject? classExport = null;
            // var exports22 = (ctx.Package as Package).ExportsLazy
            //     .Select((exp, index) => (Export: exp.Value, Index: index))
            //     .ToList();
            // try {
            //     var (mainExport, index) = BaseAsset.GetMainExport(ctx.Package).GetValueOrDefault();
            //     
            //     if (mainExport != null) {
            //         
            //         var lazyExport = ctx.Package.GetExport(index);
            //         classExport = mainExport.ClassIndex.Load();
            //         var templateExport = mainExport.TemplateIndex.Load();
            //         var resolvedTemplate = ctx.Package.ResolvePackageIndex(mainExport.TemplateIndex);
            //         var outer = mainExport.ClassIndex.ResolvedObject?.Load();
            //         var resolvedOuter = ctx.Package.ResolvePackageIndex(mainExport.ClassIndex);
            //         var resolvedOuter2 = ctx.Package.ResolvePackageIndex(mainExport.OuterIndex);
            //         var className = classExport.Name;
            //         if (classExport is null)
            //             return;
            //         if (classExport.ExportType.EndsWith("BlueprintGeneratedClass"))
            //         {
            //             className = classExport.Class.Name;
            //             if (classExport is UStruct ustruct) {
            //                 if (ustruct.SuperStruct != null)
            //                     className = ustruct.SuperStruct.Name;
            //             }
            //             Debug.WriteLine("Using BlueprintGeneratedClass");
            //         }
            //         
            //         var entry = GenericAssetEntry.FromSource(
            //             new ScanContextAssetSource(ctx),
            //             new ());
            //         result.AddGenericEntry(entry, className ?? "CDO");
            //     }
            //     else Debug.WriteLine($"Can't find mainExport for {ctx.Package.Name}");
            //
            //     return;
            // }
            // catch (Exception ex) {
            //     Debug.WriteLine(ex.Message);
            // }
            // var first = ctx.Package.ExportsLazy.Where(val => val.Value.Super?.GetPathName() != null).ToList();
            // var names = new List<string>();
            // if (first.Count > 0)
            //     names.Add(first[0].Value.Super?.GetPathName());
            // //((CUE4Parse.UE4.Assets.Exports.UObject)(new System.LazyDebugView<CUE4Parse.UE4.Assets.Exports.UObject>(((CUE4Parse.UE4.Assets.AbstractUePackage)ctx.Package).ExportsLazy[0]).Value).Template.Package).Name
            // var matches = ctx.Package.ExportsLazy
            //     //.Where(e => e.Value.Class?.Name.Contains(_targetClassName, StringComparison.OrdinalIgnoreCase) == true);
            //     // .Where(e => (e.Value.Super ?? e.Value.Template?.Outer)?.GetPathName().Contains(_targetClassName, StringComparison.OrdinalIgnoreCase) == true);
            //     .Where(e => (e.Value.Super ?? e.Value.Template?.Outer)?.GetPathName() == _targetClassName);
            // //((CUE4Parse.UE4.Assets.Exports.UObject)(new System.LazyDebugView<CUE4Parse.UE4.Assets.Exports.UObject>(ctx.Package.ExportsLazy[8]).Value).Super.Package).Name
            //
            // //foreach (var export in ctx.Package.ExportsLazy) {
            // //    var super = export.Value.Super?.GetPathName();
            // //    var template = export.Value.Template?.Outer?.GetPathName();
            //
            // //    Debug.WriteLine($"{ctx.Package.Name} : {export.Value.Name} super {super} template {template}");
            // //}
            //
            // var cdos = ctx.GetClassDefaultObjects();
            // foreach (var export in matches) {
            //     var _properties = new List<FPropertyTag>();
            //     if (export.Value is UBlueprintGeneratedClass bpc) {
            //         var cdo = bpc.ClassDefaultObject.Load();
            //         if (cdo == null) continue;
            //         _properties = cdo.Properties;
            //     }
            //     else {
            //         _properties = export.Value.Properties;
            //     }
            //
            //     var base_name = (export.Value.Super ?? export.Value.Template?.Outer)?.GetPathName();
            //
            //     // var entry = new GenericAssetEntry {
            //     //     AssetPath = ctx.FilePath,
            //     //     ClassName = export.Value.Name
            //     // };
            //
            //     var propertyMap = _properties.ToDictionary(p => p.Name.Text, p => p);
            //
            //     var filteredProperties = new Dictionary<string, object?>();
            //     foreach (var propConfig in _propertyConfigs) {
            //         if (!propertyMap.TryGetValue(propConfig.Name, out var propTag) || propTag.Tag == null)
            //             continue;
            //
            //         var rawValue = propTag.Tag.GetValue(propTag.Tag.GetType()) ?? propTag.Tag.GenericValue;
            //
            //         // if (rawValue != null)
            //         //     Console.WriteLine($"Actually got val {rawValue}");
            //         if (rawValue == null) continue;
            //         
            //         filteredProperties[propConfig.Name] = propConfig.Mode switch {
            //             //EExtractionMode.Json => rawValue switch {
            //             //    UObject nestedObj => nestedObj.ToSafeJson(0, propConfig.MaxDepth),
            //             //    _ => JsonConvert.SerializeObject(rawValue)
            //             //},
            //             EExtractionMode.Json => rawValue switch {
            //                 UObject nestedObj => JToken.Parse(nestedObj.ToSafeJson(0, propConfig.MaxDepth)?.ToString() ?? ""),
            //
            //                 UScriptMap map => JToken.Parse(JsonConvert.SerializeObject(rawValue)).Children<JObject>().ToDictionary(
            //                                             obj => obj["Key"]?.ToString() ?? "UnknownKey",
            //                                             obj => obj["Value"] 
            //                                         ),
            //                 // This turns the string into a "live" JArray/JObject
            //                 _ => JToken.Parse(JsonConvert.SerializeObject(rawValue))
            //             },
            //             //EExtractionMode.Json => rawValue switch {
            //             //    UObject nestedObj => nestedObj.ToSafeJson(0, propConfig.MaxDepth),
            //             //    //UScriptMap map => JsonConvert.SerializeObject(rawValue, [new UScriptMapConverter()]),
            //             //    UScriptMap map => JToken.FromObject(map, new JsonSerializer {
            //             //        Converters = { new UScriptMapConverter() }
            //             //    }),
            //             //    UScriptSet sset => JsonConvert.SerializeObject(sset),
            //             //    // Convert to a JToken so it remains a "live" JSON structure 
            //             //    // instead of an escaped string
            //             //    //_ => rawValue != null ? JToken.FromObject(rawValue) : null
            //             //    _ => rawValue != null ? JsonConvert.SerializeObject(rawValue) : null
            //             //},
            //             EExtractionMode.String => rawValue?.ToString() ?? "null",
            //             EExtractionMode.Raw => rawValue,
            //             EExtractionMode.StringJson => JsonConvert.DeserializeObject(rawValue.ToString()), // Whats the right way to do this?
            //             _ => throw new ArgumentOutOfRangeException()
            //         };
            //     }
            //
            //     var entry = GenericAssetEntry.FromSource(
            //         new ScanContextAssetSource(ctx),
            //         filteredProperties);
            //     result.AddGenericEntry(entry, base_name ?? "CDO");
            // }

            //foreach (var match in matches) {
            //    UObject? targetObj = match.Value is UBlueprintGeneratedClass bpc
            //        ? bpc.ClassDefaultObject.Load()
            //        : match.Value;

            //    if (targetObj == null) continue;

            //    var entry = new GenericAssetEntry { AssetPath = ctx.FilePath, ClassName = _targetClassName };

            //    foreach (var propConfig in _propertyConfigs) {
            //        var val = targetObj.GetOrDefault<object>(propConfig.Name);
            //        if (val == null) continue;

            //        // TODO: check when this is called
            //        if (propConfig.Mode == EExtractionMode.Json) {
            //            // We use CUE4Parse's ToJson with formatting options if available,
            //            // otherwise, we fall back to a depth-limited custom serializer.
            //            if (val is UObject nestedObj) {
            //                entry.Properties[propConfig.Name] = nestedObj.ToSafeJson(0, propConfig.MaxDepth);
            //            }
            //            else {
            //                entry.Properties[propConfig.Name] = JsonConvert.SerializeObject(val);
            //            }

            //            // NOTE: In production, you would pass propConfig.MaxDepth 
            //            // to a custom JsonWriter if the target game has circular refs.
            //        }
            //        else {
            //            entry.Properties[propConfig.Name] = val.ToString();
            //        }
            //    }
            //    result.AddGenericEntry(entry);
            //}
        }

    }
}