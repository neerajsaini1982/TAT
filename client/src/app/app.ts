import { Component, computed, inject } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatDividerModule } from '@angular/material/divider';

import { Theme, THEMES } from './core/theme';
import { Auth } from './core/auth';

type Portal = 'admin' | 'employee' | null;

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
  private readonly router = inject(Router);

  // Which portal the URL is currently in — an Admin/Lead account can also
  // browse its own /employee/* pages (see employeeGuard), so this has to
  // follow the route, not the account's role.
  private readonly portal = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map((e) => this.portalFromUrl(e.urlAfterRedirects)),
    ),
    { initialValue: this.portalFromUrl(this.router.url) },
  );

  private portalFromUrl(url: string): Portal {
    if (/^\/[^/]+\/admin(\/|$)/.test(url)) {
      return 'admin';
    }
    if (/^\/[^/]+\/employee(\/|$)/.test(url)) {
      return 'employee';
    }
    return null;
  }

  protected readonly title = computed(() => {
    const locationName = this.auth.locationName();
    if (!locationName) {
      return 'TAT — Time & Attendance';
    }
    switch (this.portal()) {
      case 'admin':
        return `${locationName} — Admin Portal`;
      case 'employee':
        return `${locationName} — Employee Portal`;
      default:
        return `${locationName} Time & Attendance tracking`;
    }
  });

  protected readonly titleIcon = computed(() => {
    switch (this.portal()) {
      case 'admin':
        return 'admin_panel_settings';
      case 'employee':
        return 'badge';
      default:
        return null;
    }
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
