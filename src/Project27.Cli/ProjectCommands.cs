using System.CommandLine;
using Project27.Cli.Completion;
using Project27.Core;
using Project27.Core.Time;
using Project27.Storage;

namespace Project27.Cli;

internal static class ProjectCommands
{
    public static Command Init()
    {
        var nameArg = new Argument<string>("name") { Description = "Project name." };
        var startOpt = new Option<string?>("--start")
        {
            Description = "Project start date (default: today).",
            HelpName = "date",
        };
        var command = new Command("init", "Create a new .p27 project file.") { nameArg, startOpt };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            if (context.IsRemote)
            {
                throw new CliException("init creates local files; with --server use `p27 project create`");
            }

            var name = parseResult.GetRequiredValue(nameArg);
            var settings = new TimeSettings();
            var startText = parseResult.GetValue(startOpt);
            var start = startText is null
                ? DateOnly.FromDateTime(DateTime.Today).ToDateTime(settings.DefaultStartTime)
                : Parsers.DateInput(startText, settings, finishLike: false);
            var path = context.ExplicitFile ?? name + SqliteProjectStore.FileExtension;

            var project = new Project(name, start);
            project.Recalculate();
            SqliteProjectStore.Create(path, project);
            context.Report(JsonShapes.ForProject(project), $"created {path}");
            return 0;
        }));
        return command;
    }

    public static Command Project()
    {
        var command = new Command("project", "Show or change project settings.");
        command.Add(Show());
        command.Add(Set());
        command.Add(ServerCommands.ProjectList());
        command.Add(ServerCommands.ProjectCreate());
        command.Add(ServerCommands.ProjectDelete());
        return command;
    }

    public static Command Schedule()
    {
        var recalc = new Command("recalc", "Recompute the schedule and save the file.");
        recalc.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject(); // Load() recalculates
            store.Save(project);
            context.Report(
                JsonShapes.ForProject(project),
                $"schedule: {Render.Date(project.StartDate)} -> {Render.Date(project.FinishDate)}");
            return 0;
        }));
        var afterOpt = new Option<string?>("--after") { HelpName = "date", Description = "Cutoff; default: the status date." };
        var reschedule = new Command("reschedule", "Move uncompleted work past the status date (or --after).") { afterOpt };
        reschedule.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var after = parseResult.GetValue(afterOpt) is { } text
                ? Parsers.DateInput(text, project.TimeSettings, finishLike: false)
                : (DateTime?)null;
            project.RescheduleUncompletedWork(after);
            store.Save(project);
            context.Report(
                JsonShapes.ForProject(project),
                $"rescheduled uncompleted work after {Render.Date(after ?? project.StatusDate)}");
            return 0;
        }));

        return new Command("schedule", "Scheduling operations.") { recalc, reschedule };
    }

    private static Command Show()
    {
        var command = new Command("show", "Print project settings and schedule anchors.");
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (_, project) = context.OpenProject();
            WriteProject(context, project);
            return 0;
        }));
        return command;
    }

    private static Command Set()
    {
        var nameOpt = new Option<string?>("--name") { Description = "Rename the project." };
        var startOpt = new Option<string?>("--start") { HelpName = "date", Description = "Project start (schedule-from-start anchor)." };
        var finishOpt = new Option<string?>("--finish") { HelpName = "date", Description = "Project finish (schedule-from-finish anchor)." };
        var scheduleFromOpt = new Option<string?>("--schedule-from") { HelpName = "start|finish" }
            .Suggests(CompletionValues.ScheduleFrom);
        var calendarOpt = new Option<string?>("--calendar") { HelpName = "name", Description = "Project default calendar." }
            .Suggests(CompletionValues.Calendars);
        var minutesPerDayOpt = new Option<int?>("--minutes-per-day");
        var minutesPerWeekOpt = new Option<int?>("--minutes-per-week");
        var daysPerMonthOpt = new Option<decimal?>("--days-per-month");
        var weekStartsOnOpt = new Option<string?>("--week-starts-on") { HelpName = "day" }.Suggests(CompletionValues.DaysOfWeek);
        var dayStartOpt = new Option<string?>("--day-start") { HelpName = "HH:mm", Description = "Default start time for date-only inputs." };
        var dayEndOpt = new Option<string?>("--day-end") { HelpName = "HH:mm", Description = "Default end time for date-only inputs." };
        var statusDateOpt = new Option<string?>("--status-date") { HelpName = "date|none", Description = "Progress reporting date for EVM and rescheduling." };
        var criticalSlackOpt = new Option<string?>("--critical-slack")
        {
            HelpName = "duration",
            Description = "Total-slack threshold at or below which tasks are critical.",
        };

        var command = new Command("set", "Change project settings; recalculates and saves.")
        {
            nameOpt, startOpt, finishOpt, scheduleFromOpt, calendarOpt, minutesPerDayOpt, minutesPerWeekOpt,
            daysPerMonthOpt, weekStartsOnOpt, dayStartOpt, dayEndOpt, criticalSlackOpt, statusDateOpt,
        };
        command.SetAction(parseResult => CliRoot.Run(parseResult, context =>
        {
            var (store, project) = context.OpenProject();
            var settings = project.TimeSettings;

            // Time settings first so date-only --start/--finish pick up new default times.
            if (parseResult.GetValue(minutesPerDayOpt) is { } minutesPerDay)
            {
                settings.MinutesPerDay = minutesPerDay;
            }

            if (parseResult.GetValue(minutesPerWeekOpt) is { } minutesPerWeek)
            {
                settings.MinutesPerWeek = minutesPerWeek;
            }

            if (parseResult.GetValue(daysPerMonthOpt) is { } daysPerMonth)
            {
                settings.DaysPerMonth = daysPerMonth;
            }

            if (parseResult.GetValue(weekStartsOnOpt) is { } weekStartsOn)
            {
                settings.WeekStartsOn = Parsers.DayOfWeekInput(weekStartsOn);
            }

            if (parseResult.GetValue(dayStartOpt) is { } dayStart)
            {
                settings.DefaultStartTime = Parsers.TimeInput(dayStart);
            }

            if (parseResult.GetValue(dayEndOpt) is { } dayEnd)
            {
                settings.DefaultEndTime = Parsers.TimeInput(dayEnd);
            }

            if (parseResult.GetValue(nameOpt) is { } name)
            {
                project.Name = name;
            }

            if (parseResult.GetValue(scheduleFromOpt) is { } scheduleFrom)
            {
                project.ScheduleFrom = scheduleFrom.Trim().ToUpperInvariant() switch
                {
                    "START" => ScheduleFrom.ProjectStart,
                    "FINISH" => ScheduleFrom.ProjectFinish,
                    _ => throw new CliException($"invalid --schedule-from '{scheduleFrom}'; use start or finish"),
                };
            }

            if (parseResult.GetValue(startOpt) is { } start)
            {
                project.StartDate = Parsers.DateInput(start, settings, finishLike: false);
            }

            if (parseResult.GetValue(finishOpt) is { } finish)
            {
                project.FinishDate = Parsers.DateInput(finish, settings, finishLike: true);
            }

            if (parseResult.GetValue(calendarOpt) is { } calendar)
            {
                project.Calendar = Parsers.CalendarByName(project, calendar);
            }

            if (parseResult.GetValue(statusDateOpt) is { } statusDate)
            {
                project.StatusDate = string.Equals(statusDate.Trim(), "none", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : Parsers.DateInput(statusDate, settings, finishLike: true);
            }

            if (parseResult.GetValue(criticalSlackOpt) is { } criticalSlack)
            {
                project.CriticalSlackThresholdMinutes = Parsers.DurationInput(criticalSlack).ToMinutes(settings);
            }

            project.Recalculate();
            store.Save(project);
            if (context.Json)
            {
                context.WriteJson(JsonShapes.ForProject(project));
            }
            else
            {
                context.Out.WriteLine($"updated project '{project.Name}'");
            }

            return 0;
        }));
        return command;
    }

    private static void WriteProject(CliContext context, Project project)
    {
        if (context.Json)
        {
            context.WriteJson(JsonShapes.ForProject(project));
            return;
        }

        var settings = project.TimeSettings;
        Render.KeyValues(context.Out,
        [
            ("name", project.Name),
            ("schedule from", project.ScheduleFrom == ScheduleFrom.ProjectStart ? "start" : "finish"),
            ("start", Render.Date(project.StartDate)),
            ("finish", Render.Date(project.FinishDate)),
            ("calendar", project.Calendar.Name),
            ("critical slack", Render.MinutesAsDays(project.CriticalSlackThresholdMinutes, settings)!),
            ("minutes/day", Render.Num(settings.MinutesPerDay)),
            ("minutes/week", Render.Num(settings.MinutesPerWeek)),
            ("days/month", Render.Num(settings.DaysPerMonth)),
            ("week starts on", Render.DayName(settings.WeekStartsOn)),
            ("default day", $"{Render.Time(settings.DefaultStartTime)}-{Render.Time(settings.DefaultEndTime)}"),
            ("tasks", Render.Num(project.Tasks.Count)),
            ("calendars", string.Join(", ", project.Calendars.Select(c => c.Name))),
            ("resources", Render.Num(project.Resources.Count)),
            ("work", Render.WorkHours(project.TotalWorkMinutes)),
            ("cost", Render.Num(project.TotalCost)),
        ]);
    }
}
