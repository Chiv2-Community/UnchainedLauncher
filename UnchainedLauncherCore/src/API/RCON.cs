using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace UnchainedLauncher.Core.API
{
    public class RCON
    {
        public static async Task sendCommand(IPEndPoint rconLocation, string command)
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
