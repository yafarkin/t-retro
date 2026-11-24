import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

@Component({
  selector: 'app-workspace-layout',
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav aria-label="Основная навигация">
      <div class="links">
        <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }"
          ariaCurrentWhenActive="page">Мои встречи</a>
        <a routerLink="/templates" routerLinkActive="active" ariaCurrentWhenActive="page">Шаблоны</a>
      </div>
    </nav>
    <ng-content />
  `,
  styleUrl: './workspace-layout.scss',
})
export class WorkspaceLayout {}
