import { Component, Inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Observable } from 'rxjs';

import { TimeEntryDto } from '../../../core/time-entries-api';

const PIN_LENGTH = 4;

export interface KioskPinDialogData {
  employeeName: string;
  // e.g. "Clock In", "Start Break", "Clock Out" — shown as the dialog's
  // subtitle so it's obvious what tapping this row is about to do.
  action: string;
  // Called once the employee has entered a full PIN. Owning the actual API
  // call from inside the dialog (rather than the caller reopening a fresh
  // dialog on failure) lets a wrong PIN show its error and let the employee
  // retry without losing the dialog.
  submit: (pin: string) => Observable<TimeEntryDto>;
}

// Large touch-friendly numeric keypad — this runs on a shared tablet, not
// typed on a keyboard. Auto-submits once PIN_LENGTH digits are entered.
@Component({
  selector: 'app-kiosk-pin-dialog',
  imports: [MatDialogModule, MatButtonModule, MatIconModule],
  templateUrl: './kiosk-pin-dialog.html',
  styleUrl: './kiosk-pin-dialog.scss',
})
export class KioskPinDialog {
  protected pin = '';
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly digits = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '', '0', 'backspace'];
  protected readonly pinDots = Array.from({ length: PIN_LENGTH });

  constructor(
    private readonly dialogRef: MatDialogRef<KioskPinDialog, TimeEntryDto>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: KioskPinDialogData,
  ) {}

  press(key: string): void {
    if (this.submitting()) {
      return;
    }
    if (key === 'backspace') {
      this.pin = this.pin.slice(0, -1);
      return;
    }
    if (!key || this.pin.length >= PIN_LENGTH) {
      return;
    }

    this.pin += key;
    this.error.set(null);
    if (this.pin.length === PIN_LENGTH) {
      this.trySubmit();
    }
  }

  private trySubmit(): void {
    this.submitting.set(true);
    this.data.submit(this.pin).subscribe({
      next: (entry) => this.dialogRef.close(entry),
      error: (err) => {
        this.submitting.set(false);
        this.pin = '';
        this.error.set(err?.error ?? 'Something went wrong.');
      },
    });
  }

  cancel(): void {
    this.dialogRef.close();
  }
}
