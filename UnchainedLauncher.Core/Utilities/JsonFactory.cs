using LanguageExt;
using static LanguageExt.Prelude;
using UnchainedLauncher.Core.Services.Mods.Registry.Downloader;

namespace UnchainedLauncher.Core.Utilities {
    /// <summary>
    /// Given a pair of conversions between TJson and TClass
    /// where TJson is a json serializable model which models the metadata necessary to construct a TClass
    /// provides methods for loading a class from a json string or json file, or writing a class to a json file.
    /// </summary>
    /// <typeparam name="TJson">A json serializable representation of the metadata necessary data to create a TClass</typeparam>
    /// <typeparam name="TClass">The target class to generate/deconstruct</typeparam>
    public class JsonFactory<TJson, TClass> {
        public readonly Func<TJson, TClass> ToClassType;
        public readonly Func<TClass, TJson> ToJsonType;
        
        public JsonFactory(Func<TJson, TClass> toClassType, Func<TClass, TJson> toJsonType) {
            ToClassType = toClassType;
            ToJsonType = toJsonType;
        }
        
        public DeserializationResult<TClass> FromJson(string json) =>
            JsonHelpers.Deserialize<TJson>(json)
                .Select(ToClassType);

        public async Task<Option<DeserializationResult<TClass>>> FromJsonFile(string path) {
            if (!File.Exists(path)) return None;

            var fileContents = await File.ReadAllTextAsync(path);
            return FromJson(fileContents);
        }

        public async Task<TClass> FromJsonFileOrDefault(string path, TClass defaultValue) =>
            (await FromJsonFile(path))
                .Bind(x => x.ToEither().ToOption())
                .IfNone(defaultValue);
        
        public Task ToJsonFile(string path, TClass obj) =>
            File.WriteAllTextAsync(path, JsonHelpers.Serialize(ToJsonType(obj)));
        
        public JsonFactory<TJson, TClass2> InvariantMapRight<TClass2>(Func<TClass, TClass2> to, Func<TClass2, TClass> from) =>
            new JsonFactory<TJson, TClass2>(
                json => to(ToClassType(json)),
                obj => ToJsonType(from(obj))
        );
        
        public JsonFactory<TJson2, TClass> InvariantMapLeft<TJson2>(Func<TJson, TJson2> to, Func<TJson2, TJson> from) =>
            new JsonFactory<TJson2, TClass>(
                json => ToClassType(from(json)),
                obj => to(ToJsonType(obj))
        );

        public JsonFactory<TJson2, TClass2> InvariantBiMap<TJson2, TClass2>(
            Func<TJson, TJson2> toJson2,
            Func<TJson2, TJson> fromJson2,
            Func<TClass, TClass2> toClass2,
            Func<TClass2, TClass> fromClass2
        ) => new JsonFactory<TJson2, TClass2>(
            json => toClass2(ToClassType(fromJson2(json))),
            obj => toJson2(ToJsonType(fromClass2(obj)))
        );
    }
}