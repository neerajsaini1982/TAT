import { Component, inject, isDevMode } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import { DEV_DEFAULTS } from '../../core/dev-defaults';

@Component({
  selector: 'app-home',
  imports: [FormsModule, RouterLink, MatCardModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home {
  private readonly router = inject(Router);

  protected adminLocationCode = isDevMode() ? DEV_DEFAULTS.locationCode : '';
  protected employeeLocationCode = isDevMode() ? DEV_DEFAULTS.locationCode : '';

  goToAdmin(): void {
    const code = this.adminLocationCode.trim().toLowerCase();
    if (code) {
      this.router.navigate(['/', code, 'admin']);
    }
  }

  goToEmployee(): void {
    const code = this.employeeLocationCode.trim().toLowerCase();
    if (code) {
      this.router.navigate(['/', code, 'employee']);
    }
  }
}
