using System.Text.Json;
using System.Net;
using System.Net.Sockets;
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
if (args[0] == "--console-probe") {
    var endpoint = Environment.GetEnvironmentVariable("UNCHAINED_CONSOLE_ENDPOINT")!;
    var token = Environment.GetEnvironmentVariable("UNCHAINED_CONSOLE_TOKEN")!;
    File.WriteAllText(args[1], JsonSerializer.Serialize(new { endpoint, token }));
    using var client = new TcpClient();
    await client.ConnectAsync(IPEndPoint.Parse(endpoint));
    using var stream = client.GetStream();
    await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { version = 1, kind = "hello", token }) + "\n"));
    var ack = new byte[3];
    await stream.ReadExactlyAsync(ack);
    if (Encoding.ASCII.GetString(ack) != "OK\n") throw new InvalidDataException("Invalid console acknowledgement.");
    await stream.WriteAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
        version = 1, kind = "log", level = "INFO", text = "native console marker", dropped = 0
    }) + "\n"));
    await stream.ReadAsync(new byte[1]); // Keep the probe alive until its receiver is disposed.
    return;
}

// The first argument is the result file; all remaining arguments come from the launcher.
File.WriteAllText(args[0], JsonSerializer.Serialize(args.Skip(1).ToArray()));
