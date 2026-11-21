using System.Text;

namespace BrPay.Emv;

/// <summary>A single EMV data object: a two-digit ID, its raw value and, for templates, its subfields.</summary>
/// <remarks>
/// <see cref="Value"/> always holds the raw value. For template IDs (see
/// <see cref="Tlv.IsTemplateId"/>) <see cref="Tlv.Parse"/> also decodes the
/// value into <see cref="Children"/>; <see cref="Tlv.Encode"/> prefers
/// <see cref="Children"/> over <see cref="Value"/> when it is not empty.
/// Equality is structural, including the children.
/// </remarks>
public sealed class EmvField : IEquatable<EmvField>
{
    /// <summary>Creates a field with a plain value.</summary>
    /// <param name="id">The two-digit ID.</param>
    /// <param name="value">The raw value.</param>
    public EmvField(string id, string value)
        : this(id, value, Array.Empty<EmvField>())
    {
    }

    /// <summary>Creates a field with a raw value and its decoded subfields.</summary>
    /// <param name="id">The two-digit ID.</param>
    /// <param name="value">The raw value.</param>
    /// <param name="children">The subfields; empty for plain fields.</param>
    public EmvField(string id, string value, IReadOnlyList<EmvField> children)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(children);
        Id = id;
        Value = value;
        Children = children;
        _byteLength = -1;
    }

    // Used by the parser, which knows how many payload bytes the value spans.
    // This differs from the UTF-8 length of Value only when a declared length
    // splits a multi-byte character, which Value then shows as U+FFFD.
    internal EmvField(string id, string value, IReadOnlyList<EmvField> children, int byteLength)
        : this(id, value, children)
    {
        _byteLength = byteLength;
    }

    private readonly int _byteLength;

    /// <summary>The two-digit field ID, e.g. <c>"59"</c>.</summary>
    public string Id { get; }

    /// <summary>The raw value as written in the payload.</summary>
    public string Value { get; }

    /// <summary>The subfields of a template field; empty for plain fields.</summary>
    public IReadOnlyList<EmvField> Children { get; }

    /// <summary>The length of the value in bytes, as counted by the EMV encoding.</summary>
    internal int ByteLength => _byteLength >= 0 ? _byteLength : Encoding.UTF8.GetByteCount(Value);

    /// <summary>Creates a template field from its subfields; the value is computed when encoding.</summary>
    /// <param name="id">The two-digit ID.</param>
    /// <param name="children">The subfields.</param>
    /// <returns>The template field.</returns>
    public static EmvField Template(string id, params EmvField[] children) => new(id, string.Empty, children);

    /// <summary>Returns the first subfield with the given ID, or <see langword="null"/>.</summary>
    /// <param name="id">The two-digit subfield ID.</param>
    /// <returns>The subfield, or <see langword="null"/>.</returns>
    public EmvField? Child(string id) => Tlv.Find(Children, id);

    /// <inheritdoc/>
    public bool Equals(EmvField? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (!string.Equals(Id, other.Id, StringComparison.Ordinal) ||
            !string.Equals(Value, other.Value, StringComparison.Ordinal) ||
            Children.Count != other.Children.Count)
        {
            return false;
        }

        for (int i = 0; i < Children.Count; i++)
        {
            if (!Children[i].Equals(other.Children[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as EmvField);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Id, StringComparer.Ordinal);
        hash.Add(Value, StringComparer.Ordinal);
        foreach (EmvField child in Children)
        {
            hash.Add(child);
        }

        return hash.ToHashCode();
    }

    /// <summary>Returns <c>ID=value</c>, e.g. <c>59="Fulano de Tal"</c>.</summary>
    /// <returns>A diagnostic representation.</returns>
    public override string ToString() => Id + "=\"" + Value + "\"";
}
