using LanguageExt;
using LanguageExt.ClassInstances;
using System.Security.Cryptography;

namespace UnchainedLauncher.Core.Utilities {
    public delegate Task<TOutput> KleisliTask<TInput, TOutput>(TInput input);

    public static class KleisliTask {
        public static KleisliTask<TInput, TOutput> From<TInput, TOutput>(Func<TInput, TOutput> f) =>
            a => Task.FromResult(f(a));

        
        /// <summary>
        /// Runs all provided tasks in parallel with the same input.
        ///
        /// The order of TOutput corresponds to the order of paramaters.
        /// </summary>
        /// <param name="launchPreparers"></param>
        /// <typeparam name="TInput"></typeparam>
        /// <typeparam name="TOutput"></typeparam>
        /// <returns></returns>
        public static KleisliTask<TInput, TOutput[]> Parallel<TInput, TOutput>(params KleisliTask<TInput, TOutput>[] fs) => 
            a => Task.WhenAll(fs.Map(f => f(a)));
        
        /// <summary>
        /// Runs all provided tasks sequentially with the same input.
        ///
        /// The order of TOutput corresponds to the order of paramaters.
        /// </summary>
        /// <param name="launchPreparers"></param>
        /// <typeparam name="TInput"></typeparam>
        /// <typeparam name="TOutput"></typeparam>
        /// <returns></returns>
        public static KleisliTask<TInput, TOutput[]> Sequential<TInput, TOutput>(params KleisliTask<TInput, TOutput>[] launchPreparers) =>
            a =>
                launchPreparers
                    .Map(f => f(a))
                    .TraverseSerial(x => x)
                    .Map(x => x.ToArray());
    }

    public static class KleisliTaskExtensions {
        public static KleisliTask<TInput, TNewOutput> AndThen<TInput, TOutput, TNewOutput>(
            this KleisliTask<TInput, TOutput> f, KleisliTask<TOutput, TNewOutput> g) =>
            a => f(a).Bind(b => g(b));
        
        public static KleisliTask<TNewInput, TOutput> Compose<TInput, TOutput, TNewInput>(
            this KleisliTask<TInput, TOutput> f, Func<TNewInput, TInput> g) =>
            a => f(g(a));

        public static KleisliTask<TInput, TNewOutput> Bind<TInput, TOutput, TNewOutput>(
            this KleisliTask<TInput, TOutput> f, KleisliTask<TOutput, TNewOutput> g) =>
            f.AndThen(g);

        public static KleisliTask<TInput, TNewOutput> Map<TInput, TOutput, TNewOutput>(
            this KleisliTask<TInput, TOutput> f, Func<TOutput, TNewOutput> g) =>
            a => f(a).Map(b => g(b));

        public static KleisliTask<TNewInput, TOutput> ContraMap<TInput, TOutput, TNewInput>(
            this KleisliTask<TInput, TOutput> f, Func<TNewInput, TInput> g) => Compose(f, g);
    }
}