using System.Data;
using System.Text.Json;
using Application;
using Dapper;
using Domain;

namespace Infrastructure.Persistence;

public sealed class GroupingRepository(RelationalConnectionFactory connections) : IGroupingRepository
{
    private static readonly JsonSerializerOptions ProposalJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MeetingCommandResult> MoveAsync(
        Guid meetingId,
        Guid userId,
        BoardItemKind sourceKind,
        Guid sourceId,
        BoardItemKind targetKind,
        Guid targetId,
        int expectedBoardVersion,
        Guid operationId,
        string fingerprint,
        Guid newGroupId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connections.BeginMeetingTransactionAsync(connection, meetingId, cancellationToken);
        var state = await GetStateAsync(connection, transaction, meetingId, userId);
        var accessError = CheckMemberAccess(state);
        if (accessError is not null)
        {
            return accessError;
        }

        var replay = await FindReplayAsync(
            connection,
            transaction,
            meetingId,
            userId,
            operationId,
            "Move",
            fingerprint);
        if (replay is not null)
        {
            return replay;
        }

        var phaseError = CheckPhaseAndVersion(state, expectedBoardVersion);
        if (phaseError is not null)
        {
            return phaseError;
        }

        var current = await LoadStructureAsync(connection, transaction, meetingId);
        BoardStructure updated;
        try
        {
            updated = current.Move(sourceKind, sourceId, targetKind, targetId, newGroupId);
        }
        catch (KeyNotFoundException)
        {
            return new MeetingCommandResult(MeetingCommandStatus.NotFound);
        }

        if (ReferenceEquals(current, updated))
        {
            return new MeetingCommandResult(
                MeetingCommandStatus.Success,
                state!.Version,
                IsReplay: true,
                BoardVersion: state.BoardVersion);
        }

        await ApplyDifferenceAsync(connection, transaction, meetingId, current, updated);
        var newVersion = state!.Version + 1;
        var newBoardVersion = state.BoardVersion + 1;
        var affected = await UpdateVersionsAsync(
            connection,
            transaction,
            meetingId,
            state.Version,
            expectedBoardVersion,
            newVersion,
            newBoardVersion,
            nowUtc);
        if (affected == 0)
        {
            return new MeetingCommandResult(MeetingCommandStatus.VersionConflict);
        }

        var createdGroup = updated.Groups.FirstOrDefault(group => current.Groups.All(item => item.Id != group.Id));
        await SaveReplayAsync(
            connection,
            transaction,
            meetingId,
            userId,
            operationId,
            "Move",
            fingerprint,
            createdGroup?.Id,
            newVersion,
            newBoardVersion,
            nowUtc);
        await transaction.CommitAsync(cancellationToken);
        return new MeetingCommandResult(
            MeetingCommandStatus.Success,
            newVersion,
            createdGroup?.Id,
            BoardVersion: newBoardVersion);
    }

    public async Task<MeetingCommandResult> RenameGroupAsync(
        Guid meetingId,
        Guid userId,
        Guid groupId,
        string name,
        int expectedBoardVersion,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connections.BeginMeetingTransactionAsync(connection, meetingId, cancellationToken);
        var state = await GetStateAsync(connection, transaction, meetingId, userId);
        var accessError = CheckMemberAccess(state);
        if (accessError is not null)
        {
            return accessError;
        }

        var phaseError = CheckPhaseAndVersion(state, expectedBoardVersion);
        if (phaseError is not null)
        {
            return phaseError;
        }

        var currentName = await connection.ExecuteScalarAsync<string?>(
            "SELECT name FROM groups WHERE meeting_id = @MeetingId AND id = @GroupId;",
            new { MeetingId = meetingId, GroupId = groupId },
            transaction);
        if (currentName is null)
        {
            return new MeetingCommandResult(MeetingCommandStatus.NotFound);
        }

        if (currentName == name)
        {
            return new MeetingCommandResult(
                MeetingCommandStatus.Success,
                state!.Version,
                IsReplay: true,
                BoardVersion: state.BoardVersion);
        }

        await connection.ExecuteAsync(
            "UPDATE groups SET name = @Name WHERE meeting_id = @MeetingId AND id = @GroupId;",
            new { MeetingId = meetingId, GroupId = groupId, Name = name },
            transaction);
        var newVersion = state!.Version + 1;
        var newBoardVersion = state.BoardVersion + 1;
        var affected = await UpdateVersionsAsync(
            connection,
            transaction,
            meetingId,
            state.Version,
            expectedBoardVersion,
            newVersion,
            newBoardVersion,
            nowUtc);
        if (affected == 0)
        {
            return new MeetingCommandResult(MeetingCommandStatus.VersionConflict);
        }

        await transaction.CommitAsync(cancellationToken);
        return new MeetingCommandResult(
            MeetingCommandStatus.Success,
            newVersion,
            groupId,
            BoardVersion: newBoardVersion);
    }

    public async Task<MeetingCommandResult> SaveProposalAsync(
        Guid meetingId,
        Guid facilitatorId,
        GroupProposal proposal,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connections.BeginMeetingTransactionAsync(connection, meetingId, cancellationToken);
        var state = await GetStateAsync(connection, transaction, meetingId, facilitatorId);
        var accessError = CheckFacilitatorAccess(state, facilitatorId);
        if (accessError is not null)
        {
            return accessError;
        }

        var phaseError = CheckPhaseAndVersion(state, proposal.BoardVersion);
        if (phaseError is not null)
        {
            return phaseError;
        }

        await connection.ExecuteAsync(
            """
            INSERT INTO ai_proposals (meeting_id, id, facilitator_id, board_version, payload_json, created_at_utc)
            VALUES (@MeetingId, @Id, @FacilitatorId, @BoardVersion, @Payload, @CreatedAtUtc);
            """,
            new
            {
                MeetingId = meetingId,
                Id = proposal.Id,
                FacilitatorId = facilitatorId,
                proposal.BoardVersion,
                Payload = JsonSerializer.Serialize(proposal.Groups, ProposalJsonOptions),
                CreatedAtUtc = nowUtc,
            },
            transaction);
        await transaction.CommitAsync(cancellationToken);
        return new MeetingCommandResult(
            MeetingCommandStatus.Success,
            state!.Version,
            proposal.Id,
            BoardVersion: state.BoardVersion);
    }

    public async Task<MeetingCommandResult> ApplyProposalAsync(
        Guid meetingId,
        Guid facilitatorId,
        Guid proposalId,
        int expectedBoardVersion,
        Guid operationId,
        string fingerprint,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connections.BeginMeetingTransactionAsync(connection, meetingId, cancellationToken);
        var state = await GetStateAsync(connection, transaction, meetingId, facilitatorId);
        var accessError = CheckFacilitatorAccess(state, facilitatorId);
        if (accessError is not null)
        {
            return accessError;
        }

        var replay = await FindReplayAsync(
            connection,
            transaction,
            meetingId,
            facilitatorId,
            operationId,
            "ApplyAiProposal",
            fingerprint);
        if (replay is not null)
        {
            return replay;
        }

        var phaseError = CheckPhaseAndVersion(state, expectedBoardVersion);
        if (phaseError is not null)
        {
            return phaseError;
        }

        var proposal = await connection.QuerySingleOrDefaultAsync<ProposalRow>(
            """
            SELECT board_version AS BoardVersion, payload_json AS PayloadJson
            FROM ai_proposals
            WHERE meeting_id = @MeetingId AND id = @ProposalId AND facilitator_id = @FacilitatorId;
            """,
            new { MeetingId = meetingId, ProposalId = proposalId, FacilitatorId = facilitatorId },
            transaction);
        if (proposal is null)
        {
            return new MeetingCommandResult(MeetingCommandStatus.NotFound);
        }

        if (proposal.BoardVersion != expectedBoardVersion)
        {
            return new MeetingCommandResult(MeetingCommandStatus.VersionConflict);
        }

        var suggestions = JsonSerializer.Deserialize<IReadOnlyList<SuggestedGroup>>(proposal.PayloadJson, ProposalJsonOptions)
            ?? throw new InvalidOperationException("Stored AI proposal is invalid.");
        var current = await LoadStructureAsync(connection, transaction, meetingId);
        BoardStructure updated;
        try
        {
            updated = current.ApplySuggestions(suggestions, Guid.NewGuid);
        }
        catch (KeyNotFoundException)
        {
            return new MeetingCommandResult(MeetingCommandStatus.NotFound);
        }

        if (ReferenceEquals(current, updated))
        {
            await SaveReplayAsync(
                connection,
                transaction,
                meetingId,
                facilitatorId,
                operationId,
                "ApplyAiProposal",
                fingerprint,
                null,
                state!.Version,
                state.BoardVersion,
                nowUtc);
            await transaction.CommitAsync(cancellationToken);
            return new MeetingCommandResult(
                MeetingCommandStatus.Success,
                state.Version,
                IsReplay: true,
                BoardVersion: state.BoardVersion);
        }

        await ApplyDifferenceAsync(connection, transaction, meetingId, current, updated);
        var newVersion = state!.Version + 1;
        var newBoardVersion = state.BoardVersion + 1;
        var affected = await UpdateVersionsAsync(
            connection,
            transaction,
            meetingId,
            state.Version,
            expectedBoardVersion,
            newVersion,
            newBoardVersion,
            nowUtc);
        if (affected == 0)
        {
            return new MeetingCommandResult(MeetingCommandStatus.VersionConflict);
        }

        await SaveReplayAsync(
            connection,
            transaction,
            meetingId,
            facilitatorId,
            operationId,
            "ApplyAiProposal",
            fingerprint,
            proposalId,
            newVersion,
            newBoardVersion,
            nowUtc);
        await transaction.CommitAsync(cancellationToken);
        return new MeetingCommandResult(
            MeetingCommandStatus.Success,
            newVersion,
            proposalId,
            BoardVersion: newBoardVersion);
    }

    private static async Task<BoardStructure> LoadStructureAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId)
    {
        var parameters = new { MeetingId = meetingId };
        var columnIds = (await connection.QueryAsync<Guid>(
            "SELECT id FROM columns WHERE meeting_id = @MeetingId ORDER BY position;",
            parameters,
            transaction)).ToArray();
        var cards = (await connection.QueryAsync<BoardCardRow>(
            "SELECT id AS Id, column_id AS ColumnId, group_id AS GroupId, text AS Text FROM cards WHERE meeting_id = @MeetingId ORDER BY created_at_utc, id;",
            parameters,
            transaction))
            .Select(row => new BoardCard(
                row.Id,
                row.ColumnId,
                row.GroupId,
                row.Text))
            .ToArray();
        var groups = (await connection.QueryAsync<BoardGroupRow>(
            "SELECT id AS Id, column_id AS ColumnId, name AS Name FROM groups WHERE meeting_id = @MeetingId;",
            parameters,
            transaction))
            .Select(row => new BoardGroup(row.Id, row.ColumnId, row.Name))
            .ToArray();
        return new BoardStructure(columnIds, cards, groups);
    }

    private static async Task ApplyDifferenceAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId,
        BoardStructure current,
        BoardStructure updated)
    {
        foreach (var group in updated.Groups.Where(group => current.Groups.All(item => item.Id != group.Id)))
        {
            await connection.ExecuteAsync(
                "INSERT INTO groups (meeting_id, id, column_id, name) VALUES (@MeetingId, @Id, @ColumnId, @Name);",
                new
                {
                    MeetingId = meetingId,
                    Id = group.Id,
                    ColumnId = group.ColumnId,
                    group.Name,
                },
                transaction);
        }

        foreach (var card in updated.Cards)
        {
            var before = current.Cards.Single(item => item.Id == card.Id);
            if (before.ColumnId == card.ColumnId && before.GroupId == card.GroupId)
            {
                continue;
            }

            await connection.ExecuteAsync(
                "UPDATE cards SET column_id = @ColumnId, group_id = @GroupId WHERE meeting_id = @MeetingId AND id = @Id;",
                new
                {
                    MeetingId = meetingId,
                    Id = card.Id,
                    ColumnId = card.ColumnId,
                    GroupId = card.GroupId,
                },
                transaction);
        }

        foreach (var group in updated.Groups)
        {
            var before = current.Groups.SingleOrDefault(item => item.Id == group.Id);
            if (before is null || (before.ColumnId == group.ColumnId && before.Name == group.Name))
            {
                continue;
            }

            await connection.ExecuteAsync(
                "UPDATE groups SET column_id = @ColumnId, name = @Name WHERE meeting_id = @MeetingId AND id = @Id;",
                new
                {
                    MeetingId = meetingId,
                    Id = group.Id,
                    ColumnId = group.ColumnId,
                    group.Name,
                },
                transaction);
        }

        foreach (var group in current.Groups.Where(group => updated.Groups.All(item => item.Id != group.Id)))
        {
            await connection.ExecuteAsync(
                "DELETE FROM groups WHERE meeting_id = @MeetingId AND id = @Id;",
                new { MeetingId = meetingId, Id = group.Id },
                transaction);
        }
    }

    private static async Task<GroupingCommandStateRow?> GetStateAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId,
        Guid userId) =>
        await connection.QuerySingleOrDefaultAsync<GroupingCommandStateRow>(
            """
            SELECT m.phase AS Phase, m.version AS Version, m.board_version AS BoardVersion,
                   m.facilitator_id AS FacilitatorId, member.status AS MemberStatus
            FROM meetings m
            LEFT JOIN members member ON member.meeting_id = m.id AND member.user_id = @UserId
            WHERE m.id = @MeetingId;
            """,
            new { MeetingId = meetingId, UserId = userId },
            transaction);

    private static MeetingCommandResult? CheckMemberAccess(GroupingCommandStateRow? state)
    {
        if (state is null || state.MemberStatus is null)
        {
            return new MeetingCommandResult(MeetingCommandStatus.NotFound);
        }

        return state.MemberStatus == MemberStatus.Excluded.ToString()
            ? new MeetingCommandResult(MeetingCommandStatus.Forbidden)
            : null;
    }

    private static MeetingCommandResult? CheckFacilitatorAccess(GroupingCommandStateRow? state, Guid facilitatorId)
    {
        var memberError = CheckMemberAccess(state);
        if (memberError is not null)
        {
            return memberError;
        }

        return state!.FacilitatorId != facilitatorId
            ? new MeetingCommandResult(MeetingCommandStatus.Forbidden)
            : null;
    }

    private static MeetingCommandResult? CheckPhaseAndVersion(
        GroupingCommandStateRow? state,
        int expectedBoardVersion) =>
        state!.Phase != MeetingPhase.Group.ToString()
            ? new MeetingCommandResult(MeetingCommandStatus.InvalidPhase)
            : state.BoardVersion != expectedBoardVersion
                ? new MeetingCommandResult(MeetingCommandStatus.VersionConflict)
                : null;

    private static async Task<int> UpdateVersionsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId,
        int expectedVersion,
        int expectedBoardVersion,
        int newVersion,
        int newBoardVersion,
        DateTimeOffset nowUtc) =>
        await connection.ExecuteAsync(
            """
            UPDATE meetings
            SET version = @NewVersion, board_version = @NewBoardVersion, updated_at_utc = @Now
            WHERE id = @MeetingId AND phase = 'Group'
              AND version = @ExpectedVersion AND board_version = @ExpectedBoardVersion;
            """,
            new
            {
                MeetingId = meetingId,
                ExpectedVersion = expectedVersion,
                ExpectedBoardVersion = expectedBoardVersion,
                NewVersion = newVersion,
                NewBoardVersion = newBoardVersion,
                Now = nowUtc,
            },
            transaction);

    private static async Task<MeetingCommandResult?> FindReplayAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId,
        Guid userId,
        Guid operationId,
        string operationType,
        string fingerprint)
    {
        var row = await connection.QuerySingleOrDefaultAsync<GroupingReplayRow>(
            """
            SELECT operation_type AS OperationType, fingerprint AS Fingerprint,
                   result_id AS ResultId, result_version AS ResultVersion,
                   result_board_version AS ResultBoardVersion
            FROM command_deduplication
            WHERE meeting_id = @MeetingId AND user_id = @UserId AND operation_id = @OperationId;
            """,
            new
            {
                MeetingId = meetingId,
                UserId = userId,
                OperationId = operationId,
            },
            transaction);
        if (row is null)
        {
            return null;
        }

        if (row.OperationType != operationType || row.Fingerprint != fingerprint)
        {
            return new MeetingCommandResult(MeetingCommandStatus.OperationConflict);
        }

        return new MeetingCommandResult(
            MeetingCommandStatus.Success,
            row.ResultVersion,
            row.ResultId,
            true,
            row.ResultBoardVersion ?? 0);
    }

    private static async Task SaveReplayAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid meetingId,
        Guid userId,
        Guid operationId,
        string operationType,
        string fingerprint,
        Guid? resultId,
        int resultVersion,
        int resultBoardVersion,
        DateTimeOffset nowUtc) =>
        await connection.ExecuteAsync(
            """
            INSERT INTO command_deduplication (
                meeting_id, user_id, operation_id, operation_type, fingerprint,
                result_id, result_version, result_board_version, created_at_utc)
            VALUES (
                @MeetingId, @UserId, @OperationId, @OperationType, @Fingerprint,
                @ResultId, @ResultVersion, @ResultBoardVersion, @CreatedAtUtc);
            """,
            new
            {
                MeetingId = meetingId,
                UserId = userId,
                OperationId = operationId,
                OperationType = operationType,
                Fingerprint = fingerprint,
                ResultId = resultId,
                ResultVersion = resultVersion,
                ResultBoardVersion = resultBoardVersion,
                CreatedAtUtc = nowUtc,
            },
            transaction);

    private sealed class GroupingCommandStateRow
    {
        public required string Phase { get; init; }
        public int Version { get; init; }
        public int BoardVersion { get; init; }
        public Guid? FacilitatorId { get; init; }
        public string? MemberStatus { get; init; }
    }

    private sealed class BoardCardRow
    {
        public Guid Id { get; init; }
        public Guid ColumnId { get; init; }
        public Guid? GroupId { get; init; }
        public required string Text { get; init; }
    }

    private sealed class BoardGroupRow
    {
        public Guid Id { get; init; }
        public Guid ColumnId { get; init; }
        public required string Name { get; init; }
    }

    private sealed class ProposalRow
    {
        public int BoardVersion { get; init; }
        public required string PayloadJson { get; init; }
    }

    private sealed class GroupingReplayRow
    {
        public required string OperationType { get; init; }
        public required string Fingerprint { get; init; }
        public Guid? ResultId { get; init; }
        public int ResultVersion { get; init; }
        public int? ResultBoardVersion { get; init; }
    }
}
