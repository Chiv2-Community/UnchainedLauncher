using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using UnchainedLauncher.UnrealModScanner.Assets;
using UnchainedLauncher.UnrealModScanner.AssetSources;
using UnchainedLauncher.UnrealModScanner.Models.Dto;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Utility;
using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.PakScanning.Processors {
    /// <summary>
    /// Extracts marker properties and queues declared actor references for the second pass.
    /// </summary>
    public class ReferenceDiscoveryProcessor : IAssetProcessor {
        private static readonly log4net.ILog Logger = log4net.LogManager.GetLogger(typeof(ReferenceDiscoveryProcessor));
        private readonly string _containerClassName;
        private readonly string _mapPropertyName;

        public ConcurrentBag<PendingBlueprintReference> DiscoveredReferences { get; } = new();

        /// <param name="containerClass">Exact marker class name, such as DA_ModMarker_C.</param>
        /// <param name="mapProperty">Reference-map property name, such as ModActors.</param>
        public ReferenceDiscoveryProcessor(string containerClass, string mapProperty) {
            _containerClassName = containerClass;
            _mapPropertyName = mapProperty;
        }

        public void Process(ScanContext ctx, PakScanResult result) {
            var (mainExport, index) = BaseAsset.GetMainExport(ctx.Package).Value;
            if (mainExport == null || mainExport.ClassName != _containerClassName) return;
            var mainExportLazy = index > 0 ? ctx.Package.GetExport(index) : ctx.Package.ExportsLazy[0].Value;
            var map = mainExportLazy.GetOrDefault<UScriptMap>(_mapPropertyName);

            var properties = new Dictionary<string, object?>();
            var childClassName = string.Empty;
            foreach (var kvp in mainExportLazy.Properties) {
                if (kvp.Tag?.GenericValue == null) continue;

                try {
                    var rawValue = kvp.Tag.GetValue(kvp.Tag.GetType()) ?? kvp.Tag.GenericValue;
                    properties.Add(kvp.Name.Text, rawValue switch {
                        UObject nestedObj => JToken.Parse(nestedObj.ToSafeJson(0, 3)?.ToString() ?? null),
                        UScriptMap => JToken.Parse(JsonConvert.SerializeObject(rawValue)).Children<JObject>()
                            .ToDictionary(
                                obj => obj["Key"]?.ToString() ?? "UnknownKey",
                                obj => obj["Value"]
                            ),
                        _ => JToken.Parse(JsonConvert.SerializeObject(rawValue))
                    });
                }
                catch (Exception e) {
                    ctx.ProcessingErrors.Add(new AssetProcessingError {
                        ProcessorName = nameof(ReferenceDiscoveryProcessor),
                        PackagePath = ctx.FilePath,
                        ExceptionType = e.GetType().Name,
                        Message = e.Message
                    });
                    Logger.Error($"Marker property '{kvp.Name.Text}' extraction failed: pak '{ctx.PakEntry.PakFileReader.Name}', asset '{ctx.FilePath}'.", e);
                }
            }

            if (map != null) {
                foreach (var entry in map.Properties) {
                    if (entry.Key.GetValue(typeof(FPackageIndex)) is FPackageIndex idx) {
                        var resolved = ctx.Package.ResolvePackageIndex(idx);
                        if (childClassName.Length == 0 && resolved.Super != null)
                            childClassName = PackageUtility.ToGamePathName(resolved.Super.GetPathName());
                        if (resolved != null) {
                            DiscoveredReferences.Add(new PendingBlueprintReference {
                                SourceMarkerPath = ctx.FilePath,
                                SourceMarkerClassName = mainExport.ClassName,
                                TargetBlueprintPath = PackageUtility.ToGamePathName(resolved.GetPathName()),
                                TargetClassName = resolved.Name.Text,
                                SourcePakFile = ctx.PakEntry.PakFileReader.Name,
                                SourceFiles = ctx.PakEntry.PakFileReader.Files,
                            });
                        }
                    }
                }
            }

            result.AddGenericMarker(GenericMarkerEntry.FromSource(
                new ScanContextAssetSource(ctx), childClassName, null, properties), mainExport.ClassName);
        }
    }
}