using System.ComponentModel.DataAnnotations;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnchainedLauncher.CLI;

public static class YamlConfiguration {
    public static CliCommandOptions Load(string path, bool client) {
        LaunchConfiguration configuration = client ? LoadDocument<ClientConfiguration>(path) : LoadDocument<ServerConfiguration>(path);
        try { configuration.Validate(); }
        catch (ValidationException ex) { throw new ArgumentException("Invalid configuration: " + ex.Message); }
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        return configuration is ServerConfiguration { Servers: not null } group
            ? ServerGroupOptions.Create(group, directory)
            : CliLaunchOptions.FromConfiguration(configuration, directory);
    }

    internal static T LoadDocument<T>(string path) where T : class {
        path = Path.GetFullPath(path);
        try {
            using var reader = File.OpenText(path);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(HyphenatedNamingConvention.Instance)
                .WithTypeConverter(new InstallationTypeYamlConverter())
                .WithTypeConverter(new IgnoredYamlNodeConverter())
                .WithDuplicateKeyChecking()
                .WithEnforceNullability()
                .Build();
            var configuration = deserializer.Deserialize<T>(reader);
            if (configuration is null) throw new ArgumentException("Configuration must contain one YAML mapping.");
            return configuration;
        }
        catch (YamlException ex) {
            // Library errors can include passwords/tokens; report only location.
            throw new ArgumentException($"Invalid YAML configuration or schema at line {ex.Start.Line}, column {ex.Start.Column}.");
        }
        catch (ValidationException ex) {
            throw new ArgumentException("Invalid configuration: " + ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            throw new ArgumentException($"Cannot read YAML configuration: {path}");
        }
    }
}