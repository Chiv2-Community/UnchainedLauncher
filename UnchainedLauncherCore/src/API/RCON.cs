using System.Net;
using System.Net.Sockets;

namespace UnchainedLauncher.Core.API
{
    public class RCON
    {
        protected IPEndPoint RconLocation;
        public RCON(IPEndPoint rconLocation)
        {
            RconLocation = rconLocation;
        }

        public async Task SendCommand(string command)
        {
            await SendCommandTo(RconLocation, command);
        }

        public static async Task SendCommandTo(IPEndPoint rconLocation, string command)
        {
            try
            {
                CancellationTokenSource cts = new(1000);
                using TcpClient client = new TcpClient();
                await client.ConnectAsync(rconLocation);
                await client.GetStream().WriteAsync(
                    (command+"\n").Map((c)=>(byte)c).ToArray(), // string -> char[]
                    cts.Token
                );
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("RCON connection timed out");
            }
            
        }
    }
}
