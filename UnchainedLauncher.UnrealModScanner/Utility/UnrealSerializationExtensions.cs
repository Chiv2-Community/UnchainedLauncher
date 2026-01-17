
using CUE4Parse.UE4.Assets.Exports;

namespace UnchainedLauncher.UnrealModScanner.Utility {
    public static class UnrealSerializationExtensions {
        public static object? ToSafeJson(this UObject obj, int currentDepth, int maxDepth) {
            if (currentDepth > maxDepth) return "[Depth Limit Reached]";

            var dict = new Dictionary<string, object?>();
            // Using CUE4Parse reflection to iterate properties
            foreach (var prop in obj.Properties) {
                var value = prop.Tag?.GetValue(prop.Tag.GetType());

                if (value is UObject nestedObj) {
                    dict[prop.Name.Text] = nestedObj.ToSafeJson(currentDepth + 1, maxDepth);
                }
                else {
                    dict[prop.Name.Text] = value?.ToString();
                }
            }
            return dict;
        }
    }
}