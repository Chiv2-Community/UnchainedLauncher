# Scanner configuration

`scan --config scanconfig_default.json` reads `PakDirectory`, `OutputDirectory`,
`ScanMode` and `OfficialDirectories`. Explicit CLI values override the file.
Relative paths use the working directory. Missing output defaults to the working
directory; missing mode defaults to `All`.

Optional `Options` contains scanner-library `ScanOptions`; otherwise the
Chivalry 2 preset is used. `OfficialDirectories` overrides its vanilla asset paths.
