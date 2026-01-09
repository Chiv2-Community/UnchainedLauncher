using log4net;
using System.Net;
using System.Net.Sockets;

namespace UnchainedLauncher.Core.Services.Server {
    public interface IRCON {
        public string RconLocation { get; }
        public Task SendCommand(string command);
    }

    public class RCON : IRCON {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(RCON));
        public string RconLocation => RconEndpoint.ToString();
        protected readonly IPEndPoint RconEndpoint;
        public RCON(IPEndPoint rconEndpoint) {
            RconEndpoint = rconEndpoint;
            Logger.Info($"RCON initialized for endpoint {rconEndpoint}");
        }

        public async Task SendCommand(string command) {
            await SendCommandTo(RconEndpoint, command);
        }

        public static async Task SendCommandTo(IPEndPoint rconEndpoint, string command) {
            try {
                using var client = new TcpClient();
                client.SendTimeout = 1000;
                client.ReceiveTimeout = 1000;
                Logger.Debug($"Connecting to RCON endpoint {rconEndpoint}");
                await client.ConnectAsync(rconEndpoint);
                await client.GetStream().WriteAsync(
                    (command + "\n").Map((c) => (byte)c).ToArray() // string -> byte[]
                );
                Logger.Info($"Sent command '{command}' to '{rconEndpoint}'");
            }
            catch (Exception ex) {
                Logger.Error($"Failed to send RCON command '{command}' to '{rconEndpoint}': {ex.Message}");
                throw;
            }
        }
    }
}