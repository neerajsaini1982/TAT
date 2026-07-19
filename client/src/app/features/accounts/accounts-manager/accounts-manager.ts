import { Component, Input, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatCardModule } from '@angular/material/card';

import { AccountsApi, AccountDto } from '../../../core/accounts-api';
import { LocationsApi, LocationDto } from '../../../core/locations-api';
import { Role } from '../../../core/auth';

interface FormModel {
  username: string;
  password: string;
  firstName: string;
  lastName: string;
  email: string;
  phone: string;
  role: Role;
  isActive: boolean;
}

const emptyForm = (): FormModel => ({
  username: '',
  password: '',
  firstName: '',
  lastName: '',
  email: '',
  phone: '',
  role: 'Employee',
  isActive: true,
});

// Used both at /sa/accounts (lockedLocationCode = null, shows a location
// picker and every location's accounts) and at /:locationCode/admin/accounts
// (lockedLocationCode set, server auto-scopes everything to that location).
@Component({
  selector: 'app-accounts-manager',
  imports: [
    RouterLink,
    FormsModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatCardModule,
  ],
  templateUrl: './accounts-manager.html',
  styleUrl: './accounts-manager.scss',
})
export class AccountsManager implements OnInit {
  @Input() lockedLocationCode: string | null = null;

  private readonly accountsApi = inject(AccountsApi);
  private readonly locationsApi = inject(LocationsApi);

  protected readonly roles: Role[] = ['Admin', 'Lead', 'Employee'];
  protected readonly accounts = signal<AccountDto[]>([]);
  protected readonly locations = signal<LocationDto[]>([]);
  protected readonly selectedLocation = signal<LocationDto | null>(null);
  protected readonly editingId = signal<number | null>(null);
  protected readonly showForm = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly resettingId = signal<number | null>(null);
  protected form: FormModel = emptyForm();

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
    const base = ['username', 'firstName', 'lastName', 'role', 'userCode', 'isActive', 'actions'];
    return this.lockedLocationCode ? base : ['locationCode', ...base];
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

  startCreate(): void {
    this.editingId.set(null);
    this.form = emptyForm();
    this.error.set(null);
    this.showForm.set(true);
  }

  startEdit(account: AccountDto): void {
    this.editingId.set(account.id);
    this.form = { ...emptyForm(), ...account, password: '' };
    this.error.set(null);
    this.showForm.set(true);
  }

  // Pre-fills the create form from an existing account so the admin can
  // adjust it and save it as a brand new account. Username/password/user
  // code are never copied — those get (re)generated on save.
  duplicate(account: AccountDto): void {
    this.editingId.set(null);
    this.form = {
      ...emptyForm(),
      firstName: `${account.firstName} (Copy)`,
      lastName: account.lastName,
      email: account.email,
      phone: account.phone,
      role: account.role,
    };
    this.error.set(null);
    this.showForm.set(true);
  }

  cancel(): void {
    this.showForm.set(false);
  }

  save(): void {
    this.error.set(null);
    const id = this.editingId();

    if (id === null) {
      const locationId = this.lockedLocationCode ? null : (this.selectedLocation()?.id ?? null);
      if (!this.lockedLocationCode && locationId === null) {
        this.error.set('Select a location before adding an account.');
        return;
      }

      const isEmployee = this.form.role === 'Employee';
      this.accountsApi
        .create({
          username: isEmployee ? undefined : this.form.username,
          password: isEmployee ? undefined : this.form.password,
          firstName: this.form.firstName,
          lastName: this.form.lastName,
          email: this.form.email,
          phone: this.form.phone,
          role: this.form.role,
          locationId,
        })
        .subscribe({
          next: () => {
            this.showForm.set(false);
            this.load();
          },
          error: (err) => this.error.set(err?.error ?? 'Failed to create account.'),
        });
      return;
    }

    this.accountsApi
      .update(id, {
        firstName: this.form.firstName,
        lastName: this.form.lastName,
        email: this.form.email,
        phone: this.form.phone,
        isActive: this.form.isActive,
      })
      .subscribe({
        next: () => {
          this.showForm.set(false);
          this.load();
        },
        error: (err) => this.error.set(err?.error ?? 'Failed to update account.'),
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
}
