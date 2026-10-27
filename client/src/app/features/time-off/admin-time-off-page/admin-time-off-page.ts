import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';

import {
  TimeOffApi,
  TimeOffRequestDto,
  TimeOffStatus,
  apiError,
  formatTimeOffMinutes,
  timeOffDateRange,
} from '../../../core/time-off-api';
import { formatDate } from '../../../core/week-utils';
import { TimeOffRequestDialog, TimeOffRequestDialogData } from '../time-off-request-dialog/time-off-request-dialog';
import { TimeOffReviewDialog, TimeOffReviewDialogData } from '../time-off-review-dialog/time-off-review-dialog';
import { TimeOffSetup } from '../time-off-setup/time-off-setup';

type Tab = 'pending' | 'upcoming' | 'all' | 'setup';

// The location admin's time-off screen: requests waiting for a decision,
// approved time off coming up, the full history, and setup (types,
// blackout dates, settings). Admin/Sa only — leads can't review.
@Component({
  selector: 'app-admin-time-off-page',
  imports: [DatePipe, MatButtonModule, MatIconModule, TimeOffSetup],
  templateUrl: './admin-time-off-page.html',
  styleUrl: './admin-time-off-page.scss',
  host: { class: 'full-width-page' },
})
export class AdminTimeOffPage implements OnInit {
  private readonly api = inject(TimeOffApi);
  private readonly dialog = inject(MatDialog);
  protected readonly locationCode = inject(ActivatedRoute).snapshot.paramMap.get('locationCode')!;

  protected readonly tab = signal<Tab>('pending');
  protected readonly requests = signal<TimeOffRequestDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly openHistoryId = signal<number | null>(null);

  protected readonly format = formatTimeOffMinutes;
  protected readonly range = timeOffDateRange;
  private readonly today = formatDate(new Date());

  protected readonly pending = computed(() => this.requests().filter((r) => r.status === 'Pending'));
  protected readonly upcoming = computed(() =>
    this.requests()
      .filter((r) => r.status === 'Approved' && r.endDate >= this.today)
      .sort((a, b) => a.startDate.localeCompare(b.startDate)),
  );
  protected readonly shown = computed(() => {
    switch (this.tab()) {
      case 'pending':
        return this.pending();
      case 'upcoming':
        return this.upcoming();
      default:
        return this.requests();
    }
  });

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.api.forLocation(this.locationCode).subscribe({
      next: (requests) => {
        this.requests.set(requests);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(apiError(err, 'Failed to load time off.'));
        this.loading.set(false);
      },
    });
  }

  protected review(request: TimeOffRequestDto, action: 'approve' | 'deny'): void {
    this.dialog
      .open<TimeOffReviewDialog, TimeOffReviewDialogData, boolean>(TimeOffReviewDialog, {
        data: { request, action },
        maxWidth: '100vw',
      })
      .afterClosed()
      .subscribe((saved) => saved && this.load());
  }

  protected add(): void {
    this.dialog
      .open<TimeOffRequestDialog, TimeOffRequestDialogData, boolean>(TimeOffRequestDialog, {
        data: { locationCode: this.locationCode, mode: 'admin' },
        maxWidth: '100vw',
      })
      .afterClosed()
      .subscribe((saved) => saved && this.load());
  }

  protected cancel(request: TimeOffRequestDto): void {
    const reason = prompt(`Cancel ${request.employeeName}'s ${request.typeName} (${timeOffDateRange(request)})? Reason:`);
    if (reason === null) return;
    this.api.cancel(request.id, reason || null).subscribe({
      next: () => this.load(),
      error: (err) => this.error.set(apiError(err, 'Failed to cancel.')),
    });
  }

  protected toggleHistory(id: number): void {
    this.openHistoryId.set(this.openHistoryId() === id ? null : id);
  }

  protected statusClass(status: TimeOffStatus): string {
    return 'status-' + status.toLowerCase();
  }

  protected hoursLabel(r: TimeOffRequestDto): string {
    if (r.isPartialDay && r.partialMinutes) return formatTimeOffMinutes(r.partialMinutes);
    if (r.totalMinutes > 0) return formatTimeOffMinutes(r.totalMinutes);
    const days = Math.round((Date.parse(r.endDate) - Date.parse(r.startDate)) / 86400000) + 1;
    return days === 1 ? 'Full day' : `${days} days`;
  }

  protected paidLabel(r: TimeOffRequestDto): string {
    return r.isPaid === null ? '' : r.isPaid ? ' · Paid' : ' · Unpaid';
  }
}
