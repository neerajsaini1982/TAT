import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatExpansionModule } from '@angular/material/expansion';

import { LocationSettingsApi } from '../../../core/location-settings-api';

// Static walkthrough of the employee self-service flow (login, clock
// in/out, breaks/lunch) — no API calls beyond LocationSettingsApi.getMine(),
// used only to personalize the clock-in-window/break/lunch limits shown
// below with this location's actual configured values.
@Component({
  selector: 'app-help-page',
  imports: [RouterLink, MatIconModule, MatButtonModule, MatExpansionModule],
  templateUrl: './help-page.html',
  styleUrl: './help-page.scss',
})
export class HelpPage {
  private readonly route = inject(ActivatedRoute);
  private readonly settingsApi = inject(LocationSettingsApi);

  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  protected readonly clockInWindowMinutes = signal<number | null>(null);
  protected readonly breakLimitMinutes = signal<number | null>(null);
  protected readonly lunchLimitMinutes = signal<number | null>(null);

  constructor() {
    this.settingsApi.getMine().subscribe((settings) => {
      this.clockInWindowMinutes.set(settings.clockInWindowMinutes);
      this.breakLimitMinutes.set(settings.breakLimitMinutes);
      this.lunchLimitMinutes.set(settings.lunchLimitMinutes);
    });
  }
}
