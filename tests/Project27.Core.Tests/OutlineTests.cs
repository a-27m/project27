using System.Globalization;
using Project27.Core;
using Project27.Core.Time;
using Xunit;

namespace Project27.Core.Tests;

public sealed class OutlineTests
{
    private static Project NewProject()
        => new("Test", DateTime.Parse("2026-01-05 08:00", CultureInfo.InvariantCulture));

    [Fact]
    public void Rows_and_outline_numbers_follow_preorder()
    {
        var project = NewProject();
        var a = project.AddTask("A");
        var b = project.AddTask("B");
        var c = project.AddTask("C");
        project.Indent(b);

        var tasks = project.Tasks;
        Assert.Equal(["A", "B", "C"], tasks.Select(t => t.Name));
        Assert.Equal([1, 2, 3], tasks.Select(t => t.RowNumber));
        Assert.Equal("1", a.OutlineNumber);
        Assert.Equal("1.1", b.OutlineNumber);
        Assert.Equal("2", c.OutlineNumber);
        Assert.Equal([0, 1, 0], tasks.Select(t => t.OutlineLevel));
        Assert.True(a.IsSummary);
        Assert.Equal("1.1", b.Wbs);
        b.CustomWbs = "ENG-42";
        Assert.Equal("ENG-42", b.Wbs);
    }

    [Fact]
    public void Outdent_restores_level()
    {
        var project = NewProject();
        var a = project.AddTask("A");
        var b = project.AddTask("B");
        project.Indent(b);
        project.Outdent(b);

        Assert.False(a.IsSummary);
        Assert.Equal("2", b.OutlineNumber);
        Assert.Throws<InvalidOperationException>(() => project.Outdent(b));
        Assert.Throws<InvalidOperationException>(() => project.Indent(a)); // no preceding sibling
    }

    [Fact]
    public void Move_places_subtree_under_new_parent()
    {
        var project = NewProject();
        var a = project.AddTask("A");
        var b = project.AddTask("B");
        var c = project.AddTask("C", parent: b);
        project.MoveTask(b, a, 0);

        Assert.Equal("1.1", b.OutlineNumber);
        Assert.Equal("1.1.1", c.OutlineNumber);
        Assert.Throws<InvalidOperationException>(() => project.MoveTask(a, c, 0)); // under own descendant
    }

    [Fact]
    public void Removing_a_summary_drops_subtree_and_links()
    {
        var project = NewProject();
        var a = project.AddTask("A");
        var summary = project.AddTask("S");
        var child = project.AddTask("C", parent: summary);
        project.Link(a, child);
        project.RemoveTask(summary);

        Assert.Equal(["A"], project.Tasks.Select(t => t.Name));
        Assert.Empty(a.Successors);
    }

    [Fact]
    public void Duration_on_summary_is_rejected()
    {
        var project = NewProject();
        var summary = project.AddTask("S");
        project.AddTask("C", parent: summary);

        Assert.Throws<InvalidOperationException>(() => summary.Duration = Duration.Parse("3d"));
    }

    [Fact]
    public void Self_lineage_and_duplicate_links_are_rejected()
    {
        var project = NewProject();
        var summary = project.AddTask("S");
        var child = project.AddTask("C", parent: summary);
        var other = project.AddTask("O");
        project.Link(other, child);

        Assert.Throws<InvalidOperationException>(() => project.Link(child, child));
        Assert.Throws<InvalidOperationException>(() => project.Link(summary, child));
        Assert.Throws<InvalidOperationException>(() => project.Link(child, summary));
        Assert.Throws<InvalidOperationException>(() => project.Link(other, child));
    }

    [Fact]
    public void Direct_cycles_are_rejected()
    {
        var project = NewProject();
        var a = project.AddTask("A");
        var b = project.AddTask("B");
        var c = project.AddTask("C");
        project.Link(a, b);
        project.Link(b, c);

        Assert.Throws<InvalidOperationException>(() => project.Link(c, a));
    }

    [Fact]
    public void Cycles_through_summaries_are_rejected()
    {
        var project = NewProject();
        var a = project.AddTask("A");
        var summary = project.AddTask("S");
        var x = project.AddTask("X", parent: summary);
        project.Link(a, summary); // a constrains x through the summary

        Assert.Throws<InvalidOperationException>(() => project.Link(x, a));
    }

    [Fact]
    public void Constraint_requires_date_when_applicable()
    {
        var project = NewProject();
        var a = project.AddTask("A");

        Assert.Throws<ArgumentException>(() => a.SetConstraint(ConstraintType.MustStartOn));
        a.SetConstraint(ConstraintType.AsSoonAsPossible);
        Assert.Null(a.ConstraintDate);
    }

    [Fact]
    public void Recurring_task_requires_a_bound()
    {
        var project = NewProject();

        Assert.Throws<ArgumentException>(() => project.AddRecurringTask(
            "Standup",
            Duration.Parse("1h"),
            new DailyRecurrence(1),
            new DateOnly(2026, 1, 5)));
    }

    [Fact]
    public void Split_validation()
    {
        var project = NewProject();
        var a = project.AddTask("A", Duration.Parse("2d"));

        Assert.Throws<ArgumentOutOfRangeException>(() => a.SplitAt(Duration.Parse("2d"), Duration.Parse("1d")));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.SplitAt(Duration.Parse("1d"), Duration.Parse("0d")));
        a.SplitAt(Duration.Parse("1d"), Duration.Parse("1d"));
        a.ClearSplits();
        Assert.False(a.IsSplit);
    }
}
