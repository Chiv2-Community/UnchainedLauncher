using PropertyChanged;

namespace UnchainedLauncher.Core.API.A2S {
    /// <summary>
    /// Periodically polls an A2S endpoint. Runs a delegate whenever a poll is successful and
    /// new A2S information is available.
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class A2SWatcher : IDisposable {
        public delegate Task OnA2SReceived(A2SInfo info);
        private readonly OnA2SReceived _onReceived;
        private readonly PeriodicRunner _runner;
        public Exception? LastException => _runner.LastException;
        public A2SInfo? LastA2SInfo { get; private set; }
        // TODO: add a task completion thing to this
        // so that other things can get async tasks that
        // complete when this becomes true.
        // this being true doesn't actually garuntee anything, though,
        // so doing that might just be a TOCTOU factory
        public bool A2SOk { get; private set; }
        private readonly IA2S _source;
        private bool _disposedValue;
        public readonly TimeSpan PollingInterval;
        public A2SWatcher(IA2S source, OnA2SReceived action, int intervalMillis = 1000) {
            this._source = source;
            A2SOk = false;
            _onReceived = action;
            PollingInterval = TimeSpan.FromMilliseconds(intervalMillis);
            _runner = new PeriodicRunner(FetchA2S, OnException);
        }

        private async Task<TimeSpan> FetchA2S() {
            var newInfo = await _source.InfoAsync();
            LastA2SInfo = newInfo;
            A2SOk = true;
            await _onReceived(newInfo);
            return PollingInterval;
        }

        private async Task<bool> OnException(Exception ex) {
            // TODO: find a cleaner way to do this.
            // potentially modify the PeriodicRunner to take
            // a nullable TimeSpan from this function, with the
            // null representing "stop ticking"

            // this delay is so that the runner does not immediately
            // retry the task and spam the endpoint. See the comment on
            // the PeriodicRunner on returning a nullable TimeSpan instead
            // of bool to do precisely this.
            A2SOk = false;
            await Task.Delay(PollingInterval);
            // keep going even if there's exceptions
            return true;
        }

        protected virtual void Dispose(bool disposing) {
            if (!_disposedValue) {
                if (disposing) {
                    _runner.Dispose();
                }
                A2SOk = false;
                _disposedValue = true;
            }
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}