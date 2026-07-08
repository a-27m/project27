using System.Globalization;
using Project27.Core.Time;

namespace Project27.Core;

/// <summary>Time base of a work-resource rate. Material rates are per unit and carry no time base.</summary>
public enum RateUnit
{
    Hour,
    Day,
    Week,
    Month,
    Year,
}

/// <summary>
/// A pay rate: amount per <see cref="RateUnit"/>. Conversion to per-minute cost uses
/// project time settings; a year is 52 × MinutesPerWeek (deviations.md #15).
/// </summary>
public readonly record struct Rate(decimal Amount, RateUnit Per)
{
    public static Rate Zero { get; } = new(0m, RateUnit.Hour);

    public bool IsZero => Amount == 0m;

    /// <summary>Cost of the given working minutes at this rate (division last, exact for whole units).</summary>
    public decimal CostForMinutes(decimal workMinutes, TimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var minutesPerUnit = Per switch
        {
            RateUnit.Hour => 60m,
            RateUnit.Day => settings.MinutesPerDay,
            RateUnit.Week => settings.MinutesPerWeek,
            RateUnit.Month => settings.DaysPerMonth * settings.MinutesPerDay,
            RateUnit.Year => 52m * settings.MinutesPerWeek,
            _ => throw new InvalidOperationException($"Unknown rate unit {Per}."),
        };
        return workMinutes * Amount / minutesPerUnit;
    }

    /// <summary>Parses "50", "50/h", "400/d", "2000/w", "8000/mo", "100000/y". A bare number is per hour.</summary>
    public static Rate Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var slash = text.IndexOf('/', StringComparison.Ordinal);
        var amountText = slash < 0 ? text : text[..slash];
        if (!decimal.TryParse(amountText.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0)
        {
            throw new FormatException($"Invalid rate amount '{amountText}'.");
        }

        var unit = slash < 0 ? RateUnit.Hour : text[(slash + 1)..].Trim().ToUpperInvariant() switch
        {
            "H" or "HR" or "HOUR" => RateUnit.Hour,
            "D" or "DAY" => RateUnit.Day,
            "W" or "WK" or "WEEK" => RateUnit.Week,
            "MO" or "MON" or "MONTH" => RateUnit.Month,
            "Y" or "YR" or "YEAR" => RateUnit.Year,
            var other => throw new FormatException($"Invalid rate unit '/{other}'; use /h, /d, /w, /mo, or /y."),
        };
        return new Rate(amount, unit);
    }

    public override string ToString()
    {
        var amount = Amount.ToString("0.##", CultureInfo.InvariantCulture);
        var suffix = Per switch
        {
            RateUnit.Hour => "/h",
            RateUnit.Day => "/d",
            RateUnit.Week => "/w",
            RateUnit.Month => "/mo",
            RateUnit.Year => "/y",
            _ => string.Empty,
        };
        return amount + suffix;
    }
}

/// <summary>One effective-dated row of a cost rate table.</summary>
public readonly record struct CostRate(DateTime EffectiveFrom, Rate StandardRate, Rate OvertimeRate, decimal CostPerUse);

/// <summary>MS Project's five cost rate tables per resource.</summary>
public enum CostRateTableId
{
    A,
    B,
    C,
    D,
    E,
}

/// <summary>
/// Effective-dated rates. Always contains a base entry at <see cref="DateTime.MinValue"/>
/// (initially zero rates); the entry in force at a moment is the latest one starting
/// at or before it.
/// </summary>
public sealed class CostRateTable
{
    private readonly List<CostRate> _entries = [new CostRate(DateTime.MinValue, Rate.Zero, Rate.Zero, 0m)];

    /// <summary>Entries ordered by effective date; the first is the base entry.</summary>
    public IReadOnlyList<CostRate> Entries => _entries;

    public CostRate RateAt(DateTime moment)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].EffectiveFrom <= moment)
            {
                return _entries[i];
            }
        }

        return _entries[0];
    }

    /// <summary>
    /// Upserts the entry effective at <paramref name="from"/>; omitted fields carry over
    /// from the entry previously in force at that moment.
    /// </summary>
    public void SetRate(DateTime from, Rate? standardRate = null, Rate? overtimeRate = null, decimal? costPerUse = null)
    {
        if (costPerUse is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(costPerUse), "Cost per use cannot be negative.");
        }

        var basis = RateAt(from);
        var entry = new CostRate(from, standardRate ?? basis.StandardRate, overtimeRate ?? basis.OvertimeRate, costPerUse ?? basis.CostPerUse);
        var index = _entries.FindIndex(e => e.EffectiveFrom == from);
        if (index >= 0)
        {
            _entries[index] = entry;
        }
        else
        {
            index = _entries.FindIndex(e => e.EffectiveFrom > from);
            _entries.Insert(index < 0 ? _entries.Count : index, entry);
        }
    }

    /// <summary>Removes the entry effective exactly at <paramref name="from"/>. The base entry cannot be removed.</summary>
    public bool RemoveRate(DateTime from)
    {
        var index = _entries.FindIndex(e => e.EffectiveFrom == from);
        if (index == 0)
        {
            throw new InvalidOperationException("The base rate entry cannot be removed; set it to zero instead.");
        }

        if (index < 0)
        {
            return false;
        }

        _entries.RemoveAt(index);
        return true;
    }

    internal void RestoreEntries(IEnumerable<CostRate> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries.OrderBy(e => e.EffectiveFrom));
        if (_entries.Count == 0 || _entries[0].EffectiveFrom != DateTime.MinValue)
        {
            _entries.Insert(0, new CostRate(DateTime.MinValue, Rate.Zero, Rate.Zero, 0m));
        }
    }
}
