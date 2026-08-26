import { Component, Inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

export interface SetKioskPinDialogData {
  employeeName: string;
}

const PIN_PATTERN = /^\d{4}$/;

// Requires the new PIN to be typed twice and match before Save enables —
// there's no "show password" toggle on a 4-digit PIN field, so a typo would
// otherwise silently lock the employee out until someone notices.
//
// pinValid/mismatch/canSave are plain methods, not computed() — pin/
// confirmPin are ngModel-bound plain fields, not signals, so a computed()
// reading them would never see Angular's change detection re-run it (it
// only reacts to signal reads) and would freeze at its first-ever value.
// Plain methods re-evaluate on every change-detection pass instead, which
// ngModel's (ngModelChange) already triggers.
@Component({
  selector: 'app-set-kiosk-pin-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './set-kiosk-pin-dialog.html',
  styleUrl: './set-kiosk-pin-dialog.scss',
})
export class SetKioskPinDialog {
  protected pin = '';
  protected confirmPin = '';
  protected touched = false;

  constructor(
    private readonly dialogRef: MatDialogRef<SetKioskPinDialog, string>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: SetKioskPinDialogData,
  ) {}

  protected pinValid(): boolean {
    return PIN_PATTERN.test(this.pin);
  }

  protected mismatch(): boolean {
    return this.touched && this.confirmPin.length === 4 && this.pin !== this.confirmPin;
  }

  protected canSave(): boolean {
    return this.pinValid() && this.confirmPin === this.pin;
  }

  onConfirmChange(): void {
    this.touched = true;
  }

  save(): void {
    if (this.canSave()) {
      this.dialogRef.close(this.pin);
    }
  }

  cancel(): void {
    this.dialogRef.close();
  }
}
