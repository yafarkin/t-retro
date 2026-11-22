import { Routes } from '@angular/router';
import { authGuard, loginGuard } from './auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [loginGuard],
    loadComponent: () => import('./login-page').then((module) => module.LoginPage),
  },
  {
    path: 'meetings/new',
    canActivate: [authGuard],
    loadComponent: () => import('./create-meeting-page').then((module) => module.CreateMeetingPage),
  },
  {
    path: 'meetings/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./meeting-settings-page').then((module) => module.MeetingSettingsPage),
  },
  {
    path: 'templates',
    canActivate: [authGuard],
    loadComponent: () => import('./templates-page').then((module) => module.TemplatesPage),
  },
  {
    path: 'join',
    canActivate: [authGuard],
    loadComponent: () => import('./join-page').then((module) => module.JoinPage),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./meetings-page').then((module) => module.MeetingsPage),
  },
  { path: '**', redirectTo: '' },
];
