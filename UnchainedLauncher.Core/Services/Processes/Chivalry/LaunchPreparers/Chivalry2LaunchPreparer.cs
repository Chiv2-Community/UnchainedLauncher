using LanguageExt;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry.LaunchPreparers {

    public static class Chivalry2LaunchPreparer {
        public static IChivalry2LaunchPreparer<T> Create<T>(Func<T, Task<Option<T>>> f) =>
            new FunctionalChivalry2LaunchPreparer<T>(f);

        public static IChivalry2LaunchPreparer<T> Create<T>(Func<T, Option<T>> f) =>
            new FunctionalChivalry2LaunchPreparer<T>(t => Task.FromResult(f(t)));
        
        public static IChivalry2LaunchPreparer<T> Noop<T>() =>
            new FunctionalChivalry2LaunchPreparer<T>(t => Task.FromResult(Some(t)));

        public static IChivalry2LaunchPreparer<T> IgnoreOptions<T>(this IChivalry2LaunchPreparer<Unit> u) {
            return Create<T>(
                async opt => await u.PrepareLaunch(Unit.Default).Map(_ => opt)
            );
        }
    }

    /// <summary>
    /// Performs tasks that sets up a proper launch
    /// </summary>
    public interface IChivalry2LaunchPreparer<T> {
        /// <summary>
        /// Runs the preparations
        /// </summary>
        /// <returns>a Task containing None when preparations fail. Some(T) when successful.</returns>
        public Task<Option<T>> PrepareLaunch(T options);

        public IChivalry2LaunchPreparer<T> AndThen(IChivalry2LaunchPreparer<T> otherLaunchPreparer) {
            return Chivalry2LaunchPreparer.Create<T>(opts => {
                var result =
                    from modifiedOpts in OptionalAsync(this.PrepareLaunch(opts))
                    from finalOpts in OptionalAsync(otherLaunchPreparer.PrepareLaunch(opts))
                    select finalOpts;

                return result.Value;
            });
        }

        /// <summary>
        /// Allows for running some other IChivalry2LaunchPreparer of a different type
        /// </summary>
        /// <param name="other"></param>
        /// <param name="map"></param>
        /// <typeparam name="T2"></typeparam>
        /// <returns>If the other launch preparer returns None then the resulting launch preparer will also return None, otherwise it'll return Some containing the original output.</returns>
        public IChivalry2LaunchPreparer<T> Sub<T2>(IChivalry2LaunchPreparer<T2> other, Func<T, T2> map) =>
            AndThen(t => other.PrepareLaunch(map(t)).Map(x => x.Map(_ => t)));

        /// <summary>
        /// Allows for running some other IChivalry2LaunchPreparer of a different type
        /// </summary>
        /// <param name="other"></param>
        /// <param name="map"></param>
        /// <typeparam name="T2"></typeparam>
        /// <returns>If the other launch preparer returns None then the resulting launch preparer will also return None, otherwise it'll return Some containing the original output.</returns>
        public IChivalry2LaunchPreparer<T> Sub(IChivalry2LaunchPreparer<Unit> other) =>
            Sub(other, _ => Unit.Default);

        /// <summary>
        /// Allows for running some other IChivalry2LaunchPreparer of a different type
        /// </summary>
        /// <param name="other"></param>
        /// <param name="toT2"></param>
        /// <param name="toT"></param>
        /// <typeparam name="T2"></typeparam>
        /// <returns>If the other launch preparer returns None, the resulting launch preparer will also return None. Otherwise it'll return Some containing the converted output.</returns>
        public IChivalry2LaunchPreparer<T> Sub<T2>(
            IChivalry2LaunchPreparer<T2> other,
            Func<T, T2> toT2,
            Func<T2, T> toT) =>
            AndThen(other.InvariantMap(toT2, toT));


        public IChivalry2LaunchPreparer<T> AndThen(Func<T, Task<Option<T>>> f) => AndThen(Chivalry2LaunchPreparer.Create(f));
        public IChivalry2LaunchPreparer<T> Bind(IChivalry2LaunchPreparer<T> launchPreparer) => AndThen(launchPreparer);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="toT"></param>
        /// <param name="fromT"></param>
        /// <typeparam name="T2"></typeparam>
        /// <returns></returns>
        public IChivalry2LaunchPreparer<T2> InvariantMap<T2>(Func<T2, T> toT, Func<T, T2> fromT) =>
            Chivalry2LaunchPreparer.Create<T2>(async t2 => (await PrepareLaunch(toT(t2))).Map(fromT));
    }

    public class FunctionalChivalry2LaunchPreparer<T> : IChivalry2LaunchPreparer<T> {
        private readonly Func<T, Task<Option<T>>> _f;

        public FunctionalChivalry2LaunchPreparer(Func<T, Task<Option<T>>> func) {
            _f = func;
        }
        public Task<Option<T>> PrepareLaunch(T options) => _f(options);
    }
}