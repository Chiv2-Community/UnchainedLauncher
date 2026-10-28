using System.Text;

namespace UnchainedLauncher.CLI;

/// <summary>Bounded, sanitized stdout/stderr history for one game launch.</summary>
internal sealed class ForwardedConsole : IDisposable {
    internal const int MaxLines = 512;
    private readonly CancellationTokenSource _stop = new();
    private readonly Queue<string> _lines = new();
    private readonly List<Task> _captures = [];
    private readonly List<Stream> _streams = [];
    private long _dropped;
    private int _disposed;
    private Task? _completion;

    internal Task CaptureAsync(Stream stream, CancellationToken token = default) {
        lock (_captures) {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _streams.Add(stream);
            var task = CaptureCoreAsync(stream, token);
            _captures.Add(task);
            return task;
        }
    }

    private async Task CaptureCoreAsync(Stream stream, CancellationToken token) {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, token);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096);
        var buffer = new char[4096];
        var line = new StringBuilder();
        var truncated = false;
        try {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellation.Token)) != 0) {
                for (var i = 0; i < count; i++) {
                    if (buffer[i] == '\n') {
                        Add(line.ToString().TrimEnd('\r') + (truncated ? " [truncated]" : ""));
                        line.Clear();
                        truncated = false;
                    }
                    else if (line.Length < 16 * 1024) line.Append(buffer[i]);
                    else truncated = true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally {
            if (line.Length != 0) Add(line.ToString() + (truncated ? " [truncated]" : ""));
        }
    }

    internal IReadOnlyList<string> Drain() {
        lock (_lines) {
            List<string> result = [];
            if (_dropped != 0) result.Add($"[console: {_dropped} lines dropped by receiver]");
            result.AddRange(_lines);
            _lines.Clear();
            _dropped = 0;
            return result;
        }
    }

    private void Add(string text) {
        if (text.Length == 0) return;
        lock (_lines) {
            _lines.Enqueue(ServerDashboard.SafeText(text));
            if (_lines.Count > MaxLines) { _lines.Dequeue(); _dropped++; }
        }
    }

    public void Dispose() {
        lock (_captures) {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _stop.Cancel();
            foreach (var stream in _streams) stream.Dispose();
            _ = Task.WhenAll(_captures).ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
        }
    }

    // Descendants can retain output handles after game exit. Never wait indefinitely.
    internal Task FinishAsync() {
        lock (_captures) return _completion ??= FinishCoreAsync();
    }

    private async Task FinishCoreAsync() {
        var reads = Task.WhenAll(_captures);
        try { await reads.WaitAsync(TimeSpan.FromMilliseconds(250)); }
        catch (TimeoutException) { }
        finally { Dispose(); }
        try { await reads.WaitAsync(TimeSpan.FromMilliseconds(250)); }
        catch (TimeoutException) { }
    }
}
