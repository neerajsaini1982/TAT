import { Component, DestroyRef, ElementRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatDialog } from '@angular/material/dialog';
import { catchError, forkJoin, fromEvent, of } from 'rxjs';

import { AvailabilityApi } from '../../../core/availability-api';
import { ShiftDto, ShiftsApi } from '../../../core/shifts-api';
import { ShiftAssignmentDto, ShiftAssignmentsApi } from '../../../core/shift-assignments-api';
import { LocationSettingsApi } from '../../../core/location-settings-api';
import { TimeEntriesApi, TimeEntryDto } from '../../../core/time-entries-api';
import { ScheduleRealtime } from '../../../core/schedule-realtime';
import { employeeColor } from '../../../core/employee-colors';
import { isAnySegmentOverLimit, isLateClockIn } from '../../../core/attendance-flags';
import { addDays, combineDateAndTime, formatDate, formatWeekRange, hoursMinutesLabel, mondayOf } from '../../../core/week-utils';
import { NoteDialog, NoteDialogData } from '../note-dialog/note-dialog';
import { PublishScheduleDialog, PublishScheduleDialogData } from '../publish-schedule-dialog/publish-schedule-dialog';
import { EditTimeEntryDialog, EditTimeEntryDialogData, EditTimeEntryResult } from '../edit-time-entry-dialog/edit-time-entry-dialog';
import { ScheduleDayView } from '../schedule-day-view/schedule-day-view';
import { ScheduleWeekTimeline, WeekTimelineDay } from '../schedule-week-timeline/schedule-week-timeline';

interface DayCell {
  date: string;
  dayLabel: string;
  isAvailable: boolean;
  availabilityLabel: string;
  // Null while isAvailable is false, or when the employee marked the
  // whole day open ("All day") rather than a specific window.
  availableStartTime: string | null;
  availableEndTime: string | null;
  assignments: ShiftAssignmentDto[];
}

interface EmployeeRow {
  accountId: number;
  name: string;
  days: DayCell[];
  totalHours: number;
}

const DAY_HEADERS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

// This is a variant of AdminSchedulePage (see admin-schedule-page.ts) for
// admins who find dragging shift chips onto cells fiddly when there are a
// lot of shift templates to choose from (issue #60). Every cell gets a
// type-to-filter text box instead of being a CDK drop target — typing e.g.
// "10:45" narrows to shifts starting/ending at 10:45, and Tab commits the
// top match and jumps to the next cell (wrapping to the next employee's
// Monday after Sunday), so a whole week can be filled in without leaving
// the keyboard. Assignment chips are no longer draggable — moving a shift
// means removing it and re-adding it on the new cell. Everything else
// (view modes, publish, filters, attendance actions) is identical to the
// drag-and-drop page, so keep the two in sync when one of them changes.
@Component({
  selector: 'app-admin-schedule-assign-page',
  imports: [
    RouterLink,
    FormsModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    ScheduleDayView,
    ScheduleWeekTimeline,
  ],
  templateUrl: './admin-schedule-assign-page.html',
  styleUrl: './admin-schedule-assign-page.scss',
})
export class AdminScheduleAssignPage implements OnInit {
  private readonly availabilityApi = inject(AvailabilityApi);
  private readonly shiftsApi = inject(ShiftsApi);
  private readonly assignmentsApi = inject(ShiftAssignmentsApi);
  private readonly settingsApi = inject(LocationSettingsApi);
  private readonly timeEntriesApi = inject(TimeEntriesApi);
  private readonly realtime = inject(ScheduleRealtime);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  // :host has `transform: translateX(-50%)` (see the stylesheet — it's how
  // this page breaks out of the app shell's centered content column). Any
  // transform on an ancestor makes it the containing block for
  // position:fixed descendants instead of the viewport, so activeCellRect
  // below has to be computed relative to :host's own rect, not the raw
  // viewport-relative getBoundingClientRect() the input reports.
  private readonly hostRef = inject(ElementRef<HTMLElement>);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  // Edit Times is only offered for today's chips: a TimeEntry can only ever
  // exist for today's date (see entryFor below), so entries for other days
  // in the visible week aren't even fetched.
  protected readonly todayIso = formatDate(new Date());

  protected readonly dayHeaders = DAY_HEADERS;
  protected readonly weekStart = signal(mondayOf(new Date()));
  protected readonly weekRangeLabel = () => formatWeekRange(this.weekStart());
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly shifts = signal<ShiftDto[]>([]);
  protected readonly rows = signal<EmployeeRow[]>([]);
  // When on, admins can assign shifts regardless of submitted availability —
  // see LocationSettings.DevelopmentMode and the server-side enforcement in
  // ShiftAssignmentsController.
  protected readonly developmentMode = signal(false);
  // Attendance thresholds used to badge today's chips as Late/Long
  // Break/Long Lunch — see LocationSettings and core/attendance-flags.ts.
  protected readonly lateClockInGraceMinutes = signal(5);
  protected readonly breakLimitMinutes = signal(15);
  protected readonly lunchLimitMinutes = signal(30);
  // Today's punches only — a TimeEntry can only ever exist for today's date
  // (see TimeEntriesController.ClockIn), so there's nothing to fetch for
  // other days in the visible week.
  private readonly entriesByAssignmentId = signal<Map<number, TimeEntryDto>>(new Map());
  protected readonly employeeColor = employeeColor;

  // Shifts sorted by start then end time, the order both the suggestion
  // list and "top match wins on Tab" logic rely on below.
  protected readonly sortedShifts = computed(() =>
    [...this.shifts()].sort((a, b) => a.startTime.localeCompare(b.startTime) || a.endTime.localeCompare(b.endTime)),
  );

  // Per-cell autocomplete text, keyed by cellKey (accountId|date) rather
  // than kept on DayCell itself — DayCell objects are rebuilt from scratch
  // on every load() (e.g. after a realtime update from another admin), and
  // an in-progress keystroke shouldn't be wiped out by that.
  private readonly cellQueries = signal<Map<string, string>>(new Map());
  // Which cell's suggestion list is open (the one currently focused).
  protected readonly activeCellKey = signal<string | null>(null);
  // Viewport position of the focused cell's input, captured on focus and
  // recomputed on scroll/resize (see recomputeActiveCellRect). The
  // suggestion list is rendered position:fixed at this rect instead of
  // position:absolute under the input, so it isn't clipped by
  // .table-scroll's overflow — which, now that the table's height is
  // unbounded (see .table-scroll in the stylesheet), is often just tall
  // enough to hold the visible rows and no taller, clipping a dropdown that
  // opens below the last one (see issue #66: this is what made "the shifts
  // aren't showing" reproduce specifically after filtering down to one
  // employee, since a filtered table is short by construction).
  protected readonly activeCellRect = signal<{ top: number; left: number; width: number } | null>(null);
  // The focused input itself, so scroll/resize can recompute activeCellRect
  // against its current position instead of just closing the dropdown —
  // the page now scrolls as a whole (see .table-scroll), so scrolling to
  // reach a cell before typing into it is a completely normal flow, not
  // something that should make the just-opened dropdown disappear.
  private activeInputEl: HTMLInputElement | null = null;

  private closeActiveCell(): void {
    this.activeCellKey.set(null);
    this.activeCellRect.set(null);
    this.activeInputEl = null;
  }

  private recomputeActiveCellRect(): void {
    if (!this.activeInputEl) {
      return;
    }
    const rect = this.activeInputEl.getBoundingClientRect();
    const hostRect = this.hostRef.nativeElement.getBoundingClientRect();
    this.activeCellRect.set({
      top: rect.bottom - hostRect.top,
      left: rect.left - hostRect.left,
      width: rect.width,
    });
  }

  cellKey(row: EmployeeRow, day: DayCell): string {
    return `${row.accountId}|${day.date}`;
  }

  cellInputId(accountId: number, date: string): string {
    return `cell-input-${accountId}-${date}`;
  }

  cellQuery(row: EmployeeRow, day: DayCell): string {
    return this.cellQueries().get(this.cellKey(row, day)) ?? '';
  }

  private setCellQuery(row: EmployeeRow, day: DayCell, value: string): void {
    const next = new Map(this.cellQueries());
    next.set(this.cellKey(row, day), value);
    this.cellQueries.set(next);
  }

  private clearCellQuery(row: EmployeeRow, day: DayCell): void {
    const next = new Map(this.cellQueries());
    next.delete(this.cellKey(row, day));
    this.cellQueries.set(next);
  }

  onCellQueryInput(event: Event, row: EmployeeRow, day: DayCell): void {
    this.setCellQuery(row, day, (event.target as HTMLInputElement).value);
  }

  onCellFocus(event: FocusEvent, row: EmployeeRow, day: DayCell): void {
    this.activeCellKey.set(this.cellKey(row, day));
    this.activeInputEl = event.target as HTMLInputElement;
    this.recomputeActiveCellRect();
  }

  onCellBlur(row: EmployeeRow, day: DayCell): void {
    if (this.activeCellKey() === this.cellKey(row, day)) {
      this.closeActiveCell();
    }
  }

  // Shifts matching the typed text against name (substring) or start time
  // (prefix only, e.g. "15" matches 15:00/15:30 but not a shift merely
  // ending at 15:00 — typing a time means "find what starts here", and a
  // substring match against end times too was pulling in unrelated shifts).
  // An empty query matches everything, same as an unfiltered dropdown would.
  filteredShifts(query: string): ShiftDto[] {
    const q = query.trim().toLowerCase();
    if (!q) {
      return this.sortedShifts();
    }
    return this.sortedShifts().filter(
      (s) => s.name.toLowerCase().includes(q) || s.startTime.slice(0, 5).startsWith(q),
    );
  }

  // Mouse pick from the suggestion list. mousedown (not click) + preventDefault
  // stops the input from blurring before this handler runs, so the cell stays
  // focused and its suggestion list doesn't flicker closed first.
  onSuggestionPick(event: MouseEvent, row: EmployeeRow, day: DayCell, shift: ShiftDto): void {
    event.preventDefault();
    this.assignShift(row, day, shift.id);
    this.clearCellQuery(row, day);
  }

  // Enter commits the top filtered match without moving focus (same cell,
  // ready for another shift on the same day). Tab commits the top match too,
  // then jumps to the next cell — day to the right, wrapping to the next
  // employee's Monday after Sunday — skipping preventDefault (and thus
  // falling through to the browser's own Tab handling) once there's no next
  // cell to jump to, e.g. the very last cell in the grid.
  onCellKeydown(event: KeyboardEvent, row: EmployeeRow, day: DayCell, rowIndex: number, dayIndex: number): void {
    if (event.key !== 'Tab' && event.key !== 'Enter') {
      return;
    }
    if (event.key === 'Tab' && event.shiftKey) {
      return;
    }

    const query = this.cellQuery(row, day).trim();
    if (query) {
      const [topMatch] = this.filteredShifts(query);
      if (topMatch) {
        this.assignShift(row, day, topMatch.id);
      }
      this.clearCellQuery(row, day);
    }

    if (event.key === 'Enter') {
      event.preventDefault();
      return;
    }

    // Tab: figure out the next cell in reading order.
    let nextAccountId: number | undefined;
    let nextDate: string | undefined;
    if (dayIndex < 6) {
      nextAccountId = row.accountId;
      nextDate = row.days[dayIndex + 1].date;
    } else {
      const nextRow = this.visibleRows()[rowIndex + 1];
      if (nextRow) {
        nextAccountId = nextRow.accountId;
        nextDate = nextRow.days[0].date;
      }
    }

    if (nextAccountId !== undefined && nextDate !== undefined) {
      event.preventDefault();
      document.getElementById(this.cellInputId(nextAccountId, nextDate))?.focus();
    }
  }

  // Header labels paired with each column's actual calendar date, e.g. "Mon" + "Jul 20".
  protected readonly dayColumns = computed(() => {
    const start = this.weekStart();
    return DAY_HEADERS.map((label, i) => ({
      label,
      dateLabel: addDays(start, i).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }),
    }));
  });

  // Week grid vs. a single day's Google-Calendar-style timeline (see
  // ScheduleDayView) vs. that same timeline style spread across all 7 days
  // (see ScheduleWeekTimeline) — the day timeline is for spotting coverage
  // gaps (is someone opening/closing, are there enough shifts) at a
  // glance, rather than reading times out of the grid's chips one by one;
  // the week timeline is a read-only, one-page-printable version of that.
  protected readonly viewMode = signal<'week' | 'day' | 'print'>('week');

  // Every employee's assignments for each day, flattened into one list per
  // day (ScheduleWeekTimeline lays employees out by time, not by row, so
  // it doesn't need them grouped by employee the way the week table does).
  protected readonly weekTimelineDays = computed<WeekTimelineDay[]>(() =>
    this.dayColumns().map((col, i) => ({
      label: col.label,
      dateLabel: col.dateLabel,
      assignments: this.visibleRows().flatMap((row) => row.days[i]?.assignments ?? []),
    })),
  );
  // Index into DAY_HEADERS/dayColumns (0 = Monday). Defaults to today if
  // today falls in the visible week, otherwise Monday; deliberately not
  // recomputed on previous/next-week navigation so flipping weeks keeps
  // you on the same day-of-week you were looking at.
  protected readonly selectedDayIndex = signal(this.defaultDayIndex());

  protected readonly selectedDayAssignments = computed(() => {
    const dayIndex = this.selectedDayIndex();
    return this.visibleRows().flatMap((row) => row.days[dayIndex]?.assignments ?? []);
  });

  // Full weekday + date for the Day view's print-only header (e.g.
  // "Saturday, July 25, 2026") — the on-screen day-tabs already show which
  // day is selected, but those are .no-print, so the printed page needs
  // its own unambiguous label for which single day this printout covers.
  protected readonly selectedDayDateLabel = computed(() =>
    addDays(this.weekStart(), this.selectedDayIndex()).toLocaleDateString(undefined, {
      weekday: 'long',
      month: 'long',
      day: 'numeric',
      year: 'numeric',
    }),
  );

  private defaultDayIndex(): number {
    const start = mondayOf(new Date());
    for (let i = 0; i < 7; i++) {
      if (formatDate(addDays(start, i)) === this.todayIso) {
        return i;
      }
    }
    return 0;
  }

  // Filters affect which rows are displayed, not the Daily Total / Total
  // Hrs figures below — those stay the full location's schedule regardless
  // of what's currently filtered into view.
  protected readonly employeeSearch = signal('');
  protected readonly showOnlyScheduled = signal(false);

  protected readonly visibleRows = computed(() => {
    const query = this.employeeSearch().trim().toLowerCase();
    const onlyScheduled = this.showOnlyScheduled();
    return this.rows().filter(
      (row) =>
        (!query || row.name.toLowerCase().includes(query)) && (!onlyScheduled || row.totalHours > 0),
    );
  });

  protected readonly dailyTotals = computed(() =>
    DAY_HEADERS.map((_, i) =>
      this.rows().reduce((sum, row) => sum + row.days[i].assignments.reduce((s, a) => s + a.hours, 0), 0),
    ),
  );
  protected readonly weekTotalHours = computed(() =>
    Math.round(this.dailyTotals().reduce((sum, hours) => sum + hours, 0) * 100) / 100,
  );
  protected readonly hoursLabel = hoursMinutesLabel;

  // The whole week is a draft/preview, invisible to employees, until the
  // admin posts it. Any create against a published week reverts to draft
  // until it's re-posted.
  private readonly allAssignments = computed(() => this.rows().flatMap((r) => r.days.flatMap((d) => d.assignments)));
  protected readonly hasAssignments = computed(() => this.allAssignments().length > 0);
  protected readonly isFullyPublished = computed(
    () => this.hasAssignments() && this.allAssignments().every((a) => a.isPublished),
  );
  protected readonly publishing = signal(false);

  // The Daily Total row lives in its own table below .table-scroll (see the
  // template comment there) so it stays visible while the body scrolls
  // vertically. It still needs to track the body's horizontal scroll so its
  // columns stay lined up underneath the body's.
  private readonly footerScroll = viewChild<ElementRef<HTMLDivElement>>('footerScroll');

  onBodyScroll(body: HTMLDivElement): void {
    const footer = this.footerScroll()?.nativeElement;
    if (footer) {
      footer.scrollLeft = body.scrollLeft;
    }
  }

  ngOnInit(): void {
    this.settingsApi.get(this.locationCode).subscribe({
      next: (settings) => {
        this.developmentMode.set(settings.developmentMode);
        this.lateClockInGraceMinutes.set(settings.lateClockInGraceMinutes);
        this.breakLimitMinutes.set(settings.breakLimitMinutes);
        this.lunchLimitMinutes.set(settings.lunchLimitMinutes);
      },
      error: () => this.developmentMode.set(false),
    });
    this.realtime
      .connect(this.locationCode)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load());
    this.load();

    // The suggestion list is position:fixed at a rect captured on focus (see
    // activeCellRect), so it has to track scroll/resize instead of silently
    // drifting away from the input it's anchored to. This recomputes rather
    // than closes: the whole page scrolls now (see .table-scroll), so
    // scrolling down to reach a cell and then typing into it is a normal
    // flow, not something that should make the dropdown disappear. Capture
    // phase catches scrolling on any ancestor, e.g. .table-scroll's
    // horizontal scroll, not just the window's own scroll.
    fromEvent(window, 'scroll', { capture: true })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.recomputeActiveCellRect());
    fromEvent(window, 'resize')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.recomputeActiveCellRect());
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    const weekIso = formatDate(this.weekStart());

    forkJoin({
      // onShiftScheduleOnly: true excludes anyone the admin has unchecked
      // "On Shift Schedule" for (see issue #61) from the schedule's rows.
      roster: this.availabilityApi.getForLocation(weekIso, this.locationCode, true),
      assignments: this.assignmentsApi.getForWeek(weekIso, this.locationCode),
      shifts: this.shiftsApi.getAll(this.locationCode),
      entries: this.timeEntriesApi.getForLocation(this.locationCode, this.todayIso).pipe(catchError(() => of([]))),
    }).subscribe({
      next: ({ roster, assignments, shifts, entries }) => {
        this.shifts.set(shifts.filter((s) => s.isActive));
        this.entriesByAssignmentId.set(new Map(entries.map((e) => [e.shiftAssignmentId, e])));

        const assignmentsByKey = new Map<string, ShiftAssignmentDto[]>();
        for (const a of assignments) {
          const key = `${a.accountId}|${a.date}`;
          assignmentsByKey.set(key, [...(assignmentsByKey.get(key) ?? []), a]);
        }

        this.rows.set(
          roster.map((person) => {
            const days = person.days.map((d, i): DayCell => ({
              date: d.date,
              dayLabel: DAY_HEADERS[i],
              isAvailable: d.isAvailable,
              availabilityLabel: this.availabilityLabel(d.isAvailable, d.startTime, d.endTime),
              availableStartTime: d.isAvailable ? d.startTime : null,
              availableEndTime: d.isAvailable ? d.endTime : null,
              assignments: assignmentsByKey.get(`${person.accountId}|${d.date}`) ?? [],
            }));
            const totalHours = days.reduce(
              (sum, d) => sum + d.assignments.reduce((s, a) => s + a.hours, 0),
              0,
            );
            return {
              accountId: person.accountId,
              name: `${person.firstName} ${person.lastName}`,
              days,
              totalHours: Math.round(totalHours * 100) / 100,
            };
          }),
        );
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load schedule.');
        this.loading.set(false);
      },
    });
  }

  printSchedule(): void {
    window.print();
  }

  previousWeek(): void {
    this.weekStart.set(addDays(this.weekStart(), -7));
    this.load();
  }

  nextWeek(): void {
    this.weekStart.set(addDays(this.weekStart(), 7));
    this.load();
  }

  private availabilityLabel(isAvailable: boolean, startTime: string | null, endTime: string | null): string {
    if (!isAvailable) {
      return 'Not available';
    }
    if (!startTime || !endTime) {
      return 'All day';
    }
    return `${startTime.slice(0, 5)}–${endTime.slice(0, 5)}`;
  }

  private assignShift(row: EmployeeRow, day: DayCell, shiftId: number): void {
    if (!day.isAvailable && !this.developmentMode()) {
      this.error.set(`${row.name} is not available on ${day.dayLabel}.`);
      return;
    }

    const shift = this.shifts().find((s) => s.id === shiftId);
    if (!shift) {
      return;
    }

    const mismatch = this.partialAvailabilityWarning(day, shift.startTime, shift.endTime);
    if (mismatch && !confirm(`${mismatch} Assign anyway?`)) {
      return;
    }

    this.error.set(null);
    this.assignmentsApi.create({ shiftId, accountId: row.accountId, date: day.date }).subscribe({
      next: () => this.load(),
      error: (err) => this.error.set(err?.error ?? 'Failed to assign shift.'),
    });
  }

  // Employee said they're available, but not necessarily for the shift's
  // full span (e.g. available 11-4, shift runs 11-7) — warn instead of
  // silently under-covering the shift. "All day" availability (no
  // specific window) always covers it.
  private partialAvailabilityWarning(day: DayCell, shiftStart: string, shiftEnd: string): string | null {
    if (!day.availableStartTime || !day.availableEndTime) {
      return null;
    }
    if (shiftStart < day.availableStartTime || shiftEnd > day.availableEndTime) {
      return `${day.dayLabel}: available ${day.availableStartTime.slice(0, 5)}–${day.availableEndTime.slice(0, 5)}, but this shift runs ${shiftStart.slice(0, 5)}–${shiftEnd.slice(0, 5)}.`;
    }
    return null;
  }

  publish(): void {
    this.dialog
      .open<PublishScheduleDialog, PublishScheduleDialogData, boolean>(PublishScheduleDialog, {
        data: { weekRangeLabel: this.weekRangeLabel() },
      })
      .afterClosed()
      .subscribe((sendEmail) => {
        if (sendEmail === undefined) {
          return;
        }
        this.publishing.set(true);
        this.error.set(null);
        this.assignmentsApi.publish(formatDate(this.weekStart()), this.locationCode, sendEmail).subscribe({
          next: () => {
            this.publishing.set(false);
            this.load();
          },
          error: (err) => {
            this.publishing.set(false);
            this.error.set(err?.error ?? 'Failed to publish schedule.');
          },
        });
      });
  }

  removeAssignment(assignment: ShiftAssignmentDto): void {
    this.assignmentsApi.delete(assignment.id).subscribe({
      next: () => this.load(),
      error: (err) => this.error.set(err?.error ?? 'Failed to remove shift.'),
    });
  }

  shiftTime(shift: ShiftDto): string {
    return `${shift.startTime.slice(0, 5)}–${shift.endTime.slice(0, 5)}`;
  }

  // A TimeEntry can only ever exist for today's date (see
  // TimeEntriesController.ClockIn), so this is null for every other day.
  entryFor(assignment: ShiftAssignmentDto): TimeEntryDto | null {
    return this.entriesByAssignmentId().get(assignment.id) ?? null;
  }

  isLate(assignment: ShiftAssignmentDto): boolean {
    const entry = this.entryFor(assignment);
    return !!entry && isLateClockIn(entry, assignment, this.lateClockInGraceMinutes());
  }

  // Checks every Break/Lunch segment on the entry, however many the
  // employee has taken — not just a fixed first/second slot.
  isBreakOver(assignment: ShiftAssignmentDto): boolean {
    const entry = this.entryFor(assignment);
    return !!entry && isAnySegmentOverLimit(entry, 'Break', this.breakLimitMinutes(), new Date());
  }

  isLunchOver(assignment: ShiftAssignmentDto): boolean {
    const entry = this.entryFor(assignment);
    return !!entry && isAnySegmentOverLimit(entry, 'Lunch', this.lunchLimitMinutes(), new Date());
  }

  // A currently-clocked-in employee — clocked-out ones don't need the
  // override, and someone who never clocked in gets Mark Absent instead.
  canClockOut(assignment: ShiftAssignmentDto): boolean {
    const entry = this.entryFor(assignment);
    return !!entry && entry.clockOutAt === null;
  }

  // Chip status at a glance: currently on the clock, already clocked out,
  // or not punched in yet (the chip's default look covers that last case).
  isClockedIn(assignment: ShiftAssignmentDto): boolean {
    return this.canClockOut(assignment);
  }

  isClockedOut(assignment: ShiftAssignmentDto): boolean {
    return this.entryFor(assignment)?.clockOutAt != null;
  }

  markAbsent(assignment: ShiftAssignmentDto): void {
    this.dialog
      .open<NoteDialog, NoteDialogData, string>(NoteDialog, {
        data: {
          title: `Mark ${assignment.accountFirstName} ${assignment.accountLastName} absent`,
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
        this.assignmentsApi.markAbsent(assignment.id, { isAbsent: true, note }).subscribe({
          next: () => this.load(),
          error: (err) => this.error.set(err?.error ?? 'Failed to mark absent.'),
        });
      });
  }

  clearAbsent(assignment: ShiftAssignmentDto): void {
    this.assignmentsApi.markAbsent(assignment.id, { isAbsent: false, note: null }).subscribe({
      next: () => this.load(),
      error: (err) => this.error.set(err?.error ?? 'Failed to clear absence.'),
    });
  }

  clockOutWithNote(assignment: ShiftAssignmentDto): void {
    const entry = this.entryFor(assignment);
    if (!entry) {
      return;
    }
    this.dialog
      .open<NoteDialog, NoteDialogData, string>(NoteDialog, {
        data: {
          title: `Clock out ${assignment.accountFirstName} ${assignment.accountLastName}`,
          label: 'Reason (e.g. left early)',
          noteRequired: true,
          confirmLabel: 'Clock Out',
        },
      })
      .afterClosed()
      .subscribe((note) => {
        if (!note) {
          return;
        }
        this.timeEntriesApi.adminClockOut(entry.id, note).subscribe({
          next: () => this.load(),
          error: (err) => this.error.set(err?.error ?? 'Failed to clock out.'),
        });
      });
  }

  isLeftEarly(assignment: ShiftAssignmentDto): boolean {
    return !!this.entryFor(assignment)?.leftEarly;
  }

  markLeftEarly(assignment: ShiftAssignmentDto): void {
    const entry = this.entryFor(assignment);
    if (!entry) {
      return;
    }
    this.dialog
      .open<NoteDialog, NoteDialogData, string>(NoteDialog, {
        data: {
          title: `Mark ${assignment.accountFirstName} ${assignment.accountLastName} left early`,
          label: 'Reason',
          noteRequired: true,
          confirmLabel: 'Mark Left Early',
        },
      })
      .afterClosed()
      .subscribe((note) => {
        if (!note) {
          return;
        }
        this.timeEntriesApi.markLeftEarly(entry.id, { leftEarly: true, note }).subscribe({
          next: () => this.load(),
          error: (err) => this.error.set(err?.error ?? 'Failed to mark left early.'),
        });
      });
  }

  clearLeftEarly(assignment: ShiftAssignmentDto): void {
    const entry = this.entryFor(assignment);
    if (!entry) {
      return;
    }
    this.timeEntriesApi.markLeftEarly(entry.id, { leftEarly: false, note: null }).subscribe({
      next: () => this.load(),
      error: (err) => this.error.set(err?.error ?? 'Failed to clear left early.'),
    });
  }

  // Lets an admin set every punch on today's entry directly — available
  // whether or not the employee has clocked in yet (entryFor is null in
  // that case, and the dialog starts blank apart from a default Clock In
  // of "now").
  editTimes(assignment: ShiftAssignmentDto): void {
    this.openEditTimesDialog(assignment, this.entryFor(assignment));
  }

  // Same dialog as editTimes, but for the Day view's "Time Punches" menu
  // item, which can point at any day in the week — not just today, so it
  // can't reuse entriesByAssignmentId (that Map is only ever populated for
  // todayIso, see load()). There's no server-side restriction to today
  // for AdminEditTimes, so this just fetches that specific day's entries
  // fresh instead of maintaining a second cache for the rest of the week.
  editTimesForDay(assignment: ShiftAssignmentDto): void {
    this.timeEntriesApi.getForLocation(this.locationCode, assignment.date).subscribe({
      next: (entries) => {
        const entry = entries.find((e) => e.shiftAssignmentId === assignment.id) ?? null;
        this.openEditTimesDialog(assignment, entry);
      },
      error: () => this.openEditTimesDialog(assignment, null),
    });
  }

  private openEditTimesDialog(assignment: ShiftAssignmentDto, entry: TimeEntryDto | null): void {
    this.dialog
      .open<EditTimeEntryDialog, EditTimeEntryDialogData, EditTimeEntryResult>(EditTimeEntryDialog, {
        data: {
          employeeName: `${assignment.accountFirstName} ${assignment.accountLastName}`,
          entry,
          scheduledBreaks: assignment.scheduledBreaks,
          shiftStartTime: assignment.shiftStartTime,
          shiftEndTime: assignment.shiftEndTime,
          date: assignment.date,
          breakLimitMinutes: this.breakLimitMinutes(),
          lunchLimitMinutes: this.lunchLimitMinutes(),
        },
      })
      .afterClosed()
      .subscribe((result) => {
        if (!result) {
          return;
        }
        const toIso = (time: string | null) => (time ? combineDateAndTime(assignment.date, time).toISOString() : null);
        this.timeEntriesApi
          .adminEditTimes(assignment.id, {
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
