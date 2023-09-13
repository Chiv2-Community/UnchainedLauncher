using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace C2GUILauncher {
    static class JsonHelpers {

        /// <summary>
        /// Attempts to deserialize T, if that fails, attempts to deserialize T2, and then convert it to T.
        /// </summary>
        /// <typeparam name="T">The target output type</typeparam>
        /// <typeparam name="T2">An alternate version of the target output type</typeparam>
        /// <param name="json">The json to deserialize</param>
        /// <param name="convert">The function to use to convert T2 to T</param>
        /// <returns>Deserialized T or null</returns>
        /// <exception cref="AggregateException"></exception>
        public static T? CascadeDeserialize<T, T2>(string json, Func<T2, T> convert) {

            var errors = new List<Exception>();

            try {
                T? result = Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);

                if (result != null)
                    return result;
            } catch (Newtonsoft.Json.JsonSerializationException e) {
                errors.Add(e);
            }

            try {
                var v2 = Newtonsoft.Json.JsonConvert.DeserializeObject<T2>(json);
                if (v2 != null)
                    return convert(v2);

            } catch (Newtonsoft.Json.JsonSerializationException e) {
                errors.Add(e);
            }

            if (errors.Count > 0) {
                var message = "Failed to deserialize json:\n";
                foreach (var e in errors) {
                    message += e.Message + "\n";
                }
                throw new AggregateException(message, errors);
            }

            return default;
        }
    }
}
