import { Component, Inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import { TIME_SUGGESTIONS, formatDisplayTime, parseTimeInput } from '../../../core/week-utils';

export interface AvailabilityDayDialogData {
  dayLabel: string;
  dateLabel: string;
  isAvailable: boolean;
  allDay: boolean;
  startTime: string;
  endTime: string;
}

export interface AvailabilityDayDialogResult {
  isAvailable: boolean;
  allDay: boolean;
  startTime: string;
  endTime: string;
}

@Component({
  selector: 'app-availability-day-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatSlideToggleModule, MatFormFieldModule, MatInputModule],
  templateUrl: './availability-day-dialog.html',
  styleUrl: './availability-day-dialog.scss',
})
export class AvailabilityDayDialog {
  protected isAvailable: boolean;
  protected allDay: boolean;
  // Whatever text the employee has typed (e.g. "3:35 PM"), not necessarily
  // a valid parsed time yet — see parseTimeInput in week-utils.ts, run
  // fresh on both fields at save() time. Plain text, not <input type="time">
  // — that native picker's segments can visually show a complete value
  // while its own .value getter still reports "" (most often when typing
  // over a value that was already there), which is how Start/End have
  // silently collapsed to "All day" before.
  protected startTime: string;
  protected endTime: string;
  protected readonly timeSuggestions = TIME_SUGGESTIONS;

  // Set once the employee focuses Start/End — lets save() tell "never
  // touched, available all day" apart from "started typing a time but
  // didn't finish it", since both look identical as an empty string.
  protected startTouched = false;
  protected endTouched = false;

  constructor(
    private readonly dialogRef: MatDialogRef<AvailabilityDayDialog, AvailabilityDayDialogResult>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: AvailabilityDayDialogData,
  ) {
    this.isAvailable = data.isAvailable;
    this.allDay = data.allDay;
    this.startTime = formatDisplayTime(data.startTime);
    this.endTime = formatDisplayTime(data.endTime);
  }

  save(): void {
    if (!this.isAvailable || this.allDay) {
      this.dialogRef.close({
        isAvailable: this.isAvailable,
        allDay: this.allDay,
        startTime: '',
        endTime: '',
      });
      return;
    }

    const startTime = parseTimeInput(this.startTime);
    if (startTime === null) {
      alert('Start isn’t a valid time — try a format like 3:35 PM.');
      return;
    }
    const endTime = parseTimeInput(this.endTime);
    if (endTime === null) {
      alert('End isn’t a valid time — try a format like 3:35 PM.');
      return;
    }

    if (this.startTouched && !startTime) {
      if (!confirm('Start time is blank even though you edited it. Save anyway?')) {
        return;
      }
    }
    if (this.endTouched && !endTime) {
      if (!confirm('End time is blank even though you edited it. Save anyway?')) {
        return;
      }
    }

    this.dialogRef.close({
      isAvailable: this.isAvailable,
      allDay: this.allDay,
      startTime,
      endTime,
    });
  }

  cancel(): void {
    this.dialogRef.close();
  }
}
