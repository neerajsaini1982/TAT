import { Component, Inject, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import { AccountDto, AccountsApi } from '../../../core/accounts-api';
import { SickTimeEntriesApi } from '../../../core/sick-time-entries-api';
import { formatDate } from '../../../core/week-utils';

export interface SickHoursDialogData {
  locationCode: string;
}

interface FormModel {
  accountId: number | null;
  date: string;
  hours: number | null;
  note: string;
}

// Lets an admin record sick hours for an employee on a day they weren't
// scheduled at all (see SickTimeEntriesApi) — distinct from the inline
// per-shift sick-hours field on the payroll report table, which only
// applies to a day the employee actually had a shift assignment.
@Component({
  selector: 'app-sick-hours-dialog',
  imports: [
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
  ],
  templateUrl: './sick-hours-dialog.html',
  styleUrl: './sick-hours-dialog.scss',
})
export class SickHoursDialog implements OnInit {
  private readonly accountsApi = inject(AccountsApi);
  private readonly sickTimeEntriesApi = inject(SickTimeEntriesApi);

  protected readonly error = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly employees = signal<AccountDto[]>([]);

  protected form: FormModel = {
    accountId: null,
    date: formatDate(new Date()),
    hours: null,
    note: '',
  };

  constructor(
    private readonly dialogRef: MatDialogRef<SickHoursDialog, boolean>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: SickHoursDialogData,
  ) {}

  ngOnInit(): void {
    this.accountsApi.getAll(this.data.locationCode).subscribe({
      next: (accounts) => {
        this.employees.set(
          accounts
            .filter((a) => a.isActive)
            .sort((a, b) => `${a.firstName} ${a.lastName}`.localeCompare(`${b.firstName} ${b.lastName}`)),
        );
      },
      error: (err) => this.error.set(err?.error ?? 'Failed to load employees.'),
    });
  }

  save(): void {
    this.error.set(null);

    if (this.form.accountId === null) {
      this.error.set('Choose an employee.');
      return;
    }
    if (!this.form.date) {
      this.error.set('Choose a date.');
      return;
    }
    if (this.form.hours === null || this.form.hours <= 0) {
      this.error.set('Sick hours must be greater than 0.');
      return;
    }

    this.saving.set(true);
    this.sickTimeEntriesApi
      .create({
        accountId: this.form.accountId,
        date: this.form.date,
        minutes: Math.round(this.form.hours * 60),
        note: this.form.note.trim() || null,
      })
      .subscribe({
        next: () => this.dialogRef.close(true),
        error: (err) => {
          this.error.set(err?.error ?? 'Failed to save sick hours.');
          this.saving.set(false);
        },
      });
  }

  cancel(): void {
    this.dialogRef.close(false);
  }
}
