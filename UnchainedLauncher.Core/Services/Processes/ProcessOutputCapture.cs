using System.IO.Pipes;

namespace UnchainedLauncher.Core.Services.Processes;

/// <summary>Redirected stdio for suspended Windows launches.</summary>
public sealed class ProcessOutputCapture : IDisposable {
    private readonly AnonymousPipeServerStream _stdout;
    private readonly AnonymousPipeServerStream _stderr;
    private readonly AnonymousPipeServerStream _stdin;
    private int _sealed;
    public Stream StandardOutput => _stdout;
    public Stream StandardError => _stderr;
    internal IntPtr[] InheritedHandles => [OutputHandle, ErrorHandle, InputHandle];
    internal IntPtr OutputHandle => _stdout.ClientSafePipeHandle.DangerousGetHandle();
    internal IntPtr ErrorHandle => _stderr.ClientSafePipeHandle.DangerousGetHandle();
    internal IntPtr InputHandle => _stdin.ClientSafePipeHandle.DangerousGetHandle();

    public ProcessOutputCapture() {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Suspended output capture requires Windows.");
        _stdout = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        try {
            _stderr = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            try { _stdin = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable); }
            catch { _stderr.Dispose(); throw; }
        }
        catch { _stdout.Dispose(); throw; }
    }

    internal void SealClientHandles() {
        if (Interlocked.Exchange(ref _sealed, 1) != 0) return;
        _stdout.DisposeLocalCopyOfClientHandle();
        _stderr.DisposeLocalCopyOfClientHandle();
        _stdin.DisposeLocalCopyOfClientHandle();
        _stdin.Dispose(); // EOF, not the launcher's keyboard input.
    }

    public void Dispose() {
        _stdout.Dispose();
        _stderr.Dispose();
        _stdin.Dispose();
    }
}