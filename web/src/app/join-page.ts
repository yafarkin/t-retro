import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TuiButton } from '@taiga-ui/core';
import { apiErrorMessage } from './api-error';
import { MeetingService } from './meeting.service';

@Component({
  selector: 'app-join-page',
  imports: [RouterLink, TuiButton],
  templateUrl: './join-page.html',
  styleUrl: './join-page.scss',
})
export class JoinPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly meetings = inject(MeetingService);
  readonly token = this.route.snapshot.queryParamMap.get('token') ?? '';
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  join(): void {
    this.busy.set(true);
    this.error.set(null);
    this.meetings.join(this.token).subscribe({
      next: (result) => this.router.navigateByUrl(`/meetings/${result.meetingId}`),
      error: (error: HttpErrorResponse) => {
        this.error.set(apiErrorMessage(error));
        this.busy.set(false);
      },
    });
  }
}
