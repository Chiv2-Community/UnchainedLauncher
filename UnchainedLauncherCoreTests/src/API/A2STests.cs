using UnchainedLauncher.Core.API;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;

namespace UnchainedLauncher.Core.API.Tests
{

    public class MockA2S : IA2S
    {
        protected A2sInfo[] results;
        protected int resultsPosition;
        public int NumRequests { get { return resultsPosition; } }
        public MockA2S()
        {
            results = new A2sInfo[1] {
                new(0, "", "test map", "", "Chivalry 2", 0, 10, 100, 5, ServerType.NONDEDICATED, Environment.WINDOWS, true, false)
            };
        }

        /// <summary>
        /// Initialize the mock a2s endpoint with an array of responses.
        /// These responses will be returned by InfoAsync in order until exhausted. When
        /// exhausted, the results will loop.
        /// </summary>
        /// <param name="a2SInfos"></param>
        public MockA2S(A2sInfo[] a2SInfos)
        {
            results = a2SInfos;
        }
        public Task<A2sInfo> InfoAsync()
        {
            var res = results[resultsPosition++ % results.Length];
            return Task.FromResult(res);
        }
    }

    // Should have a chivalry/A2S server running on 127.0.0.1:7071 for these to
    // test against
    [TestClass()]
    public class A2STests
    {
        private static readonly IPEndPoint endpoint = IPEndPoint.Parse("127.0.0.1:7071");
        [TestMethod()]
        public async Task InfoAsyncTest()
        {
            A2S A2sEndpoint = new(endpoint);
            var info = await A2sEndpoint.InfoAsync();

            Assert.IsNotNull(info);
            Assert.AreEqual(info.Game, "Chivalry 2");
        }
    }
}