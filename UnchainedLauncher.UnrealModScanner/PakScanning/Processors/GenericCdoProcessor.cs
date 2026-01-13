

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
    using CUE4Parse.UE4.Assets.Exports;
    using CUE4Parse.UE4.Assets.Objects;
    using CUE4Parse.UE4.Objects.Engine;
    using global::UnrealModScanner.Models;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using UnchainedLauncher.UnrealModScanner.Config;
    using UnchainedLauncher.UnrealModScanner.Models.Dto;
    using UnchainedLauncher.UnrealModScanner.Utility;

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
            var first = ctx.Package.ExportsLazy.Where(val => val.Value.Super?.GetPathName() != null).ToList();
            var names = new List<string>();
            if (first.Count > 0)
                names.Add(first[0].Value.Super?.GetPathName());
            //((CUE4Parse.UE4.Assets.Exports.UObject)(new System.LazyDebugView<CUE4Parse.UE4.Assets.Exports.UObject>(((CUE4Parse.UE4.Assets.AbstractUePackage)ctx.Package).ExportsLazy[0]).Value).Template.Package).Name
            var matches = ctx.Package.ExportsLazy
                //.Where(e => e.Value.Class?.Name.Contains(_targetClassName, StringComparison.OrdinalIgnoreCase) == true);
                .Where(e => (e.Value.Super ?? e.Value.Template?.Outer)?.GetPathName().Contains(_targetClassName, StringComparison.OrdinalIgnoreCase) == true);
            //((CUE4Parse.UE4.Assets.Exports.UObject)(new System.LazyDebugView<CUE4Parse.UE4.Assets.Exports.UObject>(ctx.Package.ExportsLazy[8]).Value).Super.Package).Name

            //foreach (var export in ctx.Package.ExportsLazy) {
            //    var super = export.Value.Super?.GetPathName();
            //    var template = export.Value.Template?.Outer?.GetPathName();

            //    Debug.WriteLine($"{ctx.Package.Name} : {export.Value.Name} super {super} template {template}");
            //}
            foreach (var export in matches) {
                var _properties = new List<FPropertyTag>();
                if (export.Value is UBlueprintGeneratedClass bpc) {
                    var cdo = bpc.ClassDefaultObject.Load();
                    if (cdo == null) continue;
                    _properties = cdo.Properties;
                }
                else {
                    _properties = export.Value.Properties;
                }

                var base_name = (export.Value.Super ?? export.Value.Template?.Outer)?.GetPathName();

                var entry = new GenericAssetEntry {
                    AssetPath = ctx.FilePath,
                    ClassName = export.Value.Name
                };

                var propertyMap = _properties.ToDictionary(p => p.Name.Text, p => p);

                foreach (var propConfig in _propertyConfigs) {
                    if (!propertyMap.TryGetValue(propConfig.Name, out var propTag) || propTag.Tag == null)
                        continue;

                    var rawValue = propTag.Tag.GetValue(propTag.Tag.GetType()) ?? propTag.Tag.GenericValue;

                    if (rawValue != null)
                        Console.WriteLine($"Actually got val {rawValue}");
                    else continue;
                    entry.Properties[propConfig.Name] = propConfig.Mode switch {
                        //EExtractionMode.Json => rawValue switch {
                        //    UObject nestedObj => nestedObj.ToSafeJson(0, propConfig.MaxDepth),
                        //    _ => JsonConvert.SerializeObject(rawValue)
                        //},
                        EExtractionMode.Json => rawValue switch {
                            UObject nestedObj => JToken.Parse(nestedObj.ToSafeJson(0, propConfig.MaxDepth)?.ToString() ?? ""),

                            UScriptMap map => JToken.Parse(JsonConvert.SerializeObject(rawValue)).Children<JObject>().ToDictionary(
                                                        obj => obj["Key"]?.ToString() ?? "UnknownKey",
                                                        obj => obj["Value"] 
                                                    ),
                            // This turns the string into a "live" JArray/JObject
                            _ => JToken.Parse(JsonConvert.SerializeObject(rawValue))
                        },
                        //EExtractionMode.Json => rawValue switch {
                        //    UObject nestedObj => nestedObj.ToSafeJson(0, propConfig.MaxDepth),
                        //    //UScriptMap map => JsonConvert.SerializeObject(rawValue, [new UScriptMapConverter()]),
                        //    UScriptMap map => JToken.FromObject(map, new JsonSerializer {
                        //        Converters = { new UScriptMapConverter() }
                        //    }),
                        //    UScriptSet sset => JsonConvert.SerializeObject(sset),
                        //    // Convert to a JToken so it remains a "live" JSON structure 
                        //    // instead of an escaped string
                        //    //_ => rawValue != null ? JToken.FromObject(rawValue) : null
                        //    _ => rawValue != null ? JsonConvert.SerializeObject(rawValue) : null
                        //},
                        EExtractionMode.String => rawValue?.ToString() ?? "null",
                        EExtractionMode.Raw => rawValue,
                        EExtractionMode.StringJson => JsonConvert.DeserializeObject(rawValue.ToString()), // Whats the right way to do this?
                        _ => throw new ArgumentOutOfRangeException()
                    };
                }

                result.AddGenericEntry(entry, base_name ?? "CDO");
            }

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
