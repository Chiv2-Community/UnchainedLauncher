using System.Diagnostics;

namespace UnchainedLauncher.CLI;

/// <summary>Lifecycle belongs to the launch supervisor; metrics belong to the game.</summary>
public sealed class LaunchedGame : IDisposable {
    private readonly Process _supervisor;
    private readonly Process _game;
    private readonly Func<Task>? _stop;
    internal ForwardedConsole? ConsoleOutput { get; set; }
    internal Action? CleanupOutput;

    public LaunchedGame(Process supervisor, Process? game = null, Func<Task>? stop = null) {
        _supervisor = supervisor;
        _game = game ?? supervisor;
        _stop = stop;
    }

    public int Id => _game.Id;
    public bool HasExited => _supervisor.HasExited;
    public int ExitCode => _supervisor.ExitCode;
    public Process Metrics => _game;

    public async Task StopAsync() {
        if (HasExited) return;
        if (_stop != null) await _stop();
        else _supervisor.Kill(entireProcessTree: true);
        await _supervisor.WaitForExitAsync();
    }

    public void Dispose() {
        Interlocked.Exchange(ref CleanupOutput, null)?.Invoke();
        ConsoleOutput?.Dispose();
        if (_game != _supervisor) _game.Dispose();
        // Client launches detach immediately; the Proton cleanup task still owns this handle.
        if (_stop == null || _supervisor.HasExited) _supervisor.Dispose();
    }
}
