using System.Security.Cryptography;
using System.Text.RegularExpressions;
using LanguageExt;
using LanguageExt.Common;
using UnchainedLauncher.Core.Utilities;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Mods;

// Published objects are immutable. Updates replace directory entries, never linked bytes.
public sealed class PakCache(string root) {
    public string Root { get; } = Path.GetFullPath(root);

    public EitherAsync<Error, Unit> Install(ModInstallRequest request, string destination,
        Option<IProgress<double>> progress, CancellationToken cancellationToken = default) => TryAsync(async () => {
            cancellationToken.ThrowIfCancellationRequested();
            var hash = request.Hash?.ToLowerInvariant();
            if (hash is null || !Regex.IsMatch(hash, "\\A[a-f0-9]{128}\\z"))
                throw new InvalidDataException("A SHA-512 release hash is required for cached paks.");
            var directory = Path.Combine(Root, hash);
            Directory.CreateDirectory(directory);
            using var lease = await AssetCacheFiles.LockAsync(Path.Combine(directory, ".lock"), cancellationToken);
            var cached = Path.Combine(directory, "mod.pak");
            if (!File.Exists(cached)) {
                var temporary = cached + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try {
                    var result = await request.Writer(temporary);
                    using var writer = result.Match(Right: value => value, Left: error => throw error.ToException());
                    var written = await writer.WriteAsync(writer.Size > 0 ? progress : None, cancellationToken);
                    written.IfLeft(error => throw error.ToException());
                    // Existing registry writers use zero when HTTP size discovery is unavailable.
                    if (writer.Size > 0 && new FileInfo(temporary).Length != writer.Size)
                        throw new InvalidDataException("Pak download size differs from the reported size.");
                    Validate(temporary, hash);
                    File.Move(temporary, cached);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            else Validate(cached, hash);
            cancellationToken.ThrowIfCancellationRequested();
            // Creating a hard link is already atomic and does not replace an existing name.
            // Renaming a staging link can fail on Windows while a peer has the pak open.
            if (AssetCacheFiles.TryCreateHardLink(destination, cached)) {
                progress.IfSome(p => p.Report(100));
                return Unit.Default;
            }
            var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.Copy(cached, staging, overwrite: false);
                // Never overwrite an unmanaged file that appeared during preparation.
                File.Move(staging, destination);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
            progress.IfSome(p => p.Report(100));
            return Unit.Default;
        }).ToEither();

    private static void Validate(string path, string expected) {
        using var stream = File.OpenRead(path);
        if (!Convert.ToHexString(SHA512.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Pak hash mismatch: {path}");
    }

}