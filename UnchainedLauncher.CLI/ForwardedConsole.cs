using System.Text;
using System.Text.Json;

namespace UnchainedLauncher.CLI;

/// <summary>One private, bounded console stream per game launch.</summary>
internal sealed class ForwardedConsole : IDisposable {
    internal const int MaxFrameBytes = 128 * 1024;
    internal const int MaxLines = 512;
    private readonly CancellationTokenSource _stop = new();
    private readonly Queue<string> _lines = new();
    private Task? _read;
    private Stream? _stream;
    private long _dropped;
    private int _disposed;
    private readonly List<Task> _captures = [];
    private Task? _completion;

    internal Task CaptureAsync(Stream stream, CancellationToken token) {
        var task = CaptureCoreAsync(stream, token);
        lock (_lines) _captures.Add(task);
        return task;
    }

    private async Task CaptureCoreAsync(Stream stream, CancellationToken token) {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, token);
        token = cancellation.Token;
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
        var buffer = new char[4096];
        var line = new StringBuilder();
        var truncated = false;
        try {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) != 0) {
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
        lock (_lines) {
            foreach (var line in text.Replace("\r\n", "\n").Split('\n')) {
                if (line.Length == 0) continue;
                _lines.Enqueue(ServerDashboard.SafeText(line));
                if (_lines.Count > MaxLines) { _lines.Dequeue(); _dropped++; }
            }
        }
    }

    internal Task ReadAsync(Stream stream, CancellationToken token = default) {
        if (_stream != null || Volatile.Read(ref _disposed) != 0)
            throw new InvalidOperationException("A console receiver owns exactly one pipe.");
        _stream = stream;
        return _read = ReadFramesAsync(stream, token);
    }

    private async Task ReadFramesAsync(Stream stream, CancellationToken token) {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, token);
        try {
            var frames = new Frames(stream);
            var connected = false;
            while (!cancellation.IsCancellationRequested) {
                using var frame = await frames.ReadAsync(cancellation.Token);
                var root = frame.RootElement;
                var kind = root.GetProperty("kind").GetString();
                if (root.GetProperty("version").GetInt32() != 1 || kind is not ("log" or "banner"))
                    throw new InvalidDataException("Unsupported console frame.");
                var dropped = root.GetProperty("dropped").GetUInt64();
                var text = root.GetProperty("text").GetString() ?? "";
                if (Encoding.UTF8.GetByteCount(text) > 16 * 1024 + 3)
                    throw new InvalidDataException("Console text exceeds limit.");
                if (!connected) { Add("[plugin console connected]"); connected = true; }
                if (dropped != 0) Add($"[console: {dropped} records dropped by plugin]");
                Add(text);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or KeyNotFoundException
            or InvalidOperationException or FormatException or OperationCanceledException or ObjectDisposedException) {
            if (!cancellation.IsCancellationRequested) Add("[plugin console disconnected; file logging remains available]");
        }
        finally { stream.Dispose(); }
    }

    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _stream?.Dispose();
        // The read task observes cancellation before its cancellation source is released.
        Task completion;
        lock (_lines) completion = Task.WhenAll(_captures.Append(_read ?? Task.CompletedTask));
        _ = completion.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }

    // Descendants may retain write handles after game exit. Allow EOF briefly,
    // then cancel capture; never hold monitor shutdown indefinitely.
    internal Task FinishAsync() {
        lock (_lines) return _completion ??= FinishCoreAsync();
    }

    private async Task FinishCoreAsync() {
        var reads = Task.WhenAll(_captures.Append(_read ?? Task.CompletedTask));
        try { await reads.WaitAsync(TimeSpan.FromMilliseconds(250)); }
        catch (TimeoutException) { }
        finally { Dispose(); }
        try { await reads.WaitAsync(TimeSpan.FromMilliseconds(250)); }
        catch (TimeoutException) { }
    }

    private sealed class Frames(Stream stream) {
        private readonly byte[] _buffer = new byte[4096];
        private int _position, _count;

        internal async Task<JsonDocument> ReadAsync(CancellationToken token) {
            using var frame = new MemoryStream();
            while (true) {
                if (_position == _count) {
                    _count = await stream.ReadAsync(_buffer, token);
                    _position = 0;
                    if (_count == 0) throw new EndOfStreamException();
                }
                var end = Array.IndexOf(_buffer, (byte)'\n', _position, _count - _position);
                var length = (end < 0 ? _count : end) - _position;
                if (frame.Length + length > MaxFrameBytes) throw new InvalidDataException("Console frame exceeds limit.");
                frame.Write(_buffer, _position, length);
                _position += length + (end < 0 ? 0 : 1);
                if (end >= 0) return JsonDocument.Parse(frame.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            }
        }
    }
}
