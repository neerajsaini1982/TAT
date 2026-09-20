import { Component, DestroyRef, Input, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { forkJoin, Observable } from 'rxjs';

import { KioskApi } from '../../../core/kiosk-api';
import { ShiftAssignmentDto } from '../../../core/shift-assignments-api';
import { TimeEntryDto, TimeEntrySegmentDto } from '../../../core/time-entries-api';
import { BreakKind } from '../../../core/shifts-api';
import { LocationSettingsApi, TimeFormat } from '../../../core/location-settings-api';
import { ScheduleRealtime } from '../../../core/schedule-realtime';
import { employeeColor } from '../../../core/employee-colors';
import { formatInstant, formatTimeOnly } from '../../../core/location-time';
import { KioskPinDialog, KioskPinDialogData } from '../kiosk-pin-dialog/kiosk-pin-dialog';

interface KioskRow {
  assignment: ShiftAssignmentDto;
  entry: TimeEntryDto | null;
}

const DEFAULT_TIME_FORMAT: TimeFormat = 'TwelveHour';
const DEFAULT_TIME_ZONE = 'America/Los_Angeles';

// Today's roster on a shared kiosk device — every row's action (Clock In /
// Start Break / Start Lunch / End / Clock Out) opens a PIN dialog scoped to
// that row's own employee, rather than gating on "isMine" the way
// CurrentWeekSchedule does for a single logged-in employee (see that
// component's isMine/canClockIn — this one has no single "self" to compare
// against, since the kiosk's own session isn't any one employee).
@Component({
  selector: 'app-kiosk-schedule',
  imports: [MatButtonModule, MatCardModule, MatIconModule],
  templateUrl: './kiosk-schedule.html',
  styleUrl: './kiosk-schedule.scss',
})
export class KioskSchedule implements OnInit {
  @Input({ required: true }) locationCode!: string;

  private readonly kioskApi = inject(KioskApi);
  private readonly settingsApi = inject(LocationSettingsApi);
  private readonly realtime = inject(ScheduleRealtime);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);

  protected readonly loading = signal(true);
  protected readonly rows = signal<KioskRow[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly employeeColor = employeeColor;

  private timeFormat: TimeFormat = DEFAULT_TIME_FORMAT;
  private timeZone: string = DEFAULT_TIME_ZONE;

  ngOnInit(): void {
    this.load();

    this.realtime
      .connect(this.locationCode)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load());
  }

  private load(): void {
    forkJoin({
      assignments: this.kioskApi.getSchedule(),
      entries: this.kioskApi.getTimeEntries(),
      settings: this.settingsApi.getMine(),
    }).subscribe({
      next: ({ assignments, entries, settings }) => {
        this.timeFormat = settings.timeFormat;
        this.timeZone = settings.timeZone;

        const entryByShiftId = new Map(entries.map((e) => [e.shiftAssignmentId, e]));
        this.rows.set(
          assignments
            .filter((a) => !a.isAbsent)
            .map((assignment) => ({
              assignment,
              entry: entryByShiftId.get(assignment.id) ?? null,
            })),
        );
        this.loading.set(false);
        this.error.set(null);
      },
      error: () => {
        this.loading.set(false);
        this.error.set('Failed to load today\'s schedule.');
      },
    });
  }

  scheduledTime(row: KioskRow): string {
    return `${formatTimeOnly(row.assignment.shiftStartTime, this.timeFormat)}–${formatTimeOnly(row.assignment.shiftEndTime, this.timeFormat)}`;
  }

  punchTime(iso: string | null | undefined): string {
    return iso ? formatInstant(iso, this.timeZone, this.timeFormat) : '-';
  }

  segmentsFor(row: KioskRow): TimeEntrySegmentDto[] {
    return row.entry ? [...row.entry.segments].sort((a, b) => a.startAt.localeCompare(b.startAt)) : [];
  }

  hasOpenSegment(entry: TimeEntryDto): boolean {
    return entry.segments.some((s) => !s.endAt);
  }

  canClockIn(row: KioskRow): boolean {
    return !row.entry;
  }

  canStartSegment(row: KioskRow): boolean {
    return !!row.entry && !row.entry.clockOutAt && !this.hasOpenSegment(row.entry);
  }

  canEndSegment(row: KioskRow): boolean {
    return !!row.entry && this.hasOpenSegment(row.entry);
  }

  canClockOut(row: KioskRow): boolean {
    return !!row.entry && !row.entry.clockOutAt && !this.hasOpenSegment(row.entry);
  }

  clockIn(row: KioskRow): void {
    this.openPinDialog(row, 'Clock In', (accountId, pin) => this.kioskApi.clockIn(row.assignment.id, accountId, pin));
  }

  startSegment(row: KioskRow, kind: BreakKind): void {
    if (!row.entry) {
      return;
    }
    const entryId = row.entry.id;
    this.openPinDialog(row, kind === 'Break' ? 'Start Break' : 'Start Lunch', (accountId, pin) =>
      this.kioskApi.startSegment(entryId, accountId, pin, kind),
    );
  }

  endSegment(row: KioskRow): void {
    if (!row.entry) {
      return;
    }
    const entryId = row.entry.id;
    this.openPinDialog(row, 'End Break/Lunch', (accountId, pin) => this.kioskApi.endSegment(entryId, accountId, pin));
  }

  clockOut(row: KioskRow): void {
    if (!row.entry) {
      return;
    }
    const entryId = row.entry.id;
    this.openPinDialog(row, 'Clock Out', (accountId, pin) => this.kioskApi.clockOut(entryId, accountId, pin));
  }

  private openPinDialog(row: KioskRow, action: string, submit: (accountId: number, pin: string) => Observable<TimeEntryDto>): void {
    this.dialog
      .open<KioskPinDialog, KioskPinDialogData, TimeEntryDto>(KioskPinDialog, {
        data: {
          employeeName: `${row.assignment.accountFirstName} ${row.assignment.accountLastName}`,
          action,
          submit: (pin: string) => submit(row.assignment.accountId, pin),
        },
      })
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          this.load();
        }
      });
  }
}
