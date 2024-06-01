using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;

namespace UnchainedLauncher.Core.API
{
    public record A2S_INFO (
        byte protocolVersion, 
        String name,
        String map,
        String folder,
        String game, 
        UInt16 gameID, 
        byte players,
        byte maxPlayers,
        byte bots, 
        ServerType serverType, 
        Environment environment, 
        bool isPublic, 
        bool vac
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

    public static class A2S
    {
        const int timeOutMillis = 1000;
        public static async Task<A2S_INFO> infoAsync(IPEndPoint ep)
        {
            try
            {
                return await infoAsync_impl(ep);
            }
            catch(TaskCanceledException)
            {
                throw new TimeoutException("A2S connection timed out");
            }
        }
        private static async Task<A2S_INFO> infoAsync_impl(IPEndPoint ep)
        {
            using CancellationTokenSource cs = new(timeOutMillis);
            //BinaryPrimitives
            //see A2S_INFO section of https://developer.valvesoftware.com/wiki/Server_queries
            //request structure is defined by https://developer.valvesoftware.com/wiki/Server_queries
            byte[] request = {0xFF, 0xFF, 0xFF, 0xFF,
                                0x54, 0x53, 0x6F, 0x75, 0x72, 0x63,
                                0x65, 0x20, 0x45, 0x6E, 0x67, 0x69,
                                0x6E, 0x65, 0x20, 0x51, 0x75, 0x65, 0x72, 0x79, 0x00
                                };
            using UdpClient client = new();
            client.Connect(ep);
            await client.SendAsync(request, cs.Token);
            UdpReceiveResult response = await client.ReceiveAsync(cs.Token);
            BinaryReader br = new(new MemoryStream(response.Buffer), Encoding.UTF8);
            // ensure header is correct
            if (!br.ReadBytes(5).SequenceEqual(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x49 }))
            {
                throw new InvalidDataException("Invalid response header");
            }
            byte protocolVersion = br.ReadByte();
            var (name, map, folder, game) = (readString(ref br), readString(ref br), readString(ref br), readString(ref br));
            UInt16 gameID = BinaryPrimitives.ReadUInt16BigEndian(br.ReadBytes(2));
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
            else if(!Enum.IsDefined(typeof(Environment), environment))
            {
                throw new InvalidDataException("Invalid environment type");
            }
            return new A2S_INFO(protocolVersion, name, map, folder, game, gameID, players, maxPlayers, bots, serverType, environment, isPublic, vac);
        }
        private static String readString(ref BinaryReader reader)
        {
            StringBuilder sb = new();
            char n = reader.ReadChar();
            while(n != 0x00)
            {
                sb.Append(n);
                n = reader.ReadChar();
            }
            return sb.ToString();
        }
    }
}
