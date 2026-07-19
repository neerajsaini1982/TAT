import { Component, Inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import { TimeEntryDto } from '../../../core/time-entries-api';
import { formatHHmm } from '../../../core/week-utils';

export interface EditTimeEntryDialogData {
  employeeName: string;
  entry: TimeEntryDto | null;
}

export interface EditTimeEntryResult {
  clockInAt: string;
  breakStartAt: string | null;
  breakEndAt: string | null;
  lunchStartAt: string | null;
  lunchEndAt: string | null;
  break2StartAt: string | null;
  break2EndAt: string | null;
  clockOutAt: string | null;
  note: string;
}

const toInput = (iso: string | null): string => (iso ? formatHHmm(new Date(iso)) : '');

// Lets an admin set every punch on today's TimeEntry directly — correcting
// a mistake, or filling one in from scratch when the employee never
// clocked in at all (data.entry is null in that case). Fields are plain
// <input type="time">; only Clock In is required. Reuses the same
// required-note convention as NoteDialog (see AdminEditTimeEntryRequest).
@Component({
  selector: 'app-edit-time-entry-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './edit-time-entry-dialog.html',
  styleUrl: './edit-time-entry-dialog.scss',
})
export class EditTimeEntryDialog {
  protected clockInAt: string;
  protected breakStartAt: string;
  protected breakEndAt: string;
  protected lunchStartAt: string;
  protected lunchEndAt: string;
  protected break2StartAt: string;
  protected break2EndAt: string;
  protected clockOutAt: string;
  protected note = '';
  protected error: string | null = null;

  constructor(
    private readonly dialogRef: MatDialogRef<EditTimeEntryDialog, EditTimeEntryResult>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: EditTimeEntryDialogData,
  ) {
    const entry = data.entry;
    this.clockInAt = toInput(entry?.clockInAt ?? null) || formatHHmm(new Date());
    this.breakStartAt = toInput(entry?.breakStartAt ?? null);
    this.breakEndAt = toInput(entry?.breakEndAt ?? null);
    this.lunchStartAt = toInput(entry?.lunchStartAt ?? null);
    this.lunchEndAt = toInput(entry?.lunchEndAt ?? null);
    this.break2StartAt = toInput(entry?.break2StartAt ?? null);
    this.break2EndAt = toInput(entry?.break2EndAt ?? null);
    this.clockOutAt = toInput(entry?.clockOutAt ?? null);
  }

  save(): void {
    this.error = null;
    if (!this.clockInAt) {
      this.error = 'Clock in time is required.';
      return;
    }
    if (!this.note.trim()) {
      this.error = 'A note explaining the edit is required.';
      return;
    }

    const pairs: [string, string, string][] = [
      [this.breakStartAt, this.breakEndAt, 'Break'],
      [this.lunchStartAt, this.lunchEndAt, 'Lunch'],
      [this.break2StartAt, this.break2EndAt, 'Second break'],
    ];
    for (const [start, end, label] of pairs) {
      if (end && !start) {
        this.error = `${label} end requires a ${label.toLowerCase()} start.`;
        return;
      }
      if (start && end && end < start) {
        this.error = `${label} end can't be before ${label.toLowerCase()} start.`;
        return;
      }
    }
    if (this.clockOutAt && this.clockOutAt < this.clockInAt) {
      this.error = "Clock out can't be before clock in.";
      return;
    }

    this.dialogRef.close({
      clockInAt: this.clockInAt,
      breakStartAt: this.breakStartAt || null,
      breakEndAt: this.breakEndAt || null,
      lunchStartAt: this.lunchStartAt || null,
      lunchEndAt: this.lunchEndAt || null,
      break2StartAt: this.break2StartAt || null,
      break2EndAt: this.break2EndAt || null,
      clockOutAt: this.clockOutAt || null,
      note: this.note.trim(),
    });
  }

  cancel(): void {
    this.dialogRef.close();
  }
}
