using C2GUILauncher.Mods;
using C2GUILauncher.ViewModels;
using log4net.Repository.Hierarchy;
using Semver;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace C2GUILauncher {
    public class GithubReleaseSynchronizer {
        public string OutputPath { get; set; }
        public string GithubRepo { get; set; }
        public string GithubOwner { get; set; }
        public string ReleaseFileName { get; set; }
        public string LatestReleaseUrl => $"https://github.com/{GithubOwner}/{GithubRepo}/releases/latest";
        public string ReleaseUrl(string tag) => $"https://github.com/{GithubOwner}/{GithubRepo}/releases/tag/{tag}";
        public string ReleaseFileUrl(string tag) => ReleaseUrl(tag) + "/" + ReleaseFileName;

        public GithubReleaseSynchronizer(string githubOwner, string githubRepo, string releaseFileName, string outputPath) {
            OutputPath = outputPath;
            GithubRepo = githubRepo;
            GithubOwner = githubOwner;
            ReleaseFileName = releaseFileName;
        }

        public SemVersion? GetCurrentVersion() {
            try {
                FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(OutputPath);

                var rawFileVersion = fileVersionInfo.FileVersion;

                if (rawFileVersion == null)
                    return null;

                var fileVersion = string.Join(".", rawFileVersion.Split(".").Take(3));
                return SemVersion.Parse(fileVersion, SemVersionStyles.Any);
            } catch (FormatException) {
                return null;
            } catch (FileNotFoundException) {
                return null;
            }
        }

        public async Task<string?> GetLatestTag() {
             var latestRelease = await HttpHelpers.GetRedirectedUrl(LatestReleaseUrl);

            if (latestRelease == null) {
                return null;
            }

            try {
                return latestRelease.Split("/").LastOrDefault();

                
            } catch (FormatException) {
                return null;
            }
        }

        public async Task<string?> CheckForUpdates(SemVersion currentVersion) {
            string? latestReleaseTag = await GetLatestTag();

            if (latestReleaseTag == null)
                return null;
           
            SemVersion latestVersion = SemVersion.Parse(latestReleaseTag, SemVersionStyles.Any);

            if (latestVersion == null || latestVersion <= currentVersion)
                return null;

            var name = OutputPath.Split("\\").Last();

            return latestReleaseTag;
        }

        public Task DownloadRelease(string tag) {
            return HttpHelpers.DownloadFileAsync(ReleaseFileUrl(tag), OutputPath).Task;
        }
    }
}
