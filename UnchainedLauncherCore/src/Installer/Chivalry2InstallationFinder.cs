﻿using log4net;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using UnchainedLauncher.Core.Utilities;
using LanguageExt;
using UnchainedLauncher.Core.JsonModels;
using System.Runtime.InteropServices;
using LanguageExt.UnsafeValueAccess;

namespace UnchainedLauncher.Core.Installer {
    using static LanguageExt.Prelude;

    public interface IChivalry2InstallationFinder {
        public abstract bool IsValidInstallation(DirectoryInfo chivInstallDir);
        public abstract bool IsSteamDir(DirectoryInfo chivInstallDir);
        public abstract bool IsEGSDir(DirectoryInfo chivInstallDir);
        public abstract Option<DirectoryInfo> FindSteamDir();
        public abstract Option<DirectoryInfo> FindEGSDir();
    }

    public class Chivalry2InstallationFinder: IChivalry2InstallationFinder {
        private static readonly ILog logger = LogManager.GetLogger(nameof(Chivalry2InstallationFinder));

        private static readonly string Chiv2SteamAppID = "1824220";
        private static readonly string Chiv2EGSAppName = "Peppermint";

        public bool IsValidInstallation(DirectoryInfo chivInstallDir) {
            string Chiv2ExePath = Path.Combine(chivInstallDir.FullName, FilePaths.GameBinPath);
            return File.Exists(Chiv2ExePath);
        }

        public bool IsSteamDir(DirectoryInfo chivInstallDir) {
            return 
                chivInstallDir.FullName.Contains("steamapps") || 
                FindSteamDir().Exists(dir => dir.FullName.Contains(chivInstallDir.FullName));
        }

        public bool IsEGSDir(DirectoryInfo chivInstallDir) {
            return chivInstallDir.FullName.Contains("Epic Games") ||
                FindEGSDir().Exists(dir => dir.FullName.Contains(chivInstallDir.FullName));
        }

        public Option<DirectoryInfo> FindSteamDir() {
            logger.Info("Searching for Chivalry 2 Steam install directory...");
            Option<string> maybeSteamPath = GetSteamPath();
            if(maybeSteamPath.IsNone) {
                logger.Info("Failed to find Steam path.");
                return None;
            }

            string SteamPath = maybeSteamPath.ValueUnsafe();
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
                                return Some(new DirectoryInfo(Path.Combine(CandidateDir, @"steamapps\common\Chivalry 2")));
                    }
                } catch (Exception e) {
                    logger.Error($"Error reading Steam library metadata file", e);
                    return None;
                }
            }

            logger.Info($"Found no Steam library with Chivalry 2 installed.");
            return None;
        }

        public Option<DirectoryInfo> FindEGSDir() {
            logger.Info("Searching for Chivalry 2 EGS install directory...");

            var ProgramDataDir = Environment.ExpandEnvironmentVariables("%PROGRAMDATA%");
            string EGSDataFile = Path.Combine(ProgramDataDir, @"Epic\UnrealEngineLauncher\LauncherInstalled.dat");

            if (File.Exists(EGSDataFile)) {
                logger.Info($"Reading EGS Install List from {EGSDataFile}...");

                var savedSettings = JsonConvert.DeserializeObject<EGSInstallList>(File.ReadAllText(EGSDataFile));
                if (savedSettings != null && savedSettings.InstallationList.Count > 0) {
                    var chivEntry = savedSettings.InstallationList.Where(x => x.AppName == Chiv2EGSAppName);
                    if (chivEntry.Any()) {
                        logger.Info($"Found Chivalry 2 EGS install directory: {chivEntry.First().InstallLocation}");
                        return Some(new DirectoryInfo(chivEntry.First().InstallLocation));
                    }
                } else {
                    logger.Warn("Failed to read EGS install list file.");
                }
            }

            logger.Info($"Found no EGS installation with Chivalry 2 installed.");
            return None;
        }

        private static Option<string> GetSteamPath() {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                var registrySteamPath = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam", "SteamPath", null);
                if (registrySteamPath == null) {
                    logger.Info("Steam library metadata location not found in registry.");
                    return None;
                }

                return (string)registrySteamPath;
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                return Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", ".steam/steam");
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                return Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", "Library/Application Support/Steam");
            }

            return null;
        }
    }
}