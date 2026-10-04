import { Component, DestroyRef, Input, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { catchError, forkJoin, of, switchMap } from 'rxjs';

import { ShiftAssignmentDto, ShiftAssignmentsApi } from '../../../core/shift-assignments-api';
import { TimeEntriesApi, TimeEntryDto } from '../../../core/time-entries-api';
import { LocationSettingsApi, TimeFormat, WorkweekDay } from '../../../core/location-settings-api';
import { AvailabilityApi, AvailabilityDto } from '../../../core/availability-api';
import { EmployeeHoursReportDto, ReportsApi } from '../../../core/reports-api';
import { formatDurationMinutes } from '../../../core/duration-format';
import { ScheduleRealtime } from '../../../core/schedule-realtime';
import { employeeColor } from '../../../core/employee-colors';
import { formatInstant, formatTimeOnly } from '../../../core/location-time';
import { isLateClockIn } from '../../../core/attendance-flags';
import {
  DAY_LABELS_SHORT,
  addDays,
  combineDateAndTime,
  dayOfWeekLabel,
  formatDate,
  formatWeekRange,
  hoursMinutesLabel,
  mondayOf,
  parseDate,
  toMmDdYyyy,
} from '../../../core/week-utils';

type ShiftStatus = 'upcoming' | 'not-in' | 'working' | 'break' | 'lunch' | 'done' | 'absent';

interface TodayShift {
  assignment: ShiftAssignmentDto;
  employeeName: string;
  timeLabel: string;
  clockLabel: string;
  status: ShiftStatus;
  statusLabel: string;
  isLate: boolean;
}

const STATUS_LABELS: Record<ShiftStatus, string> = {
  upcoming: 'Upcoming',
  'not-in': 'Not clocked in',
  working: 'Working',
  break: 'On break',
  lunch: 'On lunch',
  done: 'Clocked out',
  absent: 'Absent',
};

// One day's pair of bars in the Scheduled vs. Logged gadget.
interface WeekDayHours {
  date: string;
  label: string;
  scheduled: number;
  worked: number;
  isToday: boolean;
  isFuture: boolean;
}

// One week's point in the Labor Hours Trend gadget. x/y are percentages of
// the plot box (y measured up from the baseline).
interface TrendWeek {
  start: string;
  label: string;
  scheduled: number;
  worked: number;
  isCurrent: boolean;
  x: number;
  scheduledY: number;
  workedY: number;
}

const TREND_WEEKS = 8;

type AttendanceKind = 'on-time' | 'late' | 'left-early' | 'absent' | 'no-punch';

interface AttendanceSlice {
  kind: AttendanceKind;
  label: string;
  count: number;
  // Donut geometry, in percent of the ring (see admin-dashboard.html).
  dash: string;
  offset: number;
}

const ATTENDANCE_LABELS: Record<AttendanceKind, string> = {
  'on-time': 'On time',
  late: 'Late',
  'left-early': 'Left early',
  absent: 'Absent',
  'no-punch': 'No punch',
};

interface OvertimeRow {
  employeeId: number;
  name: string;
  worked: number;
  remaining: number;
  // Overtime + double time already earned this workweek (daily rules
  // included), per the server's own calculation.
  overtimeSoFar: number;
}

interface AttentionItem {
  icon: string;
  text: string;
  detail: string | null;
  linkLabel: string;
  link: string[];
}

const DEFAULT_SETTINGS = {
  timeFormat: 'TwelveHour' as TimeFormat,
  timeZone: 'America/Los_Angeles',
  lateClockInGraceMinutes: 5,
  nextPayDate: null as string | null,
  weeklyOvertimeAfterMinutes: null as number | null,
  overtimeDailyThresholdMinutes: null as number | null,
  workweekStartDay: 'Monday' as WorkweekDay,
};

const WEEKDAYS: WorkweekDay[] = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

// Show someone in Overtime Watch once their worked + still-scheduled time
// reaches this share of the weekly threshold.
const OVERTIME_WATCH_RATIO = 0.8;
const OVERTIME_WATCH_MAX_ROWS = 6;

const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? '' : 's'}`;

// What an Admin lands on right after login (see AdminHome) — a read-only
// at-a-glance view laid out as a grid of gadgets, one per metric, each with
// its own title bar, refresh button and "last refreshed" footer. The full week table with its
// punch/absent/edit actions that used to be the landing page now lives at
// admin/view-schedule (AdminViewSchedulePage). Admin-only: the location-wide
// assignments endpoint (ShiftAssignmentsController.GetForWeek) is
// AdminOrAbove, so a Lead keeps the week view as their home instead.
//
// Everything is derived client-side from endpoints the other admin pages
// already use: the week's assignments and punches, the payroll hours report
// (worked/overtime minutes per employee per day) and next week's
// availability. All charts are plain CSS/SVG, like PayrollHoursChart.
@Component({
  selector: 'app-admin-dashboard',
  imports: [RouterLink, MatButtonModule, MatIconModule],
  templateUrl: './admin-dashboard.html',
  styleUrls: ['./admin-dashboard.scss', './admin-dashboard-charts.scss'],
})
export class AdminDashboard implements OnInit {
  @Input({ required: true }) locationCode!: string;

  private readonly assignmentsApi = inject(ShiftAssignmentsApi);
  private readonly timeEntriesApi = inject(TimeEntriesApi);
  private readonly settingsApi = inject(LocationSettingsApi);
  private readonly reportsApi = inject(ReportsApi);
  private readonly availabilityApi = inject(AvailabilityApi);
  private readonly realtime = inject(ScheduleRealtime);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly shifts = signal<TodayShift[]>([]);
  protected readonly todayLabel = signal('');
  protected readonly lastRefreshed = signal('');
  protected readonly nextPayDate = signal<string | null>(null);
  protected readonly payDayIsToday = signal(false);

  protected readonly weekRangeLabel = signal('');
  protected readonly weekDays = signal<WeekDayHours[]>([]);
  protected readonly attendance = signal<AttendanceSlice[]>([]);
  protected readonly trendWeeks = signal<TrendWeek[]>([]);
  protected readonly trendMaxMinutes = signal(60);
  protected readonly payDayCountdown = signal('');
  // Weekly limit when the location has one — Overtime Watch then projects
  // each employee against it. With only a daily limit there is nothing to
  // project, so it lists the overtime already logged this week instead.
  protected readonly overtimeThreshold = signal<number | null>(null);
  protected readonly dailyOvertimeThreshold = signal<number | null>(null);
  protected readonly overtimeRows = signal<OvertimeRow[]>([]);
  protected readonly attentionItems = signal<AttentionItem[]>([]);

  protected readonly toMmDdYyyy = toMmDdYyyy;
  protected readonly duration = formatDurationMinutes;

  protected readonly employeeColor = employeeColor;
  protected readonly hoursLabel = hoursMinutesLabel;

  protected readonly scheduledCount = computed(() => this.shifts().length);
  protected readonly onTheClockCount = computed(
    () => this.shifts().filter((s) => s.status === 'working' || s.status === 'break' || s.status === 'lunch').length,
  );
  protected readonly notInCount = computed(() => this.shifts().filter((s) => s.status === 'not-in').length);
  protected readonly absentCount = computed(() => this.shifts().filter((s) => s.status === 'absent').length);
  protected readonly scheduledHours = computed(() =>
    this.shifts().reduce((sum, s) => sum + s.assignment.hours, 0),
  );

  protected readonly weekScheduledMinutes = computed(() => this.weekDays().reduce((sum, d) => sum + d.scheduled, 0));
  protected readonly weekWorkedMinutes = computed(() => this.weekDays().reduce((sum, d) => sum + d.worked, 0));
  // Shared scale so the seven days are comparable.
  private readonly weekMaxMinutes = computed(() =>
    Math.max(60, ...this.weekDays().flatMap((d) => [d.scheduled, d.worked])),
  );

  // The trend's two lines and the fill under "logged", in the SVG's 0-100
  // coordinate space (y flipped: SVG's origin is top-left).
  protected readonly trendScheduledPoints = computed(() =>
    this.trendWeeks().map((w) => `${w.x},${100 - w.scheduledY}`).join(' '),
  );
  protected readonly trendWorkedPoints = computed(() =>
    this.trendWeeks().map((w) => `${w.x},${100 - w.workedY}`).join(' '),
  );
  protected readonly trendWorkedArea = computed(() => {
    const weeks = this.trendWeeks();
    return weeks.length === 0
      ? ''
      : `${weeks[0].x},100 ${this.trendWorkedPoints()} ${weeks[weeks.length - 1].x},100`;
  });
  protected readonly trendAxisLabels = computed(() => {
    const maxHours = this.trendMaxMinutes() / 60;
    return [`${maxHours}h`, `${maxHours / 2}h`, '0'];
  });

  protected readonly attendanceTotal = computed(() => this.attendance().reduce((sum, s) => sum + s.count, 0));
  protected readonly onTimePercent = computed(() => {
    const total = this.attendanceTotal();
    const onTime = this.attendance().find((s) => s.kind === 'on-time')?.count ?? 0;
    return total === 0 ? 0 : Math.round((onTime / total) * 100);
  });

  // Room past the threshold so an over-threshold bar still fits the track.
  private readonly overtimeScale = computed(() =>
    Math.max((this.overtimeThreshold() ?? 0) * 1.2, ...this.overtimeRows().map((r) => r.worked + r.remaining), 60),
  );

  protected dayBarPct(minutes: number): number {
    return (minutes / this.weekMaxMinutes()) * 100;
  }

  protected overtimePct(minutes: number): number {
    return (minutes / this.overtimeScale()) * 100;
  }

  protected isOverThreshold(row: OvertimeRow): boolean {
    return row.worked > (this.overtimeThreshold() ?? Infinity);
  }

  protected overtimeNote(row: OvertimeRow): string {
    const threshold = this.overtimeThreshold()!;
    const projected = row.worked + row.remaining;
    if (row.worked > threshold) {
      return `${formatDurationMinutes(row.worked - threshold)} over`;
    }
    if (projected > threshold) {
      return `On track for ${formatDurationMinutes(projected - threshold)} over`;
    }
    return `${formatDurationMinutes(threshold - projected)} under if all shifts are worked`;
  }

  ngOnInit(): void {
    this.load();

    // Same live-refresh hook the week view uses, so a clock-in at the kiosk
    // flips a row to Working here without a reload.
    this.realtime
      .connect(this.locationCode)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load());
  }

  protected load(): void {
    const now = new Date();
    const today = formatDate(now);
    const monday = mondayOf(now);
    const weekStart = formatDate(monday);
    const weekEnd = formatDate(addDays(monday, 6));
    const nextMonday = addDays(monday, 7);
    const yesterday = formatDate(addDays(now, -1));
    this.todayLabel.set(`${dayOfWeekLabel(today)} · ${toMmDdYyyy(today)}`);
    this.weekRangeLabel.set(formatWeekRange(monday));

    // Punches are fetched a day at a time (the endpoint is per-date), for
    // the days of this week that have happened so far.
    const elapsedDates: string[] = [];
    for (let d = monday; formatDate(d) <= today; d = addDays(d, 1)) {
      elapsedDates.push(formatDate(d));
    }

    this.settingsApi
      .get(this.locationCode)
      .pipe(
        catchError(() => of(DEFAULT_SETTINGS)),
        switchMap((settings) => {
          // Overtime is counted over the location's own workweek, which
          // needn't be the Monday-Sunday week the rest of the page shows.
          const daysIntoWorkweek = (now.getDay() - WEEKDAYS.indexOf(settings.workweekStartDay) + 7) % 7;
          const workweekStart = formatDate(addDays(now, -daysIntoWorkweek));
          const workweekEnd = formatDate(addDays(now, 6 - daysIntoWorkweek));
          // One report covers every gadget; each filters it down by date.
          // The trend needs the seven weeks before this one as well.
          const reportStart = formatDate(addDays(monday, -7 * (TREND_WEEKS - 1)));
          const reportEnd = [weekEnd, workweekEnd].sort()[1];

          return forkJoin({
            assignments: this.assignmentsApi.getForWeek(weekStart, this.locationCode),
            entries: forkJoin(
              elapsedDates.map((date) =>
                this.timeEntriesApi.getForLocation(this.locationCode, date).pipe(catchError(() => of([]))),
              ),
            ),
            report: this.reportsApi
              .getHoursReport(this.locationCode, reportStart, reportEnd)
              .pipe(catchError(() => of([] as EmployeeHoursReportDto[]))),
            nextWeekAssignments: this.assignmentsApi
              .getForWeek(formatDate(nextMonday), this.locationCode)
              .pipe(catchError(() => of(null))),
            availability: this.availabilityApi
              .getForLocation(formatDate(nextMonday), this.locationCode, true)
              .pipe(catchError(() => of([] as AvailabilityDto[]))),
            settings: of(settings),
            workweek: of({ start: workweekStart, end: workweekEnd }),
          });
        }),
      )
      .subscribe({
        next: ({ assignments, entries, report, nextWeekAssignments, availability, settings, workweek }) => {
          const entryByShiftId = new Map(entries.flat().map((e) => [e.shiftAssignmentId, e]));
          const grace = settings.lateClockInGraceMinutes;

          this.shifts.set(
            assignments
              .filter((a) => a.date === today)
              .sort(
                (a, b) =>
                  a.shiftStartTime.localeCompare(b.shiftStartTime) ||
                  a.accountFirstName.localeCompare(b.accountFirstName),
              )
              .map((assignment) => {
                const entry = entryByShiftId.get(assignment.id) ?? null;
                const status = this.statusOf(assignment, entry, grace, now);
                const time = (iso: string) => formatInstant(iso, settings.timeZone, settings.timeFormat);
                return {
                  assignment,
                  employeeName: `${assignment.accountFirstName} ${assignment.accountLastName}`,
                  timeLabel: `${formatTimeOnly(assignment.shiftStartTime, settings.timeFormat)} – ${formatTimeOnly(assignment.shiftEndTime, settings.timeFormat)}`,
                  clockLabel: entry
                    ? `${time(entry.clockInAt)} – ${entry.clockOutAt ? time(entry.clockOutAt) : 'now'}`
                    : '-',
                  status,
                  statusLabel: STATUS_LABELS[status],
                  isLate: !!entry && isLateClockIn(entry, assignment, grace),
                };
              }),
          );

          this.weekDays.set(this.buildWeekDays(monday, today, assignments, report));
          this.attendance.set(this.buildAttendance(today, assignments, entryByShiftId, grace));
          this.setTrend(monday, report);
          this.overtimeThreshold.set(settings.weeklyOvertimeAfterMinutes);
          this.dailyOvertimeThreshold.set(settings.overtimeDailyThresholdMinutes);
          this.overtimeRows.set(
            this.buildOvertimeRows(report, workweek.start, workweek.end, today, settings.weeklyOvertimeAfterMinutes),
          );
          this.attentionItems.set(
            this.buildAttentionItems(today, yesterday, weekStart, nextMonday, assignments, nextWeekAssignments, availability, report),
          );

          this.nextPayDate.set(settings.nextPayDate);
          this.payDayIsToday.set(settings.nextPayDate === today);
          this.payDayCountdown.set(this.countdownTo(settings.nextPayDate, today));
          this.lastRefreshed.set(formatInstant(now.toISOString(), settings.timeZone, settings.timeFormat));
          this.error.set(null);
          this.loading.set(false);
        },
        error: () => {
          this.error.set("Couldn't load the dashboard.");
          this.loading.set(false);
        },
      });
  }

  // Scheduled comes from the assignments (so future days show too); logged
  // is the report's net worked time, which excludes unpaid lunches and is
  // only counted once a shift is clocked out.
  private buildWeekDays(
    monday: Date,
    today: string,
    assignments: ShiftAssignmentDto[],
    report: EmployeeHoursReportDto[],
  ): WeekDayHours[] {
    return DAY_LABELS_SHORT.map((label, i) => {
      const date = formatDate(addDays(monday, i));
      return {
        date,
        label,
        scheduled: Math.round(
          assignments.filter((a) => a.date === date && !a.isAbsent).reduce((sum, a) => sum + a.hours * 60, 0),
        ),
        worked: report.reduce(
          (sum, e) => sum + (e.days.find((d) => d.date === date)?.netWorkedMinutes ?? 0),
          0,
        ),
        isToday: date === today,
        isFuture: date > today,
      };
    });
  }

  // Scheduled and logged hours for this week and the seven before it. The
  // y-scale tops out at the next 20 hours above the tallest point so the
  // axis labels are round numbers.
  private setTrend(monday: Date, report: EmployeeHoursReportDto[]): void {
    const days = report.flatMap((e) => e.days);
    const weeks = Array.from({ length: TREND_WEEKS }, (_, i) => {
      const startDate = addDays(monday, -7 * (TREND_WEEKS - 1 - i));
      const start = formatDate(startDate);
      const end = formatDate(addDays(startDate, 6));
      const inWeek = days.filter((d) => d.date >= start && d.date <= end);
      return {
        start,
        label: formatWeekRange(startDate),
        scheduled: inWeek.reduce((sum, d) => sum + (d.isAbsent ? 0 : (d.scheduledMinutes ?? 0)), 0),
        worked: inWeek.reduce((sum, d) => sum + (d.netWorkedMinutes ?? 0), 0),
        isCurrent: i === TREND_WEEKS - 1,
      };
    });
    const max = Math.max(1200, Math.ceil(Math.max(...weeks.flatMap((w) => [w.scheduled, w.worked])) / 1200) * 1200);
    this.trendMaxMinutes.set(max);
    this.trendWeeks.set(
      weeks.map((w, i) => ({
        ...w,
        x: ((i + 0.5) / TREND_WEEKS) * 100,
        scheduledY: (w.scheduled / max) * 100,
        workedY: (w.worked / max) * 100,
      })),
    );
  }

  private countdownTo(isoDate: string | null, today: string): string {
    if (!isoDate) {
      return '';
    }
    const days = Math.round((parseDate(isoDate).getTime() - parseDate(today).getTime()) / 86_400_000);
    return days <= 0 ? '' : days === 1 ? 'Tomorrow' : `In ${days} days`;
  }

  // One outcome per shift, for shifts whose day has come. Today's shifts
  // nobody has punched yet are left out — they may simply not have started.
  private buildAttendance(
    today: string,
    assignments: ShiftAssignmentDto[],
    entryByShiftId: Map<number, TimeEntryDto>,
    graceMinutes: number,
  ): AttendanceSlice[] {
    const counts: Record<AttendanceKind, number> = { 'on-time': 0, late: 0, 'left-early': 0, absent: 0, 'no-punch': 0 };
    for (const a of assignments) {
      if (a.date > today) {
        continue;
      }
      const entry = entryByShiftId.get(a.id);
      if (a.isAbsent) {
        counts.absent++;
      } else if (!entry) {
        if (a.date < today) {
          counts['no-punch']++;
        }
      } else if (isLateClockIn(entry, a, graceMinutes)) {
        counts.late++;
      } else if (entry.leftEarly) {
        counts['left-early']++;
      } else {
        counts['on-time']++;
      }
    }

    const total = Object.values(counts).reduce((sum, n) => sum + n, 0);
    let cumulative = 0;
    return (Object.keys(counts) as AttendanceKind[])
      .filter((kind) => counts[kind] > 0)
      .map((kind) => {
        const pct = (counts[kind] / total) * 100;
        // The ring's circumference is 100 units and it starts at 3 o'clock;
        // 25 rotates the start to 12 o'clock.
        // A sliver of surface between slices, unless one slice is the ring.
        const drawn = pct === 100 ? pct : Math.max(pct - 0.8, 0.5);
        const slice = {
          kind,
          label: ATTENDANCE_LABELS[kind],
          count: counts[kind],
          dash: `${drawn} ${100 - drawn}`,
          offset: 25 - cumulative,
        };
        cumulative += pct;
        return slice;
      });
  }

  private buildOvertimeRows(
    report: EmployeeHoursReportDto[],
    workweekStart: string,
    workweekEnd: string,
    today: string,
    threshold: number | null,
  ): OvertimeRow[] {
    return report
      .filter((e) => !e.isOvertimeExempt)
      .map((e) => {
        const days = e.days.filter((d) => d.date >= workweekStart && d.date <= workweekEnd);
        return {
          employeeId: e.employeeId,
          name: e.fullName,
          worked: days.reduce((sum, d) => sum + (d.netWorkedMinutes ?? 0), 0),
          // Shifts from today on that haven't been worked (or are still
          // being worked) yet.
          remaining: days
            .filter((d) => d.date >= today && d.netWorkedMinutes === null && !d.isAbsent)
            .reduce((sum, d) => sum + (d.scheduledMinutes ?? 0), 0),
          overtimeSoFar: days.reduce((sum, d) => sum + d.overtimeMinutes + d.doubleTimeMinutes, 0),
        };
      })
      .filter(
        (r) => r.overtimeSoFar > 0 || (threshold !== null && r.worked + r.remaining >= threshold * OVERTIME_WATCH_RATIO),
      )
      .sort((a, b) =>
        threshold === null ? b.overtimeSoFar - a.overtimeSoFar : b.worked + b.remaining - (a.worked + a.remaining),
      )
      .slice(0, OVERTIME_WATCH_MAX_ROWS);
  }

  private buildAttentionItems(
    today: string,
    yesterday: string,
    weekStart: string,
    nextMonday: Date,
    assignments: ShiftAssignmentDto[],
    nextWeekAssignments: ShiftAssignmentDto[] | null,
    availability: AvailabilityDto[],
    report: EmployeeHoursReportDto[],
  ): AttentionItem[] {
    const items: AttentionItem[] = [];
    const admin = ['/', this.locationCode, 'admin'];
    const nextWeekLabel = formatWeekRange(nextMonday);

    const openPunches = report.flatMap((e) =>
      e.days
        .filter((d) => d.stillClockedIn && d.date < today && (d.date >= weekStart || d.date === yesterday))
        .map(() => e.fullName),
    );
    if (openPunches.length > 0) {
      items.push({
        icon: 'timer_off',
        text: `${plural(openPunches.length, 'shift')} from earlier never clocked out`,
        detail: [...new Set(openPunches)].join(', '),
        linkLabel: 'Fix punches',
        link: [...admin, 'view-schedule'],
      });
    }

    const draftsThisWeek = assignments.filter((a) => a.date >= today && a.date >= weekStart && !a.isPublished).length;
    if (draftsThisWeek > 0) {
      items.push({
        icon: 'unpublished',
        text: `${plural(draftsThisWeek, 'shift')} left this week ${draftsThisWeek === 1 ? "isn't" : "aren't"} published`,
        detail: "Employees can't see them yet.",
        linkLabel: 'Open schedule',
        link: [...admin, 'schedule'],
      });
    }

    if (nextWeekAssignments) {
      const drafts = nextWeekAssignments.filter((a) => !a.isPublished).length;
      if (nextWeekAssignments.length === 0) {
        items.push({
          icon: 'event_busy',
          text: 'Next week has no shifts scheduled',
          detail: nextWeekLabel,
          linkLabel: 'Build schedule',
          link: [...admin, 'schedule'],
        });
      } else if (drafts > 0) {
        items.push({
          icon: 'unpublished',
          text: "Next week's schedule isn't published",
          detail: `${nextWeekLabel} · ${plural(drafts, 'draft shift')}`,
          linkLabel: 'Open schedule',
          link: [...admin, 'schedule'],
        });
      }
    }

    const notSubmitted = availability.filter((a) => !a.isSubmitted);
    if (notSubmitted.length > 0) {
      items.push({
        icon: 'event_available',
        text: `${notSubmitted.length} of ${plural(availability.length, 'employee')} haven't submitted availability for next week`,
        detail: notSubmitted.map((a) => `${a.firstName} ${a.lastName}`).join(', '),
        linkLabel: 'View availability',
        link: [...admin, 'availability'],
      });
    }

    return items;
  }

  // "Not clocked in" only once the late-clock-in grace has run out — before
  // that the shift is simply still upcoming.
  private statusOf(
    assignment: ShiftAssignmentDto,
    entry: TimeEntryDto | null,
    graceMinutes: number,
    now: Date,
  ): ShiftStatus {
    if (assignment.isAbsent) {
      return 'absent';
    }
    if (!entry) {
      const latestOnTime = combineDateAndTime(assignment.date, assignment.shiftStartTime);
      latestOnTime.setMinutes(latestOnTime.getMinutes() + graceMinutes);
      return now > latestOnTime ? 'not-in' : 'upcoming';
    }
    if (entry.clockOutAt) {
      return 'done';
    }
    const open = entry.segments.find((s) => !s.endAt);
    if (open) {
      return open.kind === 'Lunch' ? 'lunch' : 'break';
    }
    return 'working';
  }
}
