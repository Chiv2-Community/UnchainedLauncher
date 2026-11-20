using System.CommandLine;
using UnchainedLauncher.Core.Services.Mods.Registry;

namespace UnchainedLauncher.CLI;

public sealed class RegistryOptions {
    public IReadOnlyList<RegistrySpec> Registries { get; }

    public RegistryOptions(IEnumerable<string>? specifications, string directory) =>
        Registries = SharedLaunchOptions.Entries(specifications).Select(value => RegistrySpec.Parse(value, directory)).ToArray();

    public AggregateModRegistry CreateRegistry() => new(new IModRegistry[] {
        new GithubModRegistry("Chiv2-Community", "C2ModRegistry")
    }.Concat(Registries.Select(spec => spec.Create())));

    internal static Option<string[]> AddOption(Command command, bool recursive = false) {
        var option = new Option<string[]>("--add-registry") {
            Description = "Additional github:owner/repo, local:directory, or config:file registry; official registry is always included.",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false,
            Recursive = recursive
        };
        command.Options.Add(option);
        return option;
    }
}