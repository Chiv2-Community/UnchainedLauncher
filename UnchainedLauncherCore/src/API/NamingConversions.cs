using LanguageExt;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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
            // Conversion to other naming convention goes here. Like SnakeCase, KebabCase etc.
            return convertedName;
        }
    }
}
