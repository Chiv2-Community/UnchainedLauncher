using LanguageExt;
using static LanguageExt.Prelude;

namespace UnchainedLauncher.Core.Services.Processes.Chivalry {

    /// <summary>
    /// Performs tasks that sets up a proper launch
    /// </summary>
    public interface IChivalry2LaunchPreparer<T> {
        /// <summary>
        /// Runs the preparations
        /// </summary>
        /// <returns>a Task containing None when preparations fail. Some(ModdedLaunchOptions) when successful.</returns>
        public Task<Option<T>> PrepareLaunch(T options);

        public static IChivalry2LaunchPreparer<T> Create<T>(Func<T, Task<Option<T>>> f) =>
            new FunctionalChivalry2LaunchPreparer<T>(f);


        public IChivalry2LaunchPreparer<T> AndThen(IChivalry2LaunchPreparer<T> otherLaunchPreparer) {
            return Create<T>(opts => {
                var result = 
                    from modifiedOpts in OptionalAsync(this.PrepareLaunch(opts))
                    from finalOpts in OptionalAsync(otherLaunchPreparer.PrepareLaunch(opts))
                    select finalOpts;
                
                return result.Value;
            });
        }
        
        public IChivalry2LaunchPreparer<T> Sub<T2>(IChivalry2LaunchPreparer<T2> other, Func<T, T2> map) => 
            AndThen(async t => {
                var result = await other.PrepareLaunch(map(t));
                return result.Map(_ => t);
            });

        
        public IChivalry2LaunchPreparer<T> AndThen(Func<T, Task<Option<T>>> f) => AndThen(Create(f));
        public IChivalry2LaunchPreparer<T> Bind(IChivalry2LaunchPreparer<T> launchPreparer) => AndThen(launchPreparer);

        public IChivalry2LaunchPreparer<T2> InvariantMap<T2>(Func<T2, T> toT, Func<T, T2> fromT) =>
            Create<T2>(async t2 => (await PrepareLaunch(toT(t2))).Map(fromT));
    }

    public class FunctionalChivalry2LaunchPreparer<T> : IChivalry2LaunchPreparer<T> {
        private readonly Func<T, Task<Option<T>>> _f;

        public FunctionalChivalry2LaunchPreparer(Func<T, Task<Option<T>>> func) {
            _f = func;
        }
        public Task<Option<T>> PrepareLaunch(T options) => _f(options);
    }
}