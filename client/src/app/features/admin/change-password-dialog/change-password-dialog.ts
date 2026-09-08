import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { firstValueFrom } from 'rxjs';

import { AccountsApi } from '../../../core/accounts-api';

// Self-service password change for Admin/Lead/Sa accounts — anyone who
// actually logs in with a password, unlike an Employee's random unknown one
// (see AccountsController.ChangeMyPassword).
@Component({
  selector: 'app-change-password-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './change-password-dialog.html',
  styleUrl: './change-password-dialog.scss',
})
export class ChangePasswordDialog {
  protected currentPassword = '';
  protected newPassword = '';
  protected confirmPassword = '';
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor(
    private readonly dialogRef: MatDialogRef<ChangePasswordDialog, boolean>,
    private readonly accountsApi: AccountsApi,
  ) {}

  async save(): Promise<void> {
    if (!this.newPassword) {
      this.error.set('New password is required.');
      return;
    }
    if (this.newPassword !== this.confirmPassword) {
      this.error.set('New password and confirmation do not match.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(
        this.accountsApi.changeMyPassword({
          currentPassword: this.currentPassword,
          newPassword: this.newPassword,
        }),
      );
      this.dialogRef.close(true);
    } catch (err) {
      this.error.set((err as { error?: string })?.error ?? 'Failed to change your password. Try again.');
    } finally {
      this.saving.set(false);
    }
  }

  cancel(): void {
    this.dialogRef.close();
  }
}
