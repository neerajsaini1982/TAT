// Super Admin portal — unchanged except for `MatIconModule`, which the
// redesigned template needs for the icons in the error notice, the submit
// button, the three grid cells and Log Out.
//
// `MatCardModule` is no longer used by the template (mat-card gave way to
// the poster-panel layout). It is left in place so this file stays a
// one-line diff; remove it if you prefer a clean imports array.

import { Component, inject, isDevMode, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

import { Auth } from '../../../core/auth';
import { DEV_DEFAULTS } from '../../../core/dev-defaults';

@Component({
  selector: 'app-sa-home',
  imports: [
    FormsModule,
    RouterLink,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
  ],
  templateUrl: './sa-home.html',
  styleUrl: './sa-home.scss',
})
export class SaHome {
  protected readonly auth = inject(Auth);

  protected username = isDevMode() ? DEV_DEFAULTS.sa.username : '';
  protected password = isDevMode() ? DEV_DEFAULTS.sa.password : '';
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);

  async login(): Promise<void> {
    this.error.set(null);
    this.loading.set(true);
    try {
      await this.auth.saLogin(this.username, this.password);
    } catch {
      this.error.set('Invalid username or password.');
    } finally {
      this.loading.set(false);
    }
  }

  logout(): void {
    this.auth.logout();
  }
}
