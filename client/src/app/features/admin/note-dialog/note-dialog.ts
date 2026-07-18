import { Component, Inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

export interface NoteDialogData {
  title: string;
  label: string;
  noteRequired: boolean;
  confirmLabel: string;
}

// Small reusable "explain yourself" dialog — same shape whether it's an
// absence note or an admin clock-out reason, so both reuse one component
// instead of two near-identical dialogs.
@Component({
  selector: 'app-note-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './note-dialog.html',
  styleUrl: './note-dialog.scss',
})
export class NoteDialog {
  protected note = '';

  constructor(
    private readonly dialogRef: MatDialogRef<NoteDialog, string>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: NoteDialogData,
  ) {}

  save(): void {
    this.dialogRef.close(this.note.trim());
  }

  cancel(): void {
    this.dialogRef.close();
  }
}
