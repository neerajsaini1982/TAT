import { Component, Inject, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import { AccountsApi } from '../../../core/accounts-api';
import { EmailHoursReportResultDto, ReportsApi } from '../../../core/reports-api';
import { toMmDdYyyy } from '../../../core/week-utils';

export interface PayrollEmailDialogData {
  locationCode: string;
  startDate: string;
  endDate: string;
  // The report rows currently shown (respecting the page's employee filter).
  employees: { employeeId: number; fullName: string }[];
}

interface Recipient {
  employeeId: number;
  fullName: string;
  email: string;
}

// Emails each employee on the Payroll Report their own hours for the range,
// using the location's Payroll Hours template (see
// ReportsController.EmailHoursReport). Shows up front who will and won't get
// one, and lets the admin send themselves a [TEST] copy with real data first.
@Component({
  selector: 'app-payroll-email-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatFormFieldModule, MatInputModule],
  templateUrl: './payroll-email-dialog.html',
  styleUrl: './payroll-email-dialog.scss',
})
export class PayrollEmailDialog implements OnInit {
  private readonly accountsApi = inject(AccountsApi);
  private readonly reportsApi = inject(ReportsApi);

  protected readonly loading = signal(true);
  protected readonly sending = signal(false);
  protected readonly testing = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly testResult = signal<string | null>(null);
  protected readonly result = signal<EmailHoursReportResultDto | null>(null);
  private readonly recipients = signal<Recipient[]>([]);

  protected readonly withEmail = computed(() => this.recipients().filter((r) => r.email.trim()));
  protected readonly withoutEmail = computed(() => this.recipients().filter((r) => !r.email.trim()));

  protected testToAddress = '';

  constructor(
    private readonly dialogRef: MatDialogRef<PayrollEmailDialog>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: PayrollEmailDialogData,
  ) {}

  ngOnInit(): void {
    forkJoin({
      accounts: this.accountsApi.getAll(this.data.locationCode),
      me: this.accountsApi.getMine(),
    }).subscribe({
      next: ({ accounts, me }) => {
        const emailById = new Map(accounts.map((a) => [a.id, a.email ?? '']));
        this.recipients.set(
          this.data.employees.map((e) => ({ ...e, email: emailById.get(e.employeeId) ?? '' })),
        );
        this.testToAddress = me.email ?? '';
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err?.error ?? 'Failed to load employees.');
        this.loading.set(false);
      },
    });
  }

  protected dateLabel(isoDate: string): string {
    return toMmDdYyyy(isoDate);
  }

  sendTest(): void {
    this.testing.set(true);
    this.error.set(null);
    this.testResult.set(null);
    this.reportsApi
      .emailHoursReport({ ...this.request(), testToAddress: this.testToAddress.trim() })
      .subscribe({
        next: (res) => {
          this.testing.set(false);
          this.testResult.set(`Test sent to ${this.testToAddress.trim()} using ${res.sent[0]}'s hours.`);
        },
        error: (err) => {
          this.testing.set(false);
          this.error.set(err?.error ?? 'Failed to send test email.');
        },
      });
  }

  send(): void {
    this.sending.set(true);
    this.error.set(null);
    this.reportsApi.emailHoursReport({ ...this.request(), testToAddress: null }).subscribe({
      next: (res) => {
        this.sending.set(false);
        this.result.set(res);
      },
      error: (err) => {
        this.sending.set(false);
        this.error.set(err?.error ?? 'Failed to send emails.');
      },
    });
  }

  close(): void {
    this.dialogRef.close();
  }

  private request() {
    return {
      locationCode: this.data.locationCode,
      startDate: this.data.startDate,
      endDate: this.data.endDate,
      employeeIds: this.data.employees.map((e) => e.employeeId),
    };
  }
}
