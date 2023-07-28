#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Reloaded.Injector;
using System.IO;
using System.Diagnostics.CodeAnalysis;

namespace C2GUILauncher
{
    class ProcessLauncher
    {

        [MemberNotNull]
        public string ExecutableLocation { get; }

        [MemberNotNull]
        public string WorkingDirectory { get; }

        public IEnumerable<string>? Dlls { get; }

        public ProcessLauncher(string executableLocation, string workingDirectory, IEnumerable<string>? dlls = null)
        {
            this.ExecutableLocation = executableLocation;
            this.WorkingDirectory = workingDirectory;
            this.Dlls = dlls;
        }

        public Process Launch(string args)
        {
            var proc = new Process();

            proc.StartInfo = new ProcessStartInfo()
            {
                FileName = this.ExecutableLocation,
                Arguments = args,
                WorkingDirectory = Path.GetFullPath(this.WorkingDirectory),
            };

            proc.Start();

            if(this.Dlls != null)
            {
                using (var injector = new Injector(proc))
                {
                    foreach (var dll in this.Dlls)
                    {
                        injector.Inject(dll);
                    }
                }
            }

            return proc;
        }
    }

    

}
