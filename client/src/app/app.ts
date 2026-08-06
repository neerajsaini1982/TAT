import { Component, computed, effect, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, firstValueFrom, map } from 'rxjs';
import { DOCUMENT } from '@angular/common';
import { Title } from '@angular/platform-browser';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatDividerModule } from '@angular/material/divider';
import { MatDialog } from '@angular/material/dialog';

import { Theme, THEMES } from './core/theme';
import { Auth } from './core/auth';
import { AccountsApi } from './core/accounts-api';
import { MyAccountDialog } from './features/employee/my-account-dialog/my-account-dialog';

type Portal = 'admin' | 'employee' | null;

const DEFAULT_FAVICON = 'favicon.ico';

// Small inline favicons so the two portals are distinguishable by their
// browser tab alone, without shipping extra binary assets — a shield for
// Admin, an ID badge for Employee, echoing the admin_panel_settings/badge
// icons shown next to the toolbar title.
const ADMIN_FAVICON =
  'data:image/svg+xml,' +
  encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">' +
      '<path fill="#1e3a8a" d="M12 1 3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4z"/>' +
      '<path fill="#ffffff" d="m10.9 14.2-2.1-2.1-1.1 1.1 3.2 3.2 6-6-1.1-1.1z"/>' +
      '</svg>',
  );
const EMPLOYEE_FAVICON =
  'data:image/svg+xml,' +
  encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">' +
      '<rect x="3" y="5" width="18" height="15" rx="3" fill="#0f766e"/>' +
      '<circle cx="12" cy="6" r="1.6" fill="#0f766e"/>' +
      '<circle cx="12" cy="11.3" r="2.3" fill="#fff"/>' +
      '<path fill="#fff" d="M7.6 17.4c.7-2.1 2.3-3.2 4.4-3.2s3.7 1.1 4.4 3.2c-1.2.9-2.7 1.4-4.4 1.4s-3.2-.5-4.4-1.4z"/>' +
      '</svg>',
  );

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
  private readonly titleService = inject(Title);
  private readonly document = inject(DOCUMENT);
  private readonly accountsApi = inject(AccountsApi);
  private readonly dialog = inject(MatDialog);

  protected readonly resettingCode = signal(false);

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

  constructor() {
    // The toolbar heading only helps while the tab is focused — the browser
    // tab itself (title + favicon) is what's visible with several tabs open
    // side by side, so it needs the same Admin/Employee Portal distinction.
    effect(() => this.titleService.setTitle(this.title()));
    effect(() => {
      const href = this.portal() === 'admin' ? ADMIN_FAVICON : this.portal() === 'employee' ? EMPLOYEE_FAVICON : DEFAULT_FAVICON;
      const link =
        this.document.querySelector<HTMLLinkElement>('link[rel="icon"]') ??
        this.document.head.appendChild(this.document.createElement('link'));
      link.setAttribute('rel', 'icon');
      link.setAttribute('href', href);
    });
  }

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
  // icons — signed-out visitors keep those icons as-is, since there's no
  // per-role nav to consolidate them with (Employee gets the same gear+name
  // treatment below, via isEmployeeMenu).
  protected readonly isAdminMenu = computed(() => {
    const role = this.auth.role();
    return role === 'Admin' || role === 'Lead';
  });

  // Plain Employee accounts get the same gear+name treatment (see
  // isAdminMenu above) — their per-page nav row (My Schedule, My
  // Availability, ...) used to live on employee-home itself, folded here
  // instead so it's available from every employee page, not just the home
  // one. Admin/Lead keep the admin menu even while browsing employee pages
  // (employeeGuard lets them), so this only kicks in for the plain role.
  protected readonly isEmployeeMenu = computed(() => this.auth.role() === 'Employee');

  logout(): void {
    this.auth.logout();
  }

  async openAccountDialog(): Promise<void> {
    const account = await firstValueFrom(this.accountsApi.getMine());
    this.dialog.open(MyAccountDialog, { data: account });
  }

  async resetCode(): Promise<void> {
    if (!confirm('Reset your login code? Your current code will stop working immediately.')) {
      return;
    }
    this.resettingCode.set(true);
    try {
      const account = await firstValueFrom(this.accountsApi.resetMyCode());
      alert(`Your new code is ${account.userCode}. Write it down — you'll need it next time you sign in.`);
    } catch {
      alert('Failed to reset your code. Try again.');
    } finally {
      this.resettingCode.set(false);
    }
  }
}
