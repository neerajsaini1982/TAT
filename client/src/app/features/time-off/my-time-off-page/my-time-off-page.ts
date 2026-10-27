import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';

import { Auth } from '../../../core/auth';
import {
  TimeOffApi,
  TimeOffRequestDto,
  TimeOffSettingsDto,
  apiError,
  formatTimeOffMinutes,
  timeOffDateRange,
} from '../../../core/time-off-api';
import { formatDate } from '../../../core/week-utils';
import { TimeOffRequestDialog, TimeOffRequestDialogData } from '../time-off-request-dialog/time-off-request-dialog';

// Employee self-service: my requests and their status, a button to ask for
// more, and cancel — pending ones any time, approved ones until they start.
@Component({
  selector: 'app-my-time-off-page',
  imports: [RouterLink, MatButtonModule, MatIconModule],
  templateUrl: './my-time-off-page.html',
  styleUrl: './my-time-off-page.scss',
})
export class MyTimeOffPage implements OnInit {
  private readonly api = inject(TimeOffApi);
  private readonly dialog = inject(MatDialog);
  private readonly auth = inject(Auth);
  protected readonly locationCode = inject(ActivatedRoute).snapshot.paramMap.get('locationCode')!;

  protected readonly requests = signal<TimeOffRequestDto[]>([]);
  protected readonly settings = signal<TimeOffSettingsDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly busyId = signal<number | null>(null);

  protected readonly format = formatTimeOffMinutes;
  protected readonly range = timeOffDateRange;
  // Admins and leads land here from the admin menu; send them back there.
  protected readonly backLink = computed(() =>
    ['Admin', 'Lead'].includes(this.auth.role() ?? '') ? ['/', this.locationCode, 'admin'] : ['/', this.locationCode, 'employee'],
  );
  private readonly today = formatDate(new Date());
  protected readonly upcoming = computed(() =>
    this.requests().filter((r) => r.endDate >= this.today && (r.status === 'Pending' || r.status === 'Approved')),
  );
  protected readonly past = computed(() => this.requests().filter((r) => !this.upcoming().includes(r)));

  ngOnInit(): void {
    this.api.settings(this.locationCode).subscribe((s) => this.settings.set(s));
    this.load();
  }

  private load(): void {
    this.api.mine().subscribe({
      next: (requests) => {
        this.requests.set(requests);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(apiError(err, 'Failed to load your time off.'));
        this.loading.set(false);
      },
    });
  }

  protected request(): void {
    this.dialog
      .open<TimeOffRequestDialog, TimeOffRequestDialogData, boolean>(TimeOffRequestDialog, {
        data: { locationCode: this.locationCode, mode: 'employee' },
        maxWidth: '100vw',
      })
      .afterClosed()
      .subscribe((saved) => saved && this.load());
  }

  protected canCancel(r: TimeOffRequestDto): boolean {
    return r.status === 'Pending' || (r.status === 'Approved' && r.startDate > this.today);
  }

  protected cancel(r: TimeOffRequestDto): void {
    const what = r.status === 'Approved' ? 'approved time off' : 'request';
    if (!confirm(`Cancel this ${what} for ${timeOffDateRange(r)}?`)) return;
    this.busyId.set(r.id);
    this.api.cancel(r.id).subscribe({
      next: () => {
        this.busyId.set(null);
        this.load();
      },
      error: (err) => {
        this.busyId.set(null);
        this.error.set(apiError(err, 'Failed to cancel.'));
      },
    });
  }

  protected hoursLabel(r: TimeOffRequestDto): string {
    if (r.isPartialDay && r.partialMinutes) return `${formatTimeOffMinutes(r.partialMinutes)} off`;
    if (r.totalMinutes > 0) return formatTimeOffMinutes(r.totalMinutes);
    const days = Math.round((Date.parse(r.endDate) - Date.parse(r.startDate)) / 86400000) + 1;
    return days === 1 ? 'Full day' : `${days} days`;
  }
}
