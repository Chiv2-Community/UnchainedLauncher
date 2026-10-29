namespace UnchainedLauncher.Core.Services.Processes;

public sealed record ProtonDll(string Path, long Size, string Sha256);

// Private, per-launch IPC. Paths are translated by Wine before reaching the helper.
public sealed record ProtonLaunchRequest(string Executable, string WorkingDirectory,
    string Arguments, IReadOnlyList<ProtonDll> Dlls, bool Server);