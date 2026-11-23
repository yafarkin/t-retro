import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MeetingService } from './meeting.service';
import { defaultColumns, defaultVoteSettings } from './meeting.models';

describe('MeetingService', () => {
  let http: HttpTestingController;
  let service: MeetingService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(MeetingService);
  });

  afterEach(() => http.verify());

  it('creates a meeting with settings', () => {
    const payload = {
      name: 'Ретро',
      columns: defaultColumns(),
      anonymousCards: true,
      privateCollection: true,
      anonymousVotes: true,
      voteSettings: defaultVoteSettings(),
    };
    service.create(payload).subscribe();

    const request = http.expectOne('/api/meetings');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(payload);
    request.flush({ id: 'meeting-id', version: 1 });
  });

  it('passes expected version during settings update', () => {
    service.update('meeting-id', 3, {
      name: 'Ретро',
      columns: defaultColumns(),
      anonymousCards: true,
      privateCollection: true,
      anonymousVotes: true,
      voteSettings: defaultVoteSettings(),
    }).subscribe();

    const request = http.expectOne('/api/meetings/meeting-id/settings');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body.expectedVersion).toBe(3);
    request.flush({});
  });

  it('updates vote settings with the meeting version', () => {
    service.updateVoteSettings('meeting-id', 4, { mode: 'fixed', percent: 5, minimum: 2, fixed: 3 }).subscribe();

    const request = http.expectOne('/api/meetings/meeting-id/vote-settings');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ mode: 'fixed', percent: 5, minimum: 2, fixed: 3, expectedVersion: 4 });
    request.flush({ version: 5 });
  });

  it('sets an absolute vote count with the ballot version', () => {
    service.setMyVotes('meeting-id', 'topic-id', 2, 1).subscribe();

    const request = http.expectOne('/api/meetings/meeting-id/topics/topic-id/my-votes');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ count: 2, expectedVersion: 1 });
    request.flush({ version: 5, ballotVersion: 2, remaining: 1 });
  });

  it('advances discussion status with the meeting version', () => {
    service.setTopicStatus('meeting-id', 'topic-id', 'discussed', true, 7).subscribe();

    const request = http.expectOne('/api/meetings/meeting-id/topics/topic-id/status');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ status: 'discussed', advance: true, expectedVersion: 7 });
    request.flush({ version: 8 });
  });

  it('creates an action with an assignee and operation id', () => {
    service.createAction('meeting-id', 'topic-id', 'Задача', 'user-id', 7).subscribe();

    const request = http.expectOne('/api/meetings/meeting-id/topics/topic-id/actions');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.text).toBe('Задача');
    expect(request.request.body.assigneeId).toBe('user-id');
    expect(request.request.body.expectedVersion).toBe(7);
    expect(request.request.body.operationId).toMatch(/[0-9a-f-]{36}/);
    request.flush({ id: 'action-id', version: 8 });
  });

  it('starts a timer with duration and operation id', () => {
    service.startTimer('meeting-id', 60).subscribe();

    const request = http.expectOne('/api/meetings/meeting-id/timer/start');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.seconds).toBe(60);
    expect(request.request.body.operationId).toMatch(/[0-9a-f-]{36}/);
    request.flush({ runId: 'run-id', phase: 'Collect', endsAtUtc: '2026-11-23T12:01:00Z', serverNowUtc: '2026-11-23T12:00:00Z' });
  });
});
