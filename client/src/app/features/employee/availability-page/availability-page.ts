import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { forkJoin } from 'rxjs';

import { AvailabilityApi, AvailabilityDayDto, AvailabilityDto } from '../../../core/availability-api';
import {
  addDays,
  dayOfWeekLabel,
  formatDate,
  isBeyondAvailabilityWindow,
  isPastDate,
  maxAvailabilityDate,
  mondayOf,
  toApiTime,
  toInputTime,
  toMmDdYyyy,
} from '../../../core/week-utils';

interface DayModel {
  date: string;
  label: string;
  dateLabel: string;
  isAvailable: boolean;
  allDay: boolean;
  startTime: string;
  endTime: string;
}

@Component({
  selector: 'app-availability-page',
  imports: [FormsModule, DatePipe, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatSlideToggleModule, MatFormFieldModule, MatInputModule],
  templateUrl: './availability-page.html',
  styleUrl: './availability-page.scss',
})
export class AvailabilityPage implements OnInit {
  private readonly api = inject(AvailabilityApi);
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  protected readonly submittedAt = signal<string | null>(null);
  protected readonly isSubmitted = signal(false);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  // Employees only plan today through the next 15 days, so instead of
  // paging week by week, this flat list always shows every visible day in
  // that window at once.
  protected readonly days = signal<DayModel[]>([]);

  ngOnInit(): void {
    this.load();
  }

  // Every Monday from this week through the week containing the last day
  // of the planning window.
  private windowWeekStarts(): Date[] {
    const starts: Date[] = [];
    const lastWeekStart = mondayOf(maxAvailabilityDate());
    for (let d = mondayOf(new Date()); d.getTime() <= lastWeekStart.getTime(); d = addDays(d, 7)) {
      starts.push(d);
    }
    return starts;
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    forkJoin(this.windowWeekStarts().map((ws) => this.api.getMine(formatDate(ws)))).subscribe({
      next: (weeks) => this.applyWeeks(weeks),
      error: () => {
        this.error.set('Failed to load availability.');
        this.loading.set(false);
      },
    });
  }

  private applyWeeks(weeks: AvailabilityDto[]): void {
    this.isSubmitted.set(weeks.every((w) => w.isSubmitted));
    const timestamps = weeks.map((w) => w.submittedAt).filter((t): t is string => !!t);
    this.submittedAt.set(timestamps.length ? timestamps.sort().at(-1)! : null);

    this.days.set(
      weeks
        .flatMap((w) => w.days)
        .filter((d) => !isPastDate(d.date) && !isBeyondAvailabilityWindow(d.date))
        .sort((a, b) => (a.date < b.date ? -1 : a.date > b.date ? 1 : 0))
        .map((d) => ({
          date: d.date,
          label: dayOfWeekLabel(d.date),
          dateLabel: toMmDdYyyy(d.date),
          isAvailable: d.isAvailable,
          allDay: d.isAvailable && !d.startTime && !d.endTime,
          startTime: toInputTime(d.startTime),
          endTime: toInputTime(d.endTime ?? '17:00:00'),
        })),
    );
    this.loading.set(false);
  }

  // Reconstructs the full 7-day payload SaveMine expects for one week.
  // Days that aren't in the visible window are sent blank; the server
  // freezes/ignores them regardless, so this is safe.
  private buildWeekPayload(weekStart: Date): AvailabilityDayDto[] {
    const currentByDate = new Map(this.days().map((d) => [d.date, d]));
    return Array.from({ length: 7 }, (_, i) => {
      const date = formatDate(addDays(weekStart, i));
      const d = currentByDate.get(date);
      return {
        date,
        isAvailable: d?.isAvailable ?? false,
        startTime: d?.isAvailable && !d.allDay ? toApiTime(d.startTime) : null,
        endTime: d?.isAvailable && !d.allDay ? toApiTime(d.endTime) : null,
      };
    });
  }

  private save(submit: boolean): void {
    this.error.set(null);
    const weekStarts = this.windowWeekStarts();
    forkJoin(
      weekStarts.map((ws) =>
        this.api.saveMine({ weekStartDate: formatDate(ws), days: this.buildWeekPayload(ws), submit }),
      ),
    ).subscribe({
      next: (results) => this.applyWeeks(results),
      error: (err) => this.error.set(err?.error ?? 'Failed to save availability.'),
    });
  }

  saveDraft(): void {
    this.save(false);
  }

  submit(): void {
    this.save(true);
  }

  // Sets every visible day back to Not Available and saves immediately, so
  // the whole window can be wiped clean in one click instead of toggling
  // each day off by hand.
  resetWeek(): void {
    if (!confirm('Reset all upcoming days to Not Available?')) {
      return;
    }
    this.days.update((days) => days.map((d) => ({ ...d, isAvailable: false, allDay: false })));
    this.save(false);
  }
}
