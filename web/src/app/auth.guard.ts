import { inject } from '@angular/core';
import { CanActivateFn, RedirectCommand, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = (_, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.user()
    ? true
    : new RedirectCommand(router.createUrlTree(['/login']), { state: { returnUrl: state.url } });
};

export const loginGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.user() ? router.createUrlTree(['/']) : true;
};
