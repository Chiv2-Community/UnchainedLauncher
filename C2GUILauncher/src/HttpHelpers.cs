using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace C2GUILauncher {
    static class HttpHelpers {
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
            if (!Directory.Exists(Path.GetDirectoryName(outputPath) ?? ".")) {
                logger.Info($"Creating directory {outputPath}...");
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
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

        public static DownloadTask<Stream> GetByteContentsAsync(string url) {
            logger.Info($"Downloading file {url}");
            return new DownloadTask<Stream>(
                _httpClient.GetStreamAsync(url),
                new DownloadTarget(url, null)
              );
        }

        public static DownloadTask<string> GetStringContentsAsync(string url) {
            logger.Info($"Downloading file {url}");
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
    }
}
