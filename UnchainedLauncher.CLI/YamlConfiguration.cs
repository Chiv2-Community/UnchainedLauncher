using System.ComponentModel.DataAnnotations;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace UnchainedLauncher.CLI;

public static class YamlConfiguration {
    public static CliLaunchOptions Load(string path, bool client) {
        path = Path.GetFullPath(path);
        try {
            using var reader = File.OpenText(path);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(HyphenatedNamingConvention.Instance)
                .WithTypeConverter(new InstallationTypeYamlConverter())
                .WithDuplicateKeyChecking()
                .WithEnforceNullability()
                .Build();
            LaunchConfiguration? configuration = client
                ? deserializer.Deserialize<ClientConfiguration>(reader)
                : deserializer.Deserialize<ServerConfiguration>(reader);
            if (configuration is null) throw new ArgumentException("Configuration must contain one YAML mapping.");
            configuration.Validate();
            return CliLaunchOptions.FromConfiguration(configuration, Path.GetDirectoryName(path)!);
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
