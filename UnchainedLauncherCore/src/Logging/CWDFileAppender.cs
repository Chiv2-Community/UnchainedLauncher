using log4net.Appender;

namespace UnchainedLauncher.Core.Logging
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
