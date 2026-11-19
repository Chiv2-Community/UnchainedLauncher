using System.Text.RegularExpressions;

namespace UnchainedLauncher.CLI;

public sealed record NamedServerOptions(string Id, ServerOptions Options);

public sealed class ServerGroupOptions : CliCommandOptions {
    public IReadOnlyList<NamedServerOptions> Servers { get; }
    public bool NoTui { get; }

    private ServerGroupOptions(IReadOnlyList<NamedServerOptions> servers, bool noTui) {
        Servers = servers;
        NoTui = noTui;
    }

    internal static ServerGroupOptions Create(ServerConfiguration configuration, string directory) {
        var servers = new List<NamedServerOptions>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ports = new HashSet<int>();
        foreach (var (id, settings) in configuration.Servers!) {
            if (!Regex.IsMatch(id, @"\A[A-Za-z0-9_-]+\z") || !ids.Add(id))
                throw new ArgumentException("Server IDs must be unique (case-insensitive) and contain only letters, digits, underscores, and hyphens.");
            var suffix = settings!.SavedDirSuffix ?? id;
            var options = new ServerOptions(new SharedLaunchOptions(configuration, directory, suffix, settings), settings, configuration.NoTui);
            if (!profiles.Add(options.GameSavedDirSuffix))
                throw new ArgumentException("Each server must use a distinct saved-dir-suffix (case-insensitive, ignoring hyphens).");
            foreach (var port in new[] { settings.GamePort, settings.PingPort, settings.QueryPort, settings.RconPort }) {
                if (!ports.Add(port)) throw new ArgumentException($"Port {port} is used by more than one server. Assign distinct game, ping, query, and RCON ports to every server.");
            }
            servers.Add(new NamedServerOptions(id, options));
        }
        return new ServerGroupOptions(servers, configuration.NoTui);
    }
}