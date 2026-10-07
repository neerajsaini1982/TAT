import { Component, Inject, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { Observable } from 'rxjs';

import { Auth } from '../../../core/auth';
import { formatClockTime, toMinutes } from '../../../core/day-view-layout';
import {
  CallOutResultDto,
  CoverCandidateDto,
  CoverCandidatesDto,
  CoverShiftOptionDto,
  ShiftAssignmentDto,
  ShiftAssignmentsApi,
} from '../../../core/shift-assignments-api';

// 'call-out' marks the employee absent and optionally assigns cover in one
// save; 'cover' assigns, changes or removes cover on a shift that's already
// absent.
export interface CallOutDialogData {
  assignment: ShiftAssignmentDto;
  mode: 'call-out' | 'cover';
}

interface CandidateGroup {
  label: string;
  candidates: CoverCandidateDto[];
}

const NO_COVER = 0;
// The "New shift" choice that isn't one of the location's shifts: one
// combined shift spanning the employee's own and the one they're covering.
const COMBINE_BOTH = 0;

const durationLabel = (minutes: number): string => {
  const h = Math.floor(minutes / 60);
  const m = Math.round(minutes % 60);
  return m === 0 ? `${h}h` : `${h}h ${m}m`;
};

const timeRange = (start: string, end: string): string => `${formatClockTime(start)} – ${formatClockTime(end)}`;

// The one-step call-out (docs/tickets/call-out-cover.md): absence note,
// optional sick hours (admins only, as on the payroll report) and a cover
// picker, saved together. Anyone at the location can be picked; people who
// said they were unavailable or who already work that day carry a warning
// that has to be ticked off. Someone already working isn't given a second
// shift: their own is replaced by a single shift for the day, typed into a
// "New shift" box that works like the schedule builder's Add Shift (a start
// time or a name narrows the location's shifts). Left blank, the two shifts
// are simply combined.
@Component({
  selector: 'app-call-out-dialog',
  imports: [
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatRadioModule,
  ],
  templateUrl: './call-out-dialog.html',
  styleUrl: './call-out-dialog.scss',
})
export class CallOutDialog implements OnInit {
  private readonly api = inject(ShiftAssignmentsApi);
  private readonly auth = inject(Auth);

  protected readonly NO_COVER = NO_COVER;
  protected readonly isCallOut: boolean;
  protected readonly employeeName: string;
  protected readonly shiftLabel: string;
  // Recording sick hours is Admin/Sa-only server-side (see SetSickMinutes).
  protected readonly canRecordSickHours = this.auth.role() === 'Admin' || this.auth.role() === 'Sa';

  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  // Set once the save went through but something worth telling the user
  // didn't (the email) — the dialog then only offers Done.
  protected readonly savedNotice = signal<string | null>(null);
  protected readonly info = signal<CoverCandidatesDto | null>(null);

  protected note = '';
  protected sickHours: number | null = null;
  protected readonly selectedId = signal<number>(NO_COVER);
  protected readonly confirmed = signal(false);
  protected readonly sendEmail = signal(false);
  // What replaces the shift of someone already working: a location shift's
  // id, or COMBINE_BOTH. previewedShift is the choice the loaded candidates
  // (their new shift, hours and overtime) were worked out for.
  protected readonly COMBINE_BOTH = COMBINE_BOTH;
  protected readonly shiftChoice = signal<number>(COMBINE_BOTH);
  private previewedShift = COMBINE_BOTH;
  protected readonly refreshing = signal(false);

  protected readonly shiftQuery = signal('');
  protected readonly suggestionsOpen = signal(false);

  // Same matching as the schedule builder's Add Shift box: the shift's name,
  // or its start time with the colon optional ("1045" finds 10:45). A box
  // still showing the picked shift lists everything, ready to change it.
  protected readonly shiftMatches = computed<CoverShiftOptionDto[]>(() => {
    const shifts = this.info()?.shifts ?? [];
    const q = this.shiftQuery().trim().toLowerCase();
    if (!q || q === this.chosenShiftLabel()) {
      return shifts;
    }
    const digits = q.replace(/[^0-9]/g, '');
    return shifts.filter((s) => {
      if (s.name.toLowerCase().includes(q)) {
        return true;
      }
      const time = s.startTime.slice(0, 5);
      return time.startsWith(q) || (digits.length > 0 && time.replace(':', '').startsWith(digits));
    });
  });

  // What happens when the box is left blank.
  protected readonly combineHint = computed<string>(() => {
    const own = this.selected()?.ownShift;
    if (!own) {
      return '';
    }
    const a = this.data.assignment;
    const end = (start: string, finish: string) => (toMinutes(finish) > toMinutes(start) ? 0 : 1440) + toMinutes(finish);
    const starts = toMinutes(own.startTime) <= toMinutes(a.shiftStartTime) ? own.startTime : a.shiftStartTime;
    const ends = end(own.startTime, own.endTime) >= end(a.shiftStartTime, a.shiftEndTime) ? own.endTime : a.shiftEndTime;
    return `Leave blank to combine both shifts (${timeRange(starts, ends)}).`;
  });

  protected readonly groups = computed<CandidateGroup[]>(() => {
    const all = this.info()?.candidates ?? [];
    const working = all.filter((c) => c.ownShift !== null);
    const free = all.filter((c) => c.ownShift === null);
    return [
      { label: 'Available', candidates: free.filter((c) => c.isAvailable) },
      { label: 'Already working that day', candidates: working },
      { label: 'Marked unavailable', candidates: free.filter((c) => !c.isAvailable) },
    ].filter((g) => g.candidates.length > 0);
  });

  protected readonly selected = computed<CoverCandidateDto | null>(
    () => this.info()?.candidates.find((c) => c.accountId === this.selectedId()) ?? null,
  );


  // What the confirm tick acknowledges, or null when the pick needs none.
  protected readonly warning = computed<string | null>(() => {
    const c = this.selected();
    if (!c) {
      return null;
    }
    const unavailable = c.isAvailable ? '' : ` ${c.firstName} is marked unavailable that day.`;
    if (c.ownShift && c.combinedShift) {
      const own = timeRange(c.ownShift.startTime, c.ownShift.endTime);
      const combined = timeRange(c.combinedShift.startTime, c.combinedShift.endTime);
      return `Replace ${c.firstName}'s ${own} shift with one ${combined} shift.${unavailable}`;
    }
    return c.isAvailable ? null : `${c.firstName} is marked unavailable that day. Assign anyway.`;
  });

  protected readonly canEmail = computed(() => !!this.info()?.emailConfigured && !!this.selected()?.hasEmail);
  protected readonly emailHint = computed<string | null>(() => {
    if (!this.selected()) {
      return null;
    }
    if (!this.info()?.emailConfigured) {
      return 'Email is not set up for this location.';
    }
    return this.selected()!.hasEmail ? null : `${this.selected()!.firstName} has no email address on file.`;
  });

  constructor(
    private readonly dialogRef: MatDialogRef<CallOutDialog, boolean>,
    @Inject(MAT_DIALOG_DATA) protected readonly data: CallOutDialogData,
  ) {
    const a = data.assignment;
    this.isCallOut = data.mode === 'call-out';
    this.employeeName = `${a.accountFirstName} ${a.accountLastName}`;
    this.shiftLabel = `${a.shiftName}, ${timeRange(a.shiftStartTime, a.shiftEndTime)}`;
  }

  ngOnInit(): void {
    this.api.getCoverCandidates(this.data.assignment.id).subscribe({
      next: (info) => {
        this.info.set(info);
        if (info.currentCoverAccountId !== null) {
          this.selectedId.set(info.currentCoverAccountId);
          this.shiftChoice.set(info.currentCoverShiftId ?? COMBINE_BOTH);
          this.previewedShift = this.shiftChoice();
          this.shiftQuery.set(this.chosenShiftLabel());
          // Already confirmed when they were first assigned.
          this.confirmed.set(true);
        }
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err?.error ?? 'Failed to load employees.');
        this.loading.set(false);
      },
    });
  }

  select(accountId: number): void {
    if (accountId === this.selectedId()) {
      return;
    }
    this.selectedId.set(accountId);
    this.confirmed.set(false);
    // Start from a location shift that already spans both, if there is one.
    this.chooseShift(this.selected()?.suggestedShiftId ?? COMBINE_BOTH);
    this.shiftQuery.set(this.chosenShiftLabel());
    if (!this.canEmail()) {
      this.sendEmail.set(false);
    }
  }

  // Re-asks the server for the candidates with this replacement shift, so
  // the new-shift preview, weekly hours and overtime all reflect it.
  chooseShift(choice: number): void {
    this.shiftChoice.set(choice);
    this.confirmed.set(false);
    if (choice === this.previewedShift) {
      return;
    }

    this.refreshing.set(true);
    this.api.getCoverCandidates(this.data.assignment.id, choice || undefined).subscribe({
      next: (info) => {
        // A later pick may already have superseded this one.
        if (choice === this.shiftChoice()) {
          this.info.set(info);
          this.previewedShift = choice;
          this.refreshing.set(false);
        }
      },
      error: (err) => {
        this.error.set(typeof err?.error === 'string' ? err.error : 'Failed to load that shift.');
        this.refreshing.set(false);
      },
    });
  }

  // "15:00–19:30", the way the schedule builder's Add Shift lists them.
  shiftTime(option: CoverShiftOptionDto): string {
    return `${option.startTime.slice(0, 5)}–${option.endTime.slice(0, 5)}`;
  }

  // What the box shows once a shift is picked; blank while combining both.
  private chosenShiftLabel(): string {
    const chosen = this.info()?.shifts.find((s) => s.id === this.shiftChoice());
    return chosen ? this.shiftTime(chosen) : '';
  }

  onShiftQueryInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.shiftQuery.set(value);
    this.suggestionsOpen.set(true);
    // Clearing the box goes back to combining both shifts.
    if (!value.trim()) {
      this.chooseShift(COMBINE_BOTH);
    }
  }

  // Enter picks the top match — and must not submit the form.
  onShiftKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Enter') {
      return;
    }
    event.preventDefault();
    const [topMatch] = this.shiftMatches();
    if (topMatch && this.shiftQuery().trim()) {
      this.pickShift(topMatch);
    }
  }

  // mousedown (not click) + preventDefault keeps the box from blurring, and
  // the list from closing, before the pick registers.
  onSuggestionPick(event: MouseEvent, option: CoverShiftOptionDto): void {
    event.preventDefault();
    this.pickShift(option);
  }

  private pickShift(option: CoverShiftOptionDto): void {
    this.chooseShift(option.id);
    this.shiftQuery.set(this.shiftTime(option));
    this.suggestionsOpen.set(false);
  }

  // Leaving the box with half-typed text puts back what is actually chosen.
  onShiftBlur(): void {
    this.suggestionsOpen.set(false);
    this.shiftQuery.set(this.chosenShiftLabel());
  }

  weekHoursLabel(c: CoverCandidateDto): string {
    return durationLabel(c.weekScheduledHours * 60);
  }

  overtimeLabel(c: CoverCandidateDto): string | null {
    return c.overtimeMinutes > 0 ? `${durationLabel(c.overtimeMinutes)} overtime` : null;
  }

  otherShiftLabel(c: CoverCandidateDto): string {
    return c.ownShift ? timeRange(c.ownShift.startTime, c.ownShift.endTime) : '';
  }

  protected readonly canSave = computed(() => {
    if (this.saving() || this.loading() || this.refreshing()) {
      return false;
    }
    if (!this.isCallOut && !this.selected()) {
      return false;
    }
    return this.warning() === null || this.confirmed();
  });

  save(): void {
    this.error.set(null);
    if (this.isCallOut && !this.note.trim()) {
      this.error.set('Enter a reason for the absence.');
      return;
    }
    if (this.sickHours !== null && this.sickHours < 0) {
      this.error.set("Sick hours can't be negative.");
      return;
    }

    const c = this.selected();
    const cover = {
      confirmUnavailable: !!c && !c.isAvailable && this.confirmed(),
      confirmCombine: !!c && c.ownShift !== null && this.confirmed(),
      sendEmail: !!c && this.canEmail() && this.sendEmail(),
      coverShiftId: c?.ownShift && this.shiftChoice() !== COMBINE_BOTH ? this.shiftChoice() : null,
    };

    const request: Observable<CallOutResultDto> = this.isCallOut
      ? this.api.callOut(this.data.assignment.id, {
          note: this.note.trim(),
          sickMinutes: this.canRecordSickHours && this.sickHours ? Math.round(this.sickHours * 60) : null,
          coverAccountId: c?.accountId ?? null,
          ...cover,
        })
      : this.api.assignCover(this.data.assignment.id, { coverAccountId: c!.accountId, ...cover });

    this.saving.set(true);
    request.subscribe({
      next: (result) => {
        if (cover.sendEmail && !result.emailSent) {
          this.saving.set(false);
          this.savedNotice.set(`Saved, but the email to ${c!.firstName} couldn't be sent. Let them know another way.`);
          return;
        }
        this.dialogRef.close(true);
      },
      error: (err) => {
        this.error.set(typeof err?.error === 'string' ? err.error : 'Failed to save.');
        this.saving.set(false);
      },
    });
  }

  removeCover(): void {
    this.error.set(null);
    this.saving.set(true);
    this.api.removeCover(this.data.assignment.id).subscribe({
      next: () => this.dialogRef.close(true),
      error: (err) => {
        this.error.set(typeof err?.error === 'string' ? err.error : 'Failed to remove cover.');
        this.saving.set(false);
      },
    });
  }

  cancel(): void {
    // After a save that only failed to email, closing still reports a change.
    this.dialogRef.close(this.savedNotice() !== null);
  }
}
