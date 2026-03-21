using Spectre.Console;
using Spectre.Console.Cli;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnchainedLauncher.PakScannerApp;
using UnchainedLauncher.UnrealModScanner.Config;
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
        var outputDir = commandSettings.OutputDirectory ?? Directory.GetCurrentDirectory();

        // Some command settings overwrite scan options, others are top level options, like PakDirectory.
        if (string.IsNullOrWhiteSpace(commandSettings.PakDirectory))
            throw new InvalidOperationException(
                "PakDirectory is required (use --pak or --config)");

        var options = LoadSettings(commandSettings);

        Directory.CreateDirectory(outputDir);

        AnsiConsole.MarkupLine("[green]Starting scan...[/]");

        var progress = new Progress<double>(p => AnsiConsole.MarkupLine($"[grey]Progress:[/] {p:F2}%"));

        var scanner = ScannerFactory.CreateModScanner(options);


        var scanFileProvider =
            FilteredFileProvider.CreateFromOptions(
                options,
                commandSettings.ScanMode ?? ScanMode.All,
                commandSettings.PakDirectory
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

    private static ScanOptions LoadSettings(ScanCommandSettings settings) {
        // TODO: Read preset in from the command line. For non-presets, allow users to specify all the options.
        //       Arguments provided in addition to a preset will override the preset values.
        //
        //       For now use the Chivalry2 options as the defaults and selectively override options
        var options =
            settings.ConfigFile != null
                ? LoadFromJson(settings.ConfigFile)
                : GameScanOptions.Chivalry2;

        if (settings.OfficialDirs?.Length > 0)
            options = options with { VanillaAssetPaths = settings.OfficialDirs.ToList() };

        return options;
    }

    private static ScanOptions? LoadFromJson(string path) {
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
        );
    }
}