import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { Observable, catchError, forkJoin, of } from 'rxjs';

import { ShiftAssignmentDto, ShiftAssignmentsApi } from '../../../core/shift-assignments-api';
import { TimeEntriesApi, TimeEntryDto } from '../../../core/time-entries-api';
import { LocationSettingsApi } from '../../../core/location-settings-api';
import { addDays, dayOfWeekLabel, formatDate, mondayOf, parseDate, toMmDdYyyy } from '../../../core/week-utils';

type PunchStatus = 'not-started' | 'working' | 'on-break' | 'on-lunch' | 'clocked-out';

interface DayShift {
  assignment: ShiftAssignmentDto;
  earliestClockInLabel: string;
  canClockIn: boolean;
  entry: TimeEntryDto | null;
  status: PunchStatus;
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

function statusOf(entry: TimeEntryDto | null): PunchStatus {
  if (!entry) {
    return 'not-started';
  }
  if (entry.clockOutAt) {
    return 'clocked-out';
  }
  if (entry.breakStartAt && !entry.breakEndAt) {
    return 'on-break';
  }
  if (entry.lunchStartAt && !entry.lunchEndAt) {
    return 'on-lunch';
  }
  return 'working';
}

// Net hours worked once clocked out: wall time minus break and lunch time.
function workedHours(entry: TimeEntryDto): number {
  const msBetween = (start: string, end: string) => new Date(end).getTime() - new Date(start).getTime();

  let ms = msBetween(entry.clockInAt, entry.clockOutAt!);
  if (entry.breakStartAt && entry.breakEndAt) {
    ms -= msBetween(entry.breakStartAt, entry.breakEndAt);
  }
  if (entry.lunchStartAt && entry.lunchEndAt) {
    ms -= msBetween(entry.lunchStartAt, entry.lunchEndAt);
  }
  return round2(ms / 3_600_000);
}

// Shown on the Employee/Admin/Lead home page right after login. Reuses the
// same "mine" endpoint as the full schedule page, just narrowed down to the
// Monday-Sunday of the current week (and, since that endpoint only returns
// today onward, effectively "what's left of this week"). Card styling
// mirrors EmployeeSchedulePage's day cards for a consistent look. Today's
// card additionally walks each shift through Clock In -> (Break | Lunch)* ->
// Clock Out, gated by the location's ClockInWindowMinutes setting.
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
  protected readonly busyShiftId = signal<number | null>(null);
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
        const entryByShiftId = new Map(entries.map((e) => [e.shiftAssignmentId, e]));
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
                const entry = entryByShiftId.get(assignment.id) ?? null;
                return {
                  assignment,
                  earliestClockInLabel: formatHHmm(earliestClockInAt),
                  canClockIn: isToday && !entry && now >= earliestClockInAt,
                  entry,
                  status: statusOf(entry),
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

  timeLabel(iso: string): string {
    return formatHHmm(new Date(iso));
  }

  hoursWorked(entry: TimeEntryDto): number {
    return workedHours(entry);
  }

  clockIn(shift: DayShift): void {
    if (!shift.canClockIn) {
      return;
    }
    this.run(shift.assignment.id, this.timeEntriesApi.clockIn(shift.assignment.id));
  }

  breakStart(shift: DayShift): void {
    if (shift.entry) {
      this.run(shift.assignment.id, this.timeEntriesApi.breakStart(shift.entry.id));
    }
  }

  breakEnd(shift: DayShift): void {
    if (shift.entry) {
      this.run(shift.assignment.id, this.timeEntriesApi.breakEnd(shift.entry.id));
    }
  }

  lunchStart(shift: DayShift): void {
    if (shift.entry) {
      this.run(shift.assignment.id, this.timeEntriesApi.lunchStart(shift.entry.id));
    }
  }

  lunchEnd(shift: DayShift): void {
    if (shift.entry) {
      this.run(shift.assignment.id, this.timeEntriesApi.lunchEnd(shift.entry.id));
    }
  }

  clockOut(shift: DayShift): void {
    if (shift.entry) {
      this.run(shift.assignment.id, this.timeEntriesApi.clockOut(shift.entry.id));
    }
  }

  private run(shiftAssignmentId: number, action: Observable<TimeEntryDto>): void {
    if (this.busyShiftId() !== null) {
      return;
    }
    this.busyShiftId.set(shiftAssignmentId);
    action.subscribe({
      next: (entry) => {
        this.busyShiftId.set(null);
        this.applyEntry(shiftAssignmentId, entry);
      },
      error: (err) => {
        this.busyShiftId.set(null);
        alert(err?.error ?? 'Something went wrong.');
      },
    });
  }

  private applyEntry(shiftAssignmentId: number, entry: TimeEntryDto): void {
    this.days.update((days) =>
      days.map((day) => ({
        ...day,
        shifts: day.shifts.map((s) =>
          s.assignment.id === shiftAssignmentId
            ? { ...s, entry, status: statusOf(entry), canClockIn: false }
            : s,
        ),
      })),
    );
  }
}
