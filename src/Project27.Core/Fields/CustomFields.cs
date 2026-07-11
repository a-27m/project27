using System.Globalization;

namespace Project27.Core.Fields;

/// <summary>An indicator rule: first match wins, yielding an icon name.</summary>
public sealed record IndicatorRule(Views.FilterOperator Operator, object Value, string Icon);

/// <summary>
/// A custom field definition: an MSP-style slot (text1…, number1…), optionally
/// aliased, holding either stored per-task values or a formula
/// (docs/spec/09-views-fields.md §9b).
/// </summary>
public sealed class CustomFieldDefinition
{
    internal CustomFieldDefinition(string id, FieldKind kind)
    {
        Id = id;
        Kind = kind;
    }

    /// <summary>Slot id, lower-case ("text1", "cost3").</summary>
    public string Id { get; }

    public FieldKind Kind { get; }

    /// <summary>Unique display name usable wherever a field key is (null = none).</summary>
    public string? Alias { get; internal set; }

    /// <summary>Formula source; null = stored values.</summary>
    public string? Formula { get; internal set; }

    internal FormulaNode? ParsedFormula { get; set; }

    public IReadOnlyList<IndicatorRule> Indicators { get; internal set; } = [];

    public string Caption => Alias ?? Id;

    /// <summary>The slot families and their counts (MSP parity).</summary>
    public static readonly IReadOnlyDictionary<string, (FieldKind Kind, int Count)> Slots =
        new Dictionary<string, (FieldKind, int)>(StringComparer.OrdinalIgnoreCase)
        {
            ["text"] = (FieldKind.Text, 30),
            ["number"] = (FieldKind.Number, 20),
            ["cost"] = (FieldKind.Cost, 10),
            ["date"] = (FieldKind.Date, 10),
            ["flag"] = (FieldKind.Flag, 20),
            ["duration"] = (FieldKind.Duration, 10),
        };

    /// <summary>Validates a slot id ("text1".."text30", …) and returns its kind.</summary>
    public static FieldKind KindOfSlot(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var trimmed = id.Trim().ToLowerInvariant();
        var digits = trimmed.Length - trimmed.TrimEnd("0123456789".ToCharArray()).Length;
        if (digits > 0)
        {
            var prefix = trimmed[..^digits];
            if (Slots.TryGetValue(prefix, out var slot)
                && int.TryParse(trimmed[^digits..], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index >= 1 && index <= slot.Count)
            {
                return slot.Kind;
            }
        }

        throw new ArgumentException(
            $"'{id}' is not a custom field slot; use " +
            string.Join(", ", Slots.Select(s => $"{s.Key}1..{s.Key}{s.Value.Count}")) + ".",
            nameof(id));
    }
}
