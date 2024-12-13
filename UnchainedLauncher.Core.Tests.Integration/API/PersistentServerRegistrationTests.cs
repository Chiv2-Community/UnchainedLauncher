using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.API.A2S;
using UnchainedLauncher.Core.API.ServerBrowser;
using Environment = UnchainedLauncher.Core.API.A2S.Environment;

namespace UnchainedLauncher.Core.Tests.Integration.API {
    public class PersistentServerRegistrationTests {
        public static readonly C2ServerInfo testServerC2Info = new() {
            Name = "Test server",
            Description = "Test description"
        };

        [Fact]
        public async Task HoldRegistrationTest() {
            using var backend = new ServerBrowser(new Uri("http://localhost:8080/api/v1/"));
            var RegistrationFactory = new PersistentServerRegistrationFactory(backend, testServerC2Info, 5, "127.0.0.1");
            var initialA2s = new A2sInfo(0, "", "test map", "", "Chivalry 2", 0, 10, 100, 5, ServerType.NONDEDICATED, Environment.WINDOWS, true, false);
            var secondA2s = initialA2s with { Players = (byte)(initialA2s.Players + 1) };
            using (var reg = await RegistrationFactory.MakeRegistration(initialA2s)) {
                Assert.False(reg.IsDead);
                Assert.Null(reg.LastException);
                // wait long enough for some heartbeats to have been required
                // this might be a good time to manually go check that this
                // server is registered with the backend, but if this
                // next update is successful then it definitely must be.
                await Task.Delay(240 * 1000);
                // send an update (that is different and will actually be pushed)
                // to make sure the registration is actually still alive
                await reg.UpdateRegistrationA2s(secondA2s);
                Assert.False(reg.IsDead);
                Assert.Null(reg.LastException);
            }
        }
    }
}