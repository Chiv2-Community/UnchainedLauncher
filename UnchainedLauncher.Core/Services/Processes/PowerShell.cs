using log4net;
using System.Diagnostics;

namespace UnchainedLauncher.Core.Services.Processes {
    public static class PowerShell {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(PowerShell));
        public static Process Run(IEnumerable<string> commands, bool createWindow = false) {
            var process = new Process();

            Logger.Info("Running powershell command:");
            foreach (var command in commands) {
                Logger.Info("    " + command);
            }

            var commandString = commands.Aggregate("", (acc, elem) => acc + elem + "; \n");
            try {
                process.StartInfo.FileName = "powershell.exe";
                process.StartInfo.Arguments = $"-Command \"{commandString}\"";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = !createWindow;
                process.StartInfo.RedirectStandardError = !createWindow;
                process.StartInfo.CreateNoWindow = !createWindow;
                process.Start();
            }
            catch (Exception e) {
                Logger.Error($"Failed to execute powershell command.", e);
                throw;
            }

            return process;
        }
    }
}