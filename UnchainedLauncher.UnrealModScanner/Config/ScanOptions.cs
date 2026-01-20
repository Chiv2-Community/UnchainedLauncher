using CUE4Parse.UE4.Versions;


namespace UnchainedLauncher.UnrealModScanner.Config {

    public enum FilterMode {
        Whitelist,
        Blacklist
    }

    /// <summary>
    /// Options for scanning a pak file associated with a specific UE game.
    /// </summary>
    /// <param name="VanillaPakNames">The names of all vanilla paks.</param>
    /// <param name="GameVersion">The unreal engine version that the game uses</param>
    /// <param name="AesKey">The encryption key used to decrypt pak files</param>
    /// <param name="VanillaAssetPaths">Folders representing vanilla content. Any content found in vanilla asset paths will be considered a vanilla asset when determining what assets qualify for asset replacements.</param>
    /// <param name="FilterPaths">A list of paths to include or exclude in the scan, based on FilterMode.</param>
    /// <param name="FilterMode">Whether to treat FilterPaths as a blacklist or whitelist.</param>
    /// <param name="CdoProcessors"></param>
    /// <param name="MarkerProcessors"></param>
    /// <param name="Targets">TODO</param>
    public record ScanOptions(
        HashSet<string> VanillaPakNames,
        EGame GameVersion,
        string AesKey,
        List<string> VanillaAssetPaths,
        List<string> FilterPaths,
        FilterMode FilterMode,
        List<CdoProcessorConfig> CdoProcessors,
        List<MarkerDiscoveryConfig> MarkerProcessors,
        List<ProcessorTarget>? Targets // NYI
    );

    public class ProcessorTarget {
        public string ClassName { get; set; } = ""; // e.g., "Default__MyMarker_C"
        public bool DumpAllProperties { get; set; } = false; // If true, ignore 'Properties' list and dump everything
        public List<PropertyConfig> Properties { get; set; } = new();
    }
}