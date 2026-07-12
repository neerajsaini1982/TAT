import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { CdkDrag, CdkDragDrop, CdkDropList, CdkDropListGroup } from '@angular/cdk/drag-drop';
import { forkJoin } from 'rxjs';

import { AvailabilityApi } from '../../../core/availability-api';
import { ShiftDto, ShiftsApi } from '../../../core/shifts-api';
import { ShiftAssignmentDto, ShiftAssignmentsApi } from '../../../core/shift-assignments-api';
import { LocationSettingsApi } from '../../../core/location-settings-api';
import { employeeColor } from '../../../core/employee-colors';
import { addDays, formatDate, formatWeekRange, mondayOf } from '../../../core/week-utils';

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

// What's being dragged: either a reusable shift template from the palette
// (creates a new assignment on drop) or an existing assignment chip being
// moved to a different employee/day.
type DragItem = { kind: 'template'; shift: ShiftDto } | { kind: 'assignment'; assignment: ShiftAssignmentDto };

const DAY_HEADERS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

@Component({
  selector: 'app-admin-schedule-page',
  imports: [RouterLink, MatButtonModule, MatIconModule, CdkDropListGroup, CdkDropList, CdkDrag],
  templateUrl: './admin-schedule-page.html',
  styleUrl: './admin-schedule-page.scss',
})
export class AdminSchedulePage implements OnInit {
  private readonly availabilityApi = inject(AvailabilityApi);
  private readonly shiftsApi = inject(ShiftsApi);
  private readonly assignmentsApi = inject(ShiftAssignmentsApi);
  private readonly settingsApi = inject(LocationSettingsApi);
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

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
  protected readonly employeeColor = employeeColor;

  protected readonly dailyTotals = computed(() =>
    DAY_HEADERS.map((_, i) =>
      this.rows().reduce((sum, row) => sum + row.days[i].assignments.reduce((s, a) => s + a.hours, 0), 0),
    ),
  );

  // The whole week is a draft/preview, invisible to employees, until the
  // admin posts it. Any create/move against a published week reverts to
  // draft until it's re-posted.
  private readonly allAssignments = computed(() => this.rows().flatMap((r) => r.days.flatMap((d) => d.assignments)));
  protected readonly hasAssignments = computed(() => this.allAssignments().length > 0);
  protected readonly isFullyPublished = computed(
    () => this.hasAssignments() && this.allAssignments().every((a) => a.isPublished),
  );
  protected readonly publishing = signal(false);

  ngOnInit(): void {
    this.settingsApi.get(this.locationCode).subscribe({
      next: (settings) => this.developmentMode.set(settings.developmentMode),
      error: () => this.developmentMode.set(false),
    });
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    const weekIso = formatDate(this.weekStart());

    forkJoin({
      roster: this.availabilityApi.getForLocation(weekIso, this.locationCode),
      assignments: this.assignmentsApi.getForWeek(weekIso, this.locationCode),
      shifts: this.shiftsApi.getAll(this.locationCode),
    }).subscribe({
      next: ({ roster, assignments, shifts }) => {
        this.shifts.set(shifts.filter((s) => s.isActive));

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

  dropCellId(accountId: number, date: string): string {
    return `cell-${accountId}-${date}`;
  }

  // Blocks the drag from even entering a cell for a day the employee said
  // they're not available (or hasn't submitted availability for at all),
  // so the drop target visibly refuses it instead of silently failing.
  // Development Mode waives this entirely.
  canDropHere = (_drag: CdkDrag<DragItem>, drop: CdkDropList<DayCell>): boolean =>
    drop.data.isAvailable || this.developmentMode();

  onDrop(event: CdkDragDrop<DayCell, DayCell, DragItem>, row: EmployeeRow, day: DayCell): void {
    if (event.previousContainer === event.container) {
      return;
    }

    if (!day.isAvailable && !this.developmentMode()) {
      this.error.set(`${row.name} is not available on ${day.dayLabel}.`);
      return;
    }

    const data = event.item.data;
    const [shiftStart, shiftEnd] =
      data.kind === 'template' ? [data.shift.startTime, data.shift.endTime] : [data.assignment.shiftStartTime, data.assignment.shiftEndTime];

    const mismatch = this.partialAvailabilityWarning(day, shiftStart, shiftEnd);
    if (mismatch && !confirm(`${mismatch} Assign anyway?`)) {
      return;
    }

    this.error.set(null);

    if (data.kind === 'template') {
      this.assignmentsApi
        .create({ shiftId: data.shift.id, accountId: row.accountId, date: day.date })
        .subscribe({
          next: () => this.load(),
          error: (err) => this.error.set(err?.error ?? 'Failed to assign shift.'),
        });
    } else {
      this.assignmentsApi
        .move(data.assignment.id, { accountId: row.accountId, date: day.date })
        .subscribe({
          next: () => this.load(),
          error: (err) => this.error.set(err?.error ?? 'Failed to move shift.'),
        });
    }
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
    if (!confirm("Publish this week's schedule? It will become visible to employees on their My Schedule page.")) {
      return;
    }
    this.publishing.set(true);
    this.error.set(null);
    this.assignmentsApi.publish(formatDate(this.weekStart()), this.locationCode).subscribe({
      next: () => {
        this.publishing.set(false);
        this.load();
      },
      error: (err) => {
        this.publishing.set(false);
        this.error.set(err?.error ?? 'Failed to publish schedule.');
      },
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
}
