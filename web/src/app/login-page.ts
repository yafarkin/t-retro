import { Component, inject } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TuiButton, TuiTextfield } from '@taiga-ui/core';
import { AuthService } from './auth.service';

@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule, TuiButton, TuiTextfield],
  templateUrl: './login-page.html',
  styleUrl: './login-page.scss',
})
export class LoginPage {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly form = new FormGroup({
    subject: new FormControl('employee-a', { nonNullable: true }),
    displayName: new FormControl('Сотрудник А', { nonNullable: true }),
  });

  submit(): void {
    if (this.auth.busy()) {
      return;
    }

    const value = this.form.getRawValue();
    this.auth.login(value.subject, value.displayName).subscribe({
      next: () => this.router.navigateByUrl('/'),
      error: () => undefined,
    });
  }
}
