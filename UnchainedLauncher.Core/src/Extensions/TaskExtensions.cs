using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnchainedLauncher.Core.Extensions {
    public static class TaskExtensions {
        public static Task<T> Tap<T>(this Task<T> task, Action<T> f) {
            return task.ContinueWith(t => {
                f(t.Result);
                return t.Result;
            });
        }
    }
}
