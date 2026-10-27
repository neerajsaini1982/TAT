import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import { AccountDto, AccountsApi } from '../../../core/accounts-api';
import {
  AdminCreateTimeOff,
  TimeOffApi,
  TimeOffBlackoutDto,
  TimeOffDayDto,
  TimeOffPreviewDto,
  TimeOffTypeDto,
  apiError,
  formatTimeOffMinutes,
  timeOffDateRange,
} from '../../../core/time-off-api';
import { formatDate } from '../../../core/week-utils';

export interface TimeOffRequestDialogData {
  locationCode: string;
  // 'employee': ask for my own time off (Pending). 'admin': enter approved
  // time off for someone, with the same shift/hours preview as approving.
  mode: 'employee' | 'admin';
}

@Component({
  selector: 'app-time-off-request-dialog',
  imports: [
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
  ],
  templateUrl: './time-off-request-dialog.html',
  styleUrl: './time-off-request-dialog.scss',
})
export class TimeOffRequestDialog implements OnInit {
  private readonly api = inject(TimeOffApi);
  private readonly accountsApi = inject(AccountsApi);
  private readonly dialogRef = inject<MatDialogRef<TimeOffRequestDialog, boolean>>(MatDialogRef);
  protected readonly data = inject<TimeOffRequestDialogData>(MAT_DIALOG_DATA);

  protected readonly types = signal<TimeOffTypeDto[]>([]);
  protected readonly blackouts = signal<TimeOffBlackoutDto[]>([]);
  protected readonly employees = signal<AccountDto[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly saving = signal(false);
  // Admin mode only: the shifts and per-day hours shown before saving.
  protected readonly preview = signal<TimeOffPreviewDto | null>(null);
  protected days: TimeOffDayDto[] = [];
  protected removeShifts = false;

  protected form = {
    accountId: null as number | null,
    typeId: null as number | null,
    startDate: formatDate(new Date()),
    endDate: formatDate(new Date()),
    isPartialDay: false,
    hours: 4 as number | null,
    note: '',
  };

  protected readonly isAdmin = this.data.mode === 'admin';
  protected readonly format = formatTimeOffMinutes;
  protected readonly range = timeOffDateRange;
  protected readonly upcomingBlackouts = computed(() => {
    const today = formatDate(new Date());
    return this.blackouts().filter((b) => b.endDate >= today);
  });

  ngOnInit(): void {
    this.api.types(this.data.locationCode).subscribe({
      next: (types) => {
        this.types.set(types);
        this.form.typeId = types[0]?.id ?? null;
      },
      error: (err) => this.error.set(apiError(err, 'Failed to load time-off types.')),
    });
    if (this.isAdmin) {
      this.accountsApi.getAll(this.data.locationCode).subscribe((accounts) =>
        this.employees.set(
          accounts
            .filter((a) => a.isActive && a.role !== 'Sa' && a.role !== 'Kiosk')
            .sort((a, b) => `${a.firstName} ${a.lastName}`.localeCompare(`${b.firstName} ${b.lastName}`)),
        ),
      );
    } else {
      this.api.blackouts(this.data.locationCode).subscribe((b) => this.blackouts.set(b));
    }
  }

  protected startChanged(): void {
    if (this.form.endDate < this.form.startDate || this.form.isPartialDay) {
      this.form.endDate = this.form.startDate;
    }
    this.preview.set(null);
  }

  protected changed(): void {
    this.preview.set(null);
  }

  private request(): AdminCreateTimeOff | null {
    this.error.set(null);
    if (this.isAdmin && this.form.accountId === null) {
      this.error.set('Choose an employee.');
      return null;
    }
    if (this.form.typeId === null) {
      this.error.set('Choose a type.');
      return null;
    }
    if (!this.form.startDate || !this.form.endDate) {
      this.error.set('Choose the dates.');
      return null;
    }
    if (this.form.isPartialDay && (!this.form.hours || this.form.hours <= 0 || this.form.hours >= 24)) {
      this.error.set('Enter how many hours you need off.');
      return null;
    }
    return {
      accountId: this.form.accountId ?? 0,
      timeOffTypeId: this.form.typeId,
      startDate: this.form.startDate,
      endDate: this.form.isPartialDay ? this.form.startDate : this.form.endDate,
      isPartialDay: this.form.isPartialDay,
      partialMinutes: this.form.isPartialDay ? Math.round((this.form.hours ?? 0) * 60) : null,
      note: this.form.note.trim() || null,
      removeConflictingAssignments: this.removeShifts,
      days: this.isAdmin ? this.days : null,
    };
  }

  protected save(): void {
    const request = this.request();
    if (!request) return;

    // Admin: show what this means (shifts, hours per day) before saving.
    if (this.isAdmin && !this.preview()) {
      this.saving.set(true);
      this.api.previewNew(request).subscribe({
        next: (preview) => {
          this.preview.set(preview);
          this.days = preview.suggestedDays.map((d) => ({ ...d }));
          this.saving.set(false);
        },
        error: (err) => {
          this.error.set(apiError(err, 'Failed to check these dates.'));
          this.saving.set(false);
        },
      });
      return;
    }

    this.saving.set(true);
    const call = this.isAdmin ? this.api.adminCreate({ ...request, days: this.days }) : this.api.submit(request);
    call.subscribe({
      next: () => this.dialogRef.close(true),
      error: (err) => {
        this.error.set(apiError(err, 'Failed to save the time off.'));
        this.saving.set(false);
      },
    });
  }

  protected hours(day: TimeOffDayDto): number {
    return Math.round((day.minutes / 60) * 100) / 100;
  }

  protected setHours(day: TimeOffDayDto, hours: number | null): void {
    day.minutes = Math.max(0, Math.min(1440, Math.round((hours ?? 0) * 60)));
  }

  protected totalMinutes(): number {
    return this.days.reduce((sum, d) => sum + d.minutes, 0);
  }

  protected dayLabel(iso: string): string {
    return timeOffDateRange({ startDate: iso, endDate: iso });
  }

  protected cancel(): void {
    this.dialogRef.close(false);
  }
}
