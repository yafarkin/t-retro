import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  GroupProposal,
  Invite,
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

  create(payload: MeetingSettingsPayload): Observable<{ id: string; version: number }> {
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

  createCard(meetingId: string, columnId: string, text: string): Observable<{ id: string; version: number }> {
    return this.http.post<{ id: string; version: number }>(`/api/meetings/${meetingId}/cards`, {
      columnId,
      text,
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
