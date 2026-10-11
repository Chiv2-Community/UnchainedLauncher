using System.Globalization;

namespace UnchainedLauncher.CLI;

public sealed class ServerDashboard : IDisposable {
    private bool _interactive;
    private bool? _previousCursorVisible;
    private DateTimeOffset _lastPlainOutput = DateTimeOffset.MinValue;

    public ServerDashboard(bool useTui) {
        _interactive = useTui && !Console.IsOutputRedirected && !Console.IsInputRedirected
            && Environment.GetEnvironmentVariable("TERM") != "dumb";
        if (_interactive) {
            try {
                if (OperatingSystem.IsWindows()) _previousCursorVisible = Console.CursorVisible;
                Console.CursorVisible = false;
                Console.Clear();
            }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException) { _interactive = false; }
        }
    }

    public bool StopRequested() {
        if (!_interactive) return false;
        try {
            while (Console.KeyAvailable) {
                if (Console.ReadKey(intercept: true).Key == ConsoleKey.Q) return true;
            }
        }
        catch (InvalidOperationException) { _interactive = false; }
        return false;
    }

    public void Render(int pid, ServerSample sample, ServerHistory history) {
        if (_interactive) {
            try {
                var width = Math.Max(1, Console.WindowWidth - 1);
                var height = Math.Max(1, Console.WindowHeight - 1);
                var lines = FormatLines(pid, sample, history, width);
                Console.SetCursorPosition(0, 0);
                for (var row = 0; row < height; row++) {
                    var text = row < lines.Count ? SafeText(lines[row]) : "";
                    Console.Write(text[..Math.Min(text.Length, width)].PadRight(width));
                    if (row + 1 < height) Console.WriteLine();
                }
                return;
            }
            catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or PlatformNotSupportedException) {
                _interactive = false;
            }
        }
        if (sample.Time - _lastPlainOutput < TimeSpan.FromSeconds(10)) return;
        _lastPlainOutput = sample.Time;
        Console.WriteLine(SafeText(string.Join(" | ", FormatLines(pid, sample, history, 80).Take(4))));
    }

    public static IReadOnlyList<string> FormatLines(int pid, ServerSample sample, ServerHistory history, int width) {
        string Number(double? value, string suffix) => value.HasValue
            ? value.Value.ToString("F1", CultureInfo.InvariantCulture) + suffix : "unknown";
        var players = sample.Players.HasValue ? $"{sample.Players}/{sample.MaxPlayers}" : "unknown";
        var uptime = $"{(int)sample.Uptime.TotalDays}d {sample.Uptime:hh\\:mm\\:ss}";
        var chartWidth = Math.Clamp(width - 22, 1, 80);
        var samples = history.Samples.ToArray();
        List<string> lines = [
            $"Unchained Server | PID {pid} | Uptime {uptime} | Q / Ctrl+C: stop",
            $"CPU {Number(sample.CpuPercent, "% host")} | Private memory {Number(sample.MemoryMiB, " MiB")}",
            $"Players {players} | Map {sample.Map ?? "unknown"}",
            $"A2S {(sample.QueryAvailable ? "responding" : "unavailable (starting or query timeout)")} | {sample.Time:HH:mm:ss}",
            "", "Recent resource/player history (up to 120 samples; '?' = unknown):",
            $"CPU (0-100%)  {Sparkline(samples.Select(s => s.CpuPercent), chartWidth, 100)}",
            $"Memory        {Sparkline(samples.Select(s => s.MemoryMiB), chartWidth)}",
            $"Players       {Sparkline(samples.Select(s => s.Players.HasValue ? (double?)s.Players.Value : null), chartWidth)}",
            "", "Timeline (latest events):"
        ];
        lines.AddRange(history.Events.TakeLast(8).Select(e => $"  {e.Time:HH:mm:ss}  {e.Message}"));
        return lines;
    }

    public static string Sparkline(IEnumerable<double?> values, int width, double? maximum = null) {
        const string levels = ".:-=+*#%@";
        var samples = values.TakeLast(Math.Max(1, width)).ToArray();
        var max = maximum ?? samples.Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(0).Max();
        return new string(samples.Select(value => !value.HasValue ? '?' : levels[max <= 0 ? 0
            : (int)Math.Clamp(value.Value / max * (levels.Length - 1), 0, levels.Length - 1)]).ToArray());
    }

    public static string SafeText(string value) => new(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());

    public void Dispose() {
        try {
            Console.CursorVisible = _previousCursorVisible ?? true;
            if (_interactive) Console.WriteLine();
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException) { }
    }
}