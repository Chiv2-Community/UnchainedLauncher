using log4net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnchainedLauncherGUI {
    public static class PowerShell {
        private static readonly ILog logger = LogManager.GetLogger(nameof(PowerShell));
        public static Process Run(IEnumerable<string> commands, bool createWindow = false) {
            var process = new Process();

            logger.Info("Running powershell command:");
            foreach (var command in commands) {
                logger.Info("    " + command);
            }

            var commandString = commands.Aggregate("", (acc, elem) => acc + elem + "; \n");
            try {
                process.StartInfo.FileName = "powershell.exe";
                process.StartInfo.Arguments = $"-Command \"{commandString}\"";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.CreateNoWindow = !createWindow;
                process.Start();
            } catch (Exception e) {
                logger.Error($"Failed to execute powershell command.", e);
                throw;
            }

            return process;
        }
    }
}
