using System.CommandLine;
using Project27.Core;
using Project27.Core.Time;

namespace Project27.Cli;

internal static class CalendarCommands
{
    private static readonly DayOfWeek[] WeekDays =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    public static Command Command()
    {
        var command = new Command("calendar", "Manage working-time calendars.");
        command.Add(List());
        command.Add(Show());
        command.Add(Add());
        command.Add(Remove());
        command.Add(SetDay());
        command.Add(SetBase());
        command.Add(AddException());
        command.Add(RemoveException());
        command.Add(AddWorkWeek());
        command.Add(RemoveWorkWeek());
        return command;
    }

    private static Argument<string> CalendarArg()
        => new("calendar") { Description = "Calendar name (case-insensitive)." };

    private static Command List()
    {
        var command = new Command("list", "List calendars.");
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (_, project) = context.OpenProject();
            if (context.Json)
            {
                context.WriteJson(project.Calendars.Select(JsonShapes.ForCalendar).ToList());
                return 0;
            }

            Render.Table(
                context.Out,
                ["Name", "Base", "Default"],
                [
                    .. project.Calendars.Select(IReadOnlyList<string> (c) =>
                    [
                        c.Name,
                        c.BaseCalendar?.Name ?? "",
                        ReferenceEquals(c, project.Calendar) ? "*" : "",
                    ]),
                ]);
            return 0;
        }));
        return command;
    }

    private static Command Show()
    {
        var calendarArg = CalendarArg();
        var command = new Command("show", "Print a calendar's week, exceptions, and work weeks.") { calendarArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (_, project) = context.OpenProject();
            var calendar = Parsers.CalendarByName(project, parseResult.GetRequiredValue(calendarArg));
            if (context.Json)
            {
                context.WriteJson(JsonShapes.ForCalendar(calendar));
                return 0;
            }

            var output = context.Out;
            Render.KeyValues(output,
            [
                ("name", calendar.Name),
                ("base", calendar.BaseCalendar?.Name ?? "(none)"),
                .. WeekDays.Select(day => (
                    Render.DayName(day),
                    calendar.DefaultWeek[day] is { } schedule ? Render.ScheduleText(schedule) : "inherit")),
            ]);
            foreach (var exception in calendar.Exceptions)
            {
                var range = Render.DateOnly(exception.Start)
                    + (exception.End is { } end && end != exception.Start ? ".." + Render.DateOnly(end) : "");
                var recur = exception.Recurrence is { } recurrence
                    ? " " + Render.RecurrenceSpec(recurrence) + (exception.Occurrences is { } n ? $" x{Render.Num(n)}" : "")
                    : "";
                output.WriteLine($"exception: {exception.Name}  {range}  {Render.ScheduleText(exception.Schedule)}{recur}");
            }

            foreach (var week in calendar.WorkWeeks)
            {
                var days = WeekDays
                    .Where(day => week.Pattern[day] is not null)
                    .Select(day => $"{Render.DayName(day)}={Render.ScheduleText(week.Pattern[day]!.Value)}");
                output.WriteLine(
                    $"workweek: {week.Name}  {Render.DateOnly(week.Start)}..{Render.DateOnly(week.End)}  {string.Join(" ", days)}");
            }

            return 0;
        }));
        return command;
    }

    private static Command Add()
    {
        var nameArg = new Argument<string>("name");
        var baseOpt = new Option<string?>("--base") { HelpName = "calendar", Description = "Derive from an existing calendar." };
        var presetOpt = new Option<string?>("--preset") { HelpName = "standard|24h|night-shift" };
        var command = new Command("add", "Add a calendar (default: standard weekday pattern).") { nameArg, baseOpt, presetOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var name = parseResult.GetRequiredValue(nameArg);
            var baseName = parseResult.GetValue(baseOpt);
            var preset = parseResult.GetValue(presetOpt);
            if (baseName is not null && preset is not null)
            {
                throw new CliException("--base and --preset are mutually exclusive");
            }

            var calendar = baseName is not null
                ? new WorkCalendar(name, Parsers.CalendarByName(project, baseName))
                : (preset?.Trim().ToUpperInvariant() ?? "STANDARD") switch
                {
                    "STANDARD" => WorkCalendar.CreateStandard(name),
                    "24H" => WorkCalendar.Create24Hours(name),
                    "NIGHT-SHIFT" => WorkCalendar.CreateNightShift(name),
                    _ => throw new CliException($"invalid --preset '{preset}'; use standard, 24h, or night-shift"),
                };
            project.AddCalendar(calendar);
            store.Save(project);
            context.Report(JsonShapes.ForCalendar(calendar), $"added calendar '{calendar.Name}'");
            return 0;
        }));
        return command;
    }

    private static Command Remove()
    {
        var calendarArg = CalendarArg();
        var command = new Command("remove", "Remove a calendar (must be unused).") { calendarArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var calendar = Parsers.CalendarByName(project, parseResult.GetRequiredValue(calendarArg));
            project.RemoveCalendar(calendar);
            store.Save(project);
            context.Report(new RemovedJson("calendar", calendar.Name), $"removed calendar '{calendar.Name}'");
            return 0;
        }));
        return command;
    }

    private static Command SetDay()
    {
        var calendarArg = CalendarArg();
        var dayArg = new Argument<string>("day") { Description = "Day of week: mon..sun." };
        var hoursArg = new Argument<string>("hours") { Description = "off, inherit, or 08:00-12:00,13:00-17:00." };
        var command = new Command("set-day", "Set a weekday's working hours in the default week.") { calendarArg, dayArg, hoursArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var calendar = Parsers.CalendarByName(project, parseResult.GetRequiredValue(calendarArg));
            calendar.SetDay(
                Parsers.DayOfWeekInput(parseResult.GetRequiredValue(dayArg)),
                Parsers.DayScheduleInput(parseResult.GetRequiredValue(hoursArg)));
            SaveAndReport(context, store, project, calendar, $"updated calendar '{calendar.Name}'");
            return 0;
        }));
        return command;
    }

    private static Command SetBase()
    {
        var calendarArg = CalendarArg();
        var baseArg = new Argument<string>("base") { Description = "Base calendar name, or 'none'." };
        var command = new Command("set-base", "Re-base a calendar onto another (or make it standalone).") { calendarArg, baseArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var calendar = Parsers.CalendarByName(project, parseResult.GetRequiredValue(calendarArg));
            var baseName = parseResult.GetRequiredValue(baseArg);
            calendar.SetBaseCalendar(
                string.Equals(baseName.Trim(), "none", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : Parsers.CalendarByName(project, baseName));
            SaveAndReport(context, store, project, calendar, $"updated calendar '{calendar.Name}'");
            return 0;
        }));
        return command;
    }

    private static Command AddException()
    {
        var calendarArg = CalendarArg();
        var nameArg = new Argument<string>("name") { Description = "Exception name (unique within the calendar)." };
        var fromOpt = new Option<string>("--from") { HelpName = "date", Required = true };
        var toOpt = new Option<string?>("--to") { HelpName = "date" };
        var hoursOpt = new Option<string?>("--hours") { HelpName = "spec", Description = "Working hours; default off." };
        var recurOpt = new Option<string?>("--recur") { HelpName = "spec", Description = "Recurrence, e.g. yearly-date:12-25." };
        var timesOpt = new Option<int?>("--times") { Description = "Number of occurrences (with --recur)." };
        var command = new Command("add-exception", "Add a calendar exception (holiday, special hours).")
        {
            calendarArg, nameArg, fromOpt, toOpt, hoursOpt, recurOpt, timesOpt,
        };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var calendar = Parsers.CalendarByName(project, parseResult.GetRequiredValue(calendarArg));
            var schedule = Parsers.DayScheduleInput(parseResult.GetValue(hoursOpt) ?? "off")
                ?? throw new CliException("'inherit' is not valid for exception hours");
            calendar.AddException(new CalendarException(
                parseResult.GetRequiredValue(nameArg),
                Parsers.DateOnlyInput(parseResult.GetRequiredValue(fromOpt)),
                parseResult.GetValue(toOpt) is { } to ? Parsers.DateOnlyInput(to) : null,
                schedule,
                parseResult.GetValue(recurOpt) is { } recur ? Parsers.RecurrenceInput(recur) : null,
                parseResult.GetValue(timesOpt)));
            SaveAndReport(context, store, project, calendar, $"added exception to calendar '{calendar.Name}'");
            return 0;
        }));
        return command;
    }

    private static Command RemoveException()
    {
        var calendarArg = CalendarArg();
        var nameArg = new Argument<string>("name");
        var command = new Command("remove-exception", "Remove a calendar exception by name.") { calendarArg, nameArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var calendar = Parsers.CalendarByName(project, parseResult.GetRequiredValue(calendarArg));
            var name = parseResult.GetRequiredValue(nameArg);
            var exception = calendar.Exceptions
                .FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new CliException($"no exception named '{name}' in calendar '{calendar.Name}'");
            calendar.RemoveException(exception);
            SaveAndReport(context, store, project, calendar, $"removed exception '{exception.Name}'");
            return 0;
        }));
        return command;
    }

    private static Command AddWorkWeek()
    {
        var calendarArg = CalendarArg();
        var nameArg = new Argument<string>("name") { Description = "Work-week name (unique within the calendar)." };
        var fromOpt = new Option<string>("--from") { HelpName = "date", Required = true };
        var toOpt = new Option<string>("--to") { HelpName = "date", Required = true };
        var dayOptions = WeekDays.ToDictionary(
            day => day,
            day => new Option<string?>("--" + Render.DayName(day)) { HelpName = "spec" });
        var command = new Command("add-workweek", "Add a dated work week overriding the default pattern.")
        {
            calendarArg, nameArg, fromOpt, toOpt,
        };
        foreach (var option in dayOptions.Values)
        {
            command.Add(option);
        }

        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var calendar = Parsers.CalendarByName(project, parseResult.GetRequiredValue(calendarArg));
            var pattern = WeeklyPattern.InheritAll;
            foreach (var (day, option) in dayOptions)
            {
                if (parseResult.GetValue(option) is { } spec)
                {
                    pattern = pattern.With(day, Parsers.DayScheduleInput(spec));
                }
            }

            calendar.AddWorkWeek(new WorkWeek(
                parseResult.GetRequiredValue(nameArg),
                Parsers.DateOnlyInput(parseResult.GetRequiredValue(fromOpt)),
                Parsers.DateOnlyInput(parseResult.GetRequiredValue(toOpt)),
                pattern));
            SaveAndReport(context, store, project, calendar, $"added work week to calendar '{calendar.Name}'");
            return 0;
        }));
        return command;
    }

    private static Command RemoveWorkWeek()
    {
        var calendarArg = CalendarArg();
        var nameArg = new Argument<string>("name");
        var command = new Command("remove-workweek", "Remove a work week by name.") { calendarArg, nameArg };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var calendar = Parsers.CalendarByName(project, parseResult.GetRequiredValue(calendarArg));
            var name = parseResult.GetRequiredValue(nameArg);
            var week = calendar.WorkWeeks
                .FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new CliException($"no work week named '{name}' in calendar '{calendar.Name}'");
            calendar.RemoveWorkWeek(week);
            SaveAndReport(context, store, project, calendar, $"removed work week '{week.Name}'");
            return 0;
        }));
        return command;
    }

    private static void SaveAndReport(
        CliContext context,
        Storage.SqliteProjectStore store,
        Project project,
        WorkCalendar calendar,
        string message)
    {
        project.Recalculate();
        store.Save(project);
        context.Report(JsonShapes.ForCalendar(calendar), message);
    }
}
