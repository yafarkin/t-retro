import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { TuiButton } from '@taiga-ui/core';
import { AuthService } from './auth.service';

@Component({
  selector: 'app-meetings-page',
  imports: [TuiButton],
  templateUrl: './meetings-page.html',
  styleUrl: './meetings-page.scss',
})
export class MeetingsPage {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  logout(): void {
    this.auth.logout().subscribe(() => this.router.navigateByUrl('/login'));
  }
}
