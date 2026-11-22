import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
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
}
