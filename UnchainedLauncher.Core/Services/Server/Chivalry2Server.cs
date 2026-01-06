using log4net;
using PropertyChanged;
using System.Diagnostics;

namespace UnchainedLauncher.Core.Services.Server {
    /// <summary>
    /// A Chivalry 2 Server that should restart and stay registered with a backend all on its own
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class Chivalry2Server : IDisposable {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(Chivalry2Server));
        private bool _disposedValue;
        public Process ServerProcess { get; private set; }
        public IRCON? Rcon { get; private set; }

        // TODO: add UPnP manager here
        public ServerRegistrationService RegistrationHandler { get; private set; }
        public Chivalry2Server(Process serverProcess, ServerRegistrationService registration, IRCON? rcon = null) {
            Logger.Info($"Initializing Chivalry2Server with PID {serverProcess.Id}, RCON: {(rcon != null ? rcon.RconLocation : "None")}");
            RegistrationHandler = registration;
            ServerProcess = serverProcess;
            Rcon = rcon;
        }

        public void KillProcess() {
            RegistrationHandler.StopEventLoop();
            if (ServerProcess.HasExited) {
                Logger.Debug($"Server process {ServerProcess.Id} has already exited");
                return;
            }
            Logger.Info($"Attempting to gracefully stop server process {ServerProcess.Id}");
            try {
                Rcon?.SendCommand("exit").Wait(1000);
                Logger.Debug("Sent exit command via RCON");
            }
            catch (Exception ex) {
                Logger.Error($"Failed to send exit RCON to process: {ex.Message}");
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
                    Logger.Info($"Server process {ServerProcess.Id} terminated successfully");
                }
                catch (Exception ex) {
                    Logger.Error($"Failed to kill server process: {ex.Message}");
                }
            }
            ServerProcess.WaitForExit();
            ServerProcess.Close();
            ServerProcess.Dispose();
        }

        protected virtual void Dispose(bool disposing) {
            if (!_disposedValue) {
                if (disposing) {
                    Logger.Info("Disposing Chivalry2Server");
                    KillProcess();
                    RegistrationHandler.StopEventLoop().Wait();
                    Logger.Debug("Chivalry2Server disposal complete");
                }

                _disposedValue = true;
            }
        }

        public void Dispose() {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}