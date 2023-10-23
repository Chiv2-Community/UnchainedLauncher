using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using UnchainedLauncherCore.src.Processes;

namespace UnchainedLauncher.Core.Processes
{
    /// <summary>
    /// Launches an executable with the provided working directory and DLLs to inject.
    /// </summary>
    public class ProcessLauncher {

        [MemberNotNull]
        public string ExecutableLocation { get; }

        [MemberNotNull]
        public string WorkingDirectory { get; }

        public IEnumerable<string>? Dlls { get; set; }

        public ProcessLauncher(string executableLocation, string workingDirectory, IEnumerable<string>? dlls = null) {
            ExecutableLocation = executableLocation;
            WorkingDirectory = workingDirectory;
            Dlls = dlls;
        }

        /// <summary>
        /// Creates a new process with the provided arguments and injects the DLLs.
        /// </summary>
        /// <param name="args"></param>
        /// <returns>
        /// The process that was created.
        /// </returns>
        public Process Launch(string args) {

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
}
