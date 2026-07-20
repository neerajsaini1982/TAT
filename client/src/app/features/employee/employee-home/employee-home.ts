import { Component, inject, isDevMode, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { firstValueFrom } from 'rxjs';

import { Auth } from '../../../core/auth';
import { AccountsApi } from '../../../core/accounts-api';
import { DEV_DEFAULTS } from '../../../core/dev-defaults';
import { CurrentWeekSchedule } from '../../schedule/current-week-schedule/current-week-schedule';

@Component({
  selector: 'app-employee-home',
  imports: [
    FormsModule,
    RouterLink,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    CurrentWeekSchedule,
  ],
  templateUrl: './employee-home.html',
  styleUrl: './employee-home.scss',
})
export class EmployeeHome {
  protected readonly auth = inject(Auth);
  private readonly accountsApi = inject(AccountsApi);
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  protected userCode = isDevMode() ? DEV_DEFAULTS.employeeCode : '';
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly resettingCode = signal(false);
  protected readonly newCode = signal<string | null>(null);

  protected get isSignedIn(): boolean {
    return this.auth.isAuthenticated() && this.auth.locationCode() === this.locationCode;
  }

  async login(): Promise<void> {
    this.error.set(null);
    this.loading.set(true);
    try {
      await this.auth.employeeLogin(this.locationCode, this.userCode);
    } catch {
      this.error.set('Invalid code.');
    } finally {
      this.loading.set(false);
    }
  }

  logout(): void {
    this.auth.logout();
  }

  async resetCode(): Promise<void> {
    if (!confirm('Reset your login code? Your current code will stop working immediately.')) {
      return;
    }
    this.resettingCode.set(true);
    this.error.set(null);
    try {
      const account = await firstValueFrom(this.accountsApi.resetMyCode());
      this.newCode.set(account.userCode);
    } catch {
      this.error.set('Failed to reset your code. Try again.');
    } finally {
      this.resettingCode.set(false);
    }
  }
}
