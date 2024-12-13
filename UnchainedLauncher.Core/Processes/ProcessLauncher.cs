using LanguageExt;
using log4net;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnchainedLauncher.Core.Extensions;

namespace UnchainedLauncher.Core.Processes
{
    using static LanguageExt.Prelude;

    public interface IProcessLauncher
    {
        public Either<ProcessLaunchFailure, Process> Launch(string workingDirectory, string args);
    }
    
    /// <summary>
    /// Launches an executable with the provided working directory and DLLs to inject.
    /// </summary>
    public class ProcessLauncher: IProcessLauncher {
        private static readonly ILog logger = LogManager.GetLogger(nameof(ProcessLauncher));

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
        public Either<ProcessLaunchFailure, Process> Launch(string workingDirectory, string args) {
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
                proc.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        logger.Info("Stdout: " + e.Data);
                    }
                };

                proc.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null) {
                        logger.Error("Stderr: " + e.Data);
                    }
                };

                proc.BeginErrorReadLine();
                proc.BeginOutputReadLine();

                return Right(proc);
            } catch (Exception e) {
                return Left(ProcessLaunchFailure.LaunchFailed(proc.StartInfo.FileName, proc.StartInfo.Arguments, e));
            }
        }
    }

    public abstract record ProcessLaunchFailure {
        private ProcessLaunchFailure() { }
        public static ProcessLaunchFailure LaunchFailed(string executablePath, string args, Exception underlying) => new LaunchFailedError(executablePath, args, underlying);
        
        // TODO: Move this error type to Chivalry2 Launcher
        public static ProcessLaunchFailure InjectionFailed(Option<IEnumerable<string>> dllPaths, Exception underlying) => new InjectionFailedError(dllPaths, underlying);


        public record LaunchFailedError(string ExecutablePath, string Args, Exception Underlying) : ProcessLaunchFailure;
        
        // TODO: Move this error type to Chivalry2 Launcher
        public record InjectionFailedError(Option<IEnumerable<string>> DllPaths, Exception Underlying) : ProcessLaunchFailure;

        public T Match<T>(
                    Func<LaunchFailedError, T> LaunchFailedError,
                    Func<InjectionFailedError, T> InjectionFailedError
                   ) => this switch {
            LaunchFailedError launchFailed => LaunchFailedError(launchFailed),
            InjectionFailedError injectionFailed => InjectionFailedError(injectionFailed),
            _ => throw new Exception("Unreachable")
        };

        public void Match(
                       Action<LaunchFailedError> LaunchFailedError,
                                  Action<InjectionFailedError> InjectionFailedError
                   ) {
            Match<Unit>(
                launchFailed => {
                    LaunchFailedError(launchFailed);
                    return default;
                },
                injectionFailed => {
                    InjectionFailedError(injectionFailed);
                    return default;
                }
            );

        }
    }
}
