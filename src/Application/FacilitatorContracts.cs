namespace Application;

public sealed record FacilitatorElection(
    Guid MeetingId,
    Guid? FacilitatorId,
    DateTimeOffset DeadlineUtc,
    int Generation);
