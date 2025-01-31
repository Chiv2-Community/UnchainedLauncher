using log4net;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Installer {
    public interface IChivalry2InstallationFinder {
        public abstract bool IsValidInstallation(DirectoryInfo chivInstallDir);
        public abstract bool IsSteamDir(DirectoryInfo chivInstallDir);
        public abstract bool IsEGSDir(DirectoryInfo chivInstallDir);
        public abstract DirectoryInfo? FindSteamDir();
        public abstract DirectoryInfo? FindEGSDir();
    }

    public class Chivalry2InstallationFinder : IChivalry2InstallationFinder {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(Chivalry2InstallationFinder));

        private static readonly string Chiv2SteamAppId = "1824220";
        private static readonly string Chiv2EGSAppName = "Peppermint";

        public bool IsValidInstallation(DirectoryInfo chivInstallDir) {
            var chiv2ExePath = Path.Combine(chivInstallDir.FullName, FilePaths.GameBinPath);
            var result = File.Exists(chiv2ExePath);
            Logger.Info($"Checking if {chiv2ExePath} exists: {result}");
            return File.Exists(chiv2ExePath);
        }

        public bool IsSteamDir(DirectoryInfo chivInstallDir) {
            var steamDir = FindEGSDir();
            var hasSteamAppId = File.Exists(Path.Combine(chivInstallDir.FullName, FilePaths.SteamAppIdPath));
            var isDefaultSteamLocation = steamDir != null && steamDir.FullName.Contains(chivInstallDir.FullName);

            return IsValidInstallation(chivInstallDir) && (hasSteamAppId || isDefaultSteamLocation);
        }

        public bool IsEGSDir(DirectoryInfo chivInstallDir) {
            var egsDir = FindEGSDir();
            var isDefaultEGSLocation = egsDir != null && egsDir.FullName.Contains(chivInstallDir.FullName);

            return IsValidInstallation(chivInstallDir) && (isDefaultEGSLocation || !IsSteamDir(chivInstallDir));
        }

        public DirectoryInfo? FindSteamDir() {
            Logger.Info("Searching for Chivalry 2 Steam install directory...");
            var maybeSteamPath = GetSteamPath();
            if (maybeSteamPath == null) {
                Logger.Info("Failed to find Steam path.");
                return null;
            }

            var steamPath = maybeSteamPath;
            var steamLibFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

            Logger.Info($"Steam library metadata location: {steamLibFile}");

            if (File.Exists(steamLibFile)) {
                try {
                    var vdfContent = File.ReadAllText(steamLibFile);
                    // This captures everything after "path"
                    var pattern = "\"(\\d+)\"[\\s\\t]*\\{[\\s\\S]*?\"path\"[\\s\\t]*\"(.*?)\"";
                    var matches = Regex.Matches(vdfContent, pattern);

                    for (var i = 0; i < matches.Count; ++i) {
                        var match = matches[i];

                        var candidateDir = match.Groups[2].Value;
                        candidateDir = Regex.Unescape(candidateDir);
                        //Console.WriteLine($"Folder Path: {folderPath}");

                        // get substring until next section
                        var maxIdx = (i < matches.Count - 1) ? matches[i + 1].Index : vdfContent.Length;
                        var ss = vdfContent[match.Index..maxIdx];

                        // skip apps and brackets, then parse each line
                        var pattern3 = "\"apps\"[\\s\\t]*\\{";
                        var matches3 = Regex.Matches(ss, pattern3);
                        var appsIdx = matches3[0].Index + matches3[0].Value.Length;
                        // get only the lines with numbers inside "apps"
                        var ssSub2 = ss[appsIdx..ss.IndexOf('}', appsIdx)];
                        string[] lines = ssSub2.Split(new[] { '\"' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                        for (var j = 0; j < lines.Length; j += 2)
                            if (lines[j].Equals(Chiv2SteamAppId))
                                return new DirectoryInfo(Path.Combine(candidateDir, @"steamapps\common\Chivalry 2"));
                    }
                }
                catch (Exception e) {
                    Logger.Error($"Error reading Steam library metadata file", e);
                    return null;
                }
            }

            Logger.Info($"Found no Steam library with Chivalry 2 installed.");
            return null;
        }

        public DirectoryInfo? FindEGSDir() {
            Logger.Info("Searching for Chivalry 2 EGS install directory...");

            var programDataDir = Environment.ExpandEnvironmentVariables("%PROGRAMDATA%");
            var egsDataFile = Path.Combine(programDataDir, @"Epic\UnrealEngineLauncher\LauncherInstalled.dat");

            if (File.Exists(egsDataFile)) {
                Logger.Info($"Reading EGS Install List from {egsDataFile}...");

                var savedSettings = JsonSerializer.Deserialize<EGSInstallList>(File.ReadAllText(egsDataFile));
                if (savedSettings != null && savedSettings.InstallationList.Count > 0) {
                    var chivEntry = savedSettings.InstallationList.Where(x => x.AppName == Chiv2EGSAppName);
                    if (chivEntry.Any()) {
                        Logger.Info($"Found Chivalry 2 EGS install directory: {chivEntry.First().InstallLocation}");
                        return new DirectoryInfo(chivEntry.First().InstallLocation);
                    }
                }
                else {
                    Logger.Warn("Failed to read EGS install list file.");
                }
            }

            Logger.Info($"Found no EGS installation with Chivalry 2 installed.");
            return null;
        }

        private static string? GetSteamPath() {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                var registrySteamPath = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam", "SteamPath", null);
                if (registrySteamPath == null) {
                    Logger.Info("Steam library metadata location not found in registry.");
                    return null;
                }

                return (string)registrySteamPath;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                return Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", ".steam/steam");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                return Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", "Library/Application Support/Steam");
            }

            return null;
        }
    }
}