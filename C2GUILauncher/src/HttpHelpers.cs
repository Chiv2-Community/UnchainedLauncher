﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls.Ribbon;
using System.Windows.Media.Animation;

namespace C2GUILauncher
{
    static class HttpHelpers
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// Downloads a file asynchronously.
        /// </summary>
        /// <param name="target"></param>
        /// <returns>
        /// The task that represents the asynchronous operation.
        /// </returns>
        public static Task DownloadFileAsync(DownloadTarget target)
        {
            return _httpClient.GetByteArrayAsync(target.Url).ContinueWith(t => File.WriteAllBytes(target.OutputPath, t.Result));
        }

        public static async Task<string> GetRawContentsAsync(string url)
        {
            return await _httpClient.GetStringAsync(url);
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

   public record DownloadTarget(string Url, string OutputPath);

   public record DownloadTask(Task Task, DownloadTarget Target);
}
