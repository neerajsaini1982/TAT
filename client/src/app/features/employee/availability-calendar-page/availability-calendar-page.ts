import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';

import { AvailabilityApi, AvailabilityDto } from '../../../core/availability-api';
import {
  DAY_LABELS_SHORT,
  addDays,
  addMonths,
  editableAvailabilityWeekStart,
  formatDate,
  formatMonthLabel,
  isAvailabilitySubmissionOpen,
  isPastDate,
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
  isLocked: boolean;
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

  // Employees only ever have one editable week (next week, until Saturday):
  // month navigation is limited to the range that could contain it.
  protected readonly canGoPreviousMonth = computed(
    () => this.monthAnchor().getTime() > startOfMonth(new Date()).getTime(),
  );
  protected readonly canGoNextMonth = computed(() => {
    const lastEditableDay = addDays(editableAvailabilityWeekStart(), 6);
    return addMonths(this.monthAnchor(), 1).getTime() <= startOfMonth(lastEditableDay).getTime();
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    const grid = monthGridDays(this.monthAnchor());
    const today = formatDate(new Date());
    const editableStart = formatDate(editableAvailabilityWeekStart());
    const editableEnd = formatDate(addDays(editableAvailabilityWeekStart(), 6));

    this.api.getMine(editableStart).subscribe({
      next: (dto) => {
        const byDate = new Map(dto.days.map((d) => [d.date, d]));
        // Submitting doesn't lock anything by itself — only the Saturday
        // deadline does — so the employee can keep adjusting right up to it.
        const weekLocked = !isAvailabilitySubmissionOpen();

        this.days.set(
          grid.map((date) => {
            const iso = formatDate(date);
            const isEditableDate = iso >= editableStart && iso <= editableEnd;
            const isLocked = !isEditableDate || weekLocked;
            // Don't surface stored data for locked days: only the one open
            // week is ever shown with real data.
            const d = isEditableDate ? byDate.get(iso) : undefined;
            return {
              date: iso,
              dayOfMonth: date.getDate(),
              isCurrentMonth: date.getMonth() === this.monthAnchor().getMonth(),
              isPast: isPastDate(iso),
              isLocked,
              isToday: iso === today,
              isAvailable: !isLocked && (d?.isAvailable ?? false),
              allDay: !isLocked && !!d?.isAvailable && !d.startTime && !d.endTime,
              startTime: toInputTime(isLocked ? null : (d?.startTime ?? null)),
              endTime: toInputTime(isLocked ? null : (d?.endTime ?? '17:00:00')),
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
    if (day.isLocked) {
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
        if (!updated || day.isLocked) {
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
