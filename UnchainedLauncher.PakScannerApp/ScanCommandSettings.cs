using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using UnchainedLauncher.UnrealModScanner.PakScanning;

namespace UnchainedLauncher.PakScannerApp {

    public sealed class ScanCommandSettings : CommandSettings {
        [CommandOption("--pak <PATH>")]
        [Description("Pak directory to scan")]
        public string? PakDirectory { get; init; }

        [CommandOption("--out <PATH>")]
        [Description("Output directory (defaults to CWD)")]
        public string? OutputDirectory { get; init; }

        [CommandOption("--mode <MODE>")]
        [Description("Scan mode (ModsOnly, All)")]
        public ScanMode? ScanMode { get; init; }

        [CommandOption("--config <FILE>")]
        [Description("Optional JSON config file")]
        public string? ConfigFile { get; init; }

        [CommandOption("--official-dir <DIR>")]
        [Description("Override official directories (can be repeated)")]
        public string[]? OfficialDirs { get; init; }

        public override ValidationResult Validate() {
            if (PakDirectory is null && ConfigFile is null)
                return ValidationResult.Error("Either --pak or --config must be specified");

            return ValidationResult.Success();
        }
    }
}