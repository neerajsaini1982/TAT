import { Component, Inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';

export interface PublishScheduleDialogData {
  weekRangeLabel: string;
}

// Confirms publishing the week's schedule and lets the admin opt into
// emailing every scheduled employee (SchedulePublished template) in the
// same step, rather than a bare window.confirm().
@Component({
  selector: 'app-publish-schedule-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatCheckboxModule],
  templateUrl: './publish-schedule-dialog.html',
  styleUrl: './publish-schedule-dialog.scss',
})
export class PublishScheduleDialog {
  protected sendEmail = true;

  constructor(
    private readonly dialogRef: MatDialogRef<PublishScheduleDialog, boolean>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: PublishScheduleDialogData,
  ) {}

  confirm(): void {
    this.dialogRef.close(this.sendEmail);
  }

  cancel(): void {
    this.dialogRef.close();
  }
}
