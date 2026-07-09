import { Component, computed, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';

import { Theme, THEMES } from './core/theme';
import { Auth } from './core/auth';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, MatToolbarModule, MatButtonModule, MatIconModule, MatMenuModule],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly themeService = inject(Theme);
  protected readonly themes = THEMES;
  private readonly auth = inject(Auth);

  protected readonly title = computed(() => {
    const locationName = this.auth.locationName();
    return locationName ? `${locationName} Time & Attendance tracking` : 'TAT — Time & Attendance';
  });
}
