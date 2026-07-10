using System.Security.Claims;
using Project27.Core;
using Project27.Core.Commands;
using Project27.Core.Persistence;
using Project27.Server.Storage;
using Project27.Storage;

namespace Project27.Server;

public sealed record LockingOptions(TimeSpan StaleAfter);

/// <summary>The snapshot-oriented v1 API (docs/spec/06-server.md).</summary>
public static class ProjectEndpoints
{
    public static void MapProjectApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/me", (ClaimsPrincipal user) =>
            Results.Ok(new MeDto(UserId(user), user.FindFirstValue(ClaimTypes.Name) ?? UserId(user))));

        var projects = api.MapGroup("/projects");

        projects.MapGet("/", async (ClaimsPrincipal user, IServerStore store, LockingOptions locking, CancellationToken cancellationToken) =>
        {
            var list = new List<ProjectInfoDto>();
            foreach (var (project, role) in await store.ListProjects(UserId(user), cancellationToken))
            {
                list.Add(await Info(store, locking, project, role, cancellationToken));
            }

            return Results.Ok(list);
        });

        projects.MapPost("/", async (CreateProjectRequest request, ClaimsPrincipal user, IServerStore store, LockingOptions locking, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Problem(422, "A project needs a name.");
            }

            var start = request.Start ?? DateTime.Today.AddHours(8);
            var project = new Project(request.Name, start);
            project.Recalculate();
            var json = ProjectDocumentSerializer.Serialize(ProjectDocumentMapper.ToDocument(project));
            var record = new ServerProject(project.Id, request.Name, UserId(user), DateTime.UtcNow, Version: 1);
            await store.CreateProject(record, json, cancellationToken);
            return Results.Created(
                $"/api/projects/{record.Id:D}",
                await Info(store, locking, record, ProjectRole.Owner, cancellationToken));
        });

        projects.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, IServerStore store, LockingOptions locking, CancellationToken cancellationToken) =>
        {
            var (access, error) = await Authorize(store, id, user, ProjectRole.Reader, cancellationToken);
            return error ?? Results.Ok(await Info(store, locking, access!.Project, access.Role, cancellationToken));
        });

        projects.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, IServerStore store, CancellationToken cancellationToken) =>
        {
            var (_, error) = await Authorize(store, id, user, ProjectRole.Owner, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            await store.DeleteProject(id, cancellationToken);
            return Results.NoContent();
        });

        projects.MapGet("/{id:guid}/document", async (Guid id, ClaimsPrincipal user, IServerStore store, HttpResponse response, CancellationToken cancellationToken) =>
        {
            var (access, error) = await Authorize(store, id, user, ProjectRole.Reader, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            var json = await store.GetDocument(id, cancellationToken)
                ?? throw new InvalidOperationException($"Project {id:D} has no snapshot; the store is corrupt.");
            response.Headers.ETag = $"\"{access!.Project.Version}\"";
            response.Headers["X-Project-Version"] = access.Project.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Results.Text(json, "application/json");
        });

        projects.MapGet("/{id:guid}/schedule", async (Guid id, ClaimsPrincipal user, IServerStore store, CancellationToken cancellationToken) =>
        {
            var (access, error) = await Authorize(store, id, user, ProjectRole.Reader, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            var json = await store.GetDocument(id, cancellationToken)
                ?? throw new InvalidOperationException($"Project {id:D} has no snapshot; the store is corrupt.");
            var project = ProjectDocumentMapper.FromDocument(ProjectDocumentSerializer.Deserialize(json));
            project.Recalculate();
            return Results.Ok(ScheduleProjection.From(project, access!.Project.Version));
        });

        projects.MapPost("/{id:guid}/commands", async (Guid id, List<ProjectCommand> commands, ClaimsPrincipal user, IServerStore store, ProjectEventBroker broker, CancellationToken cancellationToken) =>
        {
            var (access, error) = await Authorize(store, id, user, ProjectRole.Editor, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            var userId = UserId(user);
            var held = await store.GetLock(id, cancellationToken);
            if (held is null || held.UserId != userId)
            {
                return Problem(409, held is null
                    ? "Check the project out before sending commands."
                    : $"The lock is held by '{held.UserId}'.");
            }

            var json = await store.GetDocument(id, cancellationToken)
                ?? throw new InvalidOperationException($"Project {id:D} has no snapshot; the store is corrupt.");
            var project = ProjectDocumentMapper.FromDocument(ProjectDocumentSerializer.Deserialize(json));
            IReadOnlyList<int?> createdUids;
            try
            {
                createdUids = CommandExecutor.ApplyAll(project, commands);
                project.Recalculate();
            }
            catch (Exception exception) when (exception is CommandException or InvalidOperationException)
            {
                return Problem(422, exception.Message);
            }

            var updated = ProjectDocumentSerializer.Serialize(ProjectDocumentMapper.ToDocument(project));
            var newVersion = await store.SaveSnapshot(id, access!.Project.Version, updated, project.Name, userId, DateTime.UtcNow, cancellationToken);
            if (newVersion is null)
            {
                return Problem(409, "Version conflict: the project changed concurrently.");
            }

            await store.TryAcquireLock(id, userId, DateTime.UtcNow, cancellationToken);
            broker.Publish(id, "checkin", new { version = newVersion.Value, user = userId });
            return Results.Ok(new CommandsResponse(newVersion.Value, createdUids, ScheduleProjection.From(project, newVersion.Value)));
        });

        projects.MapPost("/{id:guid}/checkout", async (Guid id, ClaimsPrincipal user, IServerStore store, LockingOptions locking, ProjectEventBroker broker, CancellationToken cancellationToken) =>
        {
            var (access, error) = await Authorize(store, id, user, ProjectRole.Editor, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            var now = DateTime.UtcNow;
            var userId = UserId(user);
            if (!await store.TryAcquireLock(id, userId, now, cancellationToken))
            {
                var held = await store.GetLock(id, cancellationToken);
                if (held is not null && IsStale(held, locking, now))
                {
                    await store.ReleaseLock(id, cancellationToken);
                    await store.TryAcquireLock(id, userId, now, cancellationToken);
                }
                else if (held is not null)
                {
                    return Problem(409, $"Project is checked out by '{held.UserId}' since {held.AcquiredAt:u}.");
                }
            }

            broker.Publish(id, "checkout", new { user = userId });
            var current = await store.GetLock(id, cancellationToken);
            return Results.Ok(new CheckoutResponse(access!.Project.Version, ToDto(current!, locking, now)));
        });

        projects.MapPut("/{id:guid}/document", async (Guid id, bool? keep, HttpRequest request, ClaimsPrincipal user, IServerStore store, ProjectEventBroker broker, CancellationToken cancellationToken) =>
        {
            var (access, error) = await Authorize(store, id, user, ProjectRole.Editor, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            var userId = UserId(user);
            var held = await store.GetLock(id, cancellationToken);
            if (held is null || held.UserId != userId)
            {
                return Problem(409, held is null
                    ? "Check the project out before checking in."
                    : $"The lock is held by '{held.UserId}'.");
            }

            if (!TryParseIfMatch(request, out var expectedVersion))
            {
                return Problem(409, "Check-in requires If-Match with the version returned by checkout.");
            }

            string json;
            using (var reader = new StreamReader(request.Body))
            {
                json = await reader.ReadToEndAsync(cancellationToken);
            }

            string name;
            try
            {
                var document = ProjectDocumentSerializer.Deserialize(json);
                var project = ProjectDocumentMapper.FromDocument(document);
                project.Recalculate();
                name = project.Name;
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                return Problem(422, $"The document is not a valid project: {exception.Message}");
            }

            var newVersion = await store.SaveSnapshot(id, expectedVersion, json, name, userId, DateTime.UtcNow, cancellationToken);
            if (newVersion is null)
            {
                return Problem(409, $"Version conflict: expected {expectedVersion}, project is at {access!.Project.Version}.");
            }

            if (keep == true)
            {
                await store.TryAcquireLock(id, userId, DateTime.UtcNow, cancellationToken);
            }
            else
            {
                await store.ReleaseLock(id, cancellationToken);
                broker.Publish(id, "lock-released", new { user = userId });
            }

            broker.Publish(id, "checkin", new { version = newVersion.Value, user = userId });
            return Results.Ok(new CheckinResponse(newVersion.Value));
        });

        projects.MapDelete("/{id:guid}/lock", async (Guid id, ClaimsPrincipal user, IServerStore store, LockingOptions locking, ProjectEventBroker broker, CancellationToken cancellationToken) =>
        {
            var (access, error) = await Authorize(store, id, user, ProjectRole.Editor, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            var held = await store.GetLock(id, cancellationToken);
            if (held is null)
            {
                return Results.NoContent();
            }

            var userId = UserId(user);
            var mayRelease = held.UserId == userId
                || access!.Role == ProjectRole.Owner
                || IsStale(held, locking, DateTime.UtcNow);
            if (!mayRelease)
            {
                return Problem(403, $"The lock is held by '{held.UserId}' and is not stale.");
            }

            await store.ReleaseLock(id, cancellationToken);
            broker.Publish(id, "lock-released", new { user = userId, stolenFrom = held.UserId == userId ? null : held.UserId });
            return Results.NoContent();
        });

        projects.MapGet("/{id:guid}/members", async (Guid id, ClaimsPrincipal user, IServerStore store, CancellationToken cancellationToken) =>
        {
            var (_, error) = await Authorize(store, id, user, ProjectRole.Reader, cancellationToken);
            return error ?? Results.Ok(
                (await store.GetMembers(id, cancellationToken)).Select(m => new MemberDto(m.UserId, m.Role)).ToList());
        });

        projects.MapPut("/{id:guid}/members/{userId}", async (Guid id, string userId, SetMemberRequest request, ClaimsPrincipal user, IServerStore store, CancellationToken cancellationToken) =>
        {
            var (_, error) = await Authorize(store, id, user, ProjectRole.Owner, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            if (request.Role != ProjectRole.Owner && await WouldLoseLastOwner(store, id, userId, cancellationToken))
            {
                return Problem(409, "A project must keep at least one owner.");
            }

            await store.SetMember(id, userId, request.Role, cancellationToken);
            return Results.Ok(new MemberDto(userId, request.Role));
        });

        projects.MapDelete("/{id:guid}/members/{userId}", async (Guid id, string userId, ClaimsPrincipal user, IServerStore store, CancellationToken cancellationToken) =>
        {
            var (_, error) = await Authorize(store, id, user, ProjectRole.Owner, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            if (await WouldLoseLastOwner(store, id, userId, cancellationToken))
            {
                return Problem(409, "A project must keep at least one owner.");
            }

            return await store.RemoveMember(id, userId, cancellationToken)
                ? Results.NoContent()
                : Problem(404, $"'{userId}' is not a member.");
        });

        projects.MapGet("/{id:guid}/events", async (Guid id, ClaimsPrincipal user, IServerStore store, ProjectEventBroker broker, HttpResponse response, CancellationToken cancellationToken) =>
        {
            var (_, error) = await Authorize(store, id, user, ProjectRole.Reader, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            response.Headers.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            await response.Body.FlushAsync(cancellationToken);
            await foreach (var @event in broker.Subscribe(id, cancellationToken))
            {
                await response.WriteAsync($"event: {@event.Kind}\ndata: {@event.Data}\n\n", cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
            }

            return Results.Empty;
        });
    }

    // -------------------------------------------------------------- helpers

    private sealed record Access(ServerProject Project, ProjectRole Role);

    private static async Task<(Access? Access, IResult? Error)> Authorize(
        IServerStore store,
        Guid id,
        ClaimsPrincipal user,
        ProjectRole required,
        CancellationToken cancellationToken)
    {
        var project = await store.GetProject(id, cancellationToken);
        if (project is null)
        {
            return (null, Problem(404, "No such project."));
        }

        var role = await store.GetRole(id, UserId(user), cancellationToken);
        if (role is null)
        {
            // Invisible to non-members: indistinguishable from absent.
            return (null, Problem(404, "No such project."));
        }

        if (role < required)
        {
            return (null, Problem(403, $"This action requires the {required} role; you are a {role}."));
        }

        return (new Access(project, role.Value), null);
    }

    private static async Task<ProjectInfoDto> Info(
        IServerStore store,
        LockingOptions locking,
        ServerProject project,
        ProjectRole role,
        CancellationToken cancellationToken)
    {
        var held = await store.GetLock(project.Id, cancellationToken);
        return new ProjectInfoDto(
            project.Id,
            project.Name,
            project.Version,
            project.CreatedBy,
            project.CreatedAt,
            role,
            held is null ? null : ToDto(held, locking, DateTime.UtcNow));
    }

    private static LockDto ToDto(ProjectLock held, LockingOptions locking, DateTime now)
        => new(held.UserId, held.AcquiredAt, held.RefreshedAt, IsStale(held, locking, now));

    private static bool IsStale(ProjectLock held, LockingOptions locking, DateTime now)
        => now - held.RefreshedAt > locking.StaleAfter;

    private static bool TryParseIfMatch(HttpRequest request, out int version)
    {
        version = 0;
        var raw = request.Headers.IfMatch.ToString().Trim().Trim('"');
        return raw.Length > 0
            && int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out version);
    }

    private static async Task<bool> WouldLoseLastOwner(IServerStore store, Guid id, string userId, CancellationToken cancellationToken)
    {
        var members = await store.GetMembers(id, cancellationToken);
        return members.Any(m => m.UserId == userId && m.Role == ProjectRole.Owner)
            && members.Count(m => m.Role == ProjectRole.Owner) == 1;
    }

    private static string UserId(ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub")
            ?? throw new InvalidOperationException("The authenticated principal has no user id claim.");

    private static IResult Problem(int status, string detail) => Results.Problem(statusCode: status, detail: detail);
}
