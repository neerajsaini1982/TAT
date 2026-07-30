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
  TIME_SUGGESTIONS,
  addDays,
  availabilitySubmissionDeadline,
  dayOfWeekLabel,
  editableAvailabilityWeekStart,
  formatDate,
  formatDisplayTime,
  formatWeekRange,
  isAvailabilitySubmissionOpen,
  parseTimeInput,
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
  // Whatever text the employee has typed (e.g. "3:35 PM"), not necessarily
  // a valid parsed time yet — see parseTimeInput in week-utils.ts, run
  // fresh on every day at save() time. Plain text, not <input type="time">
  // — that native picker's segments can visually show a complete value
  // while its own .value getter still reports "" (most often when typing
  // over a value that was already there), which is how Start/End have
  // silently collapsed to "All day" before.
  startTime: string;
  endTime: string;
  // Set once the employee focuses Start/End — lets save() tell "never
  // touched, available all day" apart from "started typing a time but
  // didn't finish it", since both look identical as an empty string.
  startTouched: boolean;
  endTouched: boolean;
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
  protected readonly copyingPreviousWeek = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly days = signal<DayModel[]>([]);
  protected readonly timeSuggestions = TIME_SUGGESTIONS;

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
        startTime: formatDisplayTime(toInputTime(d.startTime)),
        endTime: formatDisplayTime(toInputTime(d.endTime ?? '17:00:00')),
        startTouched: false,
        endTouched: false,
      })),
    );
    this.loading.set(false);
  }

  private save(submit: boolean): void {
    // Parsed once up front so both the confirm-blank checks below and the
    // actual API payload use the same "HH:mm" values, rather than the raw
    // display text (e.g. "3:35 PM") the days() signal holds.
    const parsed = new Map<string, { start: string; end: string }>();
    for (const d of this.days()) {
      if (!d.isAvailable || d.allDay) {
        continue;
      }
      const start = parseTimeInput(d.startTime);
      if (start === null) {
        this.error.set(`${d.label}'s start isn’t a valid time — try a format like 3:35 PM.`);
        return;
      }
      const end = parseTimeInput(d.endTime);
      if (end === null) {
        this.error.set(`${d.label}'s end isn’t a valid time — try a format like 3:35 PM.`);
        return;
      }
      parsed.set(d.date, { start, end });
    }

    for (const d of this.days()) {
      const p = parsed.get(d.date);
      if (!p) {
        continue;
      }
      if (d.startTouched && !p.start) {
        if (!confirm(`${d.label}'s start time is blank even though you edited it. Save anyway?`)) {
          return;
        }
      }
      if (d.endTouched && !p.end) {
        if (!confirm(`${d.label}'s end time is blank even though you edited it. Save anyway?`)) {
          return;
        }
      }
    }

    this.error.set(null);
    this.api
      .saveMine({
        weekStartDate: formatDate(this.weekStart),
        days: this.days().map((d) => {
          const p = parsed.get(d.date);
          return {
            date: d.date,
            isAvailable: d.isAvailable,
            startTime: p?.start ? toApiTime(p.start) : null,
            endTime: p?.end ? toApiTime(p.end) : null,
          };
        }),
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

  // Fills the form with last week's values, matched up by weekday (index),
  // not by date. Deliberately doesn't save — the employee reviews/adjusts
  // and still has to click Save Draft or Submit themselves. If nothing was
  // ever submitted for last week (dto.id === 0, the API's "blank" marker —
  // see AvailabilityController.ToDto), there's nothing meaningful to copy,
  // so every day defaults to available all day instead of blank/unavailable.
  copyPreviousWeek(): void {
    if (!confirm("Copy last week's availability into this form? This replaces what's currently shown here (not yet saved).")) {
      return;
    }
    this.copyingPreviousWeek.set(true);
    this.error.set(null);
    this.api.getMine(formatDate(addDays(this.weekStart, -7))).subscribe({
      next: (dto) => {
        const hasPrevious = dto.id !== 0;
        const source = dto.days;
        this.days.update((days) =>
          days.map((d, i) => {
            if (!hasPrevious) {
              return {
                ...d,
                isAvailable: true,
                allDay: true,
                startTime: formatDisplayTime(toInputTime(null)),
                endTime: formatDisplayTime(toInputTime('17:00:00')),
                startTouched: false,
                endTouched: false,
              };
            }
            const s = source[i];
            if (!s) {
              return d;
            }
            return {
              ...d,
              isAvailable: s.isAvailable,
              allDay: s.isAvailable && !s.startTime && !s.endTime,
              startTime: formatDisplayTime(toInputTime(s.startTime)),
              endTime: formatDisplayTime(toInputTime(s.endTime ?? '17:00:00')),
              startTouched: false,
              endTouched: false,
            };
          }),
        );
        this.copyingPreviousWeek.set(false);
      },
      error: () => {
        this.error.set('Failed to load previous week.');
        this.copyingPreviousWeek.set(false);
      },
    });
  }
}
