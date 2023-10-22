using log4net.Appender;

namespace UnchainedLauncherCore.src.Logging
{
    class CWDFileAppender : FileAppender
    {
        public override string File
        {
            set
            {
                base.File = Path.Combine(Directory.GetCurrentDirectory(), value);
            }
        }
    }
}
