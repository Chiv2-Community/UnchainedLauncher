using UnchainedLauncher.Core.API.A2S;

namespace UnchainedLauncher.Core.Tests.Unit.API.Mocks {
    using Environment = Core.API.A2S.Environment;

    public class MockA2S : IA2S {
        protected A2SInfo[] results;
        protected int resultsPosition;
        public int NumRequests { get { return resultsPosition; } }
        public MockA2S() {
            results = new A2SInfo[1] {
                new(0, "", "test map", "", "Chivalry 2", 0, 10, 100, 5, ServerType.NonDedicated, Environment.Windows, true, false)
            };
        }

        /// <summary>
        /// Initialize the mock a2s endpoint with an array of responses.
        /// These responses will be returned by InfoAsync in order until exhausted. When
        /// exhausted, the results will loop.
        /// </summary>
        /// <param name="a2SInfos"></param>
        public MockA2S(A2SInfo[] a2SInfos) {
            results = a2SInfos;
        }
        public Task<A2SInfo> InfoAsync() {
            var res = results[resultsPosition++ % results.Length];
            return Task.FromResult(res);
        }
    }
}