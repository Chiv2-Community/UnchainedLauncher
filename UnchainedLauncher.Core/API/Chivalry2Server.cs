using PropertyChanged;

namespace UnchainedLauncher.Core.API {
    /// <summary>
    /// A Chivalry 2 Server that should restart and stay registered with a backend all on its own
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class Chivalry2Server : IDisposable {
        private bool disposedValue;

        // TODO: add the game process and re-launch logic here
        // TODO: add RCON endpoint here
        // TODO: add UPnP manager here
        public A2SBoundRegistration RegistrationHandler { get; private set; }
        public Chivalry2Server(A2SBoundRegistration Registration) {
            this.RegistrationHandler = Registration;
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
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