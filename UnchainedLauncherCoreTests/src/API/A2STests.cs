using UnchainedLauncher.Core.API.A2S;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;

namespace UnchainedLauncher.Core.API.A2S.Tests
{
    // Should have a chivalry/A2S server running on 127.0.0.1:7071 for these to
    // test against
    [TestClass()]
    public class A2STests
    {
        private static readonly IPEndPoint endpoint = IPEndPoint.Parse("127.0.0.1:7071");
        [TestMethod()]
        public void infoAsyncTest()
        {
            var infoTask = A2S.infoAsync(endpoint);
            infoTask.Wait();
            var info = infoTask.Result;

            Assert.IsNotNull(info);
            Assert.AreEqual(info.game, "Chivalry 2");
        }
    }
}