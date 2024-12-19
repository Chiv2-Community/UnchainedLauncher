using LanguageExt;

namespace UnchainedLauncher.Core.Extensions {
    public static class PrimitiveExtensions {
        public static T Match<T>(this bool b, Func<T> True, Func<T> False) {
            return b ? True() : False();
        }

        public static Either<L, R> ToEither<L, R>(this bool b, Func<L> False, Func<R> True) {
            return b ? Prelude.Right(True()) : Prelude.Left(False());
        }

        public static EitherAsync<L, R> ToEitherAsync<L, R>(this bool b, Func<Task<L>> False, Func<Task<R>> True) {
            return b ? Prelude.RightAsync<L, R>(True()) : Prelude.LeftAsync<L, R>(False());
        }
    }
}