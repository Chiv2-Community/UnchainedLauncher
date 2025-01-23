using log4net;
using PropertyChanged;
using System.Diagnostics;

namespace UnchainedLauncher.Core.API {
    /// <summary>
    /// A Chivalry 2 Server that should restart and stay registered with a backend all on its own
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class Chivalry2Server : IDisposable {
        private static readonly ILog logger = LogManager.GetLogger(nameof(Chivalry2Server));
        private bool disposedValue;
        public Process ServerProcess { get; private set; }
        public IRCON? Rcon { get; private set; }

        // TODO: add UPnP manager here
        public A2SBoundRegistration RegistrationHandler { get; private set; }
        public Chivalry2Server(Process serverProcess, A2SBoundRegistration Registration, IRCON? rcon = null) {
            this.RegistrationHandler = Registration;
            ServerProcess = serverProcess;
            Rcon = rcon;
        }

        public void KillProcess() {
            if (ServerProcess.HasExited) return;
            try {
                Rcon?.SendCommand("exit").Wait(1000);
            }
            catch (Exception ex) {
                logger.Error($"Failed to send exit RCON to process: {ex.Message}");
            }
            finally {
                try {
                    ServerProcess.Kill();
                    // Kill does not actually kill immediately.
                    // We do not want to continue past this point
                    // until all listeners for the Process.Exited event
                    // have run. This WaitForExit ensures that happens
                    // and disposing/closing below does not break things
                    ServerProcess.WaitForExit();
                }
                catch (Exception ex) {
                    logger.Error($"Failed to kill server process: {ex.Message}");
                }
            }
            ServerProcess.WaitForExit();
            ServerProcess.Close();
            ServerProcess.Dispose();
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    KillProcess();
                    this.RegistrationHandler.Dispose();
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