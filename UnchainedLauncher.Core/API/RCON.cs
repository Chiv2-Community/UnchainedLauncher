using log4net;
using System.Net;
using System.Net.Sockets;

namespace UnchainedLauncher.Core.API {
    public interface IRCON {
        public Task SendCommand(string command);
    }

    public class RCON : IRCON {
        private static readonly ILog logger = LogManager.GetLogger(nameof(RCON));
        protected IPEndPoint RconLocation;
        public RCON(IPEndPoint rconLocation) {
            RconLocation = rconLocation;
        }

        public async Task SendCommand(string command) {
            await SendCommandTo(RconLocation, command);
        }

        public static async Task SendCommandTo(IPEndPoint rconLocation, string command) {
            using TcpClient client = new TcpClient();
            client.SendTimeout = 1000;
            client.ReceiveTimeout = 1000;
            await client.ConnectAsync(rconLocation);
            await client.GetStream().WriteAsync(
                (command + "\n").Map((c) => (byte)c).ToArray() // string -> byte[]
            );
            logger.Info($"Sent command '{command}' to '{rconLocation}'");
        }
    }
}