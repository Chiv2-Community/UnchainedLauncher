using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace StructuredINI.Codecs;

public sealed class DerivedRecordCodec<T> : IINICodec<T>
{
    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly NullabilityInfoContext NullabilityContext = new();

    private readonly ConstructorInfo _ctor;
    private readonly ParameterInfo[] _parameters;
    private readonly Dictionary<string, PropertyInfo> _propertyByName;

    public DerivedRecordCodec()
    {
        _ctor = typeof(T).GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault()
                ?? throw new InvalidOperationException($"Type {typeof(T).Name} has no public constructor.");

        _parameters = _ctor.GetParameters();
        _propertyByName = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod != null)
            .ToDictionary(p => p.Name, p => p, KeyComparer);
    }

    public T Decode(string value)
    {
        var map = ParseKeyValuePairs(value);

        var args = new object[_parameters.Length];
        for (var i = 0; i < _parameters.Length; i++)
        {
            var p = _parameters[i];
            var key = p.Name ?? throw new InvalidOperationException($"Constructor parameter #{i} has no name on {typeof(T).Name}.");

            if (map.TryGetValue(key, out var raw))
            {
                args[i] = DecodeParameterValue(p, raw);
                continue;
            }

            if (p.HasDefaultValue)
            {
                args[i] = p.DefaultValue;
                continue;
            }

            if (IsNullable(p))
            {
                args[i] = null;
                continue;
            }

            if (p.ParameterType.IsValueType)
            {
                args[i] = Activator.CreateInstance(p.ParameterType);
                continue;
            }

            args[i] = null;
        }

        return (T)_ctor.Invoke(args);
    }

    public string Encode(T value)
    {
        if (value == null)
        {
            // Upstream callers typically skip nulls, but if asked, encode as None.
            return "None";
        }

        var parts = new List<string>(_parameters.Length);
        foreach (var p in _parameters)
        {
            var name = p.Name ?? throw new InvalidOperationException($"Constructor parameter has no name on {typeof(T).Name}.");
            if (!_propertyByName.TryGetValue(name, out var prop))
            {
                throw new InvalidOperationException($"Type {typeof(T).Name} is missing readable property '{name}' required for derived codec.");
            }

            var rawVal = prop.GetValue(value);
            string encoded;
            if (rawVal == null)
            {
                encoded = "None";
            }
            else
            {
                var codec = CodecRegistry.Get(p.ParameterType) as dynamic;
                encoded = codec.Encode((dynamic)rawVal);
            }

            parts.Add($"{name}={encoded}");
        }

        return $"({string.Join(",", parts)})";
    }

    private object DecodeParameterValue(ParameterInfo parameter, string raw)
    {
        if (IsNullable(parameter) && string.Equals(raw, "None", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var codec = CodecRegistry.Get(parameter.ParameterType) as dynamic;
        try
        {
            return codec.Decode(raw);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to decode '{raw}' for parameter {typeof(T).Name}.{parameter.Name} ({parameter.ParameterType.Name}).", ex);
        }
    }

    private static bool IsNullable(ParameterInfo parameter)
    {
        var t = parameter.ParameterType;
        if (!t.IsValueType)
        {
            // Respect nullable reference types when metadata is available.
            var info = NullabilityContext.Create(parameter);
            return info.ReadState == NullabilityState.Nullable;
        }

        return Nullable.GetUnderlyingType(t) != null;
    }

    private static Dictionary<string, string> ParseKeyValuePairs(string value)
    {
        var map = new Dictionary<string, string>(KeyComparer);
        if (string.IsNullOrWhiteSpace(value))
        {
            return map;
        }

        var content = value.Trim();
        if (content.StartsWith("(") && content.EndsWith(")"))
        {
            content = content.Substring(1, content.Length - 2);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return map;
        }

        var parts = content.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            map[kv[0].Trim()] = kv[1].Trim();
        }

        return map;
    }
}
