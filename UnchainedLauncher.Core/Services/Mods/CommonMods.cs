using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.Core.Services.Mods {
    public class CommonMods {
        public static readonly ModIdentifier UnchainedMods = new("Chiv2-Community", "Unchained-Mods");
        public static readonly List<ModIdentifier> AllCommonMods = new List<ModIdentifier>() { UnchainedMods };
    }
}