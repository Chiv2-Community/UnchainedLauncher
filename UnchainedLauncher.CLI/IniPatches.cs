using StructuredINI;

namespace UnchainedLauncher.CLI;

internal static class IniPatches {
    internal static void Write(string directory, Dictionary<string, StructuredINIWriter> files, bool isConfigRoot) {
        foreach (var (file, writer) in files) {
            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, file + ".ini");
            var temporary = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".tmp");
            try {
                if (File.Exists(destination)) File.Copy(destination, temporary);
                if (!writer.WriteOut(temporary, isConfigRoot)) throw new IOException("Could not apply server INI profile; refusing to launch.");
                if (!File.Exists(destination) || !File.ReadAllBytes(destination).SequenceEqual(File.ReadAllBytes(temporary)))
                    File.Move(temporary, destination, overwrite: true);
            } finally {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
