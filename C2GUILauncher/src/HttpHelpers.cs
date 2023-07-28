using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace C2GUILauncher
{
    static class HttpHelpers
    {
        /// <summary>
        /// Downloads a file asynchronously.
        /// </summary>
        /// <param name="target"></param>
        /// <returns>
        /// The task that represents the asynchronous operation.
        /// </returns>
        public static async Task DownloadFileAsync(DownloadTarget target)
        {

            using (HttpClient client = new HttpClient())
            {
                using (HttpResponseMessage response = await client.GetAsync(target.Url))
                {
                    response.EnsureSuccessStatusCode();

                    using (Stream contentStream = await response.Content.ReadAsStreamAsync(),
                            fileStream = new FileStream(target.OutputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await contentStream.CopyToAsync(fileStream);
                    }
                }
            }
        }

        /// <summary>
        /// Downloads all files in the given list in asynchronously.
        /// </summary>
        /// <param name="files"></param>
        /// <returns>
        /// A list of DownloadTasks, which can be used to track the overall progress of all the downloads.
        /// </returns>
        public static IEnumerable<DownloadTask> DownloadAllFiles(IEnumerable<DownloadTarget> files)
        {
            return files.Select(x => new DownloadTask(DownloadFileAsync(x), x));
        }
    }

    /// <summary>
    /// This class represents a URL to download and the path to save it to.
    /// </summary>
    class DownloadTarget
    {
        [MemberNotNull]
        public string Url { get; }

        [MemberNotNull]
        public string OutputPath { get; }

        public DownloadTarget(string url, string outputPath)
        {
            Url = url;
            OutputPath = outputPath;
        }
    }

    /// <summary>
    /// This class represents an asynchronous download task, as well as the target being downloaded.
    /// </summary>
    class DownloadTask
    {
        [MemberNotNull]
        public Task Task { get; }
        [MemberNotNull]
        public DownloadTarget Target { get; }

        public DownloadTask(Task task, DownloadTarget target)
        {
            Task = task;
            Target = target;
        }
    }
}
