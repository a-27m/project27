using System.Globalization;
using Project27.Core;
using Project27.Core.Fields;
using Project27.Core.Persistence;
using Project27.Core.Time;
using Project27.Core.Views;
using Xunit;

namespace Project27.Core.Tests;

/// <summary>Custom fields: slots, aliases, formulas, indicators, persistence (phase 9b).</summary>
public sealed class CustomFieldTests
{
    private static DateTime At(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture);

    private static Duration Dur(string text) => Duration.Parse(text);

    private static Project NewProject() => new("Test", At("2026-01-05 08:00"));

    [Fact]
    public void Slots_validate_and_carry_kinds()
    {
        Assert.Equal(FieldKind.Text, CustomFieldDefinition.KindOfSlot("text30"));
        Assert.Equal(FieldKind.Duration, CustomFieldDefinition.KindOfSlot("Duration10"));
        Assert.Throws<ArgumentException>(() => CustomFieldDefinition.KindOfSlot("text31"));
        Assert.Throws<ArgumentException>(() => CustomFieldDefinition.KindOfSlot("banana1"));
    }

    [Fact]
    public void Stored_values_are_typed_and_resolvable_by_alias()
    {
        var project = NewProject();
        var task = project.AddTask("T", Dur("1d"));
        var phase = project.DefineCustomField("text1", alias: "Phase");
        task.SetCustomValue(phase, "Rollout");

        Assert.Equal("Rollout", FieldCatalog.Resolve(project, "Phase").Accessor(task));
        Assert.Equal("Rollout", FieldCatalog.Resolve(project, "text1").Accessor(task));
        Assert.Throws<ArgumentException>(() => task.SetCustomValue(phase, 42m)); // wrong type

        task.SetCustomValue(phase, null);
        Assert.Null(task.GetCustomValue("text1"));
    }

    [Fact]
    public void Aliases_must_not_shadow_existing_fields()
    {
        var project = NewProject();
        Assert.Throws<ArgumentException>(() => project.DefineCustomField("text1", alias: "name"));
        Assert.Throws<ArgumentException>(() => project.DefineCustomField("text1", alias: "cost3"));
        project.DefineCustomField("text1", alias: "Phase");
        Assert.Throws<ArgumentException>(() => project.DefineCustomField("text2", alias: "phase"));
    }

    [Fact]
    public void Formulas_compute_from_other_fields()
    {
        var project = NewProject();
        var task = project.AddTask("Build", Dur("4d"));
        task.FixedCost = 100m;
        project.Recalculate();

        project.DefineCustomField("number1", alias: "Risk", formula: "IIf([totalSlack] < 1d, 100, 0)");
        project.DefineCustomField("text2", alias: "Label", formula: "[name] + \" (\" + [wbs] + \")\"");
        project.DefineCustomField("cost1", alias: "Padded", formula: "Round([fixedCost] * 1.1, 0)");

        Assert.Equal(100m, FieldCatalog.Resolve(project, "Risk").Accessor(task)); // critical => slack 0
        Assert.Equal("Build (1)", FieldCatalog.Resolve(project, "Label").Accessor(task));
        Assert.Equal(110m, FieldCatalog.Resolve(project, "Padded").Accessor(task));
    }

    [Fact]
    public void Formula_fields_can_reference_other_custom_fields_but_not_cyclically()
    {
        var project = NewProject();
        var task = project.AddTask("T", Dur("1d"));
        project.Recalculate();
        var baseField = project.DefineCustomField("number1", alias: "Base");
        task.SetCustomValue(baseField, 10m);
        project.DefineCustomField("number2", alias: "Derived", formula: "[Base] * 2");
        Assert.Equal(20m, FieldCatalog.Resolve(project, "Derived").Accessor(task));

        project.DefineCustomField("number3", alias: "LoopA", formula: "[LoopB] + 1");
        project.DefineCustomField("number4", alias: "LoopB", formula: "[LoopA] + 1");
        Assert.Throws<InvalidOperationException>(() => FieldCatalog.Resolve(project, "LoopA").Accessor(task));
    }

    [Fact]
    public void Formula_errors_are_rejected_at_definition_time()
    {
        var project = NewProject();
        Assert.Throws<ArgumentException>(() => project.DefineCustomField("number1", formula: "1 +"));
        Assert.Throws<ArgumentException>(() => project.DefineCustomField("number1", formula: "[unclosed"));
        Assert.Throws<ArgumentException>(() => project.DefineCustomField("number1", formula: "bare_word"));
    }

    [Fact]
    public void Evaluator_covers_operators_and_functions()
    {
        var project = NewProject();
        var task = project.AddTask("T", Dur("2d"));
        project.StatusDate = At("2026-02-01 08:00");
        project.Recalculate();

        object? Eval(string formula) => FormulaEvaluator.Evaluate(FormulaEvaluator.Parse(formula), task);

        Assert.Equal(7m, Eval("1 + 2 * 3"));
        Assert.Equal(9m, Eval("(1 + 2) * 3"));
        Assert.Equal(-4m, Eval("-Abs(-4)"));
        Assert.Equal(2m, Eval("Min(Max(2, 1), 5)"));
        Assert.Equal(3.14m, Eval("Round(3.14159, 2)"));
        Assert.Equal(true, Eval("2 > 1 and not (1 = 2)"));
        Assert.Equal("a-b", Eval("\"a\" + \"-\" + \"b\""));
        Assert.Equal(At("2026-02-01 08:00"), Eval("StatusDate()"));
        Assert.Equal(true, Eval("[duration] = 2d")); // duration literal -> 960 minutes
        Assert.Equal(480m, Eval("2d - 1d"));
        Assert.Throws<InvalidOperationException>(() => Eval("1 / 0"));
        Assert.Throws<InvalidOperationException>(() => Eval("Nope(1)"));
    }

    [Fact]
    public void Indicators_pick_the_first_matching_rule()
    {
        var project = NewProject();
        var task = project.AddTask("T", Dur("1d"));
        project.Recalculate();
        var field = project.DefineCustomField(
            "number1",
            alias: "Risk",
            indicators:
            [
                new IndicatorRule(FilterOperator.GreaterOrEqual, 100m, "red-flag"),
                new IndicatorRule(FilterOperator.GreaterOrEqual, 50m, "yellow-flag"),
            ]);

        task.SetCustomValue(field, 70m);
        Assert.Equal("yellow-flag", FieldCatalog.EvaluateIndicator(field, task));
        Assert.Equal("yellow-flag", FieldCatalog.Resolve(project, "Risk.icon").Accessor(task));

        task.SetCustomValue(field, 120m);
        Assert.Equal("red-flag", FieldCatalog.EvaluateIndicator(field, task));

        task.SetCustomValue(field, 5m);
        Assert.Null(FieldCatalog.EvaluateIndicator(field, task));
    }

    [Fact]
    public void Custom_fields_work_in_views_and_filters()
    {
        var project = NewProject();
        var a = project.AddTask("A", Dur("1d"));
        var b = project.AddTask("B", Dur("1d"));
        project.Recalculate();
        var phase = project.DefineCustomField("text1", alias: "Phase");
        a.SetCustomValue(phase, "One");
        b.SetCustomValue(phase, "Two");

        var filtered = TaskView.Evaluate(project, new ViewDefinition(
            ["id", "name", "Phase"],
            FilterParser.Parse(project, "Phase = One")));
        Assert.Equal(["A"], filtered.Groups.Single().Rows.Select(r => r.Task.Name));

        var grouped = TaskView.Evaluate(project, new ViewDefinition(["name"], GroupBy: "Phase"));
        Assert.Equal(2, grouped.Groups.Count);
    }

    [Fact]
    public void Removal_drops_values_everywhere()
    {
        var project = NewProject();
        var task = project.AddTask("T", Dur("1d"));
        var field = project.DefineCustomField("flag1", alias: "Approved");
        task.SetCustomValue(field, true);

        Assert.True(project.RemoveCustomField("Approved"));
        Assert.Null(task.GetCustomValue("flag1"));
        Assert.Null(project.FindCustomField("flag1"));
        Assert.Throws<KeyNotFoundException>(() => FieldCatalog.Resolve(project, "Approved"));
    }

    [Fact]
    public void Custom_fields_round_trip_through_the_document()
    {
        var project = NewProject();
        var task = project.AddTask("T", Dur("2d"));
        project.Recalculate();
        var phase = project.DefineCustomField("text1", alias: "Phase");
        project.DefineCustomField(
            "number1",
            alias: "Risk",
            formula: "IIf([totalSlack] < 1d, 100, 0)",
            indicators: [new IndicatorRule(FilterOperator.GreaterOrEqual, 100m, "red-flag")]);
        var due = project.DefineCustomField("date1", alias: "Due");
        task.SetCustomValue(phase, "Rollout");
        task.SetCustomValue(due, At("2026-03-01 17:00"));

        var restored = ProjectDocumentMapper.FromDocument(ProjectDocumentMapper.ToDocument(project));
        restored.Recalculate();
        var restoredTask = restored.Tasks.Single();

        Assert.Equal("Rollout", FieldCatalog.Resolve(restored, "Phase").Accessor(restoredTask));
        Assert.Equal(At("2026-03-01 17:00"), FieldCatalog.Resolve(restored, "Due").Accessor(restoredTask));
        Assert.Equal(100m, FieldCatalog.Resolve(restored, "Risk").Accessor(restoredTask));
        Assert.Equal("red-flag", FieldCatalog.Resolve(restored, "Risk.icon").Accessor(restoredTask));
        Assert.Equal("IIf([totalSlack] < 1d, 100, 0)", restored.FindCustomField("Risk")!.Formula);
    }
}
