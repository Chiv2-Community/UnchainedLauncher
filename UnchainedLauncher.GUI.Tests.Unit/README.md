# GUI tests

Run `dotnet test UnchainedLauncher.GUI.Tests.Unit/UnchainedLauncher.GUI.Tests.Unit.csproj`.
Linux runs the portable tests. Windows also compiles the production `CollapsibleCard`
and runs STA visual-tree, nested-layout, collapse, theme, and hit-testing checks.
The scale cases use layout transforms, not monitor DPI changes.

Cross-compile those WPF tests on Linux:

```sh
dotnet build UnchainedLauncher.GUI.Tests.Unit/UnchainedLauncher.GUI.Tests.Unit.csproj -p:WpfTests=true -p:EnableWindowsTargeting=true
```

For issue #164, also compare the server configuration and mod-actor cards before
and after the fix on Windows at 100%, 125%, 150%, and 200% display scaling:

- Check expanded and collapsed cards, including nested cards.
- Check light/dark themes, border alignment, shadows, scrolling, and text clarity.
- Toggle headers and edit text boxes/checkboxes to check input behavior.

Structural tests do not establish screenshot quality or physical-monitor DPI behavior.
WPF effects introduce intermediate render targets and can disable ClearType:
https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.renderoptions.cleartypehint
