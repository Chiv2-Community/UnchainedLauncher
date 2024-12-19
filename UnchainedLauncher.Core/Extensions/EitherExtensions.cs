using LanguageExt;
using LanguageExt.Common;

namespace UnchainedLauncher.Core.Extensions {
    public static class UnchainedEitherExtensions {
        public static EitherAsync<Error, T> AttemptAsync<T>(Task<T> action) {
            return Prelude.TryAsync(action).ToEither();
        }

        public static EitherAsync<Error, T> AttemptAsync<T>(Func<T> action) {
            return Prelude.Try(action).ToAsync().ToEither();
        }

        public static EitherAsync<Error, Unit> AttemptAsync(Action action) {
            return Prelude.Try(() => {
                action.Invoke();
                return default(Unit);
            }).ToAsync().ToEither();
        }

        public static Either<Exception, T> Attempt<T>(Func<T> action) {
            return Prelude.Try(action).ToEither();
        }

        public static Either<Exception, Unit> Attempt<T>(Action action) {
            return Prelude.Try(() => {
                action.Invoke();
                return default(Unit);
            }).ToEither();
        }

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