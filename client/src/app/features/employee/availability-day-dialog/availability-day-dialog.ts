import { Component, Inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

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
  protected startTime: string;
  protected endTime: string;

  // Set once the employee focuses Start/End — a native <input type="time">
  // reports '' for a half-typed value (e.g. hour and minute set but AM/PM
  // never touched) exactly the same as "never touched", and blank Start/End
  // here is silently read as "All day" by the pages that consume this
  // dialog's result, so save() confirms rather than saving that silently.
  protected startTouched = false;
  protected endTouched = false;

  constructor(
    private readonly dialogRef: MatDialogRef<AvailabilityDayDialog, AvailabilityDayDialogResult>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: AvailabilityDayDialogData,
  ) {
    this.isAvailable = data.isAvailable;
    this.allDay = data.allDay;
    this.startTime = data.startTime;
    this.endTime = data.endTime;
  }

  save(): void {
    if (this.isAvailable && !this.allDay) {
      if (this.startTouched && !this.startTime) {
        if (!confirm('Start time is blank even though you edited it. Save anyway?')) {
          return;
        }
      }
      if (this.endTouched && !this.endTime) {
        if (!confirm('End time is blank even though you edited it. Save anyway?')) {
          return;
        }
      }
    }

    this.dialogRef.close({
      isAvailable: this.isAvailable,
      allDay: this.allDay,
      startTime: this.startTime,
      endTime: this.endTime,
    });
  }

  cancel(): void {
    this.dialogRef.close();
  }
}
