import { Component, Inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import { TimeEntryDto } from '../../../core/time-entries-api';
import { BreakKind, ScheduledBreakDto } from '../../../core/shifts-api';
import { formatHHmm } from '../../../core/week-utils';

export interface EditTimeEntryDialogData {
  employeeName: string;
  entry: TimeEntryDto | null;
  // Shown as read-only reference at the top of the dialog so the admin can
  // see what was scheduled while entering what actually happened.
  scheduledBreaks: ScheduledBreakDto[];
}

interface SegmentRow {
  kind: BreakKind;
  start: string;
  end: string;
  // Set once the admin focuses the End field — lets save() tell "never
  // touched, still on this break" apart from "started typing an end time
  // but didn't finish it", since both look identical as an empty string.
  endTouched: boolean;
}

export interface EditTimeEntryResult {
  clockInAt: string;
  clockOutAt: string | null;
  segments: { kind: BreakKind; start: string; end: string | null }[];
  note: string;
}

const toInput = (iso: string | null): string => (iso ? formatHHmm(new Date(iso)) : '');

const DEFAULT_WINDOW: Record<BreakKind, { start: string; end: string }> = {
  Break: { start: '10:00', end: '10:15' },
  Lunch: { start: '12:00', end: '12:30' },
};

// Lets an admin set every punch on today's TimeEntry directly — correcting
// a mistake, or filling one in from scratch when the employee never
// clocked in at all (data.entry is null in that case). Any number of
// break/lunch segments, mirroring the same add/remove list editor as
// ShiftsManager's scheduled breaks. Only Clock In is required. Reuses the
// same required-note convention as NoteDialog.
@Component({
  selector: 'app-edit-time-entry-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule],
  templateUrl: './edit-time-entry-dialog.html',
  styleUrl: './edit-time-entry-dialog.scss',
})
export class EditTimeEntryDialog {
  protected clockInAt: string;
  protected clockOutAt: string;
  protected segments: SegmentRow[];
  protected note = '';
  protected error: string | null = null;

  // Same reasoning as SegmentRow.endTouched — Clock Out is optional (someone
  // still clocked in has none), so an empty value is only worth confirming
  // when the admin actually put their cursor there.
  protected clockOutTouched = false;

  constructor(
    private readonly dialogRef: MatDialogRef<EditTimeEntryDialog, EditTimeEntryResult>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: EditTimeEntryDialogData,
  ) {
    const entry = data.entry;
    this.clockInAt = toInput(entry?.clockInAt ?? null) || formatHHmm(new Date());
    this.clockOutAt = toInput(entry?.clockOutAt ?? null);
    // Nobody's punched in yet, so there's nothing actually recorded to
    // default to — start from what was scheduled instead of an empty list,
    // since that's almost always what actually happens. Once real
    // clock/segment data exists, it takes over as the source of truth and
    // scheduledBreaks goes back to being just the reference line above.
    this.segments = entry
      ? entry.segments
          .slice()
          .sort((a, b) => a.startAt.localeCompare(b.startAt))
          .map((s) => ({ kind: s.kind, start: toInput(s.startAt), end: toInput(s.endAt), endTouched: false }))
      : data.scheduledBreaks
          .slice()
          .sort((a, b) => a.startTime.localeCompare(b.startTime))
          .map((b) => ({ kind: b.kind, start: b.startTime.slice(0, 5), end: b.endTime.slice(0, 5), endTouched: false }));
  }

  addSegment(kind: BreakKind): void {
    const { start, end } = DEFAULT_WINDOW[kind];
    this.segments = [...this.segments, { kind, start, end, endTouched: false }];
  }

  removeSegment(index: number): void {
    this.segments = this.segments.filter((_, i) => i !== index);
  }

  save(): void {
    this.error = null;

    if (!this.clockInAt) {
      this.error = 'Clock in time is required.';
      return;
    }
    if (!this.note.trim()) {
      this.error = 'A note explaining the edit is required.';
      return;
    }

    for (const s of this.segments) {
      if (!s.start) {
        this.error = `${s.kind} needs a start time.`;
        return;
      }
      if (s.end && s.end < s.start) {
        this.error = `${s.kind} end can't be before its start.`;
        return;
      }
    }

    const sorted = [...this.segments].sort((a, b) => a.start.localeCompare(b.start));
    for (let i = 0; i < sorted.length; i++) {
      for (let j = i + 1; j < sorted.length; j++) {
        const aEnd = sorted[i].end || '23:59';
        const bEnd = sorted[j].end || '23:59';
        if (sorted[i].start < bEnd && sorted[j].start < aEnd) {
          this.error = 'Breaks and lunches can’t overlap.';
          return;
        }
      }
    }

    if (this.clockOutAt && this.clockOutAt < this.clockInAt) {
      this.error = "Clock out can't be before clock in.";
      return;
    }

    // Clock Out and segment End are both optional — someone still clocked
    // in, or still on their current break, legitimately has none. But a
    // native <input type="time"> reports '' for a half-typed value (e.g.
    // hour and minute set but AM/PM never touched) exactly the same as for
    // "never touched", so a blank value the admin actually clicked into is
    // worth a confirmation rather than silently saving as "still clocked
    // in" — that's how Clock Out times have gone missing before.
    if (this.clockOutTouched && !this.clockOutAt) {
      if (!confirm("Clock Out is blank even though you edited it. Save without a clock-out time?")) {
        return;
      }
    }
    for (const s of this.segments) {
      if (s.endTouched && !s.end) {
        if (!confirm(`${s.kind} end time is blank even though you edited it. Save without an end time?`)) {
          return;
        }
      }
    }

    this.dialogRef.close({
      clockInAt: this.clockInAt,
      clockOutAt: this.clockOutAt || null,
      segments: this.segments.map((s) => ({ kind: s.kind, start: s.start, end: s.end || null })),
      note: this.note.trim(),
    });
  }

  cancel(): void {
    this.dialogRef.close();
  }

  clearClockOut(): void {
    this.clockOutAt = '';
    // An explicit clear is unambiguous — no need to confirm it again in save().
    this.clockOutTouched = false;
  }

  get scheduledLabel(): string {
    if (this.data.scheduledBreaks.length === 0) {
      return 'None scheduled for this shift.';
    }
    return this.data.scheduledBreaks
      .map((b) => `${b.kind} ${b.startTime.slice(0, 5)}–${b.endTime.slice(0, 5)}`)
      .join(', ');
  }
}
