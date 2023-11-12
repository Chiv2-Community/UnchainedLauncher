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
        public string ReleaseFileUrl(string tag) => $"https://github.com/{GithubOwner}/{GithubRepo}/releases/download/{tag}/{ReleaseFileName}";

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

        public async Task<UpdateCheck> CheckForUpdates(SemVersion currentVersion) {
            string? latestReleaseTag = await GetLatestTag();

            if (latestReleaseTag == null)
                return UpdateCheck.Failed;
           
            SemVersion latestVersion = SemVersion.Parse(latestReleaseTag, SemVersionStyles.Any);

            if (latestVersion == null || latestVersion <= currentVersion)
                return UpdateCheck.UpToDate;

            return UpdateCheck.Available(latestReleaseTag);
        }

        public DownloadTask DownloadRelease(string tag) {
            return HttpHelpers.DownloadFileAsync(ReleaseFileUrl(tag), OutputPath);
        }
    }

    public abstract record UpdateCheck {
        private UpdateCheck() { }

        public record UpToDateResult() : UpdateCheck;
        public record UpdateAvailableResult(string Tag) : UpdateCheck;
        public record UpdateCheckFailedResult() : UpdateCheck;

        public static UpdateCheck Failed => new UpdateCheckFailedResult();
        public static UpdateCheck UpToDate => new UpToDateResult();
        public static UpdateCheck Available(string tag) => new UpdateAvailableResult(tag);

        public T Match<T>(Func<T> failed, Func<T> upToDate, Func<string, T> available) {
            switch (this) {
                case UpToDateResult: return upToDate();
                case UpdateAvailableResult updateAvailableResult: return available(updateAvailableResult.Tag);
                case UpdateCheckFailedResult: return failed();
                default:
                    throw new Exception("Invalid UpdateCheck type");
            }
        }

        public void MatchVoid(Action failed, Action upToDate, Action<string> available) {
            switch (this) {
                case UpToDateResult: upToDate(); break;
                case UpdateAvailableResult updateAvailableResult: available(updateAvailableResult.Tag); break;
                case UpdateCheckFailedResult: failed(); break;
                default:
                    throw new Exception("Invalid UpdateCheck type");
            }
        }
    }
}
