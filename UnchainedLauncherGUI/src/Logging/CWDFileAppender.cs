using log4net.Appender;
using System.IO;

namespace UnchainedLauncherGUI.Logging {
    class CWDFileAppender : FileAppender {
        public override string File {
            set {
                base.File = Path.Combine(Directory.GetCurrentDirectory(), value);
            }
        }
    }
}
