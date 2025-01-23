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
        private static readonly ILog logger = LogManager.GetLogger(nameof(Chivalry2InstallationFinder));

        private static readonly string Chiv2SteamAppID = "1824220";
        private static readonly string Chiv2EGSAppName = "Peppermint";

        public bool IsValidInstallation(DirectoryInfo chivInstallDir) {
            string Chiv2ExePath = Path.Combine(chivInstallDir.FullName, FilePaths.GameBinPath);
            var result = File.Exists(Chiv2ExePath);
            logger.Info($"Checking if {Chiv2ExePath} exists: {result}");
            return File.Exists(Chiv2ExePath);
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
            logger.Info("Searching for Chivalry 2 Steam install directory...");
            string? maybeSteamPath = GetSteamPath();
            if (maybeSteamPath == null) {
                logger.Info("Failed to find Steam path.");
                return null;
            }

            string SteamPath = maybeSteamPath;
            string SteamLibFile = Path.Combine(SteamPath, "steamapps", "libraryfolders.vdf");

            logger.Info($"Steam library metadata location: {SteamLibFile}");

            if (File.Exists(SteamLibFile)) {
                try {
                    string vdfContent = File.ReadAllText(SteamLibFile);
                    // This captures everything after "path"
                    string pattern = "\"(\\d+)\"[\\s\\t]*\\{[\\s\\S]*?\"path\"[\\s\\t]*\"(.*?)\"";
                    MatchCollection matches = Regex.Matches(vdfContent, pattern);

                    for (int i = 0; i < matches.Count; ++i) {
                        Match match = matches[i];

                        string CandidateDir = match.Groups[2].Value;
                        CandidateDir = Regex.Unescape(CandidateDir);
                        //Console.WriteLine($"Folder Path: {folderPath}");

                        // get substring until next section
                        var maxIdx = (i < matches.Count - 1) ? matches[i + 1].Index : vdfContent.Length;
                        string ss = vdfContent[match.Index..maxIdx];

                        // skip apps and brackets, then parse each line
                        string pattern3 = "\"apps\"[\\s\\t]*\\{";
                        MatchCollection matches3 = Regex.Matches(ss, pattern3);
                        int appsIdx = matches3[0].Index + matches3[0].Value.Length;
                        // get only the lines with numbers inside "apps"
                        var ss_sub2 = ss[appsIdx..ss.IndexOf('}', appsIdx)];
                        string[] lines = ss_sub2.Split(new[] { '\"' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                        for (int j = 0; j < lines.Length; j += 2)
                            if (lines[j].Equals(Chiv2SteamAppID))
                                return new DirectoryInfo(Path.Combine(CandidateDir, @"steamapps\common\Chivalry 2"));
                    }
                }
                catch (Exception e) {
                    logger.Error($"Error reading Steam library metadata file", e);
                    return null;
                }
            }

            logger.Info($"Found no Steam library with Chivalry 2 installed.");
            return null;
        }

        public DirectoryInfo? FindEGSDir() {
            logger.Info("Searching for Chivalry 2 EGS install directory...");

            var ProgramDataDir = Environment.ExpandEnvironmentVariables("%PROGRAMDATA%");
            string EGSDataFile = Path.Combine(ProgramDataDir, @"Epic\UnrealEngineLauncher\LauncherInstalled.dat");

            if (File.Exists(EGSDataFile)) {
                logger.Info($"Reading EGS Install List from {EGSDataFile}...");

                var savedSettings = JsonSerializer.Deserialize<EGSInstallList>(File.ReadAllText(EGSDataFile));
                if (savedSettings != null && savedSettings.InstallationList.Count > 0) {
                    var chivEntry = savedSettings.InstallationList.Where(x => x.AppName == Chiv2EGSAppName);
                    if (chivEntry.Any()) {
                        logger.Info($"Found Chivalry 2 EGS install directory: {chivEntry.First().InstallLocation}");
                        return new DirectoryInfo(chivEntry.First().InstallLocation);
                    }
                }
                else {
                    logger.Warn("Failed to read EGS install list file.");
                }
            }

            logger.Info($"Found no EGS installation with Chivalry 2 installed.");
            return null;
        }

        private static string? GetSteamPath() {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                var registrySteamPath = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam", "SteamPath", null);
                if (registrySteamPath == null) {
                    logger.Info("Steam library metadata location not found in registry.");
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