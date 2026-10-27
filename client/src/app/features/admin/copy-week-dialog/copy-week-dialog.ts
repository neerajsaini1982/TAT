import { Component, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatRadioModule } from '@angular/material/radio';
import { FormsModule } from '@angular/forms';

import { CopyWeekResultDto, ShiftAssignmentsApi } from '../../../core/shift-assignments-api';

export interface CopyWeekDialogData {
  locationCode: string;
  fromWeekStartDate: string;
  toWeekStartDate: string;
  fromWeekLabel: string;
  toWeekLabel: string;
  // How many shifts the target week already has, to warn before merging.
  existingCount: number;
}

// Confirms copying last week's schedule into this one, runs it, then shows
// what was copied and what was skipped (and why). Closes with true when
// anything was copied so the page knows to reload.
@Component({
  selector: 'app-copy-week-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatRadioModule],
  templateUrl: './copy-week-dialog.html',
  styleUrl: './copy-week-dialog.scss',
})
export class CopyWeekDialog {
  private readonly api = inject(ShiftAssignmentsApi);
  private readonly dialogRef = inject<MatDialogRef<CopyWeekDialog, boolean>>(MatDialogRef);
  protected readonly data = inject<CopyWeekDialogData>(MAT_DIALOG_DATA);

  protected readonly copying = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly result = signal<CopyWeekResultDto | null>(null);
  // Only asked when the target week already has shifts.
  protected overwrite = true;

  copy(): void {
    this.copying.set(true);
    this.error.set(null);
    this.api
      .copyWeek(this.data.fromWeekStartDate, this.data.toWeekStartDate, this.data.existingCount > 0 && this.overwrite, this.data.locationCode).subscribe({
      next: (result) => {
        this.copying.set(false);
        this.result.set(result);
      },
      error: (err) => {
        this.copying.set(false);
        // A 404/405 with no message means the API server predates this
        // endpoint (it needs a restart after updating).
        this.error.set(
          typeof err?.error === 'string' && err.error
            ? err.error
            : err?.status === 404 || err?.status === 405
              ? 'The server does not support copying yet — restart the API server and try again.'
              : 'Failed to copy the schedule.',
        );
      },
    });
  }

  close(): void {
    const r = this.result();
    this.dialogRef.close(!!r && (r.copiedCount > 0 || r.removedCount > 0));
  }

  dayLabel(date: string): string {
    const [y, m, d] = date.split('-').map(Number);
    return new Date(y, m - 1, d).toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' });
  }
}
