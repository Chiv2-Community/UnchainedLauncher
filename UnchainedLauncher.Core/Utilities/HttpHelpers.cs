using LanguageExt;
using log4net;

namespace UnchainedLauncher.Core.Utilities {
    using static LanguageExt.Prelude;
    
    public static class HttpHelpers {
        private static readonly ILog logger = LogManager.GetLogger(nameof(HttpHelpers));

        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// Downloads a file asynchronously.
        /// </summary>
        /// <param name="target"></param>
        /// <returns>
        /// The task that represents the asynchronous operation.
        /// </returns>
        public static DownloadTask DownloadFileAsync(string url, string outputPath) {
            var dirName = Path.GetDirectoryName(outputPath);
            if (dirName != null && dirName != "" && !Directory.Exists(dirName)) {
                logger.Info($"Creating directory {outputPath}...");
                Directory.CreateDirectory(dirName);
            }

            if (FileHelpers.IsFileLocked(outputPath)) {
                logger.Info($"{outputPath} is locked, skipping download.");
                return new DownloadTask(
                    Task.CompletedTask,
                    new DownloadTarget(url, outputPath)
                );
            }

            logger.Info($"Downloading file {outputPath} from {url}");
            return new DownloadTask(
                _httpClient.GetByteArrayAsync(url).ContinueWith(t => File.WriteAllBytes(outputPath, t.Result)),
                new DownloadTarget(url, outputPath)
            );
        }

        public static async Task<long> GetContentLengthAsync(string url) {
            try {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.TryGetValues("Content-Length", out var values)) {
                    if (long.TryParse(values.FirstOrDefault(), out long contentLength)) {
                        return contentLength;
                    }
                }
            }
            catch (Exception ex) {
                logger.Error($"Error retrieving file size: {ex.Message}");
            }

            return 0L;
        }

        public static DownloadTask<Stream> GetByteContentsAsync(string url) {
            logger.Info($"Downloading file {url}");
            return new DownloadTask<Stream>(
                _httpClient.GetStreamAsync(url),
                new DownloadTarget(url, null)
              );
        }

        public static DownloadTask<string> GetStringContentsAsync(string url) {
            logger.Info($"Fetching string contents from {url}");
            return new DownloadTask<string>(
                _httpClient.GetStringAsync(url),
                new DownloadTarget(url, null)
            );
        }

        /// <summary>
        /// Downloads all files in the given list in asynchronously.
        /// </summary>
        /// <param name="files"></param>
        /// <returns>
        /// A list of DownloadTasks, which can be used to track the overall progress of all the downloads.
        /// </returns>
        public static IEnumerable<DownloadTask> DownloadAllFiles(IEnumerable<DownloadTarget> files) {
            return files.Select(x =>
                x.OutputPath == null
                    ? throw new ArgumentNullException("OutputPath")
                    : DownloadFileAsync(x.Url, x.OutputPath!)
            );
        }


        /// <summary>
        /// Downloads the assets of a release to the specified download targets.
        /// 
        /// The assetDownloadTargets thing is probably overkill, but may come in handy if we ever split out the installer and launcher in to separate exes.
        /// </summary>
        /// <param name="download">The release to download files from</param>
        /// <param name="assetMapping">A function returning Some(filePath) whenever an asset should be downloaded</param>
        /// <param name="logResult">An Action which does something to handle a log string.
        /// <returns>A bool returning false if any download targets have a download failure</returns>
        public static async Task<bool> DownloadReleaseTarget(ReleaseTarget download,
            Func<ReleaseAsset, string?> assetMapping, 
            Action<string>? logResult = null
        )
        {
            Action<string> log = message =>
            {
                logger.Info(message);
                logResult?.Invoke(message);
            };

            var results =
                await download.Assets.ToList().Select(async asset =>
                    {
                        var downloadTarget = assetMapping(asset);

                        if (downloadTarget == null)
                        {
                            logger.Debug($"Skipping asset with no download target {asset.Name}");
                            return true;
                        }

                        try
                        {
                            await HttpHelpers.DownloadFileAsync(asset.DownloadUrl, downloadTarget!).Task;
                            log($"Downloaded {asset.Name} to {downloadTarget!}");
                            return true;
                        }
                        catch (Exception e)
                        {
                            log($"Failed to download launcher\n    from {asset.DownloadUrl}\n    to {downloadTarget!}");
                            log(e.ToString());
                            return false;
                        }
                    })
                    .SequenceParallel();

            return results.ForAll(identity);

        }
    }

    public record DownloadTarget(string Url, string? OutputPath);

    // The DownloadTask records below will eventually be used to hold on to a reference which indicates the current download progress.
    // Wrapping the task is necessary so that we can show results before they have completed.
    public record DownloadTask(Task Task, DownloadTarget Target) {
        public DownloadTask ContinueWith(Action action) {
            Task.ContinueWith(t => action());
            return this;
        }
    }
    public record DownloadTask<T>(Task<T> Task, DownloadTarget Target) {
        public DownloadTask<U> ContinueWith<U>(Func<T, U> action) {
            return new DownloadTask<U>(Task.ContinueWith(t => action(t.Result)), Target);
        }

        public DownloadTask<T> RecoverWith(Func<Exception?, DownloadTask<T>> recover) {
            Task.ContinueWith(t =>
            {
                if (t.Exception != null) {
                    return recover(t.Exception);
                }

                return this;
            });
            return this;
        }
    }
}