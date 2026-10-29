using System.Text;
using System.Text.Json;

if (args[0] == "--std-probe") {
    Console.OutputEncoding = new UTF8Encoding(false);
    if (Console.ReadLine() != null) throw new InvalidDataException("Capture must not inherit the launcher's keyboard input.");
    Console.WriteLine("stdout marker 世界");
    Console.Error.WriteLine("stderr marker");
    File.WriteAllText(args[1], "started");
    await Task.Delay(TimeSpan.FromMinutes(1));
    return;
}

if (args[0] == "--environment-probe") {
    File.WriteAllText(args[1], JsonSerializer.Serialize(args.Skip(2).ToDictionary(name => name, Environment.GetEnvironmentVariable)));
    return;
}
// The first argument is the result file; all remaining arguments come from the launcher.
File.WriteAllText(args[0], JsonSerializer.Serialize(args.Skip(1).ToArray()));