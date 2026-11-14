# Portable installer workflow tests

Run `dotnet test UnchainedLauncher.GUI.Tests/UnchainedLauncher.GUI.Tests.csproj`.

Tests compile the production installer view models with the GUI's toolkit and
Fody versions. Only Windows dialogs, views, and external services are replaced.
The single-thread synchronization context checks collection event affinity;
it does not run WPF or validate rendering. No game files or processes are used.
