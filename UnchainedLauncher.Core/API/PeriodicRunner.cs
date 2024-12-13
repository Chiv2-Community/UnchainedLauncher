using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace UnchainedLauncher.Core.API {
    /// <summary>
    /// Runs a delegate which tells it when it wants to be run next.
    /// Each run of the delegate is called a "tick"
    /// Provides optional delegate to be run whenever the original
    /// delegate raises an exception.
    /// 
    /// When a delegate is running, it will NOT be run again until it
    /// finishes. This ensures that it is being run only once simultaneously
    /// </summary>
    public sealed class PeriodicRunner : IDisposable {
        private Timer Timer;
        private bool disposedValue;

        // whether the delegate is currently scheduled to be run
        public bool IsTicking { get; private set; }

        // last exception thrown by the delegate
        public Exception? LastException { get; private set; }

        // method to be called periodically. Returns a timespan representing
        // the next time it should be called
        public delegate Task<TimeSpan> Execute();
        public delegate Task<bool> OnException(Exception exception);
        private readonly Execute _Execute;
        private readonly OnException _OnException;

        public PeriodicRunner(Execute execute, OnException? OnException = null, TimeSpan? after = null) {
            this._Execute = execute;
            this._OnException = OnException ?? DefaultOnException;
            this.Timer = new Timer(this.Execute_Update, null, after ?? TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            this.IsTicking = true;
        }

        private static Task<bool> DefaultOnException(Exception ex) {
            return Task.FromResult(false);
        }

        /// <summary>
        /// Resume ticking, with the next run scheduled immediately or with the given
        /// timespan.
        /// </summary>
        /// <param name="after">How long to wait before next tick</param>
        public void Resume(TimeSpan? after = null) {
            if (this.IsTicking) {
                return;
            }

            this.Timer.Dispose();
            this.Timer = new Timer(this.Execute_Update, null, after ?? TimeSpan.Zero, Timeout.InfiniteTimeSpan);
            this.IsTicking = true;
        }

        /// <summary>
        /// Stop the timer from ticking. An already scheduled run will not occur after this.
        /// </summary>
        public void Stop() {
            this.Timer.Dispose();
            this.IsTicking = false;
        }

        // Execute a tick delegate to get when it wants to be run
        // catch any errors and run the OnException delegate as needed
        private async void Execute_Update(object? state) {
            try {
                var nextTimeSpan = await _Execute();
                // saturate negative timespans to run immediately
                nextTimeSpan = nextTimeSpan < TimeSpan.Zero ? TimeSpan.Zero : nextTimeSpan;
                this.Timer = new Timer(this.Execute_Update, null, nextTimeSpan, Timeout.InfiniteTimeSpan);
                return;
            }
            catch (Exception exception) {
                this.LastException = exception;
                var shouldContinue = await this._OnException(exception);
                if (shouldContinue) {
                    this.Stop();
                    this.Resume();
                }
            }
            finally {
                // in case we were disposed after the timeout expired but before
                // we set the new timer
                if (this.disposedValue) {
                    this.Stop();
                }
            }
        }

        private void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    this.Stop();
                }
                disposedValue = true;
            }
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}