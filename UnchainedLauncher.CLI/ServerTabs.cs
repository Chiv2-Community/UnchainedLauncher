using System.Globalization;
using System.Text;

namespace UnchainedLauncher.CLI;

public sealed class ServerTabs(IReadOnlyList<string> names) {
    private readonly List<(int Start, int End, int Index)> _targets = [];
    private int _first;
    public int Selected { get; private set; }

    public void Move(int delta) {
        if (names.Count != 0) Selected = (Selected + delta % names.Count + names.Count) % names.Count;
    }

    public bool Click(int column, int row) {
        if (row != 1) return false;
        foreach (var target in _targets) {
            if (column < target.Start || column > target.End) continue;
            Selected = target.Index;
            return true;
        }
        return false;
    }

    public string Layout(int width) {
        width = Math.Max(1, width);
        var text = Build();
        if (_targets.All(target => target.Index != Selected)) {
            _first = Selected;
            text = Build();
        }
        return text;

        string Build() {
            _targets.Clear();
            var text = new StringBuilder();
            for (var i = _first; i < names.Count; i++) {
                var label = i == Selected ? $">{names[i]}<" : $"[{names[i]}]";
                label = ServerDashboard.SafeText(label);
                label = label[..Math.Min(label.Length, Math.Min(width, 24))];
                if (text.Length + label.Length > width) break;
                _targets.Add((text.Length + 1, text.Length + label.Length, i));
                text.Append(label);
                if (text.Length < width) text.Append(' ');
            }
            return text.ToString();
        }
    }
}

public sealed class TerminalMouseParser {
    private string? _pending;

    public bool Feed(char value, out (int Column, int Row)? click) {
        click = null;
        if (value == '\x1b') { _pending = "\x1b"; return true; }
        if (_pending is null) return false;
        _pending += value;
        if (_pending.Length > 64 || !(char.IsAsciiDigit(value) || value is '[' or '<' or ';' or 'M' or 'm')) {
            _pending = null;
            return false;
        }
        if (value is not ('M' or 'm')) return true;
        var sequence = _pending;
        _pending = null;
        if (!sequence.StartsWith("\x1b[<", StringComparison.Ordinal) || value != 'M') return true;
        var parts = sequence[3..^1].Split(';');
        if (parts.Length == 3 && parts[0] == "0"
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var column)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var row)
            && column > 0 && row > 0) click = (column, row);
        return true;
    }
}
