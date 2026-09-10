import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatDialog } from '@angular/material/dialog';

import { AccountsApi, AccountDto } from '../../../core/accounts-api';
import { LocationsApi, LocationDto } from '../../../core/locations-api';
import { ResetPasswordDialog } from '../reset-password-dialog/reset-password-dialog';
import { AccountFormDialog, AccountFormDialogData } from '../account-form-dialog/account-form-dialog';

// Used both at /sa/accounts (lockedLocationCode = null, shows a location
// picker and every location's accounts) and at /:locationCode/admin/accounts
// (lockedLocationCode set, server auto-scopes everything to that location).
@Component({
  selector: 'app-accounts-manager',
  imports: [RouterLink, FormsModule, MatTableModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  templateUrl: './accounts-manager.html',
  styleUrl: './accounts-manager.scss',
})
export class AccountsManager implements OnInit {
  @Input() lockedLocationCode: string | null = null;

  private readonly accountsApi = inject(AccountsApi);
  private readonly locationsApi = inject(LocationsApi);
  private readonly dialog = inject(MatDialog);

  protected readonly accounts = signal<AccountDto[]>([]);
  protected readonly locations = signal<LocationDto[]>([]);
  protected readonly selectedLocation = signal<LocationDto | null>(null);
  protected readonly resettingId = signal<number | null>(null);
  protected readonly sendingId = signal<number | null>(null);
  protected readonly resettingPasswordId = signal<number | null>(null);

  // Hides terminated/inactive employees by default — a location can pick up
  // 100+ inactive rows from an ADP import, and most day-to-day account work
  // (scheduling, resets) only ever cares about who's currently active.
  protected readonly showInactive = signal(false);
  protected readonly searchTerm = signal('');

  protected readonly filteredAccounts = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    return this.accounts().filter((a) => {
      if (!this.showInactive() && !a.isActive) {
        return false;
      }
      if (!term) {
        return true;
      }
      return a.firstName.toLowerCase().includes(term) || a.lastName.toLowerCase().includes(term);
    });
  });

  get columns(): string[] {
    const base = ['serial', 'username', 'firstName', 'lastName', 'role', 'userCode', 'actions'];
    return this.lockedLocationCode ? base : ['serial', 'locationCode', ...base.slice(1)];
  }

  ngOnInit(): void {
    if (!this.lockedLocationCode) {
      this.locationsApi.getAll().subscribe((locations) => this.locations.set(locations));
    }
    this.load();
  }

  load(): void {
    const code = this.lockedLocationCode ?? this.selectedLocation()?.locationCode;
    this.accountsApi.getAll(code).subscribe((accounts) => this.accounts.set(accounts));
  }

  onLocationFilterChange(location: LocationDto | null): void {
    this.selectedLocation.set(location);
    this.load();
  }

  compareLocations = (a: LocationDto | null, b: LocationDto | null): boolean => a?.id === b?.id;

  private openAccountFormDialog(data: AccountFormDialogData): void {
    this.dialog
      .open(AccountFormDialog, { data, maxWidth: 'none', maxHeight: '90vh', autoFocus: 'dialog' })
      .afterClosed()
      .subscribe((saved) => {
        if (saved) {
          this.load();
        }
      });
  }

  startCreate(): void {
    this.openAccountFormDialog({
      account: null,
      duplicateFrom: null,
      lockedLocationCode: this.lockedLocationCode,
      locationId: this.lockedLocationCode ? null : (this.selectedLocation()?.id ?? null),
    });
  }

  startEdit(account: AccountDto): void {
    this.openAccountFormDialog({
      account,
      duplicateFrom: null,
      lockedLocationCode: this.lockedLocationCode,
      locationId: null,
    });
  }

  // Pre-fills the create form from an existing account so the admin can
  // adjust it and save it as a brand new account. Username/password/user
  // code are never copied — those get (re)generated on save.
  duplicate(account: AccountDto): void {
    this.openAccountFormDialog({
      account: null,
      duplicateFrom: account,
      lockedLocationCode: this.lockedLocationCode,
      locationId: this.lockedLocationCode ? null : (this.selectedLocation()?.id ?? null),
    });
  }

  remove(account: AccountDto): void {
    if (!confirm(`Delete account "${account.username}"?`)) {
      return;
    }
    this.accountsApi.delete(account.id).subscribe({
      next: () => this.load(),
      error: (err) => alert(err?.error ?? 'Failed to delete account.'),
    });
  }

  resetCode(account: AccountDto): void {
    if (!confirm(`Generate a new user code for ${account.firstName} ${account.lastName}? The old code will stop working immediately.`)) {
      return;
    }
    this.resettingId.set(account.id);
    this.accountsApi.resetCode(account.id).subscribe({
      next: () => {
        this.resettingId.set(null);
        this.load();
      },
      error: (err) => {
        this.resettingId.set(null);
        alert(err?.error ?? 'Failed to reset user code.');
      },
    });
  }

  resetPassword(account: AccountDto): void {
    this.dialog
      .open(ResetPasswordDialog, { data: { accountName: `${account.firstName} ${account.lastName}` } })
      .afterClosed()
      .subscribe((newPassword: string | undefined) => {
        if (!newPassword) {
          return;
        }
        this.resettingPasswordId.set(account.id);
        this.accountsApi.resetPassword(account.id, newPassword).subscribe({
          next: () => this.resettingPasswordId.set(null),
          error: (err) => {
            this.resettingPasswordId.set(null);
            alert(err?.error ?? 'Failed to reset password.');
          },
        });
      });
  }

  canSendCredentials(account: AccountDto): boolean {
    return account.role === 'Employee' && !!account.email && !!account.userCode;
  }

  sendCredentials(account: AccountDto): void {
    if (!this.canSendCredentials(account)) {
      return;
    }
    const locationCode = this.lockedLocationCode ?? account.locationCode;
    const loginLink = `${window.location.origin}/${locationCode}/employee`;

    this.sendingId.set(account.id);
    this.accountsApi.sendCredentials(account.id, loginLink).subscribe({
      next: () => {
        this.sendingId.set(null);
        alert(`Login credentials sent to ${account.firstName} ${account.lastName}.`);
      },
      error: (err) => {
        this.sendingId.set(null);
        alert(err?.error ?? 'Failed to send login credentials.');
      },
    });
  }
}
