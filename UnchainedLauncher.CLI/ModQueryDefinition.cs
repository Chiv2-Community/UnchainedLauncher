using System.CommandLine;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.CLI;

public enum ModQueryOperation { List, Get, Versions }
public enum RegistryPackageKind { Mod, Dll }

public sealed class ModQueryOptions : CliCommandOptions {
    public RegistryPackageKind Kind { get; init; }
    public ModQueryOperation Operation { get; init; }
    public ModIdentifier? Mod { get; init; }
    public string? Version { get; init; }
    public required RegistryOptions RegistryOptions { get; init; }
}

internal sealed class ModQueryDefinition {
    public Command Command { get; }
    private readonly RegistryPackageKind _kind;
    private readonly Command _list = new("list", "List mod IDs, names, and latest versions.");
    private readonly Command _get = new("get", "Show release metadata, pak information, dependencies, and manifest contents.");
    private readonly Command _versions = new("versions", "List available release tags for a mod.");
    private readonly Argument<string> _getId = new("mod-id") { Description = "organization/id" };
    private readonly Argument<string> _versionsId = new("mod-id") { Description = "organization/id" };
    private readonly Option<string?> _version = new("--version") { Description = "Exact release tag; defaults to latest stable (or newest release if none is stable).", Arity = ArgumentArity.ExactlyOne };
    private readonly Option<string[]> _registries;

    public ModQueryDefinition(RegistryPackageKind kind = RegistryPackageKind.Mod) {
        _kind = kind;
        Command = new Command(kind == RegistryPackageKind.Dll ? "dll-query" : "mod-query", "Query configured registries without installing or launching the game.");
        if (kind == RegistryPackageKind.Dll) {
            _list.Description = "List DLL IDs, names, and latest versions.";
            _get.Description = "Show DLL release metadata, hashes, sizes, targets, and dependencies.";
            _versions.Description = "List available DLL release tags.";
        }
        _registries = RegistryOptions.AddOption(Command, recursive: true);
        _get.Arguments.Add(_getId);
        _versions.Arguments.Add(_versionsId);
        _get.Options.Add(_version);
        _version.Validators.Add(result => {
            if (result.IdentifierTokenCount != 1 || result.Tokens.Count != 1 || string.IsNullOrWhiteSpace(result.Tokens[0].Value))
                result.AddError("Specify one non-empty --version release tag.");
        });
        Command.Subcommands.Add(_list);
        Command.Subcommands.Add(_get);
        Command.Subcommands.Add(_versions);
    }

    public bool Contains(Command command) => command == Command || command == _list || command == _get || command == _versions;

    public ModQueryOptions ToOptions(ParseResult parsed, Option<string?> config) {
        if (parsed.CommandResult.Command == Command) throw new ArgumentException($"Use {Command.Name} list, get <organization/id>, or versions <organization/id>.");
        var directory = Directory.GetCurrentDirectory();
        IEnumerable<string>? specifications = parsed.GetValue(_registries);
        if (parsed.GetResult(config) is { } selector) {
            if (selector.IdentifierTokenCount != 1 || selector.Tokens.Count != 1 || string.IsNullOrWhiteSpace(selector.Tokens[0].Value))
                throw new ArgumentException("Specify exactly one non-empty --config-path.");
            var path = Path.GetFullPath(parsed.GetValue(config)!);
            specifications = YamlConfiguration.LoadDocument<RegistryQueryConfiguration>(path).ModSetup?.Registries;
            directory = Path.GetDirectoryName(path)!;
        }
        var operation = parsed.CommandResult.Command == _list ? ModQueryOperation.List
            : parsed.CommandResult.Command == _get ? ModQueryOperation.Get : ModQueryOperation.Versions;
        var id = operation == ModQueryOperation.List ? null : parsed.GetValue(operation == ModQueryOperation.Get ? _getId : _versionsId);
        ModIdentifier? identifier = null;
        if (id is not null) {
            // Reuse coordinate validation; no second grammar for organization/id.
            var coordinate = SharedLaunchOptions.ParseCoordinates(id + ":query");
            identifier = new ModIdentifier(coordinate.Org, coordinate.ModuleName);
        }
        return new ModQueryOptions {
            Kind = _kind,
            Operation = operation,
            Mod = identifier,
            Version = parsed.GetValue(_version),
            RegistryOptions = new RegistryOptions(specifications, directory)
        };
    }
}