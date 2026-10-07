import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import { formatClockTime } from '../../../core/day-view-layout';
import { EmployeeCallOutReportDto, ReportsApi } from '../../../core/reports-api';
import { addDays, dayOfWeekLabel, formatDate, toMmDdYyyy } from '../../../core/week-utils';

type SortBy = 'name' | 'covers' | 'callOuts';

// Who calls out and who steps in (see ReportsController.GetCallOutReport):
// one row per employee with how many shifts they were scheduled, how many
// they were marked absent for, and how many they covered for someone else,
// each expandable to the shifts behind the numbers. Admin/Sa only.
@Component({
  selector: 'app-admin-call-out-report-page',
  imports: [
    FormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
  ],
  templateUrl: './admin-call-out-report-page.html',
  styleUrl: './admin-call-out-report-page.scss',
  // Wide table — opts out of the shell's 960px column (styles.scss).
  host: { class: 'full-width-page' },
})
export class AdminCallOutReportPage implements OnInit {
  private readonly reportsApi = inject(ReportsApi);
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  // Defaults to the trailing 30 days — a week is too short to say much
  // about how reliable someone is.
  protected startDate = formatDate(addDays(new Date(), -29));
  protected endDate = formatDate(new Date());

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly report = signal<EmployeeCallOutReportDto[]>([]);
  protected readonly sortBy = signal<SortBy>('covers');
  // Most of a roster has nothing to show in a given month, so they're left
  // out unless asked for.
  protected readonly onlyWithActivity = signal(true);
  private readonly expandedIds = signal<ReadonlySet<number>>(new Set());

  protected readonly rows = computed(() => {
    const rows = this.report().filter((r) => !this.onlyWithActivity() || r.callOuts > 0 || r.shiftsCovered > 0);
    const byName = (a: EmployeeCallOutReportDto, b: EmployeeCallOutReportDto) => a.fullName.localeCompare(b.fullName);
    switch (this.sortBy()) {
      case 'covers':
        return rows.sort((a, b) => b.shiftsCovered - a.shiftsCovered || byName(a, b));
      case 'callOuts':
        return rows.sort((a, b) => b.callOuts - a.callOuts || byName(a, b));
      default:
        return rows.sort(byName);
    }
  });

  protected readonly totals = computed(() => {
    const all = this.report();
    const callOuts = all.reduce((sum, r) => sum + r.callOuts, 0);
    const covered = all.reduce((sum, r) => sum + r.callOutsCovered, 0);
    return {
      scheduled: all.reduce((sum, r) => sum + r.scheduledShifts, 0),
      callOuts,
      covered,
      uncovered: callOuts - covered,
    };
  });

  ngOnInit(): void {
    this.run();
  }

  run(): void {
    if (this.endDate < this.startDate) {
      this.error.set("End date can't be before start date.");
      return;
    }

    this.error.set(null);
    this.loading.set(true);
    this.reportsApi.getCallOutReport(this.locationCode, this.startDate, this.endDate).subscribe({
      next: (report) => {
        this.report.set(report);
        this.expandedIds.set(new Set());
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(typeof err?.error === 'string' ? err.error : 'Failed to load the report.');
        this.loading.set(false);
      },
    });
  }

  isExpanded(row: EmployeeCallOutReportDto): boolean {
    return this.expandedIds().has(row.employeeId);
  }

  toggle(row: EmployeeCallOutReportDto): void {
    const next = new Set(this.expandedIds());
    if (!next.delete(row.employeeId)) {
      next.add(row.employeeId);
    }
    this.expandedIds.set(next);
  }

  // Share of their scheduled shifts they were absent for.
  callOutRate(row: EmployeeCallOutReportDto): string {
    return row.scheduledShifts === 0 ? '-' : `${Math.round((row.callOuts / row.scheduledShifts) * 100)}%`;
  }

  dateLabel(isoDate: string): string {
    return `${dayOfWeekLabel(isoDate).slice(0, 3)} ${toMmDdYyyy(isoDate)}`;
  }

  shiftLabel(detail: { shiftName: string; shiftStartTime: string; shiftEndTime: string }): string {
    return `${detail.shiftName}, ${formatClockTime(detail.shiftStartTime)} – ${formatClockTime(detail.shiftEndTime)}`;
  }
}
