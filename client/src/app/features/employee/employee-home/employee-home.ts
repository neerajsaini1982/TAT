import { Component, inject, isDevMode, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';

import { Auth } from '../../../core/auth';
import { DEV_DEFAULTS } from '../../../core/dev-defaults';

@Component({
  selector: 'app-employee-home',
  imports: [FormsModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  templateUrl: './employee-home.html',
  styleUrl: './employee-home.scss',
})
export class EmployeeHome {
  protected readonly auth = inject(Auth);
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  protected userCode = isDevMode() ? DEV_DEFAULTS.employeeCode : '';
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);

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
}
