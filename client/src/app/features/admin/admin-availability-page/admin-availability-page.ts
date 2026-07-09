import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

import { AvailabilityApi, AvailabilityDto } from '../../../core/availability-api';
import { DAY_LABELS, addDays, formatDate, formatWeekRange, mondayOf } from '../../../core/week-utils';

@Component({
  selector: 'app-admin-availability-page',
  imports: [RouterLink, MatCardModule, MatButtonModule, MatIconModule],
  templateUrl: './admin-availability-page.html',
  styleUrl: './admin-availability-page.scss',
})
export class AdminAvailabilityPage implements OnInit {
  private readonly api = inject(AvailabilityApi);
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  protected readonly dayLabels = DAY_LABELS;
  protected readonly weekStart = signal(addDays(mondayOf(new Date()), 7));
  protected readonly weekRangeLabel = () => formatWeekRange(this.weekStart());
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
}
