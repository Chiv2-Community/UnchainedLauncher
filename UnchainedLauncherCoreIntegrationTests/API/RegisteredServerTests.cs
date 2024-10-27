using System.Net;

namespace UnchainedLauncher.Core.API.Tests
{
    // You must have a backend instance running for these tests to succeed
    public class RegisteredServerTests
    {
        private static readonly Uri backendUri = new("http://localhost:8080/api/v1");

        //you might want to watch this test case run using wireshark
        // requires a full setup; a real local backend server, and a real instance of chiv running
        [Fact]
        public async Task IntegrationTest() {
            using var httpClient = new HttpClient();
            var backend = new ServerBrowser(backendUri, httpClient);

            var c2ServerInfo = new C2ServerInfo {
                Ports = new PublicPorts(
                    7777, 
                    7778, 
                    7779
                )
            };

            var a2s = new A2S(new IPEndPoint(IPAddress.Parse("127.0.0.1"), c2ServerInfo.Ports.A2s));
            var l = new RegisteredServer(backend, a2s, c2ServerInfo, "127.0.0.1", 9001);
            await Task.Delay(70000); //give time for heartbeats to happen
            var reg = l.RemoteInfo;
            Assert.NotNull(reg);
            Assert.NotEqual("", reg!.UniqueId);
        }
    }
}