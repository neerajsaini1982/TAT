import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import { AccountDto, AccountsApi } from '../../../core/accounts-api';
import { formatClockTime } from '../../../core/day-view-layout';
import { formatDurationMinutes } from '../../../core/duration-format';
import { ReportsApi, ScheduledShiftDto } from '../../../core/reports-api';
import { addDays, dayOfWeekLabel, formatDate, toMmDdYyyy } from '../../../core/week-utils';

// One employee's posted shifts between two dates (see
// ReportsController.GetEmployeeScheduleReport). Scheduled times only — a
// shift they called out of or were absent for is listed like any other.
// Admin/Sa only.
@Component({
  selector: 'app-admin-employee-schedule-report-page',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  templateUrl: './admin-employee-schedule-report-page.html',
  styleUrl: './admin-employee-schedule-report-page.scss',
})
export class AdminEmployeeScheduleReportPage implements OnInit {
  private readonly reportsApi = inject(ReportsApi);
  private readonly accountsApi = inject(AccountsApi);
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  protected employeeId: number | null = null;
  protected startDate = formatDate(addDays(new Date(), -29));
  protected endDate = formatDate(new Date());

  protected readonly employees = signal<AccountDto[]>([]);
  // Former employees stay pickable — their past schedule is still worth
  // looking up — but below everyone current.
  protected readonly activeEmployees = computed(() => this.employees().filter((e) => e.isActive));
  protected readonly inactiveEmployees = computed(() => this.employees().filter((e) => !e.isActive));
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  // Null until a report has been run.
  protected readonly shifts = signal<ScheduledShiftDto[] | null>(null);
  // Who and when the shifts on screen are for — the controls can be changed
  // without re-running.
  protected readonly ranFor = signal<{ name: string; startDate: string; endDate: string } | null>(null);

  protected readonly totalMinutes = computed(() =>
    (this.shifts() ?? []).reduce((sum, s) => sum + s.scheduledMinutes, 0),
  );
  protected readonly daysScheduled = computed(() => new Set((this.shifts() ?? []).map((s) => s.date)).size);

  protected readonly formatClockTime = formatClockTime;
  protected readonly formatDurationMinutes = formatDurationMinutes;
  protected readonly dayOfWeekLabel = dayOfWeekLabel;
  protected readonly toMmDdYyyy = toMmDdYyyy;

  ngOnInit(): void {
    this.accountsApi.getAll(this.locationCode).subscribe({
      next: (accounts) => this.employees.set(accounts.sort((a, b) => this.fullName(a).localeCompare(this.fullName(b)))),
      error: () => this.error.set('Failed to load employees.'),
    });
  }

  run(): void {
    const employee = this.employees().find((e) => e.id === this.employeeId);
    if (!employee) {
      this.error.set('Choose an employee.');
      return;
    }
    if (this.endDate < this.startDate) {
      this.error.set("End date can't be before start date.");
      return;
    }

    const { startDate, endDate } = this;
    this.error.set(null);
    this.loading.set(true);
    this.reportsApi.getEmployeeScheduleReport(this.locationCode, employee.id, startDate, endDate).subscribe({
      next: (shifts) => {
        this.shifts.set(shifts);
        this.ranFor.set({ name: this.fullName(employee), startDate, endDate });
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(typeof err?.error === 'string' ? err.error : 'Failed to load the report.');
        this.loading.set(false);
      },
    });
  }

  fullName(account: AccountDto): string {
    return `${account.firstName} ${account.lastName}`;
  }
}
