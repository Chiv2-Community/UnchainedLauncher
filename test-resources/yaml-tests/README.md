# YAML test inputs

CLI tests load YAML from these files, not C# strings. Keep INI values concise
unless a case exercises explicit types, conversions, raw values, or unknown records.

- Directories follow the test areas: configuration, includes, INI, rotation,
  server groups, argument boundaries, registries, Proton, and monitoring.
- Invalid cases intentionally contain malformed syntax, duplicate keys, missing
  values, or unsupported settings. `configuration/empty-document.yaml` is empty.
- `__TOKEN__` placeholders supply runtime paths, parameterized scalar/list values,
  schema names, or resource-backed nodes through `YamlTestResources`.
- Quote or JSON-encode replacement strings where required by the fixture;
  replacements are literal, not YAML serialization.
- Include tests copy resources under the filenames referenced by `!load`.
- Golden server-output cases remain in `../server-configuration`; layered-settings
  cases and their referenced files remain in `../settings-resolution`.
