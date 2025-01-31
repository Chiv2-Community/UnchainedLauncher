using UnchainedLauncher.Core.API;

namespace UnchainedLauncher.Core.Tests.Unit.API {
    public class PeriodicRunnerTests {
        [Fact]
        public async Task TestPolling() {
            var pollCount = 0;
            var testTimeMillis = 2000;
            var expectedPollCount = 10;
            var delay = testTimeMillis / expectedPollCount;
            Task<TimeSpan> Execute() {
                pollCount++;
                return Task.FromResult(TimeSpan.FromMilliseconds(delay));
            }

            using (PeriodicRunner runner = new(Execute)) {
                await Task.Delay(testTimeMillis);
            }

            try {
                Assert.Equal(expectedPollCount, pollCount);
            }
            catch {
                // sometimes it does an extra poll because of timing stuff
                Assert.Equal(expectedPollCount + 1, pollCount);
            }

        }

        [Fact]
        public async Task TestExceptions() {
            var pollCount = 0;
            var testTimeMillis = 200;
            Task<TimeSpan> Execute() {
                pollCount++;
                throw new NotImplementedException();
            }

            var exceptionHandled = false;
            Task<bool> OnException(Exception e) {
                exceptionHandled = true;
                return Task.FromResult(false);
            }

            using (PeriodicRunner runner = new(Execute, OnException)) {
                await Task.Delay(testTimeMillis);
                Assert.Equal(1, pollCount);
                Assert.True(exceptionHandled);
                Assert.True(runner.LastException is NotImplementedException);
            }
        }

        [Fact]
        public async Task TestExceptionRetries() {
            var pollCount = 0;
            var testTimeMillis = 200;
            Task<TimeSpan> Execute() {
                pollCount++;
                throw new NotImplementedException();
            }

            var exceptionHandled = false;
            Task<bool> OnException(Exception e) {
                exceptionHandled = true;
                return Task.FromResult(true);
            }

            using (PeriodicRunner runner = new(Execute, OnException)) {
                await Task.Delay(testTimeMillis);
                Assert.NotEqual(1, pollCount);
                Assert.True(exceptionHandled);
                Assert.True(runner.LastException is NotImplementedException);
            }
        }
    }
}