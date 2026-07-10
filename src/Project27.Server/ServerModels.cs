namespace Project27.Server;

/// <summary>Per-project role; higher values include lower ones' permissions.</summary>
public enum ProjectRole
{
    Reader,
    Editor,
    Owner,
}

public sealed record ServerProject(Guid Id, string Name, string CreatedBy, DateTime CreatedAt, int Version);

public sealed record ProjectLock(string UserId, DateTime AcquiredAt, DateTime RefreshedAt);

public sealed record ProjectMember(string UserId, ProjectRole Role);

// ------------------------------------------------------------------ API DTOs

public sealed record CreateProjectRequest(string Name, DateTime? Start);

public sealed record ProjectInfoDto(
    Guid Id,
    string Name,
    int Version,
    string CreatedBy,
    DateTime CreatedAt,
    ProjectRole Role,
    LockDto? Lock);

public sealed record LockDto(string UserId, DateTime AcquiredAt, DateTime RefreshedAt, bool Stale);

public sealed record CheckoutResponse(int Version, LockDto Lock);

public sealed record CheckinResponse(int Version);

public sealed record MemberDto(string UserId, ProjectRole Role);

public sealed record SetMemberRequest(ProjectRole Role);

public sealed record MeDto(string Id, string Name);
