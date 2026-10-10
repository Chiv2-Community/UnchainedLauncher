using Spectre.Console;
using Spectre.Console.Cli;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnchainedLauncher.PakScannerApp;
using UnchainedLauncher.UnrealModScanner.Config.Games;
using UnchainedLauncher.UnrealModScanner.Export;
using UnchainedLauncher.UnrealModScanner.PakScanning;
using UnchainedLauncher.UnrealModScanner.PakScanning.Config;
using UnchainedLauncher.UnrealModScanner.Services;

public sealed class ScanCommand : AsyncCommand<ScanCommandSettings> {

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        ScanCommandSettings commandSettings,
        CancellationToken cancellationToken) {
        var settings = LoadSettings(commandSettings);
        var outputDir = settings.OutputDirectory!;
        var options = settings.Options!;

        Directory.CreateDirectory(outputDir);

        AnsiConsole.MarkupLine("[green]Starting scan...[/]");

        var progress = new Progress<double>(p => AnsiConsole.MarkupLine($"[grey]Progress:[/] {p:F2}%"));

        var scanner = ScannerFactory.CreateModScanner(options);


        using var scanFileProvider =
            FilteredFileProvider.CreateFromOptions(
                options,
                settings.ScanMode!.Value,
                settings.PakDirectory!
            );

        var scanContext = await scanner.RunScanAsync(
            scanFileProvider,
            options,
            progress
        );

        var manifest = ModManifestConverter.ProcessModScan(scanContext);

        var outputFile = Path.Combine(outputDir, "manifest.json");
        ModScanJsonExporter.ExportToFile(manifest, outputFile);

        AnsiConsole.MarkupLine($"[green]Processed {manifest.Paks.Count} paks[/]");
        AnsiConsole.MarkupLine($"[green]Exported manifest to[/] {outputFile}");

        return 0;
    }

    public static ScanConfiguration LoadSettings(ScanCommandSettings settings) {
        var config = settings.ConfigFile != null
            ? LoadFromJson(settings.ConfigFile)
            : new ScanConfiguration();
        var options = config.Options ?? GameScanOptions.Chivalry2;
        var officialDirs = settings.OfficialDirs ?? config.OfficialDirectories;
        if (officialDirs != null)
            // Legacy directory names are relative to Chivalry's content root; full asset prefixes stay unchanged.
            options = options with {
                VanillaAssetPaths = officialDirs.Select(dir =>
                    dir.Contains('/') || dir.Contains('\\') ? dir : $"TBL/Content/{dir}").ToList()
            };

        var resolved = config with {
            PakDirectory = settings.PakDirectory ?? config.PakDirectory,
            OutputDirectory = settings.OutputDirectory ?? config.OutputDirectory ?? Directory.GetCurrentDirectory(),
            ScanMode = settings.ScanMode ?? config.ScanMode ?? ScanMode.All,
            OfficialDirectories = officialDirs,
            Options = options
        };
        if (string.IsNullOrWhiteSpace(resolved.PakDirectory))
            throw new InvalidOperationException("PakDirectory is required (use --pak or --config)");
        return resolved;
    }

    private static ScanConfiguration LoadFromJson(string path) {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Config file not found: {path}");

        var options = new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            // or JsonNamingPolicy.Default if your enum is PascalCase
        };

        return JsonSerializer.Deserialize<ScanConfiguration>(
            File.ReadAllText(path),
            options
        ) ?? throw new InvalidOperationException($"Config file is empty: {path}");
    }
}