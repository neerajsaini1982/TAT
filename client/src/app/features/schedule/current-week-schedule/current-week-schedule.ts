import { Component, DestroyRef, Input, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { Observable, catchError, forkJoin, of } from 'rxjs';

import { ShiftAssignmentDto, ShiftAssignmentsApi } from '../../../core/shift-assignments-api';
import { TimeEntriesApi, TimeEntryDto, TimeEntrySegmentDto } from '../../../core/time-entries-api';
import { BreakKind } from '../../../core/shifts-api';
import { LocationSettingsApi, TimeFormat } from '../../../core/location-settings-api';
import { ScheduleRealtime } from '../../../core/schedule-realtime';
import { employeeColor } from '../../../core/employee-colors';
import { formatInstant, formatTimeOnly } from '../../../core/location-time';
import { isAnySegmentOverLimit, isLateClockIn } from '../../../core/attendance-flags';
import { addDays, combineDateAndTime, dayOfWeekLabel, formatDate, mondayOf, toMmDdYyyy } from '../../../core/week-utils';
import { NoteDialog, NoteDialogData } from '../../admin/note-dialog/note-dialog';
import { EditTimeEntryDialog, EditTimeEntryDialogData, EditTimeEntryResult } from '../../admin/edit-time-entry-dialog/edit-time-entry-dialog';

interface DayShift {
  assignment: ShiftAssignmentDto;
  employeeName: string;
  isMine: boolean;
  earliestClockInLabel: string;
  canClockIn: boolean;
  entry: TimeEntryDto | null;
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
  timeFormat: 'TwelveHour' as TimeFormat,
  timeZone: 'America/Los_Angeles',
  clockInWindowMinutes: 15,
  lateClockInGraceMinutes: 5,
  breakLimitMinutes: 15,
  lunchLimitMinutes: 30,
};

// Net time worked once clocked out: wall time minus every closed segment,
// in whole minutes (formatted as "H Hrs M Mins" — see hoursWorkedLabel).
function workedMinutes(entry: TimeEntryDto): number {
  const msBetween = (start: string, end: string) => new Date(end).getTime() - new Date(start).getTime();

  let ms = msBetween(entry.clockInAt, entry.clockOutAt!);
  for (const segment of entry.segments) {
    if (segment.endAt) {
      ms -= msBetween(segment.startAt, segment.endAt);
    }
  }
  return Math.round(ms / 60_000);
}

// Shown on the Employee/Admin/Lead home page right after login. In the
// default 'mine' scope it's just the caller's own upcoming shifts (used on
// the Employee home page). In 'location' scope (Admin/Lead home page) it
// shows every account's shifts for the location, each labeled with the
// employee's name since more than one person can be working the same day.
// Rendered as one aligned table per day. Clock In/Out get their own
// columns; any number of breaks/lunches share a single "Breaks" column
// (see ScheduledBreak/TimeEntrySegment — a shift can have any number of
// scheduled windows, and an employee can take any number of actual ones,
// at most one open at a time). Self-punch actions (Clock In/+Break/+Lunch/
// End/Clock Out) only ever render for the caller's own shift, since
// punching is self-service; Mark/Clear Absent and Edit Times are
// location-scope-only admin actions on any employee's row.
// Narrowed down to the Monday-Sunday of the current week, today onward
// (i.e. "what's left of this week"). Every time is displayed in the
// location's own configured timezone and 12/24-hour format (see
// LocationSettings.TimeZone/TimeFormat), not the viewing browser's — an
// admin checking in from a different timezone, or a screen physically in
// the store, should both read the same wall-clock time.
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

  // Attendance thresholds + display prefs, all sourced from LocationSettings
  // (see LocationSettingsController.GetMine) — every signed-in role reads
  // the same subset, so admin and employee views always agree.
  private timeFormat: TimeFormat = DEFAULT_SETTINGS.timeFormat;
  private timeZone = DEFAULT_SETTINGS.timeZone;
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
      // Location scope (Admin/Lead) needs every employee's punches to fill
      // the table's columns, not just the caller's own — mine scope only
      // ever needs the caller's own, which is also all getMine ever returns.
      entries: isLocationScope
        ? this.timeEntriesApi.getForLocation(this.locationCode!, today).pipe(catchError(() => of([])))
        : this.timeEntriesApi.getMine(today).pipe(catchError(() => of([]))),
    }).subscribe({
      next: ({ mine, assignments, settings, entries }) => {
        this.timeFormat = settings.timeFormat;
        this.timeZone = settings.timeZone;
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
                // Location scope shows every employee's punches (see the
                // getForLocation fetch above); only the *actions* stay
                // self-service (see the isMine checks on each canX below).
                const entry = entryByShiftId.get(assignment.id) ?? null;
                return {
                  assignment,
                  employeeName: `${assignment.accountFirstName} ${assignment.accountLastName}`,
                  isMine,
                  earliestClockInLabel: this.punchTime(earliestClockInAt.toISOString()),
                  canClockIn: isToday && isMine && !entry && now >= earliestClockInAt,
                  entry,
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

  scheduledTime(shift: ShiftAssignmentDto): string {
    return `${formatTimeOnly(shift.shiftStartTime, this.timeFormat)}–${formatTimeOnly(shift.shiftEndTime, this.timeFormat)}`;
  }

  // "-" is the universal empty state for a punch cell: it just hasn't
  // happened yet.
  punchTime(iso: string | null | undefined): string {
    return iso ? formatInstant(iso, this.timeZone, this.timeFormat) : '-';
  }

  segmentsFor(shift: DayShift): TimeEntrySegmentDto[] {
    return shift.entry ? [...shift.entry.segments].sort((a, b) => a.startAt.localeCompare(b.startAt)) : [];
  }

  hoursWorkedLabel(entry: TimeEntryDto): string {
    const totalMinutes = workedMinutes(entry);
    const hours = Math.floor(totalMinutes / 60);
    const minutes = totalMinutes % 60;
    return `${hours} Hrs ${minutes} Mins`;
  }

  hasOpenSegment(entry: TimeEntryDto): boolean {
    return entry.segments.some((s) => !s.endAt);
  }

  // Badges below only ever apply to the caller's own shift: entry is null
  // for teammates' shifts in location scope (see load()), so these are
  // naturally false there without an extra isMine check.
  isLate(shift: DayShift): boolean {
    return !!shift.entry && isLateClockIn(shift.entry, shift.assignment, this.lateClockInGraceMinutes);
  }

  isBreakOver(entry: TimeEntryDto): boolean {
    return isAnySegmentOverLimit(entry, 'Break', this.breakLimitMinutes, new Date());
  }

  isLunchOver(entry: TimeEntryDto): boolean {
    return isAnySegmentOverLimit(entry, 'Lunch', this.lunchLimitMinutes, new Date());
  }

  // Punching is self-service only, even though entry data itself is now
  // visible for every employee in location scope (see load()) — any
  // number of breaks/lunches, but at most one open at a time.
  canStartSegment(shift: DayShift): boolean {
    const e = shift.entry;
    return shift.isMine && !!e && !e.clockOutAt && !this.hasOpenSegment(e);
  }

  canEndSegment(shift: DayShift): boolean {
    return shift.isMine && !!shift.entry && this.hasOpenSegment(shift.entry);
  }

  canClockOut(shift: DayShift): boolean {
    const e = shift.entry;
    return shift.isMine && !!e && !e.clockOutAt && !this.hasOpenSegment(e);
  }

  clockIn(shift: DayShift): void {
    if (!shift.canClockIn) {
      return;
    }
    this.run(shift.assignment.id, this.timeEntriesApi.clockIn(shift.assignment.id));
  }

  startSegment(shift: DayShift, kind: BreakKind): void {
    if (shift.isMine && shift.entry) {
      this.run(shift.assignment.id, this.timeEntriesApi.startSegment(shift.entry.id, kind));
    }
  }

  endSegment(shift: DayShift): void {
    if (shift.isMine && shift.entry) {
      this.run(shift.assignment.id, this.timeEntriesApi.endSegment(shift.entry.id));
    }
  }

  clockOut(shift: DayShift): void {
    if (shift.isMine && shift.entry) {
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
          s.assignment.id === shiftAssignmentId ? { ...s, entry, canClockIn: false } : s,
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

  // Location-scope only — lets an admin set every punch on today's entry
  // directly, whether correcting a mistake or filling one in from scratch.
  // Same dialog/endpoint admin-schedule-page's weekly grid uses.
  editTimes(shift: DayShift): void {
    this.dialog
      .open<EditTimeEntryDialog, EditTimeEntryDialogData, EditTimeEntryResult>(EditTimeEntryDialog, {
        data: {
          employeeName: shift.employeeName,
          entry: shift.entry,
          scheduledBreaks: shift.assignment.scheduledBreaks,
        },
      })
      .afterClosed()
      .subscribe((result) => {
        if (!result) {
          return;
        }
        const toIso = (time: string | null) =>
          time ? combineDateAndTime(shift.assignment.date, time).toISOString() : null;
        this.timeEntriesApi
          .adminEditTimes(shift.assignment.id, {
            clockInAt: toIso(result.clockInAt)!,
            clockOutAt: toIso(result.clockOutAt),
            segments: result.segments.map((s) => ({
              kind: s.kind,
              startAt: toIso(s.start)!,
              endAt: toIso(s.end),
            })),
            note: result.note,
          })
          .subscribe({
            next: () => this.load(),
            error: (err) => this.error.set(err?.error ?? 'Failed to update punch times.'),
          });
      });
  }
}
