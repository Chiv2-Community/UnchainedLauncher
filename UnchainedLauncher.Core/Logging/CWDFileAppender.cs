using log4net.Appender;

namespace UnchainedLauncher.Core.Logging
{
    class CWDFileAppender : FileAppender
    {
        public override string? File {
            set => base.File = value == null ? null : Path.Combine(Directory.GetCurrentDirectory(), value);
        }
    }
}
