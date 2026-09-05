// Employee clock in — keypad + clocked-in view.
//
// Two changes from the original beyond the added imports:
//
//  * `userCode` is a signal, so the six code boxes re-render as digits are
//    pressed. `login()` reads `this.userCode()`.
//  * `press` / `backspace` / `clear` drive the keypad. `press` caps input at
//    six digits and clears a previous error on the next keypress, so a
//    failed attempt doesn't leave the boxes ruled red while someone retypes.
//
// The auth call, dev defaults, route parameter and `isSignedIn` rule are
// untouched.

import { Component, inject, isDevMode, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

import { Auth } from '../../../core/auth';
import { DEV_DEFAULTS } from '../../../core/dev-defaults';
import { CurrentWeekSchedule } from '../../schedule/current-week-schedule/current-week-schedule';
import { PayDayBanner } from '../../schedule/pay-day-banner/pay-day-banner';

@Component({
  selector: 'app-employee-home',
  imports: [MatButtonModule, MatIconModule, CurrentWeekSchedule, PayDayBanner],
  templateUrl: './employee-home.html',
  styleUrl: './employee-home.scss',
})
export class EmployeeHome {
  protected readonly auth = inject(Auth);
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  protected readonly userCode = signal(isDevMode() ? DEV_DEFAULTS.employeeCode : '');
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);

  // Keypad layout: 1-9 in the grid, then Clear / 0 / backspace on the last
  // row (see the template).
  protected readonly digits = ['1', '2', '3', '4', '5', '6', '7', '8', '9'];
  protected readonly positions = [0, 1, 2, 3, 4, 5];

  protected get isSignedIn(): boolean {
    return this.auth.isAuthenticated() && this.auth.locationCode() === this.locationCode;
  }

  press(digit: string): void {
    if (this.error()) {
      this.error.set(null);
      this.userCode.set('');
    }
    if (this.userCode().length >= 6) return;
    this.userCode.update((code) => code + digit);
  }

  backspace(): void {
    this.error.set(null);
    this.userCode.update((code) => code.slice(0, -1));
  }

  clear(): void {
    this.error.set(null);
    this.userCode.set('');
  }

  async login(): Promise<void> {
    this.error.set(null);
    this.loading.set(true);
    try {
      await this.auth.employeeLogin(this.locationCode, this.userCode());
    } catch {
      this.error.set('Invalid code.');
    } finally {
      this.loading.set(false);
    }
  }
}
