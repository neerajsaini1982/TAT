import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import { Auth } from '../../../core/auth';

export interface LocationCodeDialogData {
  portal: 'admin' | 'employee';
}

// Asks a signed-out visitor for their location code before sending them to
// the Admin or Employee login screen (see the toolbar buttons in app.html) —
// those routes are keyed by locationCode with no directory of them, so this
// checks GET /api/auth/location-exists first rather than bouncing the user
// to a login page for a location that doesn't exist.
@Component({
  selector: 'app-location-code-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  templateUrl: './location-code-dialog.html',
  styleUrl: './location-code-dialog.scss',
})
export class LocationCodeDialog {
  private readonly auth = inject(Auth);
  private readonly dialogRef = inject(MatDialogRef<LocationCodeDialog, string>);
  protected readonly data = inject<LocationCodeDialogData>(MAT_DIALOG_DATA);

  protected locationCode = '';
  protected readonly checking = signal(false);
  protected readonly error = signal<string | null>(null);

  protected get portalLabel(): string {
    return this.data.portal === 'admin' ? 'Admin' : 'Employee';
  }

  async continue(): Promise<void> {
    const code = this.locationCode.trim();
    if (!code) {
      this.error.set('Enter your location code.');
      return;
    }

    this.checking.set(true);
    this.error.set(null);
    try {
      const exists = await this.auth.locationExists(code);
      if (!exists) {
        this.error.set('Invalid location code.');
        return;
      }
      this.dialogRef.close(code.toLowerCase());
    } catch {
      this.error.set('Could not verify that location code. Try again.');
    } finally {
      this.checking.set(false);
    }
  }

  cancel(): void {
    this.dialogRef.close();
  }
}
