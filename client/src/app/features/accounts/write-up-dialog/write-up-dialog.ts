import { Component, DestroyRef, ViewChild, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import {
  DEFAULT_WRITE_UP_SEVERITY,
  DEFAULT_WRITE_UP_TYPE,
  WRITE_UP_MAX_DESCRIPTION_LENGTH,
  WRITE_UP_SEVERITIES,
  WRITE_UP_TYPES,
  WriteUpDto,
  WriteUpsApi,
} from '../../../core/write-ups-api';
import { formatDate } from '../../../core/week-utils';
import { Auth } from '../../../core/auth';
import { SignaturePad } from '../signature-pad/signature-pad';

export interface WriteUpDialogData {
  accountId: number;
  employeeName: string;
}

// Add-only write-up form, opened from the schedule screen and the admin's
// Write-Ups page, for anyone who can write a coworker up (Admin/Sa, or an
// account with CanWriteUpOthers). Both signature pads are optional — they're
// for delivering the write-up in person; the server stamps each with the
// signer's name and the time it was saved. An employee signature counts as
// their acknowledgment. It
// deliberately shows nothing about the employee's existing write-ups — a
// CanWriteUpOthers account can create one but not see any. Closes with the
// created write-up, or undefined on Cancel.
@Component({
  selector: 'app-write-up-dialog',
  imports: [DatePipe, FormsModule, SignaturePad, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  templateUrl: './write-up-dialog.html',
  styleUrl: './write-up-dialog.scss',
})
export class WriteUpDialog {
  private readonly api = inject(WriteUpsApi);
  private readonly dialogRef = inject<MatDialogRef<WriteUpDialog, WriteUpDto>>(MatDialogRef);
  protected readonly data = inject<WriteUpDialogData>(MAT_DIALOG_DATA);
  private readonly auth = inject(Auth);

  @ViewChild('employeePad') private readonly employeePad!: SignaturePad;
  @ViewChild('authorPad') private readonly authorPad!: SignaturePad;

  protected readonly authorName = `${this.auth.session()?.firstName ?? ''} ${this.auth.session()?.lastName ?? ''}`.trim();

  // The date/time shown under a signed pad. Only a preview — the server
  // stamps the real time when it saves.
  protected readonly now = signal(new Date());

  constructor() {
    const timer = setInterval(() => this.now.set(new Date()), 15_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected readonly types = WRITE_UP_TYPES;
  protected readonly severities = WRITE_UP_SEVERITIES;
  protected readonly maxDescriptionLength = WRITE_UP_MAX_DESCRIPTION_LENGTH;

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected form = {
    date: formatDate(new Date()),
    type: DEFAULT_WRITE_UP_TYPE,
    severity: DEFAULT_WRITE_UP_SEVERITY,
    description: '',
  };

  save(): void {
    const description = this.form.description.trim();
    if (!this.form.date) {
      this.error.set('Choose a date.');
      return;
    }
    if (!description) {
      this.error.set('Enter a description.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.api
      .create(this.data.accountId, {
        ...this.form,
        description,
        employeeSignature: this.employeePad.hasInk() ? this.employeePad.toDataUrl() : undefined,
        authorSignature: this.authorPad.hasInk() ? this.authorPad.toDataUrl() : undefined,
      })
      .subscribe({
        next: (writeUp) => this.dialogRef.close(writeUp),
        error: (err) => {
          this.saving.set(false);
          this.error.set(
            typeof err?.error === 'string' && err.error
              ? err.error
              : err?.status === 404
                ? "You don't have permission to write up this employee."
                : 'Failed to save write-up.',
          );
        },
      });
  }
}
