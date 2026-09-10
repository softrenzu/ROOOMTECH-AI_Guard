namespace Rooomtech.AIGuard.Core;

public sealed record GuardPolicy
{
    public List<string> ProtectedPaths { get; init; } = [];
    public List<AllowedApplication> AllowedApplications { get; init; } = [];
    public bool DenyByDefault { get; init; } = true;
}

public sealed record AllowedApplication
{
    public required string ExecutablePath { get; init; }
    public string? Sha256 { get; init; }
    public string? PublisherThumbprint { get; init; }
}

public sealed record AccessRequest
{
    public required string FilePath { get; init; }
    public required string ProcessPath { get; init; }
    public string Operation { get; init; } = "read";
    public int? ProcessId { get; init; }
    public string? ProcessSha256 { get; init; }
}

public sealed record AccessDecision
{
    public required bool IsProtected { get; init; }
    public required bool Allowed { get; init; }
    public required string Reason { get; init; }
}

public sealed record AuditRecord
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required AccessRequest Request { get; init; }
    public required AccessDecision Decision { get; init; }
}
