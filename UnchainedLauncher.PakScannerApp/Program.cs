using Spectre.Console.Cli;

var app = new CommandApp();

/*
 * JSON CONF FORMAT
 {
  "PakDirectory": "/mnt/u/Games/Chivalry2_c/TBL/Content/Paks",
  "OutputDirectory": "./out",
  "ScanMode": "ModsOnly",
  "OfficialDirectories": [
    "Abilities",
    "AI",
    "Animation",
    "Game",
    "UI",
    "Weapons"
  ]
}

 */

app.Configure(config => {
    config.SetApplicationName("UnchainedScanner");

    config.AddCommand<ScanCommand>("scan")
        .WithDescription("Scan Unreal Engine pak files")
        .WithExample(new[]
        {
            "scan",
            "--pak", "/path/to/Paks",
            "--out", "./out"
        });
});

return await app.RunAsync(args);

// // See https://aka.ms/new-console-template for more information
//
// using Serilog;
// using System.Diagnostics;
// using UnchainedLauncher.UnrealModScanner.Models;
// using UnchainedLauncher.UnrealModScanner.PakScanning;
// using UnrealModScanner.Export;
//
// var progressReporter = new Progress<double>(percent => {
//     Debug.WriteLine($"Scan Progress: {percent:F2}%");
// });
//
// // 2. Configuration for Scanner
// var officialDirs = new[] {
//             "Abilities","AI","Animation","Audio","Blueprint","Characters","Cinematics",
//             "Collections","Config","Custom_Lens_Flare_VFX","Customization","Debug",
//             "Developers","Environments","FX","Game","GameModes","Gameplay","Interactables",
//             "Inventory","Localization","MapGen","Maps","MapsTest","Materials","Meshes",
//             "Trailer_Cinematic","UI","Weapons","Engine","Mannequin"
//         };
//
//
//
// Console.WriteLine("Hello, World!");
// var modScanner = ScannerFactory.CreateModScanner(officialDirs);
// // var pakDir = "U:\\Games\\Chivalry2_c\\TBL\\Content\\Paks";
// var pakDir = "/mnt/u/Games/Chivalry2_c/TBL/Content/Paks";
// var context = await Task.Run(() => modScanner.RunScanAsync(pakDir, ScanMode.ModsOnly, progressReporter));
// var ScanManifest = MetadataProcessor.ProcessModScan(context);
//
// Console.WriteLine($"Processed {ScanManifest.Paks.Count} Paks");
// var out_dir = Directory.GetCurrentDirectory();
// var manifest_file = Path.Combine(pakDir, "manifest.json");
// ModScanJsonExporter.ExportToFile(ScanManifest, manifest_file);
// Console.WriteLine($"Exported manifest to {manifest_file}");