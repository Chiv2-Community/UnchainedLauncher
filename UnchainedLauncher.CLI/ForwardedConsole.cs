using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UnchainedLauncher.CLI;

/// <summary>One authenticated, bounded console stream per game launch.</summary>
internal sealed class ForwardedConsole : IDisposable {
    internal const int MaxFrameBytes = 128 * 1024;
    internal const int MaxLines = 512;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Queue<string> _lines = new();
    private readonly Task _read;
    private long _dropped;
    private int _disposed;
    internal string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal string Endpoint => ((IPEndPoint)_listener.LocalEndpoint).ToString();

    internal ForwardedConsole(bool listen = true) {
        if (listen) _listener.Start();
        _read = listen ? ReadAsync() : Task.CompletedTask;
    }

    internal async Task CaptureAsync(Stream stream, CancellationToken token) {
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
            if (line.Length != 0) Add(line.ToString() + (truncated ? " [truncated]" : ""));
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
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

    private async Task ReadAsync() {
        try {
            while (!_stop.IsCancellationRequested) {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                using var stream = client.GetStream();
                var frames = new Frames(stream);
                using var handshake = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                handshake.CancelAfter(TimeSpan.FromSeconds(2));
                try {
                    using var hello = await frames.ReadAsync(handshake.Token);
                    var root = hello.RootElement;
                    if (root.GetProperty("version").GetInt32() != 1 || root.GetProperty("kind").GetString() != "hello"
                        || root.GetProperty("token").GetString() is not { Length: 64 } token
                        || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(Token))) continue;
                    await stream.WriteAsync("OK\n"u8.ToArray(), handshake.Token);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or KeyNotFoundException
                    or InvalidOperationException or FormatException or OperationCanceledException) {
                    if (_stop.IsCancellationRequested) return;
                    continue;
                }
                // Only the authenticated game owns this stream; do not mix another launch's records.
                _listener.Stop();
                Add("[plugin console connected]");
                while (!_stop.IsCancellationRequested) {
                    using var frame = await frames.ReadAsync(_stop.Token);
                    var root = frame.RootElement;
                    var kind = root.GetProperty("kind").GetString();
                    if (root.GetProperty("version").GetInt32() != 1 || kind is not ("log" or "banner"))
                        throw new InvalidDataException("Unsupported console frame.");
                    if (root.GetProperty("dropped").GetUInt64() is var dropped && dropped != 0)
                        Add($"[console: {dropped} records dropped by plugin]");
                    var text = root.GetProperty("text").GetString() ?? "";
                    if (Encoding.UTF8.GetByteCount(text) > 16 * 1024 + 3)
                        throw new InvalidDataException("Console text exceeds limit.");
                    Add(text);
                }
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or SocketException or JsonException or KeyNotFoundException
            or InvalidOperationException or FormatException or OperationCanceledException or ObjectDisposedException) {
            if (!_stop.IsCancellationRequested) Add("[plugin console disconnected; file logging remains available]");
        }
        finally { _listener.Stop(); }
    }

    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _listener.Stop();
        // The read task observes cancellation before its cancellation source is released.
        _ = _read.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }

    private sealed class Frames(NetworkStream stream) {
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
