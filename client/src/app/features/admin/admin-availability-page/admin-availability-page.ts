import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';

import { AvailabilityApi, AvailabilityDayDto, AvailabilityDto } from '../../../core/availability-api';
import {
  DAY_LABELS,
  addDays,
  formatDate,
  formatWeekRange,
  mondayOf,
  parseDate,
  toApiTime,
  toInputTime,
} from '../../../core/week-utils';
import {
  AvailabilityDayDialog,
  AvailabilityDayDialogResult,
} from '../../employee/availability-day-dialog/availability-day-dialog';

@Component({
  selector: 'app-admin-availability-page',
  imports: [RouterLink, MatCardModule, MatButtonModule, MatIconModule],
  templateUrl: './admin-availability-page.html',
  styleUrl: './admin-availability-page.scss',
})
export class AdminAvailabilityPage implements OnInit {
  private readonly api = inject(AvailabilityApi);
  private readonly dialog = inject(MatDialog);
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  protected readonly dayLabels = DAY_LABELS;
  protected readonly weekStart = signal(addDays(mondayOf(new Date()), 7));
  protected readonly weekRangeLabel = () => formatWeekRange(this.weekStart());
  protected readonly error = signal<string | null>(null);
  protected readonly roster = signal<AvailabilityDto[]>([]);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.api.getForLocation(formatDate(this.weekStart()), this.locationCode).subscribe((roster) => this.roster.set(roster));
  }

  previousWeek(): void {
    this.weekStart.set(addDays(this.weekStart(), -7));
    this.load();
  }

  nextWeek(): void {
    this.weekStart.set(addDays(this.weekStart(), 7));
    this.load();
  }

  dayLabel(day: AvailabilityDto['days'][number]): string {
    if (!day.isAvailable) {
      return '—';
    }
    if (!day.startTime || !day.endTime) {
      return 'All day';
    }
    return `${day.startTime.slice(0, 5)}–${day.endTime.slice(0, 5)}`;
  }

  // Admin override: opens the same day-editor dialog the employee calendar
  // uses, but saves through SaveForAccount, which bypasses the employee's
  // submit/deadline lock entirely.
  editDay(person: AvailabilityDto, day: AvailabilityDayDto): void {
    this.dialog
      .open<AvailabilityDayDialog, unknown, AvailabilityDayDialogResult>(AvailabilityDayDialog, {
        data: {
          dayLabel: parseDate(day.date).toLocaleDateString(undefined, { weekday: 'long' }),
          dateLabel: parseDate(day.date).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' }),
          isAvailable: day.isAvailable,
          allDay: day.isAvailable && !day.startTime && !day.endTime,
          startTime: toInputTime(day.startTime),
          endTime: toInputTime(day.endTime ?? '17:00:00'),
        },
      })
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          this.saveDay(person, day.date, result);
        }
      });
  }

  private saveDay(person: AvailabilityDto, date: string, result: AvailabilityDayDialogResult): void {
    this.error.set(null);
    const days: AvailabilityDayDto[] = person.days.map((d) =>
      d.date === date
        ? {
            date,
            isAvailable: result.isAvailable,
            startTime: result.isAvailable && !result.allDay ? toApiTime(result.startTime) : null,
            endTime: result.isAvailable && !result.allDay ? toApiTime(result.endTime) : null,
          }
        : { date: d.date, isAvailable: d.isAvailable, startTime: d.startTime, endTime: d.endTime },
    );

    this.api
      .saveForAccount(person.accountId, { weekStartDate: formatDate(this.weekStart()), days, submit: false })
      .subscribe({
        next: () => this.load(),
        error: (err) => this.error.set(err?.error ?? 'Failed to update availability.'),
      });
  }
}
