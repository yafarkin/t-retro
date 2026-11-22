import { Routes } from '@angular/router';
import { authGuard, loginGuard } from './auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [loginGuard],
    loadComponent: () => import('./login-page').then((module) => module.LoginPage),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./meetings-page').then((module) => module.MeetingsPage),
  },
  { path: '**', redirectTo: '' },
];
