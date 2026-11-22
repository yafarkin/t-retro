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
});
