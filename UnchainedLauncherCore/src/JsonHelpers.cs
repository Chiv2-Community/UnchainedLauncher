using log4net;

namespace UnchainedLauncherCore {
    static class JsonHelpers {
        private static readonly ILog logger = LogManager.GetLogger(nameof(JsonHelpers));
        /// <summary>
        /// Returns a composable deserialization result with the result and exception.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        public static DeserializationResult<T> Deserialize<T>(string json) {
            try {
                var result = Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
                return new DeserializationResult<T>(result, null);
            } catch (Newtonsoft.Json.JsonSerializationException e) {
                return new DeserializationResult<T>(default, e);
            }
        }
    }

    record DeserializationResult<T>(T? Result, Exception? Exception) {
        public bool Success => Result != null;

        /// <summary>
        /// If !Successful, run the function and return the result. 
        /// Combining the errors if this function also fails.
        /// </summary>
        /// <param name="recover"></param>
        /// <returns></returns>
        public DeserializationResult<T> RecoverWith(Func<Exception?, DeserializationResult<T>> recover) {
            if (Success) {
                return this;
            }

            var result = recover(this.Exception);

            result = this.CombineErrors(result);

            return result;
        }

        /// <summary>
        /// Functor map. Run a function to transform the result if successful.
        /// </summary>
        /// <typeparam name="T2"></typeparam>
        /// <param name="func"></param>
        /// <returns></returns>
        public DeserializationResult<T2> Select<T2>(Func<T, T2> func) {
            if (Success) {
                return new DeserializationResult<T2>(func(Result!), Exception);
            }

            return new DeserializationResult<T2>(default, Exception);
        }

        private DeserializationResult<T> CombineErrors(DeserializationResult<T> other) {
            Exception? e = null;

            if (other.Exception != null && Exception != null) {
                e = new AggregateException(
                    "Failed to deserialize either of the provided types",
                    new List<Exception>() { other.Exception, Exception }
                );
            } else if (other.Exception != null) {
                e = other.Exception;
            } else if (Exception != null) {
                e = other.Exception;
            }

            return new DeserializationResult<T>(Success ? Result : other.Result, e);
        }
    }
}
