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
import { TIME_SUGGESTIONS, formatDisplayTime, formatHHmm, isPastDate, parseTimeInput } from '../../../core/week-utils';

export interface EditTimeEntryDialogData {
  employeeName: string;
  entry: TimeEntryDto | null;
  // Shown as read-only reference at the top of the dialog so the admin can
  // see what was scheduled while entering what actually happened. Also
  // doubles as the default Clock In/Out when nobody's punched in at all yet
  // (see the constructor) — the same "start from what was scheduled"
  // treatment scheduledBreaks already gets below.
  scheduledBreaks: ScheduledBreakDto[];
  shiftStartTime: string;
  shiftEndTime: string;
  // The shift's own date (not today's) — "still clocked in" only makes
  // sense for today; a blank Clock Out on a day that's already over is
  // never correct, so save() blocks on it below regardless of whether the
  // admin actually touched the field (see isPastDate usage in save()).
  date: string;
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

const toInput = (iso: string | null): string => (iso ? formatDisplayTime(formatHHmm(new Date(iso))) : '');

const DEFAULT_WINDOW: Record<BreakKind, { start: string; end: string }> = {
  Break: { start: '10:00 AM', end: '10:15 AM' },
  Lunch: { start: '12:00 PM', end: '12:30 PM' },
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
  // These hold whatever text the admin has typed (e.g. "3:35 PM"), not
  // necessarily a valid parsed time yet — see parseTimeInput, run fresh on
  // every field at save() time rather than trusted from some earlier parse.
  protected clockInAt: string;
  protected clockOutAt: string;
  protected segments: SegmentRow[];
  protected note = '';
  protected error: string | null = null;
  protected readonly timeSuggestions = TIME_SUGGESTIONS;

  // Same reasoning as SegmentRow.endTouched — Clock Out is optional (someone
  // still clocked in has none), so an empty value is only worth confirming
  // when the admin actually put their cursor there.
  protected clockOutTouched = false;

  constructor(
    private readonly dialogRef: MatDialogRef<EditTimeEntryDialog, EditTimeEntryResult>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: EditTimeEntryDialogData,
  ) {
    const entry = data.entry;
    // Nobody's punched in yet, so there's nothing actually recorded to
    // default to — start from what was scheduled (shift start/end here,
    // scheduledBreaks below) instead of blank fields, since that's almost
    // always what actually happens and the admin only needs to adjust it
    // rather than build it from scratch. Once a real clock-in exists, it
    // and whatever clock-out/segments came with it take over as the source
    // of truth entirely — including a still-blank Clock Out, since that
    // genuinely means still clocked in rather than "not filled in yet".
    this.clockInAt = entry ? toInput(entry.clockInAt) : formatDisplayTime(data.shiftStartTime.slice(0, 5));
    this.clockOutAt = entry ? toInput(entry.clockOutAt) : formatDisplayTime(data.shiftEndTime.slice(0, 5));
    this.segments = entry
      ? entry.segments
          .slice()
          .sort((a, b) => a.startAt.localeCompare(b.startAt))
          .map((s) => ({ kind: s.kind, start: toInput(s.startAt), end: toInput(s.endAt), endTouched: false }))
      : data.scheduledBreaks
          .slice()
          .sort((a, b) => a.startTime.localeCompare(b.startTime))
          .map((b) => ({
            kind: b.kind,
            start: formatDisplayTime(b.startTime.slice(0, 5)),
            end: formatDisplayTime(b.endTime.slice(0, 5)),
            endTouched: false,
          }));
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

    const clockInAt = parseTimeInput(this.clockInAt);
    if (clockInAt === null) {
      this.error = 'Clock in isn’t a valid time — try a format like 3:35 PM.';
      return;
    }
    if (!clockInAt) {
      this.error = 'Clock in time is required.';
      return;
    }

    const clockOutAt = parseTimeInput(this.clockOutAt);
    if (clockOutAt === null) {
      this.error = 'Clock out isn’t a valid time — try a format like 3:35 PM.';
      return;
    }

    if (!this.note.trim()) {
      this.error = 'A note explaining the edit is required.';
      return;
    }

    const parsedSegments: { kind: BreakKind; start: string; end: string; endTouched: boolean }[] = [];
    for (const s of this.segments) {
      const start = parseTimeInput(s.start);
      if (start === null) {
        this.error = `${s.kind} start isn’t a valid time — try a format like 3:35 PM.`;
        return;
      }
      if (!start) {
        this.error = `${s.kind} needs a start time.`;
        return;
      }
      const end = parseTimeInput(s.end);
      if (end === null) {
        this.error = `${s.kind} end isn’t a valid time — try a format like 3:35 PM.`;
        return;
      }
      if (end && end < start) {
        this.error = `${s.kind} end can't be before its start.`;
        return;
      }
      parsedSegments.push({ kind: s.kind, start, end, endTouched: s.endTouched });
    }

    const sorted = [...parsedSegments].sort((a, b) => a.start.localeCompare(b.start));
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

    if (clockOutAt && clockOutAt < clockInAt) {
      this.error = "Clock out can't be before clock in.";
      return;
    }

    // Clock Out and segment End are both optional — someone still clocked
    // in, or still on their current break, legitimately has none — so a
    // blank value only needs confirming when the admin actually put their
    // cursor there, or when the shift is from a day that's already over
    // (see isPastDate below): "still clocked in" only makes sense for today.
    if (this.clockOutTouched && !clockOutAt) {
      if (!confirm('Clock Out is blank. Save without a clock-out time?')) {
        return;
      }
    } else if (!clockOutAt && isPastDate(this.data.date)) {
      if (!confirm('This shift is from a past day. Leaving Clock Out blank will show as still clocked in on reports. Save without a clock-out time?')) {
        return;
      }
    }
    for (const s of parsedSegments) {
      if (s.endTouched && !s.end) {
        if (!confirm(`${s.kind} end time is blank. Save without an end time?`)) {
          return;
        }
      }
    }

    this.dialogRef.close({
      clockInAt,
      clockOutAt: clockOutAt || null,
      segments: parsedSegments.map((s) => ({ kind: s.kind, start: s.start, end: s.end || null })),
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

  clearSegmentEnd(segment: SegmentRow): void {
    segment.end = '';
    segment.endTouched = false;
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
