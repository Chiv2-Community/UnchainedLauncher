#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Diagnostics.CodeAnalysis;
using System.Windows;

namespace C2GUILauncher
{
    /// <summary>
    /// Launches an executable with the provided working directory and DLLs to inject.
    /// </summary>
    class ProcessLauncher
    {

        [MemberNotNull]
        public string ExecutableLocation { get; }

        [MemberNotNull]
        public string WorkingDirectory { get; }

        public IEnumerable<string>? Dlls { get; set; }

        public ProcessLauncher(string executableLocation, string workingDirectory, IEnumerable<string>? dlls = null)
        {
            this.ExecutableLocation = executableLocation;
            this.WorkingDirectory = workingDirectory;
            this.Dlls = dlls;
        }

        /// <summary>
        /// Creates a new process with the provided arguments and injects the DLLs.
        /// </summary>
        /// <param name="args"></param>
        /// <returns>
        /// The process that was created.
        /// </returns>
        public Process Launch(string args)
        {
            // Initialize a process
            var proc = new Process();

            // Build the process start info
            proc.StartInfo = new ProcessStartInfo()
            {
                FileName = this.ExecutableLocation,
                Arguments = args,
                WorkingDirectory = Path.GetFullPath(this.WorkingDirectory),
            };

            // Execute the process
            proc.Start();
            //Paths to be injected MUST be absolute
            //TODO: put this as an automatic conversion inside the Inject.cs
            Dlls = Dlls.Select(a => Path.GetFullPath(a)); 
            //MessageBox.Show(Dlls.Aggregate((a,b) => a + "\n" + b));
            // If dlls are present inject them
            if(Dlls != null && Dlls.Any()){
                src.Inject.InjectAll(proc, Dlls); //TODO: add error checking
            }

            return proc;
        }
    }
}
