import { Component, computed, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';

import { ShiftAssignmentsApi } from '../../../core/shift-assignments-api';

export interface SwapShiftOption {
  assignmentId: number;
  shiftId: number;
  employeeName: string;
  shiftLabel: string;
}

export interface SwapShiftDialogData {
  dateLabel: string;
  options: SwapShiftOption[];
}

// Admin picks two employees working the same day and trades their shifts.
// Closes with true once the swap is saved.
@Component({
  selector: 'app-swap-shift-dialog',
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatSelectModule],
  templateUrl: './swap-shift-dialog.html',
  styleUrl: './swap-shift-dialog.scss',
})
export class SwapShiftDialog {
  private readonly api = inject(ShiftAssignmentsApi);
  private readonly dialogRef = inject<MatDialogRef<SwapShiftDialog, boolean>>(MatDialogRef);
  protected readonly data = inject<SwapShiftDialogData>(MAT_DIALOG_DATA);

  protected readonly firstId = signal<number | null>(null);
  protected readonly secondId = signal<number | null>(null);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly first = computed(() => this.data.options.find((o) => o.assignmentId === this.firstId()) ?? null);
  protected readonly second = computed(() => this.data.options.find((o) => o.assignmentId === this.secondId()) ?? null);

  // Second list leaves out the first pick and anyone already on the same shift.
  protected readonly secondOptions = computed(() => {
    const first = this.first();
    return this.data.options.filter((o) => !first || (o.assignmentId !== first.assignmentId && o.shiftId !== first.shiftId));
  });

  setFirst(id: number): void {
    this.firstId.set(id);
    if (!this.secondOptions().some((o) => o.assignmentId === this.secondId())) {
      this.secondId.set(null);
    }
    this.error.set(null);
  }

  setSecond(id: number): void {
    this.secondId.set(id);
    this.error.set(null);
  }

  swap(): void {
    const first = this.firstId();
    const second = this.secondId();
    if (first === null || second === null || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.api.swap(first, second).subscribe({
      next: () => this.dialogRef.close(true),
      error: (err) => {
        this.saving.set(false);
        this.error.set(typeof err?.error === 'string' ? err.error : 'Failed to swap shifts.');
      },
    });
  }

  cancel(): void {
    this.dialogRef.close(false);
  }
}
