import { Component, Inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

export interface ResetPasswordDialogData {
  accountName: string;
}

// Lets an admin (or Sa) set a new password directly on an account they
// manage — no current password needed, unlike ChangePasswordDialog, since
// the person resetting it isn't the account holder (see
// AccountsController.ResetPassword).
@Component({
  selector: 'app-reset-password-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './reset-password-dialog.html',
  styleUrl: './reset-password-dialog.scss',
})
export class ResetPasswordDialog {
  protected newPassword = '';
  protected confirmPassword = '';
  protected readonly error = signal<string | null>(null);

  constructor(
    private readonly dialogRef: MatDialogRef<ResetPasswordDialog, string>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: ResetPasswordDialogData,
  ) {}

  save(): void {
    if (!this.newPassword) {
      this.error.set('New password is required.');
      return;
    }
    if (this.newPassword !== this.confirmPassword) {
      this.error.set('New password and confirmation do not match.');
      return;
    }
    this.dialogRef.close(this.newPassword);
  }

  cancel(): void {
    this.dialogRef.close();
  }
}
