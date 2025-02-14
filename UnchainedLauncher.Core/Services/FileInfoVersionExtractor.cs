﻿using log4net;
using Semver;
using System.Diagnostics;

namespace UnchainedLauncher.Core.Services {
    /// <summary>
    /// Uses file metadata to extract the SemVersion of the provided file path
    /// </summary>
    public class FileInfoVersionExtractor : IVersionExtractor {
        private readonly ILog _logger = LogManager.GetLogger(typeof(FileInfoVersionExtractor));

        public SemVersion? GetVersion(string filePath) {
            if (!File.Exists(filePath)) return null;

            var fileInfo = FileVersionInfo.GetVersionInfo(filePath);

            var versionString = fileInfo.ProductVersion ?? fileInfo.FileVersion ?? "";
            _logger.Debug($"Raw file version for '{filePath}': {versionString}");
            var splitVersionString = versionString.Split('.');
            versionString = String.Join('.', splitVersionString.Take(3));
            _logger.Debug($"Cleaned file version for '{filePath}': {versionString}");

            try {
                return SemVersion.Parse(versionString, SemVersionStyles.Any);
            }
            catch (Exception e) {
                _logger.Error($"Unable to parse version for '{filePath}': {e.Message}");
                return null;
            }
        }
    }

}