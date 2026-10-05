import { Component, OnInit, inject, signal } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';

import { LocationSettingsApi } from '../../../core/location-settings-api';
import { formatDate, toMmDdYyyy } from '../../../core/week-utils';

// Shown on the Employee/Admin/Lead home page right after login, alongside
// CurrentWeekSchedule. Sourced from LocationSettings.GetNextPayDate via the
// same self-service GetMine endpoint CurrentWeekSchedule already uses, so a
// single admin-entered pay day + interval (see admin-location-settings-page)
// is all that's ever stored — the "next" date is recomputed on every load
// rather than requiring an ever-growing list of entries.
@Component({
  selector: 'app-pay-day-banner',
  imports: [MatCardModule, MatIconModule],
  templateUrl: './pay-day-banner.html',
  styleUrl: './pay-day-banner.scss',
})
export class PayDayBanner implements OnInit {
  private readonly settingsApi = inject(LocationSettingsApi);

  protected readonly nextPayDate = signal<string | null>(null);
  protected readonly isToday = signal(false);
  // "09/20/2026 – 10/03/2026", or null when the pay period isn't configured.
  protected readonly payPeriod = signal<string | null>(null);

  ngOnInit(): void {
    this.settingsApi.getMine().subscribe({
      next: (settings) => {
        this.nextPayDate.set(settings.nextPayDate);
        this.isToday.set(settings.nextPayDate === formatDate(new Date()));
        this.payPeriod.set(
          settings.nextPayPeriodStart && settings.nextPayPeriodEnd
            ? `${toMmDdYyyy(settings.nextPayPeriodStart)} – ${toMmDdYyyy(settings.nextPayPeriodEnd)}`
            : null,
        );
      },
      error: () => this.nextPayDate.set(null),
    });
  }

  readonly toMmDdYyyy = toMmDdYyyy;
}
