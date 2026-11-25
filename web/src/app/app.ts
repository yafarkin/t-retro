import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { TuiButton, TuiHint, TuiRoot } from '@taiga-ui/core';
import { AuthService } from './auth.service';
import { ConnectionService } from './connection.service';
import { MeetingHeaderService } from './meeting-header.service';

type ThemePreference = 'light' | 'dark' | 'system';
const THEME_STORAGE_KEY = 't-retro.theme';
const NEXT_THEME: Record<ThemePreference, ThemePreference> = { system: 'light', light: 'dark', dark: 'system' };
const THEME_LABEL: Record<ThemePreference, string> = {
  system: 'Системная тема',
  light: 'Светлая тема',
  dark: 'Тёмная тема',
};

function readTheme(): ThemePreference {
  try {
    const saved = window.localStorage.getItem(THEME_STORAGE_KEY);
    return saved === 'light' || saved === 'dark' ? saved : 'system';
  } catch {
    return 'system';
  }
}

@Component({
  imports: [NgTemplateOutlet, RouterLink, RouterOutlet, TuiButton, TuiHint, TuiRoot],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly router = inject(Router);
  private readonly systemTheme = window.matchMedia?.('(prefers-color-scheme: dark)');
  private readonly systemDark = signal(this.systemTheme?.matches ?? false);
  readonly auth = inject(AuthService);
  readonly connection = inject(ConnectionService);
  readonly meetingHeader = inject(MeetingHeaderService);
  readonly theme = signal<ThemePreference>(readTheme());
  readonly themeHint = computed(() => THEME_LABEL[this.theme()]);
  readonly resolvedTheme = computed(() => {
    const preference = this.theme();
    return preference === 'system' ? (this.systemDark() ? 'dark' : 'light') : preference;
  });

  constructor() {
    const updateSystemTheme = (event: MediaQueryListEvent) => this.systemDark.set(event.matches);
    this.systemTheme?.addEventListener('change', updateSystemTheme);
    inject(DestroyRef).onDestroy(() => this.systemTheme?.removeEventListener('change', updateSystemTheme));
  }

  cycleTheme(): void {
    const value = NEXT_THEME[this.theme()];
    this.theme.set(value);
    try {
      window.localStorage.setItem(THEME_STORAGE_KEY, value);
    } catch {
      // The preference still applies in the current tab when storage is unavailable.
    }
  }

  logout(): void {
    this.auth.logout().subscribe(() => this.router.navigateByUrl('/login'));
  }
}
