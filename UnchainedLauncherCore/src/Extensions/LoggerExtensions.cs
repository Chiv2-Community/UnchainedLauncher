using LanguageExt;
using log4net;

namespace UnchainedLauncher.Core.Extensions
{
    public static class LoggerExtensions
    {
        public static void LogListInfo<T>(this ILog logger, string initialMessage, IEnumerable<T> list)
        {
            logger.Info("");
            logger.Info(initialMessage);
            foreach (var item in list)
            {
                logger.Info("\t" + (item?.ToString() ?? "null"));
            }
            logger.Info("");
        }

        public static Unit InfoUnit(this ILog logger, string message)
        {
            logger.Info(message);
            return Unit.Default;
        }

        public static Unit WarnUnit(this ILog logger, string message)
        {
            logger.Warn(message);
            return Unit.Default;
        }

        public static Unit ErrorUnit(this ILog logger, string message)
        {
            logger.Error(message);
            return Unit.Default;
        }

        public static Unit FatalUnit(this ILog logger, string message)
        {
            logger.Fatal(message);
            return Unit.Default;
        }

        public static Unit DebugUnit(this ILog logger, string message)
        {
            logger.Debug(message);
            return Unit.Default;
        }

        public static Unit InfoUnit(this ILog logger, string message, Exception exception)
        {
            logger.Info(message, exception);
            return Unit.Default;
        }

        public static Unit WarnUnit(this ILog logger, string message, Exception exception)
        {
            logger.Warn(message, exception);
            return Unit.Default;
        }

        public static Unit ErrorUnit(this ILog logger, string message, Exception exception)
        {
            logger.Error(message, exception);
            return Unit.Default;
        }

        public static Unit FatalUnit(this ILog logger, string message, Exception exception)
        {
            logger.Fatal(message, exception);
            return Unit.Default;
        }

        public static Unit DebugUnit(this ILog logger, string message, Exception exception)
        {
            logger.Debug(message, exception);
            return Unit.Default;
        }

    }
}
