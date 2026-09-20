import { Component, Input, OnChanges, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCardModule } from '@angular/material/card';

import {
  DEFAULT_WRITE_UP_SEVERITY,
  WRITE_UP_MAX_DESCRIPTION_LENGTH,
  WRITE_UP_SEVERITIES,
  WriteUpDto,
  WriteUpSeverity,
  WriteUpsApi,
} from '../../../core/write-ups-api';
import { formatDate } from '../../../core/week-utils';

// Write-ups / warnings for one employee — used both by an admin
// (canManage: add, edit, delete) and by the employee viewing their own
// (canManage = false: read-only).
@Component({
  selector: 'app-write-ups-manager',
  imports: [
    DatePipe,
    FormsModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatCardModule,
  ],
  templateUrl: './write-ups-manager.html',
  styleUrl: './write-ups-manager.scss',
})
export class WriteUpsManager implements OnChanges {
  @Input({ required: true }) accountId!: number;
  @Input() canManage = false;

  private readonly api = inject(WriteUpsApi);

  protected readonly writeUps = signal<WriteUpDto[]>([]);
  protected readonly loaded = signal(false);
  protected readonly showForm = signal(false);
  protected readonly editingId = signal<number | null>(null);
  protected readonly saving = signal(false);
  protected readonly deletingId = signal<number | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly severities = WRITE_UP_SEVERITIES;
  protected readonly maxDescriptionLength = WRITE_UP_MAX_DESCRIPTION_LENGTH;

  protected get columns(): string[] {
    const columns = ['date', 'severity', 'description', 'createdBy'];
    return this.canManage ? [...columns, 'actions'] : columns;
  }

  protected form = this.blankForm();

  ngOnChanges(): void {
    if (this.accountId) {
      this.load();
    }
  }

  load(): void {
    this.api.list(this.accountId).subscribe((writeUps) => {
      this.writeUps.set(writeUps);
      this.loaded.set(true);
    });
  }

  startAdd(): void {
    this.form = this.blankForm();
    this.editingId.set(null);
    this.error.set(null);
    this.showForm.set(true);
  }

  startEdit(writeUp: WriteUpDto): void {
    this.form = { date: writeUp.date, description: writeUp.description, severity: writeUp.severity };
    this.editingId.set(writeUp.id);
    this.error.set(null);
    this.showForm.set(true);
  }

  cancelForm(): void {
    this.showForm.set(false);
    this.editingId.set(null);
  }

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

    const request = { date: this.form.date, description, severity: this.form.severity };
    const editingId = this.editingId();
    const call =
      editingId === null
        ? this.api.create(this.accountId, request)
        : this.api.update(this.accountId, editingId, request);

    this.saving.set(true);
    this.error.set(null);
    call.subscribe({
      next: () => {
        this.saving.set(false);
        this.cancelForm();
        this.load();
      },
      error: (err) => {
        this.saving.set(false);
        this.error.set(errorMessage(err, 'Failed to save write-up.'));
      },
    });
  }

  remove(writeUp: WriteUpDto): void {
    if (!confirm(`Delete the write-up from ${writeUp.date}? This cannot be undone.`)) {
      return;
    }
    this.deletingId.set(writeUp.id);
    this.api.remove(this.accountId, writeUp.id).subscribe({
      next: () => {
        this.deletingId.set(null);
        this.load();
      },
      error: (err) => {
        this.deletingId.set(null);
        alert(errorMessage(err, 'Failed to delete write-up.'));
      },
    });
  }

  private blankForm(): { date: string; description: string; severity: WriteUpSeverity } {
    return { date: formatDate(new Date()), description: '', severity: DEFAULT_WRITE_UP_SEVERITY };
  }
}

// The API returns a plain string for its own validation errors, but a
// ProblemDetails object (framework 400/404s) or an empty body (401/403,
// or a server that predates the endpoint) otherwise — so only a string is
// shown as-is, and the status explains the rest.
function errorMessage(err: { status?: number; error?: unknown }, fallback: string): string {
  if (typeof err?.error === 'string' && err.error) {
    return err.error;
  }
  switch (err?.status) {
    case 0:
      return `${fallback} Could not reach the server.`;
    case 401:
      return `${fallback} Your session has expired — sign in again.`;
    case 403:
      return `${fallback} Only an admin can add, edit or delete write-ups.`;
    case 404:
    case 405:
      return `${fallback} The server doesn't have this feature yet — restart it so it picks up the latest version.`;
    default:
      return fallback;
  }
}
