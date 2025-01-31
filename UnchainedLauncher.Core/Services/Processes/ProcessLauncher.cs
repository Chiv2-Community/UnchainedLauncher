using LanguageExt;
using LanguageExt.Common;
using log4net;
using System.Diagnostics;

namespace UnchainedLauncher.Core.Services.Processes {
    using static LanguageExt.Prelude;

    public interface IProcessLauncher {
        public Either<LaunchFailed, Process> Launch(string workingDirectory, string args);
    }

    public class PowershellProcessLauncher : IProcessLauncher {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(PowershellProcessLauncher));
        public string Tag { get; }
        public PowershellProcessLauncher(string tag) {
            Tag = tag;
        }

        public Either<LaunchFailed, Process> Launch(string workingDirectory, string args) {
            try {
                return Right(PowerShell.Run(new List<string> { $"Read-Host -Prompt \'{Tag} args: ({args}). Press enter to close\'" }, true));
            }
            catch (Exception e) {
                return Left(new LaunchFailed("powershell.exe", args, e));
            }
        }
    }

    public record LaunchFailed(string ExecutablePath, string Args, Error Underlying)
        : Expected($"Failed to launch executable '{ExecutablePath}' with args '{Args}'", 0, Underlying);


    /// <summary>
    /// Launches an executable with the provided working directory and DLLs to inject.
    /// </summary>
    public class ProcessLauncher : IProcessLauncher {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(ProcessLauncher));

        public string ExecutableLocation { get; }

        public ProcessLauncher(string executableLocation) {
            ExecutableLocation = executableLocation;
        }

        /// <summary>
        /// Creates a new process with the provided arguments and injects the DLLs.
        /// </summary>
        /// <param name="workingDirectory"></param>
        /// <param name="args"></param>
        /// 
        /// <returns>
        /// The process that was created.
        /// </returns>
        public Either<LaunchFailed, Process> Launch(string workingDirectory, string args) {
            var proc = new Process {
                StartInfo = new ProcessStartInfo() {
                    FileName = ExecutableLocation,
                    Arguments = args,
                    WorkingDirectory = Path.GetFullPath(workingDirectory),
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                }
            };
            // Execute the process
            try {
                proc.Start();
                proc.OutputDataReceived += (sender, e) => {
                    if (e.Data != null) {
                        Logger.Info("Stdout: " + e.Data);
                    }
                };

                proc.ErrorDataReceived += (sender, e) => {
                    if (e.Data != null) {
                        Logger.Error("Stderr: " + e.Data);
                    }
                };

                proc.BeginErrorReadLine();
                proc.BeginOutputReadLine();

                return Right(proc);
            }
            catch (Exception e) {
                return Left(new LaunchFailed(proc.StartInfo.FileName, proc.StartInfo.Arguments, e));
            }
        }
    }

}