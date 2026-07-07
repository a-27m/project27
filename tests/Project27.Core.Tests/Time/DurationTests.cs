using Project27.Core.Time;
using Xunit;

namespace Project27.Core.Tests.Time;

public sealed class DurationTests
{
    private static readonly TimeSettings Settings = new();

    [Theory]
    [InlineData("3d", 3, DurationUnit.Days, false)]
    [InlineData("2.5 wks", 2.5, DurationUnit.Weeks, false)]
    [InlineData("90 min", 90, DurationUnit.Minutes, false)]
    [InlineData("8h", 8, DurationUnit.Hours, false)]
    [InlineData("1 month", 1, DurationUnit.Months, false)]
    [InlineData("4ed", 4, DurationUnit.ElapsedDays, false)]
    [InlineData("2 emons", 2, DurationUnit.ElapsedMonths, false)]
    [InlineData("3d?", 3, DurationUnit.Days, true)]
    [InlineData("1.5 weeks ?", 1.5, DurationUnit.Weeks, true)]
    [InlineData("0d", 0, DurationUnit.Days, false)]
    public void Parses_valid_durations(string text, double value, DurationUnit unit, bool estimated)
    {
        var duration = Duration.Parse(text);

        Assert.Equal((decimal)value, duration.Value);
        Assert.Equal(unit, duration.Unit);
        Assert.Equal(estimated, duration.IsEstimated);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("d")]
    [InlineData("3")]
    [InlineData("3x")]
    [InlineData("-1d")]
    [InlineData("?")]
    [InlineData("3 elapsed")]
    public void Rejects_invalid_durations(string text)
    {
        Assert.False(Duration.TryParse(text, out _));
        Assert.Throws<FormatException>(() => Duration.Parse(text));
    }

    [Theory]
    [InlineData(3, DurationUnit.Days, 1440)]        // 3 × 480
    [InlineData(2, DurationUnit.Weeks, 4800)]       // 2 × 2400
    [InlineData(1, DurationUnit.Months, 9600)]      // 20 days × 480
    [InlineData(1.5, DurationUnit.Hours, 90)]
    [InlineData(45, DurationUnit.Minutes, 45)]
    [InlineData(1, DurationUnit.ElapsedDays, 1440)] // clock time: 24 h
    [InlineData(1, DurationUnit.ElapsedWeeks, 10080)]
    [InlineData(1, DurationUnit.ElapsedMonths, 43200)]
    public void Converts_to_minutes(double value, DurationUnit unit, double expectedMinutes)
    {
        var duration = new Duration((decimal)value, unit);

        Assert.Equal((decimal)expectedMinutes, duration.ToMinutes(Settings));
    }

    [Fact]
    public void Converts_from_minutes()
    {
        var duration = Duration.FromMinutes(720, DurationUnit.Days, Settings);

        Assert.Equal(1.5m, duration.Value);
        Assert.Equal(DurationUnit.Days, duration.Unit);
    }

    [Fact]
    public void Conversion_respects_custom_settings()
    {
        var settings = new TimeSettings { MinutesPerDay = 360, MinutesPerWeek = 1800, DaysPerMonth = 18 };

        Assert.Equal(360m, new Duration(1, DurationUnit.Days).ToMinutes(settings));
        Assert.Equal(1800m, new Duration(1, DurationUnit.Weeks).ToMinutes(settings));
        Assert.Equal(6480m, new Duration(1, DurationUnit.Months).ToMinutes(settings));
    }

    [Theory]
    [InlineData(1.5, DurationUnit.Weeks, false, "1.5w", "1.5 weeks")]
    [InlineData(1, DurationUnit.Days, false, "1d", "1 day")]
    [InlineData(3, DurationUnit.Days, true, "3d?", "3 days?")]
    [InlineData(2, DurationUnit.ElapsedMonths, false, "2emo", "2 elapsed months")]
    public void Formats_short_and_long(double value, DurationUnit unit, bool estimated, string shortText, string longText)
    {
        var duration = new Duration((decimal)value, unit, estimated);

        Assert.Equal(shortText, duration.ToString(DurationFormat.Compact));
        Assert.Equal(longText, duration.ToString(DurationFormat.Verbose));
    }

    [Fact]
    public void Short_format_round_trips()
    {
        var original = new Duration(2.25m, DurationUnit.ElapsedWeeks, isEstimated: true);

        var parsed = Duration.Parse(original.ToString());

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void Rejects_negative_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Duration(-1, DurationUnit.Days));
    }
}
