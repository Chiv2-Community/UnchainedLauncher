using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Mods;

public sealed class ProfileAssetPaths {
    public const string SharedSuffix = "unchained";
    public string Root { get; }
    public string Paks => Path.Combine(Path.GetDirectoryName(Root)!, "Paks");
    public string Plugins => Path.Combine(Root, "plugins");
    public string PakCache { get; }
    public string DllCache { get; }

    public ProfileAssetPaths(string suffix, string? localAppData = null) {
        var game = Path.Combine(localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chivalry 2");
        var normalized = suffix.Replace("-", "", StringComparison.Ordinal);
        if (normalized.Length == 0 || normalized.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("Invalid saved directory suffix.");
        Root = Path.Combine(game, "Saved_" + normalized, "unchained");
        PakCache = Path.Combine(game, "unchained", "cache", "paks");
        DllCache = Path.Combine(game, "unchained", "cache", "dlls");
    }

    public PakDir CreatePakDir(string installation, IEnumerable<ManagedPak>? managedPaks = null) {
        Directory.CreateDirectory(Paks);
        return new PakDir(Paks, managedPaks ?? [], Path.Combine(installation, FilePaths.PakDir, "pakchunk0-WindowsNoEditor.sig"), PakCache);
    }
}
