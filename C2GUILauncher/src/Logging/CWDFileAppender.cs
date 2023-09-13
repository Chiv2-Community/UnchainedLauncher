using log4net.Appender;
using System.IO;

namespace C2GUILauncher.Logging {
    class CWDFileAppender : FileAppender {
        public override string File {
            set {
                base.File = Path.Combine(Directory.GetCurrentDirectory(), value);
            }
        }
    }
}
