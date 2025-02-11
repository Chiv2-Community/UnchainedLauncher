using LanguageExt;

namespace UnchainedLauncher.Core.Extensions {
    using static LanguageExt.Prelude;
    public static class PrimitiveExtensions {

        public static T Match<T>(this bool b, Func<T> trueFunc, Func<T> falseFunc) {
            return b ? trueFunc() : falseFunc();
        }

        public static Either<TL, TR> ToEither<TL, TR>(this bool b, Func<TL> falseFunc, Func<TR> trueFunc) {
            return b ? Prelude.Right(trueFunc()) : Prelude.Left(falseFunc());
        }

        public static EitherAsync<TL, TR> ToEitherAsync<TL, TR>(this bool b, Func<Task<TL>> falseFunc, Func<Task<TR>> trueFunc) {
            return b ? Prelude.RightAsync<TL, TR>(trueFunc()) : Prelude.LeftAsync<TL, TR>(falseFunc());
        }

        public static Try<Unit> TryVoid(Action a) => Try(() => {
            a();
            return Unit.Default;
        });
    }
}