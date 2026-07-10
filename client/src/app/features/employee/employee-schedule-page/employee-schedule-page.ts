import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

import { ShiftAssignmentDto, ShiftAssignmentsApi } from '../../../core/shift-assignments-api';
import { dayOfWeekLabel, toMmDdYyyy } from '../../../core/week-utils';

interface DayGroup {
  date: string;
  dayLabel: string;
  dateLabel: string;
  shifts: ShiftAssignmentDto[];
  hours: number;
}

const round2 = (n: number): number => Math.round(n * 100) / 100;

@Component({
  selector: 'app-employee-schedule-page',
  imports: [RouterLink, MatCardModule, MatButtonModule, MatIconModule],
  templateUrl: './employee-schedule-page.html',
  styleUrl: './employee-schedule-page.scss',
})
export class EmployeeSchedulePage implements OnInit {
  private readonly api = inject(ShiftAssignmentsApi);
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  // Every day the caller has at least one shift assigned, today onward.
  protected readonly days = signal<DayGroup[]>([]);

  protected readonly totalHours = computed(() =>
    round2(this.days().reduce((sum, d) => sum + d.hours, 0)),
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.getMine().subscribe({
      next: (assignments) => this.applyAssignments(assignments),
      error: () => {
        this.error.set('Failed to load your schedule.');
        this.loading.set(false);
      },
    });
  }

  private applyAssignments(assignments: ShiftAssignmentDto[]): void {
    const byDate = new Map<string, ShiftAssignmentDto[]>();
    for (const a of assignments) {
      byDate.set(a.date, [...(byDate.get(a.date) ?? []), a]);
    }

    this.days.set(
      Array.from(byDate.entries())
        .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
        .map(([date, shifts]) => ({
          date,
          dayLabel: dayOfWeekLabel(date),
          dateLabel: toMmDdYyyy(date),
          shifts,
          hours: round2(shifts.reduce((sum, s) => sum + s.hours, 0)),
        })),
    );
    this.loading.set(false);
  }

  shiftTime(shift: ShiftAssignmentDto): string {
    return `${shift.shiftStartTime.slice(0, 5)}–${shift.shiftEndTime.slice(0, 5)}`;
  }
}
