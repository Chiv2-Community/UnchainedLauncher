using System.Text.Json;

// The first argument is the result file; all remaining arguments come from the launcher.
File.WriteAllText(args[0], JsonSerializer.Serialize(args.Skip(1).ToArray()));