import { Component, Input, OnChanges, ViewChild, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCardModule } from '@angular/material/card';

import {
  DEFAULT_WRITE_UP_SEVERITY,
  DEFAULT_WRITE_UP_TYPE,
  WRITE_UP_MAX_DESCRIPTION_LENGTH,
  WRITE_UP_MAX_VOID_REASON_LENGTH,
  WRITE_UP_SEVERITIES,
  WRITE_UP_TYPES,
  WriteUpAcknowledgment,
  WriteUpDto,
  WriteUpEventAction,
  WriteUpSeverity,
  WriteUpType,
  WriteUpsApi,
  writeUpTypeLabel,
} from '../../../core/write-ups-api';
import { formatDate } from '../../../core/week-utils';
import { SignatureImage } from '../signature-image/signature-image';
import { SignaturePad } from '../signature-pad/signature-pad';
import { WriteUpDialog, WriteUpDialogData } from '../write-up-dialog/write-up-dialog';
import { Auth } from '../../../core/auth';

const EVENT_LABELS: Record<WriteUpEventAction, string> = {
  Created: 'Created',
  Edited: 'Edited',
  Voided: 'Voided',
  Acknowledged: 'Acknowledged by the employee',
  AcknowledgmentDeclined: 'Employee declined to acknowledge',
};

const STATUS_LABELS: Record<WriteUpAcknowledgment, string> = {
  Pending: 'Awaiting acknowledgment',
  Acknowledged: 'Acknowledged',
  Declined: 'Declined to acknowledge',
};

// Write-ups / warnings for one employee. Used two ways:
//  - canManage (an admin, on someone else's account): add, edit, void,
//    record a declined acknowledgment, and see each write-up's history.
//  - canAcknowledge (the employee viewing their own): read-only, plus an
//    Acknowledge button on each write-up still pending.
// Nothing is ever deleted — a mistaken write-up is voided with a reason.
@Component({
  selector: 'app-write-ups-manager',
  imports: [
    DatePipe,
    FormsModule,
    RouterLink,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatCardModule,
    SignaturePad,
    SignatureImage,
  ],
  templateUrl: './write-ups-manager.html',
  styleUrl: './write-ups-manager.scss',
})
export class WriteUpsManager implements OnChanges {
  @Input({ required: true }) accountId!: number;
  @Input() canManage = false;
  @Input() canAcknowledge = false;

  // The employee's full name as it is on their account — what they're asked
  // to type to acknowledge. The server checks it too; this only lets the
  // button wait until it looks right.
  @Input() signerName = '';

  // Shown on the Add popup's employee signature pad (canManage only).
  @Input() employeeName = '';

  private readonly api = inject(WriteUpsApi);
  private readonly dialog = inject(MatDialog);
  protected readonly locationCode = inject(Auth).locationCode();

  protected readonly writeUps = signal<WriteUpDto[]>([]);
  protected readonly loaded = signal(false);
  protected readonly showForm = signal(false);
  protected readonly editingId = signal<number | null>(null);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  // Row currently being acted on, so its buttons can't be double-clicked.
  protected readonly busyId = signal<number | null>(null);

  protected readonly voidingId = signal<number | null>(null);
  protected readonly voidError = signal<string | null>(null);
  protected voidReason = '';

  // The signing box, present only while the acknowledge panel is open.
  @ViewChild(SignaturePad) private signaturePad?: SignaturePad;

  protected readonly acknowledgingId = signal<number | null>(null);
  protected readonly acknowledgeError = signal<string | null>(null);
  protected typedName = '';

  protected readonly historyId = signal<number | null>(null);
  protected readonly historyFor = computed(() => this.writeUps().find((w) => w.id === this.historyId()) ?? null);

  protected readonly severities = WRITE_UP_SEVERITIES;
  protected readonly types = WRITE_UP_TYPES;
  protected readonly maxDescriptionLength = WRITE_UP_MAX_DESCRIPTION_LENGTH;
  protected readonly maxVoidReasonLength = WRITE_UP_MAX_VOID_REASON_LENGTH;
  protected readonly typeLabel = writeUpTypeLabel;

  protected readonly hasPending = computed(() =>
    this.writeUps().some((w) => !w.isVoided && w.acknowledgmentStatus !== 'Acknowledged'),
  );

  protected get columns(): string[] {
    const columns = ['date', 'type', 'severity', 'description', 'status', 'createdBy'];
    return this.canManage || this.canAcknowledge ? [...columns, 'actions'] : columns;
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

  eventLabel(action: WriteUpEventAction): string {
    return EVENT_LABELS[action];
  }

  statusLabel(status: WriteUpAcknowledgment): string {
    return STATUS_LABELS[status];
  }

  // Adding goes through the same popup as the schedule screen, which carries
  // the optional in-person signature pads; editing stays inline below.
  startAdd(): void {
    this.closePanels();
    this.dialog
      .open<WriteUpDialog, WriteUpDialogData>(WriteUpDialog, {
        data: { accountId: this.accountId, employeeName: this.employeeName },
      })
      .afterClosed()
      .subscribe((created) => {
        if (created) {
          this.load();
        }
      });
  }

  startEdit(writeUp: WriteUpDto): void {
    this.closePanels();
    this.form = {
      date: writeUp.date,
      description: writeUp.description,
      severity: writeUp.severity,
      type: writeUp.type,
    };
    this.editingId.set(writeUp.id);
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

    const request = {
      date: this.form.date,
      description,
      severity: this.form.severity,
      type: this.form.type,
    };
    const editingId = this.editingId();
    if (editingId === null) {
      return;
    }

    this.saving.set(true);
    this.error.set(null);
    this.api.update(this.accountId, editingId, request).subscribe({
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

  startVoid(writeUp: WriteUpDto): void {
    this.closePanels();
    this.voidReason = '';
    this.voidingId.set(writeUp.id);
  }

  cancelVoid(): void {
    this.voidingId.set(null);
  }

  confirmVoid(): void {
    const id = this.voidingId();
    const reason = this.voidReason.trim();
    if (id === null) {
      return;
    }
    if (!reason) {
      this.voidError.set('Enter a reason for voiding this write-up.');
      return;
    }

    this.saving.set(true);
    this.voidError.set(null);
    this.api.void(this.accountId, id, reason).subscribe({
      next: () => {
        this.saving.set(false);
        this.voidingId.set(null);
        this.load();
      },
      error: (err) => {
        this.saving.set(false);
        this.voidError.set(errorMessage(err, 'Failed to void write-up.'));
      },
    });
  }

  toggleHistory(writeUp: WriteUpDto): void {
    this.historyId.set(this.historyId() === writeUp.id ? null : writeUp.id);
  }

  startAcknowledge(writeUp: WriteUpDto): void {
    this.closePanels();
    this.typedName = '';
    this.acknowledgingId.set(writeUp.id);
  }

  cancelAcknowledge(): void {
    this.acknowledgingId.set(null);
  }

  // Same rule as the server: case and extra spaces don't matter.
  protected nameMatches(): boolean {
    const typed = normalizeName(this.typedName).toLowerCase();
    return typed !== '' && typed === normalizeName(this.signerName).toLowerCase();
  }

  confirmAcknowledge(): void {
    const id = this.acknowledgingId();
    if (id === null) {
      return;
    }
    if (!this.nameMatches()) {
      this.acknowledgeError.set(`Type your full name as "${normalizeName(this.signerName)}" to acknowledge.`);
      return;
    }
    const pad = this.signaturePad;
    if (!pad?.hasInk()) {
      this.acknowledgeError.set('Sign in the box to acknowledge.');
      return;
    }

    this.saving.set(true);
    this.acknowledgeError.set(null);
    this.api.acknowledge(this.accountId, id, this.typedName, pad.toDataUrl()).subscribe({
      next: () => {
        this.saving.set(false);
        this.acknowledgingId.set(null);
        this.load();
      },
      error: (err) => {
        this.saving.set(false);
        this.acknowledgeError.set(errorMessage(err, 'Failed to acknowledge write-up.'));
      },
    });
  }

  recordDeclined(writeUp: WriteUpDto): void {
    const ok = confirm(
      `Record that this employee declined to acknowledge the write-up from ${writeUp.date}? ` +
        'They can still acknowledge it later.',
    );
    if (ok) {
      this.run(writeUp, this.api.recordDeclined(this.accountId, writeUp.id), 'Failed to record the decline.');
    }
  }

  private run(writeUp: WriteUpDto, call: ReturnType<WriteUpsApi['recordDeclined']>, fallback: string): void {
    this.busyId.set(writeUp.id);
    call.subscribe({
      next: () => {
        this.busyId.set(null);
        this.load();
      },
      error: (err) => {
        this.busyId.set(null);
        alert(errorMessage(err, fallback));
      },
    });
  }

  // Only one panel (add/edit form, void form, history) is open at a time.
  private closePanels(): void {
    this.cancelForm();
    this.voidingId.set(null);
    this.acknowledgingId.set(null);
    this.historyId.set(null);
    this.error.set(null);
    this.voidError.set(null);
    this.acknowledgeError.set(null);
  }

  private blankForm(): { date: string; description: string; severity: WriteUpSeverity; type: WriteUpType } {
    return {
      date: formatDate(new Date()),
      description: '',
      severity: DEFAULT_WRITE_UP_SEVERITY,
      type: DEFAULT_WRITE_UP_TYPE,
    };
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
      return `${fallback} You don't have permission to do that.`;
    case 404:
    case 405:
      return `${fallback} The server doesn't have this feature yet — restart it so it picks up the latest version.`;
    default:
      return fallback;
  }
}

// Collapses any run of whitespace to one space and trims.
function normalizeName(name: string): string {
  return name.split(/\s+/).filter(Boolean).join(' ');
}
