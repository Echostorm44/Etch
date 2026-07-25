namespace Etch.Sourcegen.Pooled;

public readonly struct ValueEquality<T> : IEquatable<ValueEquality<T>>
{
    public T Value { get; }

    public ValueEquality(T value) => Value = value;

    public bool Equals(ValueEquality<T> other) => EqualityComparer<T>.Default.Equals(Value, other.Value);
    public override bool Equals(object? obj) => obj is ValueEquality<T> other && Equals(other);
    public override int GetHashCode() => EqualityComparer<T>.Default.GetHashCode(Value);
    public static bool operator ==(ValueEquality<T> left, ValueEquality<T> right) => left.Equals(right);
    public static bool operator !=(ValueEquality<T> left, ValueEquality<T> right) => !left.Equals(right);
}
