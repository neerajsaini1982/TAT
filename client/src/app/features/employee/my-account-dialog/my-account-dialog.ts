import { Component, Inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { firstValueFrom } from 'rxjs';

import { AccountDto, AccountsApi } from '../../../core/accounts-api';

@Component({
  selector: 'app-my-account-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './my-account-dialog.html',
  styleUrl: './my-account-dialog.scss',
})
export class MyAccountDialog {
  protected email: string;
  protected phone: string;
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  // Login code: userCode tracks the current value shown at the top (kept in
  // sync after a reset or a custom save); customCode is just the scratch
  // input the employee is typing a replacement into — it's only applied
  // when they hit the main Save button (see save()), not on its own.
  protected readonly userCode = signal<string | null>(null);
  protected customCode = '';
  protected readonly resettingCode = signal(false);

  constructor(
    private readonly dialogRef: MatDialogRef<MyAccountDialog, AccountDto>,
    private readonly accountsApi: AccountsApi,
    @Inject(MAT_DIALOG_DATA) protected readonly data: AccountDto,
  ) {
    this.email = data.email;
    this.phone = data.phone;
    this.userCode.set(data.userCode);
  }

  // Saves the profile fields, and — only if the employee typed something
  // into "Set Your Own Code" — the new login code too, as one action behind
  // the single Save button.
  async save(): Promise<void> {
    const code = this.customCode.trim();
    if (code && !/^\d{6}$/.test(code)) {
      this.error.set('Your code must be exactly 6 digits.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    try {
      let account = await firstValueFrom(
        this.accountsApi.updateMine({ email: this.email.trim(), phone: this.phone.trim() }),
      );
      if (code) {
        account = await firstValueFrom(this.accountsApi.setMyCode(code));
      }
      this.dialogRef.close(account);
    } catch (err) {
      this.error.set((err as { error?: string })?.error ?? 'Failed to save your changes. Try again.');
    } finally {
      this.saving.set(false);
    }
  }

  async resetCode(): Promise<void> {
    if (!confirm('Reset your login code? Your current code will stop working immediately.')) {
      return;
    }
    this.resettingCode.set(true);
    this.error.set(null);
    try {
      const account = await firstValueFrom(this.accountsApi.resetMyCode());
      this.userCode.set(account.userCode);
      this.customCode = '';
    } catch {
      this.error.set('Failed to reset your code. Try again.');
    } finally {
      this.resettingCode.set(false);
    }
  }

  // Keeps the scratch input digits-only as the employee types, rather than
  // waiting until save()'s validation to reject letters/symbols.
  sanitizeCode(value: string): string {
    return value.replace(/\D/g, '').slice(0, 6);
  }

  cancel(): void {
    this.dialogRef.close();
  }
}
