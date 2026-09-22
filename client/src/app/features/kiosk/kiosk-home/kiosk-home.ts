import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

import { Auth } from '../../../core/auth';
import { KioskSchedule } from '../kiosk-schedule/kiosk-schedule';

// Logs in the shared kiosk device itself with a per-location passcode (set
// by an Admin under Location Settings), then shows today's roster —
// individual employees identify themselves per-punch with their own PIN
// (see kiosk-schedule.ts/kiosk-pin-dialog.ts), not by signing in here.
@Component({
  selector: 'app-kiosk-home',
  imports: [FormsModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, KioskSchedule],
  templateUrl: './kiosk-home.html',
  styleUrl: './kiosk-home.scss',
})
export class KioskHome {
  protected readonly auth = inject(Auth);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  protected passcode = '';
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);

  protected get isSignedIn(): boolean {
    return this.auth.isAuthenticated() && this.auth.role() === 'Kiosk' && this.auth.locationCode() === this.locationCode;
  }

  async login(): Promise<void> {
    this.error.set(null);
    this.loading.set(true);
    try {
      await this.auth.kioskLogin(this.locationCode, this.passcode);
      this.passcode = '';
    } catch {
      this.error.set('Invalid passcode.');
    } finally {
      this.loading.set(false);
    }
  }

  // A shared device stays logged in all day on purpose (see
  // Jwt:KioskExpiryMinutes) — this is the deliberate "end of day" action to
  // sign it back out, not something that happens automatically.
  logout(): void {
    this.auth.logout();
    this.router.navigateByUrl(`/${this.locationCode}/kiosk`);
  }
}
