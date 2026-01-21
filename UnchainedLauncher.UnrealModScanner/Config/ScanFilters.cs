using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Pak.Objects;
using System.Text.Json.Serialization;

namespace UnchainedLauncher.UnrealModScanner.Config {
    using PakContentFilter = Predicate<KeyValuePair<string, GameFile>>;
    
    // Discriminated unions avoid common json security vulnerabilities that prevent arbitrary
    // classes being loaded and executed during json serialization. They add a concrete and narrow scope for what
    // is allowed to be json serialized. Other implementations of IScanFilter can be created elsewhere, but only
    // those defined here will be able to be serialized.  
    //
    // For example, this avoids an issue where a malicious person could tell somebody to copy their ScanOptions
    // config that could contain a malicious payload that would be loaded when an abstract type like IScanFilter
    // got deserialized, if using reflection-based methods like Newtonsoft.Json.TypeNameHandling
    //
    // So while all of this is a little bit obtuse, it's not too bad and probably worth it.
    //
    // https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca2326
    // https://stackoverflow.com/questions/55924299/insecure-deserialization-using-json-net
    
    /// <summary>
    /// The IScanFilter class provides a means of filtering through pak contents.
    /// These are used in the FilteredFileProvider to specify which pak contents to include or exclude in a scan.
    ///
    /// Example usage:
    ///   new Whitelist(["foo", "bar"])
    ///     .With(new AssetsOnlyScanFilter())
    ///     .With(new IgnorePakScanFilter("pak1.pak", "pak2.pak"))
    /// </summary>
    [JsonDerivedType(typeof(Whitelist), "Whitelist")]
    [JsonDerivedType(typeof(Blacklist), "Blacklist")]
    [JsonDerivedType(typeof(AssetsOnlyScanFilter), "AssetsOnly")]
    [JsonDerivedType(typeof(MapsOnlyScanFilter), "MapsOnly")]
    [JsonDerivedType(typeof(IgnorePakScanFilter), "IgnorePak")]
    [JsonDerivedType(typeof(SpecificPakScanFilter), "SpecificPak")]
    [JsonDerivedType(typeof(PassScanFilter), "Pass")]
    [JsonDerivedType(typeof(CombinedScanFilter), "Combined")]
    
    public abstract class IScanFilter(string filterName) {

        /// <summary>
        /// The FilterName can be useful for debugging, but is otherwise useless
        /// It could probably be removed?
        /// </summary>
        [JsonIgnore] public readonly string FilterName = filterName;
        public abstract PakContentFilter CreatePakContentFilter();

        public static IScanFilter CombineAll(params IScanFilter?[] others) {
            var filterNoops =
                others
                    .Where(x => x != null)
                    .Cast<IScanFilter>()
                    .Where(x => x is not PassScanFilter)
                    .ToArray();

            if (filterNoops.Length == 0) return new PassScanFilter();
            return new CombinedScanFilter(filterNoops.ToArray());
        }

        public IScanFilter With(IScanFilter? other) {
            return other is null or PassScanFilter ? this : new CombinedScanFilter(this, other);
        }

    }
    
    public class CombinedScanFilter(params IScanFilter?[] filterMembers) : IScanFilter(string.Join(" with ", filterMembers.Select(x => x.FilterName))) {
        [JsonPropertyName("FilterMembers")]
        public IScanFilter[] FilterMembers { get; init; } =
            filterMembers
                .Where(x => x != null)
                .SelectMany(x => x is CombinedScanFilter y ? y.FilterMembers : [x!])
                .ToArray();

        public override PakContentFilter CreatePakContentFilter() {
            var createdContentFilters =
                FilterMembers.Select(x => x.CreatePakContentFilter());
                
            return kv => createdContentFilters.All(x => x(kv));
        }
    }
    
    public class Whitelist(IEnumerable<string> whitelist) : IScanFilter("Whitelist") {
        public IEnumerable<string> Items { get; init; } = whitelist;
        public override PakContentFilter CreatePakContentFilter() => 
            kv => Items.Any(x => kv.Key.Contains(x));
    }
    
    public class Blacklist(IEnumerable<string> blacklist) : IScanFilter("Exclude Paths") {
        public IEnumerable<string> Items { get; init; } = blacklist;
        public override PakContentFilter CreatePakContentFilter() => 
            kv => !Items.Any(path => kv.Key.Contains(path));
    }
    
    public class AssetsOnlyScanFilter() : IScanFilter("Assets Only") {
        public override PakContentFilter CreatePakContentFilter() => 
            kv => {
                var path = kv.Key;
                var isAsset = path.EndsWith(".uasset") || path.EndsWith(".umap");
                return isAsset;
            };
    }
    
    public class MapsOnlyScanFilter() : IScanFilter("Maps Only") {
        public override PakContentFilter CreatePakContentFilter() => 
            kv => kv.Key.EndsWith(".umap");
    }
    
    public class IgnorePakScanFilter(params string[] pakNamesToIgnore) : IScanFilter("Ignore Paks") {
        public string[] PakNames { get; init; } = pakNamesToIgnore;
        public override PakContentFilter CreatePakContentFilter() => 
            kv => kv.Value is not FPakEntry fPakEntry || !PakNames.Contains(fPakEntry.PakFileReader.Name);
    }
    
    public class SpecificPakScanFilter(params string[] pakNamesToInclude) : IScanFilter("Include specific paks") {
        public string[] PakNames { get; init; } = pakNamesToInclude;
        public override PakContentFilter CreatePakContentFilter() => 
            kv => kv.Value is not FPakEntry fPakEntry || PakNames.Contains(fPakEntry.PakFileReader.Name);
    }
    
    public class PassScanFilter() : IScanFilter("Pass") {
        public override PakContentFilter CreatePakContentFilter() => 
            _ => true;
    }
}