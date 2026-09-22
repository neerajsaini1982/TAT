import { Component, Inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';

export interface ViewKioskPinDialogData {
  employeeName: string;
  pin: string | null;
}

// Read-only — Admin-only lookup of an employee's existing kiosk PIN (see
// AccountsController.GetKioskPin). Lead can reset a PIN but not see this
// dialog; the caller (AccountsManager) is what actually gates who can open
// it, this just displays whatever was fetched.
@Component({
  selector: 'app-view-kiosk-pin-dialog',
  imports: [MatDialogModule, MatButtonModule],
  templateUrl: './view-kiosk-pin-dialog.html',
  styleUrl: './view-kiosk-pin-dialog.scss',
})
export class ViewKioskPinDialog {
  constructor(
    private readonly dialogRef: MatDialogRef<ViewKioskPinDialog>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: ViewKioskPinDialogData,
  ) {}

  close(): void {
    this.dialogRef.close();
  }
}
