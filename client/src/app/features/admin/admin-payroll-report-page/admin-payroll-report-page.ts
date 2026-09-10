import { Component, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatTable, MatTableModule } from '@angular/material/table';
import { MatDialog } from '@angular/material/dialog';

import { DailyHoursDto, EmployeeHoursReportDto, ReportsApi } from '../../../core/reports-api';
import { ShiftAssignmentsApi } from '../../../core/shift-assignments-api';
import { formatDurationOrDash } from '../../../core/duration-format';
import { addDays, dayOfWeekLabel, formatDate, toMmDdYyyy } from '../../../core/week-utils';
import { Auth } from '../../../core/auth';
import { SickHoursDialog } from '../sick-hours-dialog/sick-hours-dialog';

@Component({
  selector: 'app-admin-payroll-report-page',
  imports: [
    FormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatSelectModule,
    MatTableModule,
  ],
  templateUrl: './admin-payroll-report-page.html',
  styleUrl: './admin-payroll-report-page.scss',
})
export class AdminPayrollReportPage implements OnInit {
  private readonly reportsApi = inject(ReportsApi);
  private readonly shiftAssignmentsApi = inject(ShiftAssignmentsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly dialog = inject(MatDialog);
  private readonly auth = inject(Auth);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  // This page is reachable by Lead as well as Admin (see adminGuard), but
  // POST /api/sick-time-entries is Admin/Sa-only server-side (same policy as
  // the inline sick-hours edit's SetSickMinutes) — hide the button rather
  // than let a Lead open a dialog that can only 403 on save.
  protected readonly canAddSickHours = this.auth.role() === 'Admin' || this.auth.role() === 'Sa';

  // CdkTable only re-evaluates matRowDef's `when` predicate when it
  // re-renders rows, which a plain signal update elsewhere doesn't trigger
  // on its own — renderRows() after toggling forces that re-check.
  @ViewChild(MatTable) private matTable?: MatTable<EmployeeHoursReportDto>;

  protected readonly columns = [
    'expand',
    'fullName',
    'netWorkedTime',
    'overtimeTime',
    'sickTime',
    'totalTime',
    'notes',
  ];

  // Assignment IDs with a sick-hours save in flight — disables that row's
  // input and lets the template show a per-row saving/error state without a
  // full report reload (which would collapse every expanded row).
  protected readonly savingSickHoursFor = signal<ReadonlySet<number>>(new Set());
  protected readonly sickHoursError = signal<string | null>(null);

  // Defaults to the trailing week, same as the report is most often run.
  protected startDate = formatDate(addDays(new Date(), -6));
  protected endDate = formatDate(new Date());

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly report = signal<EmployeeHoursReportDto[]>([]);
  private readonly expandedIds = signal<ReadonlySet<number>>(new Set());

  // 'all' or one employeeId — deliberately not reset by run(), so switching
  // date ranges keeps the admin looking at the same employee. Options are
  // always derived from the full (unfiltered) report so the dropdown itself
  // doesn't shrink to just the one currently selected.
  protected readonly selectedEmployeeId = signal<number | 'all'>('all');

  protected readonly employeeOptions = computed(() => {
    const byId = new Map<number, string>();
    for (const row of this.report()) {
      byId.set(row.employeeId, row.fullName);
    }
    return [...byId.entries()]
      .map(([employeeId, fullName]) => ({ employeeId, fullName }))
      .sort((a, b) => a.fullName.localeCompare(b.fullName));
  });

  protected readonly filteredReport = computed(() => {
    const rows = this.report();
    const selected = this.selectedEmployeeId();
    return selected === 'all' ? rows : rows.filter((r) => r.employeeId === selected);
  });

  // Bound via matRowDef's `when` so the detail row simply doesn't render
  // (rather than rendering collapsed/empty) until its employee is expanded.
  protected readonly isExpandedRow = (_index: number, row: EmployeeHoursReportDto): boolean =>
    this.expandedIds().has(row.employeeId);

  // Grand totals across every employee currently visible (i.e. respecting
  // the employee filter) — rendered as a footer row under the table.
  protected readonly totals = computed(() => {
    const rows = this.filteredReport();
    return {
      regularMinutes: rows.reduce((sum, r) => sum + this.regularMinutes(r), 0),
      overtimeMinutes: rows.reduce((sum, r) => sum + r.totalOvertimeMinutes, 0),
      sickMinutes: rows.reduce((sum, r) => sum + r.totalSickMinutes, 0),
      totalMinutes: rows.reduce((sum, r) => sum + this.totalMinutes(r), 0),
      absentDays: rows.reduce((sum, r) => sum + r.absentDays, 0),
      openEntryDays: rows.reduce((sum, r) => sum + r.openEntryDays, 0),
    };
  });

  // totalNetWorkedMinutes includes overtime, but ADP takes regular and
  // overtime hours as two separate entries — this is the "Net Worked Time"
  // column's regular-hours-only figure (excludes whatever's already
  // counted in the Overtime column) so the two columns add back up to the
  // actual time worked without double-counting overtime into regular.
  regularMinutes(emp: EmployeeHoursReportDto): number {
    return emp.totalNetWorkedMinutes - emp.totalOvertimeMinutes;
  }

  // Total Hours = regular + overtime + sick, i.e. every paid minute in the
  // range regardless of column. Equivalent to totalNetWorkedMinutes (which
  // already includes overtime) + totalSickMinutes.
  totalMinutes(emp: EmployeeHoursReportDto): number {
    return emp.totalNetWorkedMinutes + emp.totalSickMinutes;
  }

  ngOnInit(): void {
    this.run();
  }

  run(): void {
    if (this.endDate < this.startDate) {
      this.error.set('End date must be on or after start date.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);
    this.reportsApi.getHoursReport(this.locationCode, this.startDate, this.endDate).subscribe({
      next: (report) => {
        this.report.set(report);
        this.expandedIds.set(new Set());
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err?.error ?? 'Failed to load report.');
        this.loading.set(false);
      },
    });
  }

  openAddSickHours(): void {
    this.dialog
      .open(SickHoursDialog, { data: { locationCode: this.locationCode }, autoFocus: 'dialog' })
      .afterClosed()
      .subscribe((saved) => {
        if (saved) {
          this.run();
        }
      });
  }

  toggle(employeeId: number): void {
    this.expandedIds.update((current) => {
      const next = new Set(current);
      if (next.has(employeeId)) {
        next.delete(employeeId);
      } else {
        next.add(employeeId);
      }
      return next;
    });
    this.matTable?.renderRows();
  }

  isExpanded(employeeId: number): boolean {
    return this.expandedIds().has(employeeId);
  }

  formatDuration(minutes: number): string {
    return formatDurationOrDash(minutes);
  }

  // Decimal-hours form of a duration (e.g. 72h 15m -> "72.25") — this is
  // the number that actually goes into ADP, which takes hours as a decimal
  // rather than hours-and-minutes.
  formatDecimalHours(minutes: number): string {
    return (minutes / 60).toFixed(2);
  }

  dayLabel(isoDate: string): string {
    return `${dayOfWeekLabel(isoDate)} ${toMmDdYyyy(isoDate)}`;
  }

  isSavingSickHours(day: DailyHoursDto): boolean {
    return day.shiftAssignmentId !== null && this.savingSickHoursFor().has(day.shiftAssignmentId);
  }

  // Admin types the decimal-hours figure directly (same convention as the
  // read-only ADP numbers elsewhere on this page) — converted to minutes
  // for storage. On success, refetches rather than patching the signal in
  // place: a day can fold in more than one ShiftAssignment (split shifts),
  // and this write only targets one of them, so the day's true total can
  // only come from the server. Deliberately doesn't reset expandedIds like
  // run() does, so the row the admin is editing stays open. Only called for
  // a day that has a ShiftAssignment — see the template's shiftAssignmentId
  // !== null guard, which is what makes the input editable at all.
  updateSickHours(day: DailyHoursDto, rawValue: string): void {
    const shiftAssignmentId = day.shiftAssignmentId;
    if (shiftAssignmentId === null) {
      return;
    }

    const hours = parseFloat(rawValue);
    if (rawValue.trim() === '' || isNaN(hours) || hours < 0) {
      this.sickHoursError.set('Sick hours must be a number 0 or greater.');
      return;
    }

    this.sickHoursError.set(null);
    this.savingSickHoursFor.update((current) => new Set(current).add(shiftAssignmentId));

    this.shiftAssignmentsApi.setSickMinutes(shiftAssignmentId, { sickMinutes: Math.round(hours * 60) }).subscribe({
      next: () => {
        this.savingSickHoursFor.update((current) => {
          const next = new Set(current);
          next.delete(shiftAssignmentId);
          return next;
        });
        this.reportsApi.getHoursReport(this.locationCode, this.startDate, this.endDate).subscribe({
          next: (report) => this.report.set(report),
          error: (err) => this.sickHoursError.set(err?.error ?? 'Saved, but failed to refresh the report.'),
        });
      },
      error: (err) => {
        this.sickHoursError.set(err?.error ?? 'Failed to save sick hours.');
        this.savingSickHoursFor.update((current) => {
          const next = new Set(current);
          next.delete(shiftAssignmentId);
          return next;
        });
      },
    });
  }
}
