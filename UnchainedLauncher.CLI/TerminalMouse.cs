using System.Runtime.InteropServices;

namespace UnchainedLauncher.CLI;

// SGR mouse reports are supported by xterm-compatible terminals and Windows Terminal.
internal sealed class TerminalMouse : IDisposable {
    private readonly List<(IntPtr Handle, uint Mode)> _modes = [];
    private bool _enabled;

    internal TerminalMouse() {
        try {
            if (OperatingSystem.IsWindows()) {
                if (!SetMode(-10, mode => (mode | 0x200u | 0x80u) & ~0x40u)
                    || !SetMode(-11, mode => mode | 0x4u)) {
                    Dispose();
                    return;
                }
            }
            Console.Write("\x1b[?1000h\x1b[?1006h");
            _enabled = true;
        }
        catch (IOException) { Dispose(); }
    }

    private bool SetMode(int stream, Func<uint, uint> update) {
        var handle = GetStdHandle(stream);
        if (!GetConsoleMode(handle, out var mode) || !SetConsoleMode(handle, update(mode))) return false;
        _modes.Add((handle, mode));
        return true;
    }

    public void Dispose() {
        try { if (_enabled) Console.Write("\x1b[?1000l\x1b[?1006l"); }
        catch (IOException) { }
        finally {
            _enabled = false;
            foreach (var (handle, mode) in _modes) SetConsoleMode(handle, mode);
            _modes.Clear();
        }
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int handle);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(IntPtr handle, uint mode);
}