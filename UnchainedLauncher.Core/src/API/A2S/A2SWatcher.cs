using log4net;
using Microsoft.VisualBasic;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.API;

namespace UnchainedLauncher.Core.API
{
    /// <summary>
    /// Periodically polls an A2S endpoint. Runs a delegate whenever a poll is successful and
    /// new A2S information is available.
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class A2SWatcher : IDisposable
    {
        public delegate Task OnA2SReceived(A2sInfo info);
        private readonly OnA2SReceived OnReceived;
        private readonly PeriodicRunner runner;
        public Exception? LastException => runner.LastException;
        public A2sInfo? LastA2sInfo {get; private set;}
        // TODO: add a task completion thing to this
        // so that other things can get async tasks that
        // complete when this becomes true.
        // this being true doesn't actually garuntee anything, though,
        // so doing that might just be a TOCTOU factory
        public bool A2sOk { get; private set; }
        private readonly IA2S source;
        private bool disposedValue;
        public readonly TimeSpan PollingInterval;
        public A2SWatcher(IA2S source, OnA2SReceived action, int intervalMillis = 1000)
        {
            this.source = source;
            A2sOk = false;
            OnReceived = action;
            PollingInterval = TimeSpan.FromMilliseconds(intervalMillis);
            runner = new PeriodicRunner(FetchA2S, OnException);
        }

        private async Task<TimeSpan> FetchA2S()
        {
            var newInfo = await source.InfoAsync();
            LastA2sInfo = newInfo;
            A2sOk = true;
            await OnReceived(newInfo);
            return PollingInterval;
        }

        private async Task<bool> OnException(Exception ex)
        {
            // TODO: find a cleaner way to do this.
            // potentially modify the PeriodicRunner to take
            // a nullable TimeSpan from this function, with the
            // null representing "stop ticking"

            // this delay is so that the runner does not immediately
            // retry the task and spam the endpoint. See the comment on
            // the PeriodicRunner on returning a nullable TimeSpan instead
            // of bool to do precisely this.
            A2sOk = false;
            await Task.Delay(PollingInterval);
            // keep going even if there's exceptions
            return true;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    runner.Dispose();
                }
                A2sOk = false;
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
