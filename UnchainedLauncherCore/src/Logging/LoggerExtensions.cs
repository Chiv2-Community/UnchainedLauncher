using log4net;

namespace UnchainedLauncherCore.Logging {
    public static class LoggerExtensions {
        public static void LogListInfo<T>(this ILog logger, string initialMessage, IEnumerable<T> list) {
            logger.Info("");
            logger.Info(initialMessage);
            foreach (var item in list) {
                logger.Info("\t" + (item?.ToString() ?? "null"));
            }
            logger.Info("");
        }
    }
}
