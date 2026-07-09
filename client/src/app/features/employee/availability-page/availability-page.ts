import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import { AvailabilityApi, AvailabilityDto } from '../../../core/availability-api';
import {
  DAY_LABELS,
  addDays,
  formatDate,
  formatWeekRange,
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
  isPast: boolean;
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

  protected readonly weekStart = signal(mondayOf(new Date()));
  protected readonly weekRangeLabel = () => formatWeekRange(this.weekStart());
  protected readonly submittedAt = signal<string | null>(null);
  protected readonly isSubmitted = signal(false);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly days = signal<DayModel[]>([]);

  // Employees only plan today through the next 15 days: earlier weeks
  // aren't navigable, and days outside that window are hidden entirely
  // rather than just shown read-only.
  protected readonly visibleDays = computed(() =>
    this.days().filter((d) => !d.isPast && !isBeyondAvailabilityWindow(d.date)),
  );
  protected readonly canGoPreviousWeek = computed(
    () => this.weekStart().getTime() > mondayOf(new Date()).getTime(),
  );
  protected readonly canGoNextWeek = computed(
    () => addDays(this.weekStart(), 7).getTime() <= maxAvailabilityDate().getTime(),
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.getMine(formatDate(this.weekStart())).subscribe({
      next: (dto) => this.applyDto(dto),
      error: () => {
        this.error.set('Failed to load availability.');
        this.loading.set(false);
      },
    });
  }

  private applyDto(dto: AvailabilityDto): void {
    this.isSubmitted.set(dto.isSubmitted);
    this.submittedAt.set(dto.submittedAt);
    this.days.set(
      dto.days.map((d, i) => ({
        date: d.date,
        label: DAY_LABELS[i],
        dateLabel: toMmDdYyyy(d.date),
        isAvailable: d.isAvailable,
        allDay: d.isAvailable && !d.startTime && !d.endTime,
        startTime: toInputTime(d.startTime),
        endTime: toInputTime(d.endTime ?? '17:00:00'),
        isPast: isPastDate(d.date),
      })),
    );
    this.loading.set(false);
  }

  previousWeek(): void {
    if (!this.canGoPreviousWeek()) {
      return;
    }
    this.weekStart.set(addDays(this.weekStart(), -7));
    this.load();
  }

  nextWeek(): void {
    if (!this.canGoNextWeek()) {
      return;
    }
    this.weekStart.set(addDays(this.weekStart(), 7));
    this.load();
  }

  private save(submit: boolean): void {
    this.error.set(null);
    this.api
      .saveMine({
        weekStartDate: formatDate(this.weekStart()),
        days: this.days().map((d) => ({
          date: d.date,
          isAvailable: d.isAvailable,
          startTime: d.isAvailable && !d.allDay ? toApiTime(d.startTime) : null,
          endTime: d.isAvailable && !d.allDay ? toApiTime(d.endTime) : null,
        })),
        submit,
      })
      .subscribe({
        next: (dto) => this.applyDto(dto),
        error: (err) => this.error.set(err?.error ?? 'Failed to save availability.'),
      });
  }

  saveDraft(): void {
    this.save(false);
  }

  submit(): void {
    this.save(true);
  }

  // Sets every visible (non-past, in-window) day back to Not Available and
  // saves immediately, so a week can be wiped clean in one click instead of
  // toggling each day off by hand. Hidden days are left untouched (frozen
  // server-side anyway).
  resetWeek(): void {
    if (!confirm('Reset all upcoming days in this week to Not Available?')) {
      return;
    }
    this.days.update((days) =>
      days.map((d) =>
        d.isPast || isBeyondAvailabilityWindow(d.date) ? d : { ...d, isAvailable: false, allDay: false },
      ),
    );
    this.save(false);
  }
}
