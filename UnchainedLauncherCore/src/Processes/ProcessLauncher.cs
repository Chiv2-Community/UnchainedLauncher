using LanguageExt;
using log4net;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnchainedLauncher.Core.Extensions;

namespace UnchainedLauncher.Core.Processes
{
    /// <summary>
    /// Launches an executable with the provided working directory and DLLs to inject.
    /// </summary>
    public class ProcessLauncher {
        private static readonly ILog logger = LogManager.GetLogger(nameof(ProcessLauncher));

        public string ExecutableLocation { get; }

        public string WorkingDirectory { get; }

        public Eff<IEnumerable<string>> FetchDLLs { get; }

        public ProcessLauncher(string executableLocation, string workingDirectory, Eff<IEnumerable<String>> fetchDLLs) {
            ExecutableLocation = executableLocation;
            WorkingDirectory = workingDirectory;
            FetchDLLs = fetchDLLs;
        }

        /// <summary>
        /// Creates a new process with the provided arguments and injects the DLLs.
        /// Retries the process according to the retry policy. Performs no retries if no retry policy is provided.
        /// </summary>
        /// <param name="args"></param>
        /// <returns>
        /// The process that was created.
        /// </returns>
        public Either<ProcessLaunchFailure, Process> Launch(string args) {
            // Initialize a process
            var proc = new Process {
                // Build the process start info
                StartInfo = new ProcessStartInfo() {
                    FileName = ExecutableLocation,
                    Arguments = args,
                    WorkingDirectory = Path.GetFullPath(WorkingDirectory),
                }
            };
            // Execute the process
            try {
                proc.Start();
            } catch (Exception e) {
                return Prelude.Left(ProcessLaunchFailure.LaunchFailed(proc.StartInfo.FileName, proc.StartInfo.Arguments, e));
            }

            var dllsResult = FetchDLLs.Run();

            return dllsResult.Match<Either<ProcessLaunchFailure, Process>>(
                Fail: e => Prelude.Left(ProcessLaunchFailure.InjectionFailed(Prelude.None, e)),
                Succ: dlls => {
                    if (dlls.Any()) {
                        try {
                            Inject.InjectAll(proc, dlls);
                        } catch (Exception e) {
                            return Prelude.Left(ProcessLaunchFailure.InjectionFailed(Prelude.Some(dlls), e));
                        }
                    }
                    return Prelude.Right(proc);
                }
            );
        }
    }

    public abstract record ProcessLaunchFailure {
        private ProcessLaunchFailure() { }
        public static ProcessLaunchFailure LaunchFailed(string executablePath, string args, Exception underlying) => new LaunchFailedError(executablePath, args, underlying);
        public static ProcessLaunchFailure InjectionFailed(Option<IEnumerable<string>> dllPaths, Exception underlying) => new InjectionFailedError(dllPaths, underlying);


        public record LaunchFailedError(string ExecutablePath, string Args, Exception Underlying) : ProcessLaunchFailure;
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
