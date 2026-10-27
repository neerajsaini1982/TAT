import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';

import {
  TimeOffApi,
  TimeOffDayDto,
  TimeOffPreviewDto,
  TimeOffRequestDto,
  apiError,
  formatTimeOffMinutes,
  timeOffDateRange,
} from '../../../core/time-off-api';

export interface TimeOffReviewDialogData {
  request: TimeOffRequestDto;
  action: 'approve' | 'deny';
}

// Approve (with the shifts already in the range and the hours each day
// counts for) or deny (reason required) one pending request.
@Component({
  selector: 'app-time-off-review-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule],
  templateUrl: './time-off-review-dialog.html',
  styleUrl: '../time-off-request-dialog/time-off-request-dialog.scss',
})
export class TimeOffReviewDialog implements OnInit {
  private readonly api = inject(TimeOffApi);
  private readonly dialogRef = inject<MatDialogRef<TimeOffReviewDialog, boolean>>(MatDialogRef);
  protected readonly data = inject<TimeOffReviewDialogData>(MAT_DIALOG_DATA);

  protected readonly r = this.data.request;
  protected readonly approving = this.data.action === 'approve';
  protected readonly preview = signal<TimeOffPreviewDto | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected days: TimeOffDayDto[] = [];
  protected removeShifts = false;
  protected comment = '';

  protected readonly format = formatTimeOffMinutes;
  protected readonly range = timeOffDateRange;

  ngOnInit(): void {
    if (this.approving) {
      this.api.preview(this.r.id).subscribe({
        next: (p) => {
          this.preview.set(p);
          this.days = p.suggestedDays.map((d) => ({ ...d }));
        },
        error: (err) => this.error.set(apiError(err, 'Failed to load the shifts for these dates.')),
      });
    }
  }

  protected hours(day: TimeOffDayDto): number {
    return Math.round((day.minutes / 60) * 100) / 100;
  }

  protected setHours(day: TimeOffDayDto, hours: number | null): void {
    day.minutes = Math.max(0, Math.min(1440, Math.round((hours ?? 0) * 60)));
  }

  protected totalMinutes(): number {
    return this.days.reduce((sum, d) => sum + d.minutes, 0);
  }

  protected dayLabel(iso: string): string {
    return timeOffDateRange({ startDate: iso, endDate: iso });
  }

  protected save(): void {
    this.error.set(null);
    const comment = this.comment.trim() || null;
    if (!this.approving && !comment) {
      this.error.set('Please say why the request is denied.');
      return;
    }
    this.saving.set(true);
    const call = this.approving
      ? this.api.approve(this.r.id, comment, this.removeShifts, this.days)
      : this.api.deny(this.r.id, comment!);
    call.subscribe({
      next: () => this.dialogRef.close(true),
      error: (err) => {
        this.error.set(apiError(err, 'Failed to save.'));
        this.saving.set(false);
      },
    });
  }

  protected cancel(): void {
    this.dialogRef.close(false);
  }
}
