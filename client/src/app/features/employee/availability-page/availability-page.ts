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

import { AvailabilityApi, AvailabilityDto } from '../../../core/availability-api';
import { DAY_LABELS, addDays, formatDate, formatWeekRange, mondayOf } from '../../../core/week-utils';

interface DayModel {
  date: string;
  label: string;
  isAvailable: boolean;
  allDay: boolean;
  startTime: string;
  endTime: string;
}

const toInputTime = (apiTime: string | null): string => (apiTime ? apiTime.slice(0, 5) : '09:00');
const toApiTime = (inputTime: string): string => (inputTime.length === 5 ? `${inputTime}:00` : inputTime);

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

  protected readonly weekStart = signal(addDays(mondayOf(new Date()), 7));
  protected readonly weekRangeLabel = () => formatWeekRange(this.weekStart());
  protected readonly submittedAt = signal<string | null>(null);
  protected readonly isSubmitted = signal(false);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected days: DayModel[] = [];

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
    this.days = dto.days.map((d, i) => ({
      date: d.date,
      label: DAY_LABELS[i],
      isAvailable: d.isAvailable,
      allDay: d.isAvailable && !d.startTime && !d.endTime,
      startTime: toInputTime(d.startTime),
      endTime: toInputTime(d.endTime ?? '17:00:00'),
    }));
    this.loading.set(false);
  }

  previousWeek(): void {
    this.weekStart.set(addDays(this.weekStart(), -7));
    this.load();
  }

  nextWeek(): void {
    this.weekStart.set(addDays(this.weekStart(), 7));
    this.load();
  }

  private save(submit: boolean): void {
    this.error.set(null);
    this.api
      .saveMine({
        weekStartDate: formatDate(this.weekStart()),
        days: this.days.map((d) => ({
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
    if (!confirm('Once submitted, you will not be able to change your availability for this week. Continue?')) {
      return;
    }
    this.save(true);
  }
}
