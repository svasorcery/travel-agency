using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Travel.Shared.Abstractions;

/// <summary>
/// An immutable array wrapper with element-wise structural equality and hash code.
/// Suitable for use in <c>record</c> types whose equality must not rely on reference identity.
/// </summary>
[JsonConverter(typeof(EquatableArrayConverterFactory))]
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
{
    private readonly T[] _items;

    public EquatableArray(T[] items)
    {
        _items = items ?? [];
    }

    public int Count => _items?.Length ?? 0;

    public T this[int index] => (_items ?? [])[index];

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(EquatableArray<T> other)
    {
        var a = _items ?? [];
        var b = other._items ?? [];
        if (a.Length != b.Length)
            return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(a[i], b[i]))
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var items = _items ?? [];
        var hash = new HashCode();
        foreach (var item in items)
            hash.Add(item);
        return hash.ToHashCode();
    }

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) =>
        left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) =>
        !left.Equals(right);

    public static implicit operator EquatableArray<T>(T[] items) => new(items);
}

/// <summary>
/// Serialises / deserialises <see cref="EquatableArray{T}"/> as a plain JSON array.
/// </summary>
public sealed class EquatableArrayConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(EquatableArray<>);

    public override JsonConverter? CreateConverter(
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        var elementType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(EquatableArrayConverter<>).MakeGenericType(elementType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

internal sealed class EquatableArrayConverter<T> : JsonConverter<EquatableArray<T>>
{
    public override EquatableArray<T> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        var list = JsonSerializer.Deserialize<T[]>(ref reader, options);
        return new EquatableArray<T>(list ?? []);
    }

    public override void Write(
        Utf8JsonWriter writer,
        EquatableArray<T> value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartArray();
        foreach (var item in value)
            JsonSerializer.Serialize(writer, item, options);
        writer.WriteEndArray();
    }
}
