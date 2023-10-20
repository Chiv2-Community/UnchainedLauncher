using LanguageExt;


namespace UnchainedLauncher.Core.LanguageExt {
    public static class EitherExtensions {
        public static EitherAsync<L, R> Tap<L, R>(this EitherAsync<L, R> either, Action<R> f) {
            return either.Map(r => {
                f(r);
                return r;
            });
        }

        public static EitherAsync<L, R> TapRight<L, R, R2>(this EitherAsync<L, R> either, Action<R> f) {
            return Tap(either, f);
        }

        public static EitherAsync<L, R> TapLeft<L, R, L2>(this EitherAsync<L, R> either, Action<L> f) {
            return either.MapLeft(l => {
                f(l);
                return l;
            });
        }

        public static EitherAsync<L, R> BindTap<L, R, R2>(this EitherAsync<L, R> either, Func<R, EitherAsync<L, R2>> f) {
            return either.Bind(r => {
                return f(r).Map(_ => r);
            });
        }
        
        public static EitherAsync<L, R> BindTapRight<L, R, R2>(this EitherAsync<L, R> either, Func<R, EitherAsync<L, R2>> f) {
            return BindTap(either, f);
        }

        public static EitherAsync<L, R> BindTapLeft<L, R, L2>(this EitherAsync<L, R> either, Func<L, EitherAsync<L2, R>> f) {
            return either.BindLeft(l => {
                return f(l).MapLeft(_ => l);
            });
        }
    }
}
