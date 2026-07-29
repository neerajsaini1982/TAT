import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

import { ShiftAssignmentDto, ShiftAssignmentsApi } from '../../../core/shift-assignments-api';
import { ScheduleRealtime } from '../../../core/schedule-realtime';
import { Auth } from '../../../core/auth';
import { dayOfWeekLabel, hoursMinutesLabel, toMmDdYyyy } from '../../../core/week-utils';

interface DayGroup {
  date: string;
  dayLabel: string;
  dateLabel: string;
  shifts: ShiftAssignmentDto[];
  // Only the caller's own shifts count toward these totals — GetMine can
  // return everyone's shifts when Schedule Visibility grants it, but "N
  // hours scheduled" should always mean the caller's own hours, not the
  // whole roster's combined.
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
  private readonly realtime = inject(ScheduleRealtime);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(Auth);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;
  private readonly myAccountId = this.auth.accountId();

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  // Every day with at least one visible shift, today onward — just the
  // caller's own unless Schedule Visibility grants their role a roster view,
  // in which case every account's shifts at this location show up too.
  protected readonly days = signal<DayGroup[]>([]);
  // True once a day group turns up a shift belonging to someone else,
  // meaning the server granted the roster view — drives whether the
  // template shows whose shift each row is.
  protected readonly showsEveryone = signal(false);

  protected readonly totalHours = computed(() =>
    round2(this.days().reduce((sum, d) => sum + d.hours, 0)),
  );
  protected readonly hoursLabel = hoursMinutesLabel;

  ngOnInit(): void {
    this.load();
    this.realtime
      .connect(this.locationCode)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load());
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
    this.showsEveryone.set(assignments.some((a) => a.accountId !== this.myAccountId));

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
          hours: round2(
            shifts.filter((s) => s.accountId === this.myAccountId).reduce((sum, s) => sum + s.hours, 0),
          ),
        })),
    );
    this.loading.set(false);
  }

  shiftTime(shift: ShiftAssignmentDto): string {
    return `${shift.shiftStartTime.slice(0, 5)}–${shift.shiftEndTime.slice(0, 5)}`;
  }

  isMine(shift: ShiftAssignmentDto): boolean {
    return shift.accountId === this.myAccountId;
  }
}
