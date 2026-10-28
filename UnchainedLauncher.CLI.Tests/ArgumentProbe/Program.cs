using System.Text.Json;
using System.IO.Pipes;
using System.Text;

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
if (args[0] is "--console-probe" or "--console-probe-idle") {
    var handle = Environment.GetEnvironmentVariable("UNCHAINED_CONSOLE_HANDLE")!;
    var endpoint = Environment.GetEnvironmentVariable("UNCHAINED_CONSOLE_ENDPOINT");
    var token = Environment.GetEnvironmentVariable("UNCHAINED_CONSOLE_TOKEN");
    File.WriteAllText(args[1], JsonSerializer.Serialize(new { handle, endpoint, token }));
    using var stream = new AnonymousPipeClientStream(PipeDirection.Out, handle);
    Console.WriteLine("stdout must not enter structured records");
    Console.Error.WriteLine("stderr must not enter structured records");
    var frame = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
        version = 1, kind = "log", dropped = 0,
        record = new { time = "2026-10-28T12:34:56+00:00", level = "INFO", message = "native console marker",
            target = "probe", thread = "main", thread_id = 1234, mdc = new { }, attributes = new { } }
    }) + "\n");
    if (args[0] == "--console-probe-idle") {
        await stream.WriteAsync(frame);
        await Task.Delay(TimeSpan.FromMinutes(1));
        return;
    }
    try {
        // A private write-only pipe detects receiver disposal on the next write.
        while (true) { await stream.WriteAsync(frame); await Task.Delay(20); }
    }
    catch (IOException) { }
    return;
}

// The first argument is the result file; all remaining arguments come from the launcher.
File.WriteAllText(args[0], JsonSerializer.Serialize(args.Skip(1).ToArray()));
