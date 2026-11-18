# Portable catalog tests

Run `dotnet test UnchainedLauncher.Core.Tests.Catalog/UnchainedLauncher.Core.Tests.Catalog.csproj`.

Links production catalog, scan view models, and Windows-project regressions against real
Core/scanner/cache APIs. Only WPF event-manager/converter/timer boundaries are replaced.
Watcher, header rendering, server selection, and restart tests still require Windows.
