using System.Text.Json.Serialization;

namespace Api.Contracts;

public sealed class ColumnRequest
{
    public string? Name { get; set; }

    public string? Color { get; set; }

    public string? Description { get; set; }
}

public class VoteSettingsRequest
{
    public string Mode { get; set; } = "percent";

    public int Percent { get; set; } = 5;

    public int Minimum { get; set; } = 2;

    public int? Fixed { get; set; }
}

public sealed class UpdateVoteSettingsRequest : VoteSettingsRequest
{
    public int ExpectedVersion { get; set; }
}

public sealed class SetMyVotesRequest
{
    public int Count { get; set; }

    public int ExpectedVersion { get; set; }
}

public class MeetingSettingsRequest
{
    public string? Name { get; set; }

    public ColumnRequest[] Columns { get; set; } = [];

    public bool AnonymousCards { get; set; } = true;

    public bool PrivateCollection { get; set; } = true;

    public bool AnonymousVotes { get; set; } = true;

    public VoteSettingsRequest VoteSettings { get; set; } = new();

    public bool? VotingEnabled { get; set; }
}

public sealed class UpdateMeetingSettingsRequest : MeetingSettingsRequest
{
    public int ExpectedVersion { get; set; }
}

public sealed class CreateMeetingRequest : MeetingSettingsRequest
{
    public bool StartCollecting { get; set; }
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
    int Position,
    string? Description);

public sealed record ParticipantResponse(
    string Id,
    string Subject,
    string DisplayName,
    string Status,
    bool IsOnline,
    bool IsReady);

public sealed record MeetingCardResponse(
    string Id,
    string ColumnId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AuthorId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AuthorDisplayName,
    bool IsMine,
    bool IsHidden,
    int Version,
    string? GroupId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OriginalColumnColor);

public sealed record MeetingGroupResponse(string Id, string ColumnId, string Name);

public sealed record MeetingTopicVoterResponse(string UserId, string DisplayName, int Count);

public sealed record MeetingTopicResponse(
    string Id,
    string Kind,
    string SourceId,
    int TotalVotes,
    int MyVotes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<MeetingTopicVoterResponse>? Voters);

public sealed record MeetingVotingResponse(
    int OriginalCardCount,
    int Budget,
    int Spent,
    int Remaining,
    int BallotVersion);

public sealed record MyVotesResponse(int Version, int BallotVersion, int Remaining);

public sealed record MeetingTimerResponse(
    string RunId,
    string Phase,
    DateTimeOffset EndsAtUtc,
    DateTimeOffset ServerNowUtc);

public sealed class StartTimerRequest
{
    public int Seconds { get; set; }
    public Guid OperationId { get; set; }
}

public sealed class AddTimerRequest
{
    public Guid RunId { get; set; }
    public int Seconds { get; set; }
    public Guid OperationId { get; set; }
}

public sealed class StopTimerRequest
{
    public Guid RunId { get; set; }
    public Guid OperationId { get; set; }
}

public sealed record DiscussionTopicResponse(string TopicId, int Position, string Status);

public sealed record TopicCommentResponse(string Id, string TopicId, string Text);

public sealed record TopicDecisionResponse(string Id, string TopicId, string Text);

public sealed record ActionItemResponse(
    string Id,
    string TopicId,
    string Text,
    string? AssigneeId,
    string ExportStatus,
    string? ExternalRequestId,
    string? IssueKey,
    string? IssueUrl);

public sealed record JiraExportResponse(
    string Status,
    string ExternalRequestId,
    string? IssueKey,
    string? IssueUrl,
    int Version);

public sealed record MeetingDiscussionResponse(
    string? CurrentTopicId,
    IReadOnlyList<DiscussionTopicResponse> Queue,
    IReadOnlyList<TopicCommentResponse> Comments,
    IReadOnlyList<TopicDecisionResponse> Decisions,
    IReadOnlyList<ActionItemResponse> Actions);

public sealed record MeetingIntegrationsResponse(bool Ai, bool Jira);

public sealed record MeetingReadinessResponse(int ReadyCount, int TotalCount, bool MyReady);

public sealed record MeetingTotalsResponse(
    DateTimeOffset ClosedAtUtc,
    int ParticipantCount,
    int CardCount,
    int TopicCount,
    int VoteCount);

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
    bool VotingEnabled,
    VoteSettingsResponse VoteSettings,
    IReadOnlyList<ColumnResponse> Columns,
    IReadOnlyList<ParticipantResponse> Participants,
    IReadOnlyList<MeetingCardResponse> Cards,
    IReadOnlyList<MeetingGroupResponse> Groups,
    IReadOnlyList<MeetingTopicResponse> Topics,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MeetingVotingResponse? Voting,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MeetingDiscussionResponse? Discussion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MeetingTimerResponse? Timer,
    MeetingIntegrationsResponse Integrations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MeetingReadinessResponse? Readiness,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MeetingTotalsResponse? Totals);

public sealed class SelectCurrentTopicRequest
{
    public Guid TopicId { get; set; }
    public int ExpectedVersion { get; set; }
}

public sealed class SetTopicStatusRequest
{
    public string Status { get; set; } = string.Empty;
    public bool Advance { get; set; }
    public int ExpectedVersion { get; set; }
}

public sealed class CreateCommentRequest
{
    public string? Text { get; set; }
    public int ExpectedVersion { get; set; }
    public Guid OperationId { get; set; }
}

public sealed class UpdateCommentRequest
{
    public string? Text { get; set; }
    public int ExpectedVersion { get; set; }
}

public sealed class CreateDecisionRequest
{
    public string? Text { get; set; }
    public int ExpectedVersion { get; set; }
    public Guid OperationId { get; set; }
}

public sealed class UpdateDecisionRequest
{
    public string? Text { get; set; }
    public int ExpectedVersion { get; set; }
}

public sealed class CreateActionRequest
{
    public string? Text { get; set; }
    public Guid? AssigneeId { get; set; }
    public int ExpectedVersion { get; set; }
    public Guid OperationId { get; set; }
}

public sealed class UpdateActionRequest
{
    public string? Text { get; set; }
    public Guid? AssigneeId { get; set; }
    public int ExpectedVersion { get; set; }
}

public sealed class DeleteDiscussionEntryRequest
{
    public int ExpectedVersion { get; set; }
}

public sealed record DiscussionEntryCreatedResponse(string Id, int Version);

public sealed class MoveCardRequest
{
    public string SourceKind { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public string TargetKind { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public int ExpectedBoardVersion { get; set; }
    public Guid OperationId { get; set; }
}

public sealed class RenameGroupRequest
{
    public string? Name { get; set; }
    public int ExpectedBoardVersion { get; set; }
}

public sealed class ApplyGroupProposalRequest
{
    public Guid ProposalId { get; set; }
    public int ExpectedBoardVersion { get; set; }
    public Guid OperationId { get; set; }
}

public sealed record BoardVersionResponse(int Version, int BoardVersion);

public sealed record SuggestedGroupResponse(string Name, string ColumnId, IReadOnlyList<string> CardIds);

public sealed record GroupProposalResponse(
    string Id,
    int BoardVersion,
    IReadOnlyList<SuggestedGroupResponse> Groups);

public sealed class CreateCardRequest
{
    public Guid ColumnId { get; set; }

    public string? Text { get; set; }

    public bool Anonymous { get; set; }

    public Guid OperationId { get; set; }
}

public sealed class UpdateCardRequest
{
    public string? Text { get; set; }

    public int ExpectedVersion { get; set; }
}

public sealed class DeleteCardRequest
{
    public int ExpectedVersion { get; set; }
}

public sealed record CardCreatedResponse(string Id, int Version);

public sealed class SetReadinessRequest
{
    public bool Ready { get; set; }

    public string Phase { get; set; } = string.Empty;
}

public sealed class AdvanceMeetingRequest
{
    public string FromPhase { get; set; } = string.Empty;

    public int ExpectedVersion { get; set; }

    public Guid OperationId { get; set; }
}

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
