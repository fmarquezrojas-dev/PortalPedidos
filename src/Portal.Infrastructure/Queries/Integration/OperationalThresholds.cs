namespace Portal.Infrastructure.Queries.Integration;

internal static class OperationalThresholds
{
    public static readonly TimeSpan PendingWarningAge = TimeSpan.FromMinutes(5);
}
