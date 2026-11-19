using LanguageExt;
using LanguageExt.Common;
using log4net;
using System.Diagnostics;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.Core.Services.Processes {
    using static LanguageExt.Prelude;

    public interface IProcessLauncher {
        string Executable { get; }
        public Either<LaunchFailed, Process> Launch(string workingDirectory, string args);

        // Diagnostic launchers and test doubles do not own a suspended native thread.
        public Either<LaunchFailed, Process> Launch(string workingDirectory, string args, Func<Process, bool> initialize) =>
            Launch(workingDirectory, args).Bind(process => ProcessLauncher.Initialize(process, initialize, Executable, args));
    }

    public class PowershellProcessLauncher : IProcessLauncher {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(PowershellProcessLauncher));
        public string Tag { get; }
        public string Executable => "powershell.exe";
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

        public string Executable { get; }
        private readonly ProcessOutputCapture? _output;

        public ProcessLauncher(string executable) : this(executable, null) { }

        public ProcessLauncher(string executable, ProcessOutputCapture? output) {
            Executable = executable;
            _output = output;
        }

        public Either<LaunchFailed, Process> Launch(string workingDirectory, string args, Func<Process, bool> initialize) {
            if (OperatingSystem.IsWindows())
                return SuspendedProcessLauncher.Launch(Executable, workingDirectory, args, initialize, _output);
            // Game launching is Windows-only; retain portable argument-probe support.
            return Launch(workingDirectory, args).Bind(process => Initialize(process, initialize, Executable, args));
        }

        internal static Either<LaunchFailed, Process> Initialize(Process process, Func<Process, bool> initialize,
            string executable, string args) {
            try {
                if (initialize(process)) return Right(process);
                throw new InvalidOperationException("DLL injection failed.");
            }
            catch (Exception error) {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception cleanupError) { Logger.Error("Could not terminate failed launch.", cleanupError); }
                finally { process.Dispose(); }
                return Left(new LaunchFailed(executable, args, error));
            }
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
            if (_output != null && OperatingSystem.IsWindows())
                return SuspendedProcessLauncher.Launch(Executable, workingDirectory, args, _ => true, _output);
            var proc = new Process {
                StartInfo = new ProcessStartInfo() {
                    FileName = Executable,
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