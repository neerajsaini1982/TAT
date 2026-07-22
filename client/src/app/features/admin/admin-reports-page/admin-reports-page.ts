import { Component, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTable, MatTableModule } from '@angular/material/table';

import { EmployeeHoursReportDto, ReportsApi } from '../../../core/reports-api';
import { formatDurationOrDash } from '../../../core/duration-format';
import { addDays, dayOfWeekLabel, formatDate, toMmDdYyyy } from '../../../core/week-utils';

@Component({
  selector: 'app-admin-reports-page',
  imports: [
    FormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatTableModule,
  ],
  templateUrl: './admin-reports-page.html',
  styleUrl: './admin-reports-page.scss',
})
export class AdminReportsPage implements OnInit {
  private readonly reportsApi = inject(ReportsApi);
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  // CdkTable only re-evaluates matRowDef's `when` predicate when it
  // re-renders rows, which a plain signal update elsewhere doesn't trigger
  // on its own — renderRows() after toggling forces that re-check.
  @ViewChild(MatTable) private matTable?: MatTable<EmployeeHoursReportDto>;

  protected readonly columns = [
    'expand',
    'employeeId',
    'fullName',
    'workedTime',
    'breakTime',
    'lunchTime',
    'netWorkedTime',
    'notes',
  ];

  // Defaults to the trailing week, same as the report is most often run.
  protected startDate = formatDate(addDays(new Date(), -6));
  protected endDate = formatDate(new Date());

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly report = signal<EmployeeHoursReportDto[]>([]);
  private readonly expandedIds = signal<ReadonlySet<number>>(new Set());

  // Bound via matRowDef's `when` so the detail row simply doesn't render
  // (rather than rendering collapsed/empty) until its employee is expanded.
  protected readonly isExpandedRow = (_index: number, row: EmployeeHoursReportDto): boolean =>
    this.expandedIds().has(row.employeeId);

  // Grand totals across every employee currently in the report — rendered
  // as a footer row under the table.
  protected readonly totals = computed(() => {
    const rows = this.report();
    return {
      workedMinutes: rows.reduce((sum, r) => sum + r.totalWorkedMinutes, 0),
      breakMinutes: rows.reduce((sum, r) => sum + r.totalBreakMinutes, 0),
      lunchMinutes: rows.reduce((sum, r) => sum + r.totalLunchMinutes, 0),
      netWorkedMinutes: rows.reduce((sum, r) => sum + r.totalNetWorkedMinutes, 0),
      absentDays: rows.reduce((sum, r) => sum + r.absentDays, 0),
      openEntryDays: rows.reduce((sum, r) => sum + r.openEntryDays, 0),
    };
  });

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

  dayLabel(isoDate: string): string {
    return `${dayOfWeekLabel(isoDate)} ${toMmDdYyyy(isoDate)}`;
  }
}
