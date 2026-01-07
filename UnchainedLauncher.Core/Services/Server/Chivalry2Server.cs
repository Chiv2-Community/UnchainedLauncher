using log4net;
using PropertyChanged;
using System.Diagnostics;
using UnchainedLauncher.Core.Services.Processes.Chivalry;
using UnchainedLauncher.Core.Services.Server.A2S;

namespace UnchainedLauncher.Core.Services.Server {
    [AddINotifyPropertyChangedInterface]
    public class Chivalry2Server(Process serverProcess, ServerLaunchOptions launchOpts, IA2S a2S, IRCON rcon)
        : IDisposable {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(Chivalry2Server));
        private bool _disposedValue;
        public Process ServerProcess { get; private set; } = serverProcess;
        public IA2S A2S { get; private set; } = a2S;
        public ServerLaunchOptions LaunchOptions { get; set; } = launchOpts;
        public IRCON RCON { get; set; } = rcon;

        private void KillProcess() {
            if (ServerProcess.HasExited) {
                Logger.Debug($"Server process {ServerProcess.Id} has already exited");
                return;
            }
            Logger.Info($"Attempting to gracefully stop server process {ServerProcess.Id}");
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
            ServerProcess.WaitForExit();
            ServerProcess.Close();
            ServerProcess.Dispose();
        }

        protected virtual void Dispose(bool disposing) {
            if (!_disposedValue) {
                if (disposing) {
                    Logger.Info("Disposing Chivalry2Server");
                    KillProcess();
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