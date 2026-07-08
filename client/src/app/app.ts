import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';

import { Theme, THEMES } from './core/theme';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, MatToolbarModule, MatButtonModule, MatIconModule, MatMenuModule],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly themeService = inject(Theme);
  protected readonly themes = THEMES;
}
