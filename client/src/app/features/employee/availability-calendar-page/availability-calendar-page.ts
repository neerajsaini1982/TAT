import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { forkJoin } from 'rxjs';

import { AvailabilityApi, AvailabilityDto } from '../../../core/availability-api';
import {
  DAY_LABELS_SHORT,
  addMonths,
  formatDate,
  formatMonthLabel,
  isBeyondAvailabilityWindow,
  isPastDate,
  maxAvailabilityDate,
  monthGridDays,
  mondayOf,
  parseDate,
  startOfMonth,
  toApiTime,
  toInputTime,
} from '../../../core/week-utils';
import { AvailabilityDayDialog, AvailabilityDayDialogResult } from '../availability-day-dialog/availability-day-dialog';

interface CalendarDay {
  date: string;
  dayOfMonth: number;
  isCurrentMonth: boolean;
  isPast: boolean;
  isBeyondWindow: boolean;
  isToday: boolean;
  isAvailable: boolean;
  allDay: boolean;
  startTime: string;
  endTime: string;
}

@Component({
  selector: 'app-availability-calendar-page',
  imports: [RouterLink, MatButtonModule, MatIconModule],
  templateUrl: './availability-calendar-page.html',
  styleUrl: './availability-calendar-page.scss',
})
export class AvailabilityCalendarPage implements OnInit {
  private readonly api = inject(AvailabilityApi);
  private readonly dialog = inject(MatDialog);
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  protected readonly dayLabels = DAY_LABELS_SHORT;
  protected readonly monthAnchor = signal(startOfMonth(new Date()));
  protected readonly monthLabel = () => formatMonthLabel(this.monthAnchor());
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly days = signal<CalendarDay[]>([]);

  // Employees only plan today through the next 15 days: earlier months
  // aren't navigable, and later ones only once they'd show an in-window day.
  protected readonly canGoPreviousMonth = computed(
    () => this.monthAnchor().getTime() > startOfMonth(new Date()).getTime(),
  );
  protected readonly canGoNextMonth = computed(
    () => addMonths(this.monthAnchor(), 1).getTime() <= maxAvailabilityDate().getTime(),
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    const grid = monthGridDays(this.monthAnchor());
    const today = formatDate(new Date());
    const weekStarts: Date[] = [];
    for (let i = 0; i < grid.length; i += 7) {
      weekStarts.push(grid[i]);
    }

    forkJoin(weekStarts.map((weekStart) => this.api.getMine(formatDate(weekStart)))).subscribe({
      next: (weeks) => {
        const byDate = new Map(weeks.flatMap((w) => w.days).map((d) => [d.date, d]));
        this.days.set(
          grid.map((date) => {
            const iso = formatDate(date);
            const dto = byDate.get(iso);
            const isPast = isPastDate(iso);
            const isBeyondWindow = isBeyondAvailabilityWindow(iso);
            // Don't surface stored data for locked days: past availability
            // shouldn't be shown, and days beyond the 15-day window aren't
            // plannable yet.
            const locked = isPast || isBeyondWindow;
            return {
              date: iso,
              dayOfMonth: date.getDate(),
              isCurrentMonth: date.getMonth() === this.monthAnchor().getMonth(),
              isPast,
              isBeyondWindow,
              isToday: iso === today,
              isAvailable: !locked && (dto?.isAvailable ?? false),
              allDay: !locked && !!dto?.isAvailable && !dto.startTime && !dto.endTime,
              startTime: toInputTime(locked ? null : (dto?.startTime ?? null)),
              endTime: toInputTime(locked ? null : (dto?.endTime ?? '17:00:00')),
            };
          }),
        );
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load availability.');
        this.loading.set(false);
      },
    });
  }

  previousMonth(): void {
    if (!this.canGoPreviousMonth()) {
      return;
    }
    this.monthAnchor.set(addMonths(this.monthAnchor(), -1));
    this.load();
  }

  nextMonth(): void {
    if (!this.canGoNextMonth()) {
      return;
    }
    this.monthAnchor.set(addMonths(this.monthAnchor(), 1));
    this.load();
  }

  today(): void {
    this.monthAnchor.set(startOfMonth(new Date()));
    this.load();
  }

  openDay(day: CalendarDay): void {
    if (day.isPast || day.isBeyondWindow) {
      return;
    }

    this.dialog
      .open<AvailabilityDayDialog, unknown, AvailabilityDayDialogResult>(AvailabilityDayDialog, {
        data: {
          dayLabel: parseDate(day.date).toLocaleDateString(undefined, { weekday: 'long' }),
          dateLabel: parseDate(day.date).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' }),
          isAvailable: day.isAvailable,
          allDay: day.allDay,
          startTime: day.startTime,
          endTime: day.endTime,
        },
      })
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          this.saveDay(day, result);
        }
      });
  }

  private saveDay(day: CalendarDay, result: AvailabilityDayDialogResult): void {
    this.error.set(null);
    const weekStart = mondayOf(parseDate(day.date));
    const weekDates = new Set<string>();
    for (let i = 0; i < 7; i++) {
      const d = new Date(weekStart);
      d.setDate(d.getDate() + i);
      weekDates.add(formatDate(d));
    }

    const currentDays = this.days();
    const payloadDays = Array.from(weekDates).map((date) => {
      if (date === day.date) {
        return {
          date,
          isAvailable: result.isAvailable,
          startTime: result.isAvailable && !result.allDay ? toApiTime(result.startTime) : null,
          endTime: result.isAvailable && !result.allDay ? toApiTime(result.endTime) : null,
        };
      }
      const existing = currentDays.find((d) => d.date === date);
      return {
        date,
        isAvailable: existing?.isAvailable ?? false,
        startTime: existing?.isAvailable && !existing.allDay ? toApiTime(existing.startTime) : null,
        endTime: existing?.isAvailable && !existing.allDay ? toApiTime(existing.endTime) : null,
      };
    });

    this.api.saveMine({ weekStartDate: formatDate(weekStart), days: payloadDays, submit: false }).subscribe({
      next: (dto) => this.applyWeekDto(dto),
      error: (err) => this.error.set(err?.error ?? 'Failed to save availability.'),
    });
  }

  private applyWeekDto(dto: AvailabilityDto): void {
    const byDate = new Map(dto.days.map((d) => [d.date, d]));
    this.days.update((days) =>
      days.map((day) => {
        const updated = byDate.get(day.date);
        if (!updated) {
          return day;
        }
        return {
          ...day,
          isAvailable: updated.isAvailable,
          allDay: updated.isAvailable && !updated.startTime && !updated.endTime,
          startTime: toInputTime(updated.startTime),
          endTime: toInputTime(updated.endTime ?? '17:00:00'),
        };
      }),
    );
  }

  dayTimeLabel(day: CalendarDay): string {
    if (day.allDay) {
      return 'All day';
    }
    return `${day.startTime}–${day.endTime}`;
  }
}
