import { Component, computed, input } from '@angular/core';

import { DailyHoursDto, EmployeeHoursReportDto } from '../../../core/reports-api';
import { formatDurationMinutes } from '../../../core/duration-format';
import { dayOfWeekLabel, toMmDdYyyy } from '../../../core/week-utils';

interface ChartRow {
  key: string;
  label: string;
  scheduled: number | null;
  actual: number | null;
  overtime: number;
  doubleTime: number;
  sick: number;
  absences: Absence[];
  // Scheduled time on the absent days not covered by sick hours, drawn as the
  // yellow part of the bar.
  absentMinutes: number;
  status: 'absent' | 'open' | null;
}

interface Absence {
  label: string;
  note: string | null;
}

const dayLabel = (isoDate: string) => `${dayOfWeekLabel(isoDate)} ${toMmDdYyyy(isoDate)}`;

// Absent-day time that sick hours don't already cover.
const missedMinutes = (d: DailyHoursDto) =>
  d.isAbsent ? Math.max(0, (d.scheduledMinutes ?? 0) - d.sickMinutes) : 0;

// Scheduled vs. actual worked time as paired horizontal bars. One employee
// selected -> one pair per day; everyone -> one pair per employee for the
// whole range, biggest overage first. The overtime and double-time parts of
// the worked bar (per the location's overtime rules) take the error color and
// a deeper shade of it; the +/- text on the right still shows the difference
// from the scheduled time.
@Component({
  selector: 'app-payroll-hours-chart',
  templateUrl: './payroll-hours-chart.html',
  styleUrl: './payroll-hours-chart.scss',
})
export class PayrollHoursChart {
  readonly report = input<EmployeeHoursReportDto[]>([]);
  readonly selectedEmployeeId = input<number | 'all'>('all');

  protected readonly isDaily = computed(() => this.selectedEmployeeId() !== 'all');

  protected readonly rows = computed<ChartRow[]>(() => {
    const selected = this.selectedEmployeeId();
    if (selected !== 'all') {
      const emp = this.report().find((r) => r.employeeId === selected);
      return (emp?.days ?? [])
        .filter((d) => d.scheduledMinutes !== null || d.netWorkedMinutes !== null || d.sickMinutes > 0)
        .map((d) => ({
          key: d.date,
          label: dayLabel(d.date),
          scheduled: d.scheduledMinutes,
          actual: d.isAbsent ? 0 : d.netWorkedMinutes,
          overtime: d.overtimeMinutes,
          doubleTime: d.doubleTimeMinutes,
          sick: d.sickMinutes,
          absences: d.isAbsent ? [{ label: dayLabel(d.date), note: d.absenceNote }] : [],
          absentMinutes: missedMinutes(d),
          status: d.isAbsent ? 'absent' : d.stillClockedIn ? 'open' : null,
        }));
    }

    return this.report()
      .map<ChartRow>((e) => ({
        key: String(e.employeeId),
        label: e.fullName,
        scheduled: e.totalScheduledMinutes,
        actual: e.totalNetWorkedMinutes,
        overtime: e.totalOvertimeMinutes,
        doubleTime: e.totalDoubleTimeMinutes,
        sick: e.totalSickMinutes,
        absences: e.days
          .filter((d) => d.isAbsent)
          .map((d) => ({ label: dayLabel(d.date), note: d.absenceNote })),
        absentMinutes: e.days.reduce((sum, d) => sum + missedMinutes(d), 0),
        status: null,
      }))
      .sort((a, b) => this.variance(b) - this.variance(a));
  });

  // Shared scale so every bar in view is comparable.
  private readonly maxMinutes = computed(() =>
    Math.max(60, ...this.rows().flatMap((r) => [r.scheduled ?? 0, (r.actual ?? 0) + r.sick + r.absentMinutes])),
  );

  protected pct(minutes: number): number {
    return (minutes / this.maxMinutes()) * 100;
  }

  // Worked time already includes overtime and double-time, so the primary
  // segment is the rest.
  protected regular(row: ChartRow): number {
    return Math.max(0, (row.actual ?? 0) - row.overtime - row.doubleTime);
  }

  protected variance(row: ChartRow): number {
    return (row.actual ?? 0) - (row.scheduled ?? 0);
  }

  protected duration(minutes: number | null): string {
    return minutes === null ? '–' : formatDurationMinutes(minutes);
  }

  protected varianceLabel(row: ChartRow): string {
    if (row.status === 'absent') {
      return 'Absent';
    }
    if (row.status === 'open') {
      return 'Clocked in';
    }
    const v = this.variance(row);
    if (v === 0) {
      return 'On schedule';
    }
    return `${v > 0 ? '+' : '−'}${formatDurationMinutes(Math.abs(v))}`;
  }

  protected summary(row: ChartRow): string {
    const parts = [`Scheduled ${this.duration(row.scheduled)}`, `Worked ${this.duration(row.actual)}`];
    if (row.overtime > 0) {
      parts.push(`Overtime ${formatDurationMinutes(row.overtime)}`);
    }
    if (row.doubleTime > 0) {
      parts.push(`Double time ${formatDurationMinutes(row.doubleTime)}`);
    }
    if (row.sick > 0) {
      parts.push(`Sick ${formatDurationMinutes(row.sick)}`);
    }
    if (row.absences.length > 0) {
      parts.push(`${row.absences.length} absent day${row.absences.length > 1 ? 's' : ''}`);
    }
    return `${row.label}: ${parts.join(', ')}`;
  }
}
