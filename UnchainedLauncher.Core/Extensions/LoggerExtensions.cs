using LanguageExt;
using log4net;
using log4net.Core;

namespace UnchainedLauncher.Core.Extensions
{
    public static class LoggerExtensions
    {
        public static Unit LogListInfo<T>(this ILog logger, string initialMessage, IEnumerable<T> list)
        {
            return LogList(logger.Info, initialMessage, list);
        }

        public static Unit LogListError<T>(this ILog logger, string initialMessage, IEnumerable<T> list) {
            return LogList(logger.Error, initialMessage, list);
        }

        public static Unit LogListWarn<T>(this ILog logger, string initialMessage, IEnumerable<T> list) {
            return LogList(logger.Warn, initialMessage, list);
        }

        public static Unit LogListDebug<T>(this ILog logger, string initialMessage, IEnumerable<T> list) {
            return LogList(logger.Debug, initialMessage, list);
        }

        public static Unit LogListFatal<T>(this ILog logger, string initialMessage, IEnumerable<T> list) {
            return LogList(logger.Fatal, initialMessage, list);
        }

        private static Unit LogList<T>(Action<string> logFunc, string initialMessage, IEnumerable<T> list) {
            logFunc("");
            logFunc(initialMessage);
            foreach (var item in list) {
                logFunc("\t" + (item?.ToString() ?? "null"));
            }
            logFunc("");
            return default;
        }

        public static Unit InfoUnit(this ILog logger, string message)
        {
            logger.Info(message);
            return default;
        }

        public static Unit WarnUnit(this ILog logger, string message)
        {
            logger.Warn(message);
            return default;
        }

        public static Unit ErrorUnit(this ILog logger, string message)
        {
            logger.Error(message);
            return default;
        }

        public static Unit FatalUnit(this ILog logger, string message)
        {
            logger.Fatal(message);
            return default;
        }

        public static Unit DebugUnit(this ILog logger, string message)
        {
            logger.Debug(message);
            return default;
        }

        public static Unit InfoUnit(this ILog logger, string message, Exception exception)
        {
            logger.Info(message, exception);
            return default;
        }

        public static Unit WarnUnit(this ILog logger, string message, Exception exception)
        {
            logger.Warn(message, exception);
            return default;
        }

        public static Unit ErrorUnit(this ILog logger, string message, Exception exception)
        {
            logger.Error(message, exception);
            return default;
        }

        public static Unit FatalUnit(this ILog logger, string message, Exception exception)
        {
            logger.Fatal(message, exception);
            return default;
        }

        public static Unit DebugUnit(this ILog logger, string message, Exception exception)
        {
            logger.Debug(message, exception);
            return default;
        }

    }
}
