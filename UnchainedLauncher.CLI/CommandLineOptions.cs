using System.CommandLine;
using System.CommandLine.Help;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services.Processes;

namespace UnchainedLauncher.CLI;

internal static class CommandLineOptions {
    public static CliCommandOptions Parse(string[] args) => Parse(args, static (root, tokens, configuration) => root.Parse(tokens, configuration));

    // The delegate is the single library-parse boundary, also exercised by tests.
    internal static CliCommandOptions Parse(string[] args, Func<RootCommand, string[], ParserConfiguration, ParseResult> parse) {
        var separator = Array.IndexOf(args, "--");
        var launcherArgs = separator < 0 ? args : args[..separator];
        var gameArgs = separator < 0 ? [] : args[(separator + 1)..];
        var root = new RootCommand("Unchained client/server launcher and mod registry queries. Only launching and DLL injection require Windows.");
        var config = new Option<string?>("--config-path") {
            Description = "Launch YAML overrides launch options; mod-query reads its registry settings.",
            Arity = ArgumentArity.ExactlyOne,
            Recursive = true
        };
        root.Options.Add(config);
        var unsafeDlls = UnsafeDllLoading.CreateOption(recursive: true);
        root.Options.Add(unsafeDlls);
        var server = new Definition(client: false);
        var client = new Definition(client: true);
        root.Subcommands.Add(server.Command);
        root.Subcommands.Add(client.Command);
        var queries = new ModQueryDefinition();
        root.Subcommands.Add(queries.Command);
        var dllQueries = new ModQueryDefinition(RegistryPackageKind.Dll);
        root.Subcommands.Add(dllQueries.Command);
        // Never interpret @values as file reads, including ignored YAML overrides.
        // Response-file expansion is deliberately disabled for all launcher input.
        var parsed = parse(root, launcherArgs, new ParserConfiguration { ResponseFileTokenReplacer = null });
        var loadAllDlls = UnsafeDllLoading.Read(parsed, unsafeDlls, launcherArgs);
        var definition = parsed.CommandResult.Command == server.Command ? server : client;
        var isQuery = queries.Contains(parsed.CommandResult.Command) || dllQueries.Contains(parsed.CommandResult.Command);
        if (!isQuery && parsed.GetResult(config) is { } configResult) {
            if (parsed.CommandResult.Command == root) throw new ArgumentException("Expected the 'server' or 'client' subcommand.");
            // Help actions skip library validators. Check the parsed selector itself
            // so --help cannot bypass its singleton/non-empty contract either.
            if (configResult.IdentifierTokenCount != 1 || configResult.Tokens.Count != 1 || string.IsNullOrWhiteSpace(configResult.Tokens[0].Value))
                throw new ArgumentException("Specify exactly one non-empty --config-path.");
            // All non-selector conversion, required-option and unknown-token errors
            // are irrelevant when YAML is authoritative; do not bind those values.
            var errors = parsed.Errors.Where(error => error.SymbolResult == configResult);
            ThrowErrors(errors.Select(error => error.Message));
            var configured = YamlConfiguration.Load(parsed.GetValue(config)!, definition.Command == client.Command);
            if (loadAllDlls) configured.Shared.EnableUnsafeDllLoading();
            return configured;
        }
        if (args.Length == 0 || parsed.GetResult("--help") is not null) {
            using var output = new StringWriter();
            var help = new HelpAction();
            parsed.InvocationConfiguration.Output = output;
            help.Invoke(parsed);
            return CliLaunchOptions.ForHelp(output.ToString());
        }
        ThrowErrors(parsed);
        if (isQuery) {
            if (loadAllDlls) throw new ArgumentException("Unsafe DLL loading is only valid for launch commands.");
            if (separator >= 0) throw new ArgumentException("mod-query does not accept forwarded game arguments.");
            return (dllQueries.Contains(parsed.CommandResult.Command) ? dllQueries : queries).ToOptions(parsed, config);
        }
        if (parsed.CommandResult.Command == root) throw new ArgumentException("Expected the 'server' or 'client' subcommand.");
        var launch = definition.ToOptions(parsed, gameArgs);
        if (loadAllDlls) launch.Shared.EnableUnsafeDllLoading();
        return launch;
    }

    private static void ThrowErrors(ParseResult result) {
        ThrowErrors(result.Errors.Select(error => error.Message));
    }

    private static void ThrowErrors(IEnumerable<string> errors) {
        var messages = errors.ToArray();
        if (messages.Length != 0) throw new ArgumentException(string.Join(Environment.NewLine, messages));
    }

    private sealed class Definition {
        public Command Command { get; }
        private readonly LaunchConfiguration _configuration;
        private readonly List<Action<ParseResult>> _bindings = [];
        private readonly Argument<string[]> _gameArgs = new("game-args") { Arity = ArgumentArity.ZeroOrMore };

        public Definition(bool client) {
            Command = new Command(client ? "client" : "server", client ? "Launch an Unchained client." : "Launch and monitor an Unchained server.");
            _configuration = client ? new ClientConfiguration() : new ServerConfiguration { Server = new ServerSettings() };
            Add<string?>("--chivalry2-path", "Game installation directory.", null, value => _configuration.Chiv2Path = value, required: true);
            var platform = new Option<InstallationType?>("--platform") {
                Description = "Steam or EGS (EpicGamesStore); EGS auth must be forwarded after --.",
                Arity = ArgumentArity.ExactlyOne,
                Required = true,
                CustomParser = result => {
                    if (result.Tokens.Count != 1) { result.AddError("Specify --platform once."); return null; }
                    try { return InstallationTypeYamlConverter.Parse(result.Tokens[0].Value); }
                    catch (ArgumentException ex) { result.AddError(ex.Message); return null; }
                }
            };
            Command.Options.Add(platform);
            _bindings.Add(result => _configuration.Platform = result.GetValue(platform));
            Add("--saved-dir-suffix", "Saved profile suffix.", _configuration.SavedDirSuffix, value => _configuration.SavedDirSuffix = value);
            Add("--server-browser-backend", "HTTP(S) server-browser backend URL.", _configuration.ServerBrowserBackend, value => _configuration.ServerBrowserBackend = value);
            Flag("--no-plugin-update", "Use the existing UnchainedPlugin.dll.", value => _configuration.UpdatePlugin = !value);
            Flag("--allow-unstable-plugin", "Allow prerelease plugin updates.", value => _configuration.AllowUnstablePlugin = value);
            _configuration.ModSetup = new ModSetupConfiguration();
            var registries = RegistryOptions.AddOption(Command);
            _bindings.Add(result => _configuration.ModSetup.Registries = [.. result.GetValue(registries) ?? []]);
            Many("--mod", "Install organization/id:version (exact release tag).", value => _configuration.ModSetup.Mods = value);
            Many("--dll", "Select a registry DLL release: organization/id:version (repeatable).", value => _configuration.ModSetup.Dlls = value);
            if (_configuration is ServerConfiguration serverConfig) {
                var settings = serverConfig.Server!;
                Add("--name", "Server name.", settings.Name, value => settings.Name = value);
                Add("--description", "Description / MOTD.", settings.Description, value => settings.Description = value);
                Add("--map", "Initial map.", settings.Map, value => settings.Map = value);
                Add<string?>("--password", "Server password.", null, value => settings.Password = value);
                Add<string?>("--local-ip", "Override auto-detected local IPv4 (VPN/multi-NIC).", null, value => settings.LocalIp = value);
                Add("--game-port", "Game port.", settings.GamePort, value => settings.GamePort = value);
                Add("--ping-port", "Ping port.", settings.PingPort, value => settings.PingPort = value);
                Add("--query-port", "A2S query port.", settings.QueryPort, value => settings.QueryPort = value);
                Add("--rcon-port", "RCON port.", settings.RconPort, value => settings.RconPort = value);
                Add("--headless", "Run without rendering (true/false). Rendering requires --no-desync-patch.", settings.Headless, value => settings.Headless = value);
                Add("--censor-mode", "none, standard, sex, or zealous.", settings.CensorMode, value => settings.CensorMode = value);
                Flag("--register", "List in the server browser.", value => settings.Register = value);
                Flag("--no-tui", "Periodic plain-text status instead of dashboard.", value => serverConfig.NoTui = value);
                Flag("--no-desync-patch", "Disable the default-enabled desync patch.", value => settings.DesyncPatch = !value);
                Flag("--no-backend-banlist", "Disable the default-enabled backend banlist.", value => settings.UseBackendBanlist = !value);
                Many("--server-mod", "Activate a blueprint class (separate from pak installation).", value => settings.ModActors = value);
                var discord = new DiscordSettings();
                settings.Discord = discord;
                Add<string?>("--discord-bot-token", "Bot token; requires at least one channel.", null, value => discord.BotToken = value);
                Add<string?>("--discord-admin-channel-id", "Admin channel.", null, value => discord.AdminChannelId = value);
                Add<string?>("--discord-general-channel-id", "General channel.", null, value => discord.GeneralChannelId = value);
                Add<string?>("--discord-dashboard-channel-id", "Dashboard channel.", null, value => discord.DashboardChannelId = value);
                Add<string?>("--discord-event-log-channel-id", "Event-log channel.", null, value => discord.EventLogChannelId = value);
                Add<string?>("--discord-admin-role-id", "Admin role.", null, value => discord.AdminRoleId = value);
                Add("--discord-mention-admins", "Mention admins (true/false).", true, value => discord.MentionAdmins = value);
            }
            // Only tokens following -- are game arguments, never misspelled options.
            Command.Arguments.Add(_gameArgs);
            Command.Validators.Add(result => {
                if (result.GetResult(_gameArgs) is { Tokens.Count: > 0 })
                    result.AddError("Pass game arguments after --.");
            });
        }

        private void Add<T>(string name, string description, T defaultValue, Action<T> bind, bool required = false) {
            var option = new Option<T>(name) {
                Description = description,
                Arity = ArgumentArity.ExactlyOne,
                DefaultValueFactory = _ => defaultValue,
                Required = required
            };
            option.Validators.Add(result => {
                if (result.Tokens.Count > 1) result.AddError($"Duplicate option: {name}");
                if (result.Tokens.Any(token => string.IsNullOrWhiteSpace(token.Value))) result.AddError($"Empty value for {name}.");
            });
            Command.Options.Add(option);
            _bindings.Add(result => bind(result.GetValue(option)!));
        }

        private void Flag(string name, string description, Action<bool> bind) {
            var option = new Option<bool>(name) { Description = description, Arity = ArgumentArity.Zero };
            option.Validators.Add(result => {
                if (result.IdentifierTokenCount > 1) result.AddError($"Duplicate option: {name}");
            });
            Command.Options.Add(option);
            _bindings.Add(result => bind(result.GetValue(option)));
        }

        private void Many(string name, string description, Action<List<string>> bind) {
            var option = new Option<string[]>(name) { Description = description, Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = false };
            Command.Options.Add(option);
            _bindings.Add(result => bind([.. result.GetValue(option) ?? []]));
        }

        public CliLaunchOptions ToOptions(ParseResult parsed, string[] gameArgs) {
            foreach (var bind in _bindings) bind(parsed);
            _configuration.GameArgs = [.. gameArgs];
            try { _configuration.Validate(); }
            catch (System.ComponentModel.DataAnnotations.ValidationException ex) { throw new ArgumentException(ex.Message); }
            return CliLaunchOptions.FromConfiguration(_configuration, Directory.GetCurrentDirectory());
        }
    }
}
