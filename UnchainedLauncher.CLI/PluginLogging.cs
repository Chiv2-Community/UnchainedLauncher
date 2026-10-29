namespace UnchainedLauncher.CLI;

internal static class PluginLogging {
    internal static bool HasOverride(IEnumerable<string> arguments) => arguments.Any(argument =>
        argument.TrimStart('-').Split('=', 2)[0] == "log4rs-yaml");

    internal static async Task PrepareAsync(CliLaunchOptions request, CancellationToken token) {
        if (request is not ServerOptions server || HasOverride(request.GameArguments)) return;
        var path = request.InstancePaths.Logging;
        if (!File.Exists(path)) {
            Directory.CreateDirectory(request.InstancePaths.Root);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                await File.WriteAllTextAsync(temporary, DefaultYaml, token);
                try { File.Move(temporary, path); }
                catch (IOException) when (File.Exists(path)) { /* Preserve an existing profile configuration. */ }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        server.PluginLoggingPath = request.Shared.Proton is { } proton
            ? await proton.WinePathAsync(path, windows: true, token) : path;
    }

    internal const string DefaultYaml = """
        appenders:
          console:
            kind: console
            target: stdout
            filters:
              - kind: threshold
                level: debug
            encoder:
              kind: json
          file:
            kind: file
            path: "__UNCHAINED_LOG_PATH__"
            encoder:
              pattern: "[{d(%Y-%m-%d %H:%M:%S)} {P} {l:5}| {M}] {m}{n}"
          kismet:
            kind: file
            path: "__KISMET_LOG_PATH__"
            encoder:
              pattern: "[{d(%Y-%m-%d %H:%M:%S)} {P} {l:5}| {M} ] {m}{n}"
        loggers:
          serenity:
            level: warn
          tracing:
            level: warn
          patternsleuth:
            level: warn
          "h2::codec":
            level: warn
          "hyper_util::client":
            level: warn
          "reqwest::connect":
            level: warn
        root:
          level: debug
          appenders: [console, file]
        """;
}
