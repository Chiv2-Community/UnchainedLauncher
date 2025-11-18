namespace UnchainedLauncher.Core.Extensions {
    public static class IEnumerableExtensions {
        /**
         * Lazily iterate over each item in the source collection and perform some kind of side effect on each item.
         */
        public static IEnumerable<T> Tap<T>(this IEnumerable<T> source, Action<T> action) {
            foreach (var item in source) {
                action(item);
                yield return item;
            }
        }
        /**
         * Lazily iterate over each item in the source collection and perform some kind of side effect on each item.
         */
        public static IEnumerable<T> Tap<T, T2>(this IEnumerable<T> source, Func<T, T2> func) {
            foreach (var item in source) {
                func(item);
                yield return item;
            }
        }
        
        /**
         * Eagerly iterate over each item in the source collection and perform some kind of side effect on each item.
         */
        public static void ForEach<T>(this IEnumerable<T> source, Action<T> action) {
            foreach (var item in source) {
                action(item);
            }
        }
        /**
         * Eagerly iterate over each item in the source collection and perform some kind of side effect on each item.
         */
        public static void ForEach<T, T2>(this IEnumerable<T> source, Func<T, T2> func) {
            foreach (var item in source) {
                func(item);
            }
        }
    }
}