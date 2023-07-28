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

        public static IEnumerable<DownloadTask> DownloadAllFiles(IEnumerable<DownloadTarget> files)
        {
            return files.Select(x => new DownloadTask(DownloadFileAsync(x), x));
        }
    }

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
