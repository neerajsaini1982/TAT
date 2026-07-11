import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { catchError, forkJoin, of } from 'rxjs';

import { ShiftAssignmentDto, ShiftAssignmentsApi } from '../../../core/shift-assignments-api';
import { TimeEntriesApi } from '../../../core/time-entries-api';
import { LocationSettingsApi } from '../../../core/location-settings-api';
import { addDays, dayOfWeekLabel, formatDate, mondayOf, parseDate, toMmDdYyyy } from '../../../core/week-utils';

interface DayShift {
  assignment: ShiftAssignmentDto;
  earliestClockInLabel: string;
  canClockIn: boolean;
  clockedIn: boolean;
}

interface DayGroup {
  date: string;
  dayLabel: string;
  dateLabel: string;
  isToday: boolean;
  shifts: DayShift[];
  hours: number;
}

const round2 = (n: number): number => Math.round(n * 100) / 100;
const DEFAULT_CLOCK_IN_WINDOW_MINUTES = 15;

function combineDateAndTime(dateIso: string, time: string): Date {
  const [hours, minutes] = time.slice(0, 5).split(':').map(Number);
  const date = parseDate(dateIso);
  date.setHours(hours, minutes, 0, 0);
  return date;
}

function formatHHmm(date: Date): string {
  return `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;
}

// Shown on the Employee/Admin/Lead home page right after login. Reuses the
// same "mine" endpoint as the full schedule page, just narrowed down to the
// Monday-Sunday of the current week (and, since that endpoint only returns
// today onward, effectively "what's left of this week"). Card styling
// mirrors EmployeeSchedulePage's day cards for a consistent look. Today's
// card additionally gets a working Clock In button per shift, gated by the
// location's ClockInWindowMinutes setting.
@Component({
  selector: 'app-current-week-schedule',
  imports: [MatButtonModule, MatCardModule, MatIconModule],
  templateUrl: './current-week-schedule.html',
  styleUrl: './current-week-schedule.scss',
})
export class CurrentWeekSchedule implements OnInit {
  private readonly api = inject(ShiftAssignmentsApi);
  private readonly timeEntriesApi = inject(TimeEntriesApi);
  private readonly settingsApi = inject(LocationSettingsApi);

  protected readonly loading = signal(true);
  protected readonly days = signal<DayGroup[]>([]);
  protected readonly clockingInId = signal<number | null>(null);
  protected readonly totalHours = computed(() =>
    round2(this.days().reduce((sum, d) => sum + d.hours, 0)),
  );

  ngOnInit(): void {
    const today = formatDate(new Date());
    const weekStart = formatDate(mondayOf(new Date()));
    const weekEnd = formatDate(addDays(mondayOf(new Date()), 6));

    forkJoin({
      assignments: this.api.getMine(),
      settings: this.settingsApi
        .getMine()
        .pipe(catchError(() => of({ clockInWindowMinutes: DEFAULT_CLOCK_IN_WINDOW_MINUTES }))),
      entries: this.timeEntriesApi.getMine(today).pipe(catchError(() => of([]))),
    }).subscribe({
      next: ({ assignments, settings, entries }) => {
        const clockedInShiftIds = new Set(entries.map((e) => e.shiftAssignmentId));
        const now = new Date();

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
            .map(([date, assignmentsForDay]) => {
              const isToday = date === today;
              const shifts: DayShift[] = assignmentsForDay.map((assignment) => {
                const earliestClockInAt = combineDateAndTime(assignment.date, assignment.shiftStartTime);
                earliestClockInAt.setMinutes(earliestClockInAt.getMinutes() - settings.clockInWindowMinutes);
                const clockedIn = clockedInShiftIds.has(assignment.id);
                return {
                  assignment,
                  earliestClockInLabel: formatHHmm(earliestClockInAt),
                  canClockIn: isToday && !clockedIn && now >= earliestClockInAt,
                  clockedIn,
                };
              });

              return {
                date,
                dayLabel: dayOfWeekLabel(date),
                dateLabel: toMmDdYyyy(date),
                isToday,
                shifts,
                hours: round2(assignmentsForDay.reduce((sum, s) => sum + s.hours, 0)),
              };
            }),
        );
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  shiftTime(shift: ShiftAssignmentDto): string {
    return `${shift.shiftStartTime.slice(0, 5)}–${shift.shiftEndTime.slice(0, 5)}`;
  }

  clockIn(shift: DayShift): void {
    if (!shift.canClockIn || this.clockingInId() !== null) {
      return;
    }
    this.clockingInId.set(shift.assignment.id);
    this.timeEntriesApi.clockIn(shift.assignment.id).subscribe({
      next: () => {
        this.clockingInId.set(null);
        this.markClockedIn(shift.assignment.id);
      },
      error: (err) => {
        this.clockingInId.set(null);
        alert(err?.error ?? 'Failed to clock in.');
      },
    });
  }

  private markClockedIn(shiftAssignmentId: number): void {
    this.days.update((days) =>
      days.map((day) => ({
        ...day,
        shifts: day.shifts.map((s) =>
          s.assignment.id === shiftAssignmentId ? { ...s, clockedIn: true, canClockIn: false } : s,
        ),
      })),
    );
  }
}
