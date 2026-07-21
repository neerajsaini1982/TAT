import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatDividerModule } from '@angular/material/divider';

import { Theme, THEMES } from './core/theme';
import { Auth } from './core/auth';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterOutlet, MatToolbarModule, MatButtonModule, MatIconModule, MatMenuModule, MatDividerModule],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly themeService = inject(Theme);
  protected readonly themes = THEMES;
  protected readonly auth = inject(Auth);

  protected readonly title = computed(() => {
    const locationName = this.auth.locationName();
    return locationName ? `${locationName} Time & Attendance tracking` : 'TAT — Time & Attendance';
  });

  // Admin/Lead get their per-location nav folded into one gear+name menu in
  // the toolbar (see admin-home, which used to render these as a row of
  // buttons on the page itself) instead of the standalone dark mode/palette
  // icons — Employee and signed-out visitors keep those icons as-is, since
  // there's no per-role nav to consolidate them with.
  protected readonly isAdminMenu = computed(() => {
    const role = this.auth.role();
    return role === 'Admin' || role === 'Lead';
  });

  logout(): void {
    this.auth.logout();
  }
}
