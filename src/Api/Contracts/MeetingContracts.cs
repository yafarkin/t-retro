namespace Api.Contracts;

public sealed class ColumnRequest
{
    public string? Name { get; set; }

    public string? Color { get; set; }
}

public sealed class VoteSettingsRequest
{
    public string Mode { get; set; } = "percent";

    public int Percent { get; set; } = 5;

    public int Minimum { get; set; } = 2;

    public int? Fixed { get; set; }
}

public class MeetingSettingsRequest
{
    public string? Name { get; set; }

    public ColumnRequest[] Columns { get; set; } = [];

    public bool AnonymousCards { get; set; } = true;

    public bool PrivateCollection { get; set; } = true;

    public bool AnonymousVotes { get; set; } = true;

    public VoteSettingsRequest VoteSettings { get; set; } = new();
}

public sealed class UpdateMeetingSettingsRequest : MeetingSettingsRequest
{
    public int ExpectedVersion { get; set; }
}

public sealed record MeetingCreatedResponse(string Id, int Version);

public sealed record MeetingSummaryResponse(
    string Id,
    string Name,
    string Phase,
    DateTimeOffset CreatedAtUtc);

public sealed record VoteSettingsResponse(
    string Mode,
    int Percent,
    int Minimum,
    int? Fixed);

public sealed record ColumnResponse(
    string Id,
    string Name,
    string Color,
    int Position);

public sealed record ParticipantResponse(
    string Id,
    string Subject,
    string DisplayName,
    string Status,
    bool IsOnline);

public sealed record MeetingStateResponse(
    DateTimeOffset ServerNowUtc,
    string Id,
    string Name,
    string Phase,
    int Version,
    int BoardVersion,
    bool CanManage,
    string? FacilitatorId,
    bool AnonymousCards,
    bool PrivateCollection,
    bool AnonymousVotes,
    VoteSettingsResponse VoteSettings,
    IReadOnlyList<ColumnResponse> Columns,
    IReadOnlyList<ParticipantResponse> Participants);

public sealed record VersionResponse(int Version);

public sealed record InviteResponse(string Url, int Version);

public sealed record JoinRequest(string? Token);

public sealed record JoinResponse(string MeetingId);

public sealed class TemplateRequest
{
    public string? Name { get; set; }

    public string Visibility { get; set; } = "personal";

    public ColumnRequest[] Columns { get; set; } = [];
}

public sealed record TemplateCreatedResponse(string Id);

public sealed record TemplateResponse(
    string Id,
    string OwnerId,
    string Name,
    string Visibility,
    IReadOnlyList<ColumnResponse> Columns);
