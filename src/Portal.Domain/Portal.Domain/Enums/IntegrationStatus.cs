namespace Portal.Domain.Enums;

public enum IntegrationStatus
{
    Pending = 0,
    Processing = 1,
    RetryWaiting = 2,
    Unknown = 3,
    Succeeded = 4,
    Rejected = 5,
    DeadLetter = 6
}