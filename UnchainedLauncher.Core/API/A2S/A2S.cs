using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using log4net;

namespace UnchainedLauncher.Core.API.A2S
{
    public record A2sInfo(
        byte ProtocolVersion,
        string Name,
        string Map,
        string Folder,
        string Game,
        ushort GameID,
        byte Players,
        byte MaxPlayers,
        byte Bots,
        ServerType ServerType,
        Environment Environment,
        bool IsPublic,
        bool Vac
        );

    public enum ServerType : byte
    {
        DEDICATED = (byte)'D',
        NONDEDICATED = (byte)'L',
        PROXY = (byte)'P'
    }

    public enum Environment : byte
    {
        LINUX = (byte)'L',
        WINDOWS = (byte)'W',
        MAC = (byte)'M'
    }

    public interface IA2S
    {
        public Task<A2sInfo> InfoAsync();
    }
    public class A2S : IA2S
    {
        private static readonly ILog logger = LogManager.GetLogger(nameof(A2S));
        protected readonly IPEndPoint ep;
        protected readonly int TimeOutMillis;
        public A2S(IPEndPoint ep, int TimeOutMillis = 1000)
        {
            this.ep = ep;
            this.TimeOutMillis = TimeOutMillis;
        }

        private async Task<UdpReceiveResult> DoInfoRequest()
        {
            //see A2S_INFO section of https://developer.valvesoftware.com/wiki/Server_queries
            //request structure is defined by https://developer.valvesoftware.com/wiki/Server_queries
            byte[] request = {0xFF, 0xFF, 0xFF, 0xFF,
                                0x54, 0x53, 0x6F, 0x75, 0x72, 0x63,
                                0x65, 0x20, 0x45, 0x6E, 0x67, 0x69,
                                0x6E, 0x65, 0x20, 0x51, 0x75, 0x65, 0x72, 0x79, 0x00
                                };
            try
            {
                using UdpClient client = new();
                using CancellationTokenSource cs = new(TimeOutMillis);
                client.Connect(ep);
                await client.SendAsync(request, cs.Token);
                return await client.ReceiveAsync(cs.Token);
            }
            catch (TaskCanceledException)
            {
                throw new TimeoutException("A2S request timed out");
            }
        }
        public async Task<A2sInfo> InfoAsync()
        {
            UdpReceiveResult response = await DoInfoRequest();
            logger.Debug($"Received {ByteArrayToHexString(response.Buffer)} bytes from {ep}");
            BinaryReader br = new(new MemoryStream(response.Buffer), Encoding.UTF8);
            // ensure header is correct
            if (!br.ReadBytes(5).SequenceEqual(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x49 }))
            {
                throw new InvalidDataException("Invalid response header");
            }
            byte protocolVersion = br.ReadByte();
            var (name, map, folder, game) = (ReadString(ref br), ReadString(ref br), ReadString(ref br), ReadString(ref br));
            ushort gameID = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2));
            var (players, maxPlayers, bots) = (br.ReadByte(), br.ReadByte(), br.ReadByte());
            var serverType = (ServerType)br.ReadByte();
            var environment = (Environment)br.ReadByte();
            //handle weird variance on mac environments
            environment = (byte)environment == (byte)'O' ? Environment.MAC : environment;
            var (isPublic, vac) = (br.ReadByte() == 0, br.ReadByte() == 1);
            // validate enum values
            if (!Enum.IsDefined(typeof(ServerType), serverType))
            {
                throw new InvalidDataException("Invalid server type");
            }
            else if (!Enum.IsDefined(typeof(Environment), environment))
            {
                throw new InvalidDataException("Invalid environment type");
            }
            return new A2sInfo(protocolVersion, name, map, folder, game, gameID, players, maxPlayers, bots, serverType, environment, isPublic, vac);
        }
        private static string ReadString(ref BinaryReader reader)
        {
            StringBuilder sb = new();
            char n = reader.ReadChar();
            while (n != 0x00)
            {
                sb.Append(n);
                n = reader.ReadChar();
            }
            return sb.ToString();
        }

        private static string ByteArrayToHexString(byte[] byteArray)
        {
            // Create a new string array to hold the hexadecimal representations
            string[] hexArray = new string[byteArray.Length];

            // Convert each byte to a two-character hexadecimal string
            for (int i = 0; i < byteArray.Length; i++)
            {
                hexArray[i] = byteArray[i].ToString("X2"); // X2 formats as two uppercase hex digits
            }

            return hexArray.Aggregate((carry, newString) => carry + " " + newString);
        }
    }
}