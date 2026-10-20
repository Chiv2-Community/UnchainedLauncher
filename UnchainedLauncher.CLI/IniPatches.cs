using StructuredINI;

namespace UnchainedLauncher.CLI;

internal static class IniPatches {
    internal static IEnumerable<KeyValuePair<string, string>> Array(string key, IEnumerable<string> values) =>
        new[] { KeyValuePair.Create("!" + key, "ClearArray") }
            .Concat(values.Select(value => KeyValuePair.Create("." + key, value)));

    internal static void Write(string directory, Dictionary<string, StructuredINIWriter> files) {
        foreach (var (file, writer) in files) {
            if (!writer.WriteOut(Path.Combine(directory, file + ".ini")))
                throw new IOException("Could not apply server INI profile; refusing to launch.");
        }
    }
}
