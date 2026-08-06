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

  constructor(
    private readonly dialogRef: MatDialogRef<MyAccountDialog, AccountDto>,
    private readonly accountsApi: AccountsApi,
    @Inject(MAT_DIALOG_DATA) protected readonly data: AccountDto,
  ) {
    this.email = data.email;
    this.phone = data.phone;
  }

  async save(): Promise<void> {
    this.saving.set(true);
    this.error.set(null);
    try {
      const account = await firstValueFrom(
        this.accountsApi.updateMine({ email: this.email.trim(), phone: this.phone.trim() }),
      );
      this.dialogRef.close(account);
    } catch {
      this.error.set('Failed to save your changes. Try again.');
    } finally {
      this.saving.set(false);
    }
  }

  cancel(): void {
    this.dialogRef.close();
  }
}
