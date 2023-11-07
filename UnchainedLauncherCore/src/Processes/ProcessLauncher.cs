using LanguageExt;
using LanguageExt.ClassInstances.Pred;
using log4net;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using UnchainedLauncher.Core.Processes;

namespace UnchainedLauncher.Core.Processes
{
    /// <summary>
    /// Launches an executable with the provided working directory and DLLs to inject.
    /// </summary>
    public class ProcessLauncher {
        public static ILog logger = LogManager.GetLogger(nameof(ProcessLauncher));

        public string ExecutableLocation { get; }

        public string WorkingDirectory { get; }

        public IEnumerable<string>? Dlls { get; set; }

        public Option<RestartPolicy> RestartPolicy { get; }

        public ProcessLauncher(string executableLocation, string workingDirectory, Option<RestartPolicy> restartPolicy, IEnumerable<string>? dlls = null) {
            ExecutableLocation = executableLocation;
            WorkingDirectory = workingDirectory;
            Dlls = dlls;
            RestartPolicy = restartPolicy;
        }

        /// <summary>
        /// Creates a new process with the provided arguments and injects the DLLs.
        /// Retries the process according to the retry policy. Performs no retries if no retry policy is provided.
        /// </summary>
        /// <param name="args"></param>
        /// <returns>
        /// The process that was created.
        /// </returns>
        public void Launch(string args) {
            Process runAndInject() {
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
                    throw new LaunchFailedException(proc.StartInfo.FileName, proc.StartInfo.Arguments, e);
                }

                // If dlls are present inject them
                if (Dlls != null && Dlls.Any()) {
                    try {
                        Inject.InjectAll(proc, Dlls);
                    } catch (Exception e) {
                        throw new InjectionFailedException(Dlls, e);
                    }
                }

                return proc;
            }

            var retries = 0ul;

            Func<bool> keepGoing = () =>
                RestartPolicy
                    .Map(p =>
                        p.MaxAttempts.Match(
                            Some: maxAttempts => maxAttempts < retries,
                            None: true
                        )
                    )
                    .FirstOrDefault(retries == 0);


            while (keepGoing()) {
                var proc = runAndInject();
                proc.WaitForExit();
                var exitCode = proc.ExitCode;

                var shouldRestart = 
                    RestartPolicy
                        .Map(policy => policy.ShouldRestart(exitCode))
                        .FirstOrDefault(false);

                if (!shouldRestart)
                    break;

                var delay = RestartPolicy.Map(policy => policy.DelayMs).IfNone(0);

                logger.Info($"Restarting process in {delay / 1000}seconds. Exit code: {exitCode}");
                Thread.Sleep(delay);

                retries++;
            }
        }
    }

    class LaunchFailedException : Exception {
        public string ExecutablePath { get; }
        public string Args { get; }
        public Exception Underlying { get; }

        public LaunchFailedException(string executablePath, string args, Exception underlying) : base($"Failed to launch executable '{executablePath} {args}'\n\n{underlying.Message}") {
            ExecutablePath = executablePath;
            Args = args;
            Underlying = underlying;
        }
    }

    class InjectionFailedException : Exception {
        public IEnumerable<string> DllPaths { get; }
        public Exception Underlying { get; }

        public InjectionFailedException(IEnumerable<string> dllPaths, Exception underlying) : base($"Failed to inject DLLs '{dllPaths.Aggregate((l, r) => l + ", " + r)}'\n\n{underlying.Message}") {
            DllPaths = dllPaths;
            Underlying = underlying;
        }
    }

    public record RestartPolicy(Option<ulong> MaxAttempts, int DelayMs, Func<int, bool> ShouldRestart);
}
