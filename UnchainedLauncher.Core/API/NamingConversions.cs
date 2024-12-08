using LanguageExt;
using System.Text.Json;

namespace UnchainedLauncher.Core.API
{
    public class SnakeCaseNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
        {
            string convertedName = string.Concat(
                    name.Select(
                        (x, i) => i > 0 && char.IsUpper(x) ? "_" + x.ToString() : x.ToString()
                    )
                ).ToLower();
            return convertedName;
        }
    }
}
