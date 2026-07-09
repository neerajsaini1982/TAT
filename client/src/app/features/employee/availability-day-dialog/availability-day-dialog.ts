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
