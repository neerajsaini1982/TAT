import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';

import { CurrentWeekSchedule } from '../../schedule/current-week-schedule/current-week-schedule';

// The week view (punches, mark absent, edit times) that used to be the
// Admin landing page — AdminHome now shows AdminDashboard there instead,
// and this is reached from the account menu's "View Schedule".
@Component({
  selector: 'app-admin-view-schedule-page',
  imports: [RouterLink, MatIconModule, MatButtonModule, CurrentWeekSchedule],
  templateUrl: './admin-view-schedule-page.html',
  styleUrl: './admin-view-schedule-page.scss',
  // Wide table — opts out of the shell's 960px column (styles.scss).
  host: { class: 'full-width-page' },
})
export class AdminViewSchedulePage {
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;
}
