using log4net;

namespace UnchainedLauncher.Core.Services.Mods.Registry {
    public class RegistryUtils {
        private static readonly ILog logger = LogManager.GetLogger(typeof(RegistryUtils));
        
        /// <summary>
        /// Some mod registry package lists are formatted as a line-separated list of $org/$modName
        /// This function parses that in to a list of ModIdentifier.
        /// </summary>
        /// <param name="packageList"></param>
        /// <returns></returns>
        public static IEnumerable<ModIdentifier> ParseLineSeparatedPackageList(string packageList) =>
            packageList.Split("\n").Bind(packageLine => {
                var id = ParseModIdentifier(packageLine, "/");
                if(id != null) return new List<ModIdentifier>(){id};
                
                logger.Warn($"Invalid package list line '{packageLine}' will be ignored.");
                return new List<ModIdentifier>();
            });

        public static ModIdentifier? ParseModIdentifier(string input, string separator) {
            var parts = input.Split(separator);
            return parts.Length == 2 
                ? new ModIdentifier(parts[0], parts[1]) 
                : null;
        }
    }
}