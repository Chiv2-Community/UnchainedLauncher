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
    
    public abstract record IScanFilter(string filterName) {

        /// <summary>
        /// The FilterName can be useful for debugging, but is otherwise useless
        /// It could probably be removed?
        /// </summary>
        [JsonIgnore] public readonly string FilterName = filterName;
        public abstract PakContentFilter CreatePakContentFilter();

        public static IScanFilter CombineAll(params IScanFilter?[] others) {
            var minimizedFilters =
                others
                    .Where(x => x != null)
                    .Cast<IScanFilter>()
                    .Where(x => x is not PassScanFilter)
                    .SelectMany(x => x is CombinedScanFilter csf ? csf.FilterMembers : [x])
                    .ToArray();

            if (minimizedFilters.Length == 0) return new PassScanFilter();
            return new CombinedScanFilter(minimizedFilters.ToArray());
        }

        public IScanFilter With(IScanFilter? other) => CombineAll(this, other);

    }
    
    public record CombinedScanFilter(params IScanFilter[] FilterMembers) : IScanFilter(string.Join(" with ", FilterMembers.Select(x => x.FilterName))) {
        public override PakContentFilter CreatePakContentFilter() {
            var createdContentFilters =
                FilterMembers.Select(x => x.CreatePakContentFilter());
                
            return kv => createdContentFilters.All(x => x(kv));
        }
    }
    
    public record Whitelist(List<string> Items) : IScanFilter("Whitelist") {
        public override PakContentFilter CreatePakContentFilter() => 
            kv => Items.Any(x => kv.Key.Contains(x));
    }
    
    public record Blacklist(List<string> Items) : IScanFilter("Exclude Paths") {
        public override PakContentFilter CreatePakContentFilter() => 
            kv => !Items.Any(path => kv.Key.Contains(path));
    }
    
    public record AssetsOnlyScanFilter() : IScanFilter("Assets Only") {
        public override PakContentFilter CreatePakContentFilter() => 
            kv => {
                var path = kv.Key;
                var isAsset = path.EndsWith(".uasset") || path.EndsWith(".umap");
                return isAsset;
            };
    }
    
    public record MapsOnlyScanFilter() : IScanFilter("Maps Only") {
        public override PakContentFilter CreatePakContentFilter() => 
            kv => kv.Key.EndsWith(".umap");
    }
    
    public record IgnorePakScanFilter(params string[] PakNames) : IScanFilter("Ignore Paks") {
        public override PakContentFilter CreatePakContentFilter() => 
            kv => kv.Value is not FPakEntry fPakEntry || !PakNames.Contains(fPakEntry.PakFileReader.Name);
    }
    
    public record SpecificPakScanFilter(params string[] PakNames) : IScanFilter("Include specific paks") {
        public override PakContentFilter CreatePakContentFilter() => 
            kv => kv.Value is not FPakEntry fPakEntry || PakNames.Contains(fPakEntry.PakFileReader.Name);
    }
    
    public record PassScanFilter() : IScanFilter("Pass") {
        public override PakContentFilter CreatePakContentFilter() => 
            _ => true;
    }
}