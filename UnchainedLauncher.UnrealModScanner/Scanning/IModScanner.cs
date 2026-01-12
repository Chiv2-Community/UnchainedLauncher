//using System;
//using System.Collections.Generic;
//using System.Text;

using UnrealModScanner.Models;

namespace UnchainedLauncher.UnrealModScanner.Scanning {
    public interface IModScanner {
        Task<IReadOnlyList<PakScanResult>> ScanAsync(
            string pakDirectory,
            CancellationToken cancellationToken = default);
    }

}
