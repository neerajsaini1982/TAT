import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import { AvailabilityApi, AvailabilityDto } from '../../../core/availability-api';
import {
  availabilitySubmissionDeadline,
  dayOfWeekLabel,
  editableAvailabilityWeekStart,
  formatDate,
  formatWeekRange,
  isAvailabilitySubmissionOpen,
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

// The actual day-by-day availability form, shared between the standalone
// /:locationCode/employee/availability page and the inline widget on the
// employee home page.
@Component({
  selector: 'app-availability-editor',
  imports: [FormsModule, DatePipe, MatCardModule, MatButtonModule, MatIconModule, MatSlideToggleModule, MatFormFieldModule, MatInputModule],
  templateUrl: './availability-editor.html',
  styleUrl: './availability-editor.scss',
})
export class AvailabilityEditor implements OnInit {
  private readonly api = inject(AvailabilityApi);

  // Employees only ever submit for the one week ahead, during the current
  // week, with a hard Saturday deadline. Fixed at load time.
  protected readonly weekStart = editableAvailabilityWeekStart();
  protected readonly weekRangeLabel = formatWeekRange(this.weekStart);
  protected readonly deadlineLabel = toMmDdYyyy(formatDate(availabilitySubmissionDeadline()));
  protected readonly submissionOpen = isAvailabilitySubmissionOpen();

  protected readonly submittedAt = signal<string | null>(null);
  protected readonly isSubmitted = signal(false);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly days = signal<DayModel[]>([]);

  // Submitting doesn't lock anything by itself — only the Saturday
  // deadline does — so the employee can keep adjusting right up to it.
  protected readonly locked = computed(() => !this.submissionOpen);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.getMine(formatDate(this.weekStart)).subscribe({
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
      dto.days.map((d) => ({
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

  private save(submit: boolean): void {
    this.error.set(null);
    this.api
      .saveMine({
        weekStartDate: formatDate(this.weekStart),
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

  // Sets every day back to Not Available and saves immediately, so the
  // week can be wiped clean in one click instead of toggling each day off
  // by hand.
  resetWeek(): void {
    if (!confirm('Reset all days to Not Available?')) {
      return;
    }
    this.days.update((days) => days.map((d) => ({ ...d, isAvailable: false, allDay: false })));
    this.save(false);
  }
}
