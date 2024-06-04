using UnchainedLauncher.Core.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;

namespace UnchainedLauncher.Core.API.Tests
{
    // Should have a chivalry/A2S server running on 127.0.0.1:7071 for these to
    // test against
    [TestClass()]
    public class A2STests
    {
        private static readonly IPEndPoint endpoint = IPEndPoint.Parse("127.0.0.1:7071");
        [TestMethod()]
        public async Task InfoAsyncTest()
        {
            var info = await A2S.InfoAsync(endpoint);

            Assert.IsNotNull(info);
            Assert.AreEqual(info.Game, "Chivalry 2");
        }
    }
}