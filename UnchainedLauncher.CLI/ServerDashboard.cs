using System.Globalization;
using UnchainedLauncher.Core.Services.Processes.Chivalry;

namespace UnchainedLauncher.CLI;

public sealed class ServerDashboard : IDisposable {
    private bool _interactive;
    private bool? _previousCursorVisible;
    private readonly Dictionary<int, (DateTimeOffset Time, string? Status)> _lastPlainOutput = [];
    private readonly ServerLaunchOptions? _launchOptions;
    private int _scrollOffset;
    private bool _showConsole;
    private bool _followConsole = true;
    private readonly Dictionary<int, long> _lastConsoleSequence = [];
    private readonly ServerTabs? _tabs;
    private readonly TerminalMouseParser _mouseParser = new();
    private TerminalMouse? _mouse;
    public bool IsInteractive => _interactive;
    public int SelectedTab => _tabs?.Selected ?? 0;

    public ServerDashboard(bool useTui, ServerLaunchOptions? launchOptions = null, IReadOnlyList<string>? tabNames = null) {
        _launchOptions = launchOptions;
        if (tabNames is { Count: > 0 }) _tabs = new ServerTabs(tabNames);
        _interactive = useTui && !Console.IsOutputRedirected && !Console.IsInputRedirected
            && Environment.GetEnvironmentVariable("TERM") != "dumb";
        if (_interactive) {
            try {
                if (OperatingSystem.IsWindows()) _previousCursorVisible = Console.CursorVisible;
                Console.CursorVisible = false;
                Console.Clear();
                if (_tabs is not null) _mouse = new TerminalMouse();
            }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException) { _interactive = false; }
        }
    }

    public bool StopRequested() {
        if (!_interactive) return false;
        try {
            while (Console.KeyAvailable) {
                var key = Console.ReadKey(intercept: true);
                if (_mouseParser.Feed(key.KeyChar, out var click)) {
                    if (click is { } position && _tabs?.Click(position.Column, position.Row) == true) _scrollOffset = 0;
                    continue;
                }
                switch (key.Key) {
                    case ConsoleKey.Q: return true;
                    case ConsoleKey.L: _showConsole = !_showConsole; _scrollOffset = 0; _followConsole = true; break;
                    case ConsoleKey.End: _followConsole = true; break;
                    case ConsoleKey.PageDown: _scrollOffset += 10; _followConsole = false; break;
                    case ConsoleKey.PageUp: _scrollOffset = Math.Max(0, _scrollOffset - 10); _followConsole = false; break;
                    case ConsoleKey.Home: _scrollOffset = 0; _followConsole = false; break;
                    case ConsoleKey.LeftArrow: _tabs?.Move(-1); _scrollOffset = 0; break;
                    case ConsoleKey.RightArrow: _tabs?.Move(1); _scrollOffset = 0; break;
                }
            }
        }
        catch (InvalidOperationException) { _interactive = false; }
        return false;
    }

    public void Render(int pid, ServerSample sample, ServerHistory history,
        ServerLaunchOptions? launchOptions = null, string? status = null) {
        if (_interactive) {
            try {
                var width = Math.Max(1, Console.WindowWidth - 1);
                var height = Math.Max(1, Console.WindowHeight - 1);
                var lines = (_showConsole
                    ? new[] { $"Plugin console | PID {pid} | L: dashboard | End: follow" }
                        .Concat(history.ConsoleLines.Select(line => line.Text))
                    : FormatLines(pid, sample, history, width, launchOptions ?? _launchOptions))
                    .SelectMany(line => WrapLine(SafeText(line), width)).ToArray();
                if (status is not null) lines = WrapLine(SafeText(status), width).Concat(lines).ToArray();
                var tabLine = _tabs?.Layout(width);
                var firstContentRow = tabLine is null ? 0 : 1;
                var contentHeight = Math.Max(0, height - firstContentRow - 1);
                if (_showConsole && _followConsole) _scrollOffset = Math.Max(0, lines.Length - contentHeight);
                _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, lines.Length - contentHeight));
                Console.SetCursorPosition(0, 0);
                for (var row = 0; row < height; row++) {
                    var index = row - firstContentRow + _scrollOffset;
                    var text = row == 0 && tabLine is not null ? tabLine
                        : row == height - 1
                        ? $"L: console/dashboard | End: follow | Left/Right: tabs | PgUp/PgDn: scroll | Q: stop | {_scrollOffset + 1}-{Math.Min(lines.Length, _scrollOffset + contentHeight)}/{lines.Length}"
                        : index < lines.Length ? lines[index] : "";
                    Console.Write(text[..Math.Min(text.Length, width)].PadRight(width));
                    if (row + 1 < height) Console.WriteLine();
                }
                return;
            }
            catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or PlatformNotSupportedException) {
                _interactive = false;
            }
        }
        var lastSequence = _lastConsoleSequence.GetValueOrDefault(pid);
        foreach (var line in history.ConsoleLines.Where(line => line.Sequence > lastSequence)) {
            Console.WriteLine(SafeText((status is null ? "" : status + " | ") + line.Text));
            _lastConsoleSequence[pid] = line.Sequence;
        }
        if (_lastPlainOutput.TryGetValue(pid, out var lastOutput) && lastOutput.Status == status
            && sample.Time - lastOutput.Time < TimeSpan.FromSeconds(10)) return;
        _lastPlainOutput[pid] = (sample.Time, status);
        Console.WriteLine(SafeText((status is null ? "" : status + " | ") + string.Join(" | ",
            FormatLines(pid, sample, history, 80, launchOptions ?? _launchOptions).TakeWhile(line => line.Length != 0))));
    }

    public static IReadOnlyList<string> FormatLines(int pid, ServerSample sample, ServerHistory history, int width,
        ServerLaunchOptions? launchOptions = null) {
        string Number(double? value, string suffix) => value.HasValue
            ? value.Value.ToString("F1", CultureInfo.InvariantCulture) + suffix : "unknown";
        var players = sample.Players.HasValue ? $"{sample.Players}/{sample.MaxPlayers}" : "unknown";
        var uptime = $"{(int)sample.Uptime.TotalDays}d {sample.Uptime:hh\\:mm\\:ss}";
        var chartWidth = Math.Clamp(width - 22, 1, 80);
        var samples = history.Samples.ToArray();
        var actors = launchOptions?.ServerMods.Where(actor => !string.IsNullOrWhiteSpace(actor)).ToArray() ?? [];
        string Port(int? port) => port?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
        List<string> lines = [
            $"{launchOptions?.Name ?? "Unchained Server"} | PID {pid} | Q / Ctrl+C: stop",
            $"State {history.State} | Uptime {uptime} | Restarts 0 (no auto-restart)",
            $"Players {players} | Map {sample.Map ?? "unknown"} | Game Type {sample.GameType ?? "unknown"}",
            $"Game Port {Port(launchOptions?.GamePort)} | Query Port {Port(launchOptions?.QueryPort)} | Beacon Port {Port(launchOptions?.BeaconPort)} | RCON Port {Port(launchOptions?.RconPort)}",
            $"Local IP {launchOptions?.LocalIp.IfNone("127.0.0.1") ?? "unknown"} | Mods {(actors.Length == 0 ? "-" : string.Join(", ", actors))}",
            $"CPU {Number(sample.CpuPercent, "% host")} | Private memory {Number(sample.MemoryMiB, " MiB")}",
            $"Peak memory {Number(sample.PeakMemoryMiB, " MiB")} | Handle Count {sample.HandleCount?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}",
            $"A2S {(sample.QueryAvailable ? "responding" : "unavailable (starting or query timeout)")} | {sample.Time:HH:mm:ss}",
            "", "History (up to 12 hours; 10s samples; '?' = unknown):",
            $"Uptime S=start U=up {StateHistory(samples, chartWidth)}",
            $"CPU (0-100%)  {Sparkline(samples.Select(s => s.CpuPercent), chartWidth, 100)}",
            $"Memory        {Sparkline(samples.Select(s => s.MemoryMiB), chartWidth)}",
            $"Players       {Sparkline(samples.Select(s => s.Players.HasValue ? (double?)s.Players.Value : null), chartWidth)}",
            "", "Timeline (latest events):"
        ];
        lines.AddRange(history.Events.TakeLast(8).Select(e => $"  {e.Time:HH:mm:ss}  {e.Message}"));
        if (history.ConsoleLines.Count != 0) {
            lines.Add("");
            lines.Add("Plugin console (L: full console; latest lines):");
            lines.AddRange(history.ConsoleLines.TakeLast(5).Select(line => line.Text));
        }
        return lines;
    }

    public static string Sparkline(IEnumerable<double?> values, int width, double? maximum = null) {
        const string levels = ".:-=+*#%@";
        var samples = values.ToArray();
        var max = maximum ?? samples.Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(0).Max();
        return new string(Compress(samples, width).Select(value => !value.HasValue ? '?' : levels[max <= 0 ? 0
            : (int)Math.Clamp(value.Value / max * (levels.Length - 1), 0, levels.Length - 1)]).ToArray());
    }

    private static IEnumerable<T> Compress<T>(T[] samples, int width) {
        var count = Math.Min(samples.Length, Math.Max(1, width));
        for (var i = 0; i < count; i++)
            yield return samples[(int)((long)(i + 1) * samples.Length / count) - 1];
    }

    private static string StateHistory(ServerSample[] samples, int width) =>
        new(Compress(samples, width).Select(sample => sample.Starting ? 'S' : 'U').ToArray());

    public static string SafeText(string value) => new(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());

    public static IEnumerable<string> WrapLine(string line, int width) {
        width = Math.Max(1, width);
        if (line.Length == 0) yield return "";
        for (var offset = 0; offset < line.Length; offset += width)
            yield return line.Substring(offset, Math.Min(width, line.Length - offset));
    }

    public void Dispose() {
        _mouse?.Dispose();
        try {
            Console.CursorVisible = _previousCursorVisible ?? true;
            if (_interactive) Console.WriteLine();
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException) { }
    }
}
