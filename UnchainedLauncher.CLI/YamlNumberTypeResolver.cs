using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace UnchainedLauncher.CLI;

// Avoid YamlDotNet's default Single rounding for untyped floating-point scalars.
internal sealed class YamlNumberTypeResolver : INodeTypeResolver {
    public bool Resolve(NodeEvent? nodeEvent, ref Type currentType) {
        if (currentType != typeof(object) || nodeEvent is not Scalar { IsKey: false, Style: ScalarStyle.Plain } scalar || !scalar.Tag.IsEmpty)
            return false;
        if (scalar.Value.IndexOfAny(['.', 'e', 'E']) < 0) return false;
        if (scalar.Value.IndexOfAny(['e', 'E']) < 0 && decimal.TryParse(scalar.Value,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) &&
            (number != 0 || !scalar.Value.Any(character => character is >= '1' and <= '9'))) currentType = typeof(decimal);
        else if (double.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) currentType = typeof(double);
        else return false;
        return true;
    }
}
