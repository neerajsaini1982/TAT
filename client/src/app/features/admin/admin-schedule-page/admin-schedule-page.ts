import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { CdkDrag, CdkDragDrop, CdkDropList, CdkDropListGroup } from '@angular/cdk/drag-drop';
import { forkJoin } from 'rxjs';

import { AvailabilityApi } from '../../../core/availability-api';
import { ShiftDto, ShiftsApi } from '../../../core/shifts-api';
import { ShiftAssignmentDto, ShiftAssignmentsApi } from '../../../core/shift-assignments-api';
import { addDays, formatDate, formatWeekRange, mondayOf } from '../../../core/week-utils';

interface DayCell {
  date: string;
  dayLabel: string;
  isAvailable: boolean;
  availabilityLabel: string;
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
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  protected readonly dayHeaders = DAY_HEADERS;
  protected readonly weekStart = signal(mondayOf(new Date()));
  protected readonly weekRangeLabel = () => formatWeekRange(this.weekStart());
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly shifts = signal<ShiftDto[]>([]);
  protected readonly rows = signal<EmployeeRow[]>([]);

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

  onDrop(event: CdkDragDrop<DayCell, DayCell, DragItem>, row: EmployeeRow, day: DayCell): void {
    if (event.previousContainer === event.container) {
      return;
    }

    const data = event.item.data;
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
