namespace EirService.HelpRequests.Domain.Entities;

public enum HelpRequestStatus
{
    Pending = 0,
    Active = 1,
    AwaitingConfirmation = 2,
    Cancelled = 4,
    AwaitingFaq = 5,
    CompletedByFaq = 6,
    CompletedByDigital = 7,
    CompletedByPhysical = 8
}
