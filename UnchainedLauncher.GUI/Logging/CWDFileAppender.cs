using log4net.Appender;
using System.IO;

namespace UnchainedLauncher.GUI.Logging {
    class CWDFileAppender : FileAppender {
        public override string? File {
            set => base.File = value == null ? null : Path.Combine(Directory.GetCurrentDirectory(), value);
        }
    }
}