# Portable installer tests

Run `dotnet test UnchainedLauncher.Installer.Tests` on any .NET 10 platform.

Links production installer view models with their real MVVM source generation and
property-change weaving. Stubs replace WPF dialogs, discovery, installation, and
process-launch UI boundaries. Tests cover navigation, UI-context publication,
selection, per-store discovery failures, installation results, recovery, and launch
target filtering. They do not replace the Windows GUI tests or exercise WPF rendering.

Ported from release v1.0.2's installer workflow tests (PR #195).
