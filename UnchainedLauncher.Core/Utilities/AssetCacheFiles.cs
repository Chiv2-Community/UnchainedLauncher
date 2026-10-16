using System.Runtime.InteropServices;

namespace UnchainedLauncher.Core.Utilities;

internal static class AssetCacheFiles {
    internal static async Task<FileStream> LockAsync(string path, CancellationToken cancellationToken) {
        var deadline = DateTime.UtcNow.AddMinutes(30);
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(100, cancellationToken); }
        }
    }

    // Direct publication is atomic and never replaces an occupied destination.
    internal static bool TryCreateHardLink(string destination, string source) => OperatingSystem.IsWindows()
        ? CreateHardLinkW(destination, source, IntPtr.Zero)
        : OperatingSystem.IsLinux() && Link(source, destination) == 0;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string existingFileName, string fileName);
}