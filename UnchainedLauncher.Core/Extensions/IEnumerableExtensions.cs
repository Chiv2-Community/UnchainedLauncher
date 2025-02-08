using LanguageExt;

namespace UnchainedLauncher.Core.Extensions {
    public static class IEnumerableExtensions {
        public static IEnumerable<T> Tap<T>(this IEnumerable<T> source, Action<T> action) {
            foreach (var item in source) {
                action(item);
                yield return item;
            }
        }

        public static IEnumerable<T> Tap<T, T2>(this IEnumerable<T> source, Func<T, T2> func) {
            foreach (var item in source) {
                func(item);
                yield return item;
            }
        }
        
        public static (IEnumerable<L>, IEnumerable<R>) SplitEithers<L, R>(this IEnumerable<Either<L, R>> el) {
            List<L> ls = new List<L>();
            List<R> rs = new List<R>();
            foreach (var e in el) {
                e.Match((r) => rs.Add(r), (l) => ls.Add(l));
            }
            return (ls, rs);
        }
    }
}