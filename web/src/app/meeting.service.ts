import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CreateMeetingPayload,
  GroupProposal,
  Invite,
  JiraExportResult,
  MeetingSettingsPayload,
  MeetingState,
  MeetingSummary,
} from './meeting.models';

@Injectable({ providedIn: 'root' })
export class MeetingService {
  constructor(private readonly http: HttpClient) {}

  list(): Observable<MeetingSummary[]> {
    return this.http.get<MeetingSummary[]>('/api/meetings');
  }

  create(payload: CreateMeetingPayload): Observable<{ id: string; version: number }> {
    return this.http.post<{ id: string; version: number }>('/api/meetings', payload);
  }

  state(id: string): Observable<MeetingState> {
    return this.http.get<MeetingState>(`/api/meetings/${id}/state`);
  }

  update(id: string, expectedVersion: number, payload: MeetingSettingsPayload): Observable<MeetingState> {
    return this.http.put<MeetingState>(`/api/meetings/${id}/settings`, {
      ...payload,
      expectedVersion,
    });
  }

  invite(id: string): Observable<Invite> {
    return this.http.get<Invite>(`/api/meetings/${id}/invite`);
  }

  rotateInvite(id: string): Observable<Invite> {
    return this.http.post<Invite>(`/api/meetings/${id}/invite/rotate`, {});
  }

  join(token: string): Observable<{ meetingId: string }> {
    return this.http.post<{ meetingId: string }>('/api/join', { token });
  }

  exclude(meetingId: string, userId: string): Observable<void> {
    return this.http.post<void>(`/api/meetings/${meetingId}/members/${userId}/exclude`, {});
  }

  transfer(meetingId: string, userId: string): Observable<void> {
    return this.http.put<void>(`/api/meetings/${meetingId}/facilitator`, { userId });
  }

  createCard(meetingId: string, columnId: string, text: string, anonymous: boolean): Observable<{ id: string; version: number }> {
    return this.http.post<{ id: string; version: number }>(`/api/meetings/${meetingId}/cards`, {
      columnId,
      text,
      anonymous,
      operationId: crypto.randomUUID(),
    });
  }

  updateCard(meetingId: string, cardId: string, text: string, expectedVersion: number): Observable<{ version: number }> {
    return this.http.put<{ version: number }>(`/api/meetings/${meetingId}/cards/${cardId}`, {
      text,
      expectedVersion,
    });
  }

  deleteCard(meetingId: string, cardId: string, expectedVersion: number): Observable<{ version: number }> {
    return this.http.request<{ version: number }>('DELETE', `/api/meetings/${meetingId}/cards/${cardId}`, {
      body: { expectedVersion },
    });
  }

  setReady(meetingId: string, phase: string, ready: boolean): Observable<{ version: number }> {
    return this.http.put<{ version: number }>(`/api/meetings/${meetingId}/ready`, { phase, ready });
  }

  advance(meetingId: string, fromPhase: string, expectedVersion: number): Observable<{ version: number }> {
    return this.http.post<{ version: number }>(`/api/meetings/${meetingId}/advance`, {
      fromPhase,
      expectedVersion,
      operationId: crypto.randomUUID(),
    });
  }

  updateVoteSettings(
    meetingId: string,
    expectedVersion: number,
    settings: MeetingState['voteSettings'],
  ): Observable<{ version: number }> {
    return this.http.put<{ version: number }>(`/api/meetings/${meetingId}/vote-settings`, {
      ...settings,
      expectedVersion,
    });
  }

  setMyVotes(
    meetingId: string,
    topicId: string,
    count: number,
    expectedVersion: number,
  ): Observable<{ version: number; ballotVersion: number; remaining: number }> {
    return this.http.put<{ version: number; ballotVersion: number; remaining: number }>(
      `/api/meetings/${meetingId}/topics/${topicId}/my-votes`,
      { count, expectedVersion },
    );
  }

  selectCurrentTopic(meetingId: string, topicId: string, expectedVersion: number): Observable<{ version: number }> {
    return this.http.put<{ version: number }>(`/api/meetings/${meetingId}/discussion/current`, {
      topicId,
      expectedVersion,
    });
  }

  setTopicStatus(
    meetingId: string,
    topicId: string,
    status: string,
    advance: boolean,
    expectedVersion: number,
  ): Observable<{ version: number }> {
    return this.http.put<{ version: number }>(`/api/meetings/${meetingId}/topics/${topicId}/status`, {
      status,
      advance,
      expectedVersion,
    });
  }

  createComment(meetingId: string, topicId: string, text: string, expectedVersion: number): Observable<{ id: string; version: number }> {
    return this.createDiscussionEntry(meetingId, topicId, 'comments', { text, expectedVersion });
  }

  updateComment(meetingId: string, commentId: string, text: string, expectedVersion: number): Observable<{ version: number }> {
    return this.updateDiscussionEntry(meetingId, 'comments', commentId, { text, expectedVersion });
  }

  deleteComment(meetingId: string, commentId: string, expectedVersion: number): Observable<{ version: number }> {
    return this.deleteDiscussionEntry(meetingId, 'comments', commentId, expectedVersion);
  }

  createDecision(meetingId: string, topicId: string, text: string, expectedVersion: number): Observable<{ id: string; version: number }> {
    return this.createDiscussionEntry(meetingId, topicId, 'decisions', { text, expectedVersion });
  }

  updateDecision(meetingId: string, decisionId: string, text: string, expectedVersion: number): Observable<{ version: number }> {
    return this.updateDiscussionEntry(meetingId, 'decisions', decisionId, { text, expectedVersion });
  }

  deleteDecision(meetingId: string, decisionId: string, expectedVersion: number): Observable<{ version: number }> {
    return this.deleteDiscussionEntry(meetingId, 'decisions', decisionId, expectedVersion);
  }

  createAction(
    meetingId: string,
    topicId: string,
    text: string,
    assigneeId: string | null,
    expectedVersion: number,
  ): Observable<{ id: string; version: number }> {
    return this.createDiscussionEntry(meetingId, topicId, 'actions', { text, assigneeId, expectedVersion });
  }

  updateAction(
    meetingId: string,
    actionId: string,
    text: string,
    assigneeId: string | null,
    expectedVersion: number,
  ): Observable<{ version: number }> {
    return this.updateDiscussionEntry(meetingId, 'actions', actionId, { text, assigneeId, expectedVersion });
  }

  deleteAction(meetingId: string, actionId: string, expectedVersion: number): Observable<{ version: number }> {
    return this.deleteDiscussionEntry(meetingId, 'actions', actionId, expectedVersion);
  }

  exportAction(meetingId: string, actionId: string): Observable<JiraExportResult> {
    return this.http.post<JiraExportResult>(
      `/api/meetings/${meetingId}/actions/${actionId}/export`,
      {},
    );
  }

  startTimer(meetingId: string, seconds: number): Observable<MeetingState['timer']> {
    return this.http.post<NonNullable<MeetingState['timer']>>(`/api/meetings/${meetingId}/timer/start`, {
      seconds,
      operationId: crypto.randomUUID(),
    });
  }

  addTimer(meetingId: string, runId: string, seconds: number): Observable<MeetingState['timer']> {
    return this.http.post<NonNullable<MeetingState['timer']>>(`/api/meetings/${meetingId}/timer/add`, {
      runId,
      seconds,
      operationId: crypto.randomUUID(),
    });
  }

  stopTimer(meetingId: string, runId: string): Observable<void> {
    return this.http.post<void>(`/api/meetings/${meetingId}/timer/stop`, {
      runId,
      operationId: crypto.randomUUID(),
    });
  }

  private createDiscussionEntry(
    meetingId: string,
    topicId: string,
    kind: string,
    payload: object,
  ): Observable<{ id: string; version: number }> {
    return this.http.post<{ id: string; version: number }>(`/api/meetings/${meetingId}/topics/${topicId}/${kind}`, {
      ...payload,
      operationId: crypto.randomUUID(),
    });
  }

  private updateDiscussionEntry(
    meetingId: string,
    kind: string,
    entryId: string,
    payload: object,
  ): Observable<{ version: number }> {
    return this.http.put<{ version: number }>(`/api/meetings/${meetingId}/${kind}/${entryId}`, payload);
  }

  private deleteDiscussionEntry(
    meetingId: string,
    kind: string,
    entryId: string,
    expectedVersion: number,
  ): Observable<{ version: number }> {
    return this.http.request<{ version: number }>('DELETE', `/api/meetings/${meetingId}/${kind}/${entryId}`, {
      body: { expectedVersion },
    });
  }

  move(
    meetingId: string,
    sourceKind: string,
    sourceId: string,
    targetKind: string,
    targetId: string,
    expectedBoardVersion: number,
  ): Observable<{ version: number; boardVersion: number }> {
    return this.http.post<{ version: number; boardVersion: number }>(`/api/meetings/${meetingId}/moves`, {
      sourceKind,
      sourceId,
      targetKind,
      targetId,
      expectedBoardVersion,
      operationId: crypto.randomUUID(),
    });
  }

  renameGroup(meetingId: string, groupId: string, name: string, expectedBoardVersion: number): Observable<{ version: number; boardVersion: number }> {
    return this.http.put<{ version: number; boardVersion: number }>(`/api/meetings/${meetingId}/groups/${groupId}`, {
      name,
      expectedBoardVersion,
    });
  }

  previewGroups(meetingId: string): Observable<GroupProposal> {
    return this.http.post<GroupProposal>(`/api/meetings/${meetingId}/ai/preview`, {});
  }

  applyGroups(meetingId: string, proposalId: string, expectedBoardVersion: number): Observable<{ version: number; boardVersion: number }> {
    return this.http.post<{ version: number; boardVersion: number }>(`/api/meetings/${meetingId}/ai/apply`, {
      proposalId,
      expectedBoardVersion,
      operationId: crypto.randomUUID(),
    });
  }
}
