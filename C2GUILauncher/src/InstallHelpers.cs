using C2GUILauncher.JsonModels;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace C2GUILauncher {
    internal class InstallHelpers {
        private static readonly string Chiv2SteamAppID = "1824220";
        private static readonly string Chiv2EGSAppName = "Peppermint";

        public static string? FindSteamDir() {
            object? steamPathObj = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam", "SteamPath", null);
            if(steamPathObj == null)
                return null;
            
            string SteamPath = (string)steamPathObj;
            string SteamLibFile = Path.Combine(SteamPath, "steamapps", "libraryfolders.vdf");


            if (File.Exists(SteamLibFile)) {

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
                            return Path.Combine(CandidateDir, @"steamapps\common\Chivalry 2");
                }
            }
            return null;
        }

        public static string? FindEGSDir() {
            var ProgramDataDir = Environment.ExpandEnvironmentVariables("%PROGRAMDATA%");
            string EGSDataFile = Path.Combine(ProgramDataDir, @"Epic\UnrealEngineLauncher\LauncherInstalled.dat");

            if (File.Exists(EGSDataFile)) {
                var savedSettings = JsonConvert.DeserializeObject<EGSInstallList>(File.ReadAllText(EGSDataFile));
                if (savedSettings != null && savedSettings.InstallationList.Count > 0) {
                    var chivEntry = savedSettings.InstallationList.Where(x => x.AppName == Chiv2EGSAppName);
                    if (chivEntry.Any())
                        return chivEntry.First().InstallLocation;
                }
            }
            return null;
        }
    }
}
