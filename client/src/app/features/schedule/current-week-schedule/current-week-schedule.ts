import { Component, DestroyRef, Input, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { Observable, catchError, forkJoin, of } from 'rxjs';

import { ShiftAssignmentDto, ShiftAssignmentsApi } from '../../../core/shift-assignments-api';
import { TimeEntriesApi, TimeEntryDto } from '../../../core/time-entries-api';
import { LocationSettingsApi } from '../../../core/location-settings-api';
import { ScheduleRealtime } from '../../../core/schedule-realtime';
import { employeeColor } from '../../../core/employee-colors';
import { isBreak2OverLimit, isBreakOverLimit, isLateClockIn, isLunchOverLimit } from '../../../core/attendance-flags';
import {
  addDays,
  combineDateAndTime,
  dayOfWeekLabel,
  formatDate,
  formatHHmm,
  mondayOf,
  toMmDdYyyy,
} from '../../../core/week-utils';
import { NoteDialog, NoteDialogData } from '../../admin/note-dialog/note-dialog';

type PunchStatus = 'not-started' | 'working' | 'on-break' | 'on-lunch' | 'on-break2' | 'clocked-out';

interface DayShift {
  assignment: ShiftAssignmentDto;
  employeeName: string;
  isMine: boolean;
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
const DEFAULT_SETTINGS = {
  clockInWindowMinutes: 15,
  lateClockInGraceMinutes: 5,
  breakLimitMinutes: 15,
  lunchLimitMinutes: 30,
};

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
  if (entry.break2StartAt && !entry.break2EndAt) {
    return 'on-break2';
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
  if (entry.break2StartAt && entry.break2EndAt) {
    ms -= msBetween(entry.break2StartAt, entry.break2EndAt);
  }
  return round2(ms / 3_600_000);
}

// Shown on the Employee/Admin/Lead home page right after login. In the
// default 'mine' scope it's just the caller's own upcoming shifts (used on
// the Employee home page). In 'location' scope (Admin/Lead home page) it
// shows every account's shifts for the location, each labeled with the
// employee's name since more than one person can be working the same day —
// but punch actions and the punch timeline only ever apply to the caller's
// own shift, since punching is self-service and there's no admin visibility
// into other employees' time entries.
// Narrowed down to the Monday-Sunday of the current week, today onward
// (i.e. "what's left of this week"). Card styling mirrors
// EmployeeSchedulePage's day cards for a consistent look. Today's card
// additionally walks the caller's own shift through Clock In -> Break ->
// Lunch -> (a second Break, once Lunch has ended) -> Clock Out, shown in
// that chronological order, gated by the location's ClockInWindowMinutes
// setting.
@Component({
  selector: 'app-current-week-schedule',
  imports: [MatButtonModule, MatCardModule, MatIconModule],
  templateUrl: './current-week-schedule.html',
  styleUrl: './current-week-schedule.scss',
})
export class CurrentWeekSchedule implements OnInit {
  @Input() scope: 'mine' | 'location' = 'mine';
  @Input() locationCode: string | null = null;

  private readonly api = inject(ShiftAssignmentsApi);
  private readonly timeEntriesApi = inject(TimeEntriesApi);
  private readonly settingsApi = inject(LocationSettingsApi);
  private readonly realtime = inject(ScheduleRealtime);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);

  protected readonly loading = signal(true);
  protected readonly days = signal<DayGroup[]>([]);
  protected readonly busyShiftId = signal<number | null>(null);
  protected readonly markingId = signal<number | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly showEmployeeNames = computed(() => this.scope === 'location');
  protected readonly employeeColor = employeeColor;
  protected readonly totalHours = computed(() =>
    round2(this.days().reduce((sum, d) => sum + d.hours, 0)),
  );

  // Attendance thresholds for badging the caller's own shift(s) as
  // Late/Long Break/Long Lunch — see LocationSettings and attendance-flags.ts.
  private lateClockInGraceMinutes = DEFAULT_SETTINGS.lateClockInGraceMinutes;
  private breakLimitMinutes = DEFAULT_SETTINGS.breakLimitMinutes;
  private lunchLimitMinutes = DEFAULT_SETTINGS.lunchLimitMinutes;

  ngOnInit(): void {
    this.load();

    if (this.locationCode) {
      this.realtime
        .connect(this.locationCode)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe(() => this.load());
    }
  }

  private load(): void {
    const today = formatDate(new Date());
    const weekStart = formatDate(mondayOf(new Date()));
    const weekEnd = formatDate(addDays(mondayOf(new Date()), 6));
    const isLocationScope = this.scope === 'location' && this.locationCode;

    forkJoin({
      mine: this.api.getMine(),
      assignments: isLocationScope ? this.api.getForWeek(weekStart, this.locationCode!) : this.api.getMine(),
      settings: this.settingsApi.getMine().pipe(catchError(() => of(DEFAULT_SETTINGS))),
      entries: this.timeEntriesApi.getMine(today).pipe(catchError(() => of([]))),
    }).subscribe({
      next: ({ mine, assignments, settings, entries }) => {
        this.lateClockInGraceMinutes = settings.lateClockInGraceMinutes;
        this.breakLimitMinutes = settings.breakLimitMinutes;
        this.lunchLimitMinutes = settings.lunchLimitMinutes;

        const entryByShiftId = new Map(entries.map((e) => [e.shiftAssignmentId, e]));
        const mineIds = new Set(mine.map((a) => a.id));
        const now = new Date();

        const byDate = new Map<string, ShiftAssignmentDto[]>();
        for (const a of assignments) {
          if (a.date < weekStart || a.date > weekEnd || a.date < today) {
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
                const isMine = mineIds.has(assignment.id);
                const earliestClockInAt = combineDateAndTime(assignment.date, assignment.shiftStartTime);
                earliestClockInAt.setMinutes(earliestClockInAt.getMinutes() - settings.clockInWindowMinutes);
                const entry = isMine ? (entryByShiftId.get(assignment.id) ?? null) : null;
                return {
                  assignment,
                  employeeName: `${assignment.accountFirstName} ${assignment.accountLastName}`,
                  isMine,
                  earliestClockInLabel: formatHHmm(earliestClockInAt),
                  canClockIn: isToday && isMine && !entry && now >= earliestClockInAt,
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

  minutesBetween(startIso: string, endIso: string): number {
    return Math.round((new Date(endIso).getTime() - new Date(startIso).getTime()) / 60_000);
  }

  hoursWorked(entry: TimeEntryDto): number {
    return workedHours(entry);
  }

  isOnBreak(entry: TimeEntryDto): boolean {
    return !!entry.breakStartAt && !entry.breakEndAt;
  }

  isOnLunch(entry: TimeEntryDto): boolean {
    return !!entry.lunchStartAt && !entry.lunchEndAt;
  }

  isOnBreak2(entry: TimeEntryDto): boolean {
    return !!entry.break2StartAt && !entry.break2EndAt;
  }

  // Badges below only ever apply to the caller's own shift: entry is null
  // for teammates' shifts in location scope (see load()), so these are
  // naturally false there without an extra isMine check.
  isLate(shift: DayShift): boolean {
    return !!shift.entry && isLateClockIn(shift.entry, shift.assignment, this.lateClockInGraceMinutes);
  }

  isBreakOver(entry: TimeEntryDto): boolean {
    return isBreakOverLimit(entry, this.breakLimitMinutes, new Date()) || isBreak2OverLimit(entry, this.breakLimitMinutes, new Date());
  }

  isLunchOver(entry: TimeEntryDto): boolean {
    return isLunchOverLimit(entry, this.lunchLimitMinutes, new Date());
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

  break2Start(shift: DayShift): void {
    if (shift.entry) {
      this.run(shift.assignment.id, this.timeEntriesApi.break2Start(shift.entry.id));
    }
  }

  break2End(shift: DayShift): void {
    if (shift.entry) {
      this.run(shift.assignment.id, this.timeEntriesApi.break2End(shift.entry.id));
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

  // Location-scope only (Admin/Lead home page) — lets an admin mark a
  // teammate absent right from today's schedule instead of having to open
  // the full weekly grid at admin/schedule. Reuses the same NoteDialog and
  // ShiftAssignmentsApi.markAbsent() the grid already uses, so both screens
  // stay backed by the same server rule (409 if a TimeEntry already exists).
  markAbsent(shift: DayShift): void {
    this.dialog
      .open<NoteDialog, NoteDialogData, string>(NoteDialog, {
        data: {
          title: `Mark ${shift.employeeName} absent`,
          label: 'Reason',
          noteRequired: true,
          confirmLabel: 'Mark Absent',
        },
      })
      .afterClosed()
      .subscribe((note) => {
        if (!note) {
          return;
        }
        this.markingId.set(shift.assignment.id);
        this.api.markAbsent(shift.assignment.id, { isAbsent: true, note }).subscribe({
          next: () => {
            this.markingId.set(null);
            this.load();
          },
          error: (err) => {
            this.markingId.set(null);
            this.error.set(err?.error ?? 'Failed to mark absent.');
          },
        });
      });
  }

  clearAbsent(shift: DayShift): void {
    this.markingId.set(shift.assignment.id);
    this.api.markAbsent(shift.assignment.id, { isAbsent: false, note: null }).subscribe({
      next: () => {
        this.markingId.set(null);
        this.load();
      },
      error: (err) => {
        this.markingId.set(null);
        this.error.set(err?.error ?? 'Failed to clear absence.');
      },
    });
  }
}
