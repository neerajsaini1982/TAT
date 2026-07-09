import { Component, inject, isDevMode, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';

import { Auth } from '../../../core/auth';
import { DEV_DEFAULTS } from '../../../core/dev-defaults';

@Component({
  selector: 'app-sa-home',
  imports: [FormsModule, RouterLink, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule],
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
