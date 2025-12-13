using LanguageExt;
using LanguageExt.Common;
using log4net;
using System.Diagnostics;
using UnchainedLauncher.Core.Utilities;

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
                var pakdir = FilePaths.PakDir;
                var plugindir = FilePaths.PluginDir;
                return Right(PowerShell.Run(new List<string> {
                    $"if(Test-Path {pakdir}){{Get-ChildItem -Path \'{pakdir}\'}}",
                    $"if(Test-Path {plugindir}){{Get-ChildItem -Path \'{plugindir}\'}}",
                    $"Read-Host -Prompt \'{Tag} args: ({args}). Press enter to close\'"
                }, true));
            }
            catch (Exception e) {
                return Left(new LaunchFailed("powershell.exe", args, e));
            }
        }
    }

    public class LaunchFailed(string executablePath, string args, Error underlying)
        : Exception($"Failed to launch executable '{executablePath}' with args '{args}'", underlying) {
        public Error Underlying { get; } = underlying;
        public string Args { get; } = args;
        public string ExecutablePath { get; } = executablePath;
    }


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
                }
            };
            try {
                proc.Start();
                proc.EnableRaisingEvents = true;
                return Right(proc);
            }
            catch (Exception e) {
                return Left(new LaunchFailed(proc.StartInfo.FileName, proc.StartInfo.Arguments, e));
            }
        }
    }

}