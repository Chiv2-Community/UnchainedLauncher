namespace UnchainedLauncher.Core.Utilities {
    /// <summary>
    /// Holds a reference to the UI thread's SynchronizationContext for use throughout the application.
    /// Must be initialized from the UI thread at application startup.
    /// </summary>
    public static class UISynchronizationContext {
        private static SynchronizationContext? _context;

        /// <summary>
        /// Gets the UI SynchronizationContext. Returns null if not initialized.
        /// </summary>
        public static SynchronizationContext? Context => _context;

        /// <summary>
        /// Initializes the UI SynchronizationContext. Should be called once from the UI thread at application startup.
        /// </summary>
        public static void Initialize() {
            _context = SynchronizationContext.Current;
        }
    }
}