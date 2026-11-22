import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TuiRoot } from '@taiga-ui/core';
import { ConnectionService } from './connection.service';

@Component({
  imports: [RouterOutlet, TuiRoot],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  readonly connection = inject(ConnectionService);
}
