import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

import { ShiftAssignmentDto, ShiftAssignmentsApi } from '../../../core/shift-assignments-api';
import { addDays, dayOfWeekLabel, formatDate, mondayOf, toMmDdYyyy } from '../../../core/week-utils';

interface DayGroup {
  date: string;
  dayLabel: string;
  dateLabel: string;
  shifts: ShiftAssignmentDto[];
  hours: number;
}

const round2 = (n: number): number => Math.round(n * 100) / 100;

// Shown on the Employee/Admin/Lead home page right after login. Reuses the
// same "mine" endpoint as the full schedule page, just narrowed down to the
// Monday-Sunday of the current week (and, since that endpoint only returns
// today onward, effectively "what's left of this week").
@Component({
  selector: 'app-current-week-schedule',
  imports: [MatIconModule],
  templateUrl: './current-week-schedule.html',
  styleUrl: './current-week-schedule.scss',
})
export class CurrentWeekSchedule implements OnInit {
  private readonly api = inject(ShiftAssignmentsApi);

  protected readonly loading = signal(true);
  protected readonly days = signal<DayGroup[]>([]);
  protected readonly totalHours = computed(() =>
    round2(this.days().reduce((sum, d) => sum + d.hours, 0)),
  );

  ngOnInit(): void {
    const weekStart = formatDate(mondayOf(new Date()));
    const weekEnd = formatDate(addDays(mondayOf(new Date()), 6));

    this.api.getMine().subscribe({
      next: (assignments) => {
        const byDate = new Map<string, ShiftAssignmentDto[]>();
        for (const a of assignments) {
          if (a.date < weekStart || a.date > weekEnd) {
            continue;
          }
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
      },
      error: () => this.loading.set(false),
    });
  }

  shiftTime(shift: ShiftAssignmentDto): string {
    return `${shift.shiftStartTime.slice(0, 5)}–${shift.shiftEndTime.slice(0, 5)}`;
  }
}
