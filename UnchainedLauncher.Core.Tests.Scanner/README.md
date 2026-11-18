# Portable scanner/cache tests

Run on .NET 10:

```sh
dotnet test UnchainedLauncher.Core.Tests.Scanner/UnchainedLauncher.Core.Tests.Scanner.csproj
dotnet test UnchainedLauncher.PakScannerApp.Tests/UnchainedLauncher.PakScannerApp.Tests.csproj
```

Links Core visibility, cache, recovery-progress, recovery, and shared-inventory tests against production Core. Reuses Core's existing friend assembly name. `PORTABLE_SCANNER_TESTS` excludes only the recovery suite's GUI release-link test; that test remains enabled in the Windows suite.

Cache reuse compares filenames, lengths, and modification timestamps. Same-length changes preserving timestamps are not detectable. Cached scan dependencies do not grant ownership or permission to retain unmanaged paks.
