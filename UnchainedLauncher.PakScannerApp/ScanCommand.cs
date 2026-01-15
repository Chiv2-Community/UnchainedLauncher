using Spectre.Console;
using Spectre.Console.Cli;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnchainedLauncher.PakScannerApp;
using UnchainedLauncher.UnrealModScanner.Config.Games;
using UnchainedLauncher.UnrealModScanner.Export;
using UnchainedLauncher.UnrealModScanner.PakScanning;
using UnchainedLauncher.UnrealModScanner.Services;

public sealed class ScanCommand : AsyncCommand<ScanCommandSettings> {
    protected async override Task<int> ExecuteAsync(
        CommandContext context,
        ScanCommandSettings settings,
        CancellationToken cancellationToken) {
        var options = LoadOptions(settings);

        var outputDir = options.OutputDirectory
            ?? Directory.GetCurrentDirectory();

        Directory.CreateDirectory(outputDir);

        AnsiConsole.MarkupLine("[green]Starting scan...[/]");

        var progress = new Progress<double>(p =>
            AnsiConsole.MarkupLine($"[grey]Progress:[/] {p:F2}%"));

        var options_new = GameScanOptions.Chivalry2;
        var scanner = ScannerFactory.CreateModScanner(
            options.OfficialDirectories, options_new);


        var scanContext = await scanner.RunScanAsync(
            options.PakDirectory,
            options.ScanMode,
            options_new,
            progress);

        var manifest = ModManifestConverter.ProcessModScan(scanContext);

        var outputFile = Path.Combine(outputDir, "manifest.json");
        ModScanJsonExporter.ExportToFile(manifest, outputFile);

        AnsiConsole.MarkupLine(
            $"[green]Processed {manifest.Paks.Count} paks[/]");
        AnsiConsole.MarkupLine(
            $"[green]Exported manifest to[/] {outputFile}");

        return 0;
    }

    private static ScanOptions LoadOptions(ScanCommandSettings settings) {
        ScanOptions options = settings.ConfigFile != null
            ? LoadFromJson(settings.ConfigFile)
            : new ScanOptions();

        if (settings.PakDirectory != null)
            options.PakDirectory = settings.PakDirectory;

        if (settings.OutputDirectory != null)
            options.OutputDirectory = settings.OutputDirectory;

        if (settings.ScanMode.HasValue)
            options.ScanMode = settings.ScanMode.Value;

        if (settings.OfficialDirs?.Length > 0)
            options.OfficialDirectories = settings.OfficialDirs;

        if (string.IsNullOrWhiteSpace(options.PakDirectory))
            throw new InvalidOperationException(
                "PakDirectory is required (use --pak or --config)");

        return options;
    }

    private static ScanOptions LoadFromJson(string path) {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Config file not found: {path}");

        var options = new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            // or JsonNamingPolicy.Default if your enum is PascalCase
        };

        return JsonSerializer.Deserialize<ScanOptions>(
            File.ReadAllText(path),
            options
        ) ?? new ScanOptions();
    }
}