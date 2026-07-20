import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatCardModule } from '@angular/material/card';

import { BreakKind, ScheduledBreakDto, ShiftsApi, ShiftDto } from '../../../core/shifts-api';
import { LocationsApi, LocationDto } from '../../../core/locations-api';

interface ScheduledBreakRow {
  kind: BreakKind;
  start: string;
  end: string;
}

interface FormModel {
  name: string;
  startTime: string;
  endTime: string;
  scheduledBreaks: ScheduledBreakRow[];
  isActive: boolean;
}

const emptyForm = (): FormModel => ({
  name: '',
  startTime: '09:00',
  endTime: '17:00',
  scheduledBreaks: [],
  isActive: true,
});

// `<input type="time">` works with "HH:mm"; the API's TimeOnly fields
// serialize as "HH:mm:ss". Convert at the edges.
const toInputTime = (apiTime: string): string => apiTime.slice(0, 5);
const toApiTime = (inputTime: string): string => (inputTime.length === 5 ? `${inputTime}:00` : inputTime);

const DEFAULT_WINDOW: Record<BreakKind, { start: string; end: string }> = {
  Break: { start: '10:00', end: '10:15' },
  Lunch: { start: '12:00', end: '12:30' },
};

const rowsToApi = (rows: ScheduledBreakRow[]): ScheduledBreakDto[] =>
  rows.map((r) => ({ kind: r.kind, startTime: toApiTime(r.start), endTime: toApiTime(r.end) }));

const rowsFromApi = (breaks: ScheduledBreakDto[]): ScheduledBreakRow[] =>
  breaks.map((b) => ({ kind: b.kind, start: toInputTime(b.startTime), end: toInputTime(b.endTime) }));

const scheduledBreaksLabel = (breaks: ScheduledBreakDto[]): string =>
  breaks.length === 0
    ? 'None'
    : breaks.map((b) => `${b.kind} ${toInputTime(b.startTime)}–${toInputTime(b.endTime)}`).join(', ');

// Used both at /sa/shifts (lockedLocationCode = null, shows a location
// picker and every location's shifts) and at /:locationCode/admin/shifts
// (lockedLocationCode set, server auto-scopes everything to that location).
@Component({
  selector: 'app-shifts-manager',
  imports: [
    FormsModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatCardModule,
  ],
  templateUrl: './shifts-manager.html',
  styleUrl: './shifts-manager.scss',
})
export class ShiftsManager implements OnInit {
  @Input() lockedLocationCode: string | null = null;

  private readonly shiftsApi = inject(ShiftsApi);
  private readonly locationsApi = inject(LocationsApi);

  protected readonly shifts = signal<ShiftDto[]>([]);
  protected readonly locations = signal<LocationDto[]>([]);
  protected readonly selectedLocation = signal<LocationDto | null>(null);
  protected readonly editingId = signal<number | null>(null);
  protected readonly showForm = signal(false);
  protected readonly error = signal<string | null>(null);
  protected form: FormModel = emptyForm();

  protected readonly toInputTime = toInputTime;
  protected readonly scheduledBreaksLabel = scheduledBreaksLabel;

  get columns(): string[] {
    const base = ['name', 'startTime', 'endTime', 'scheduledBreaks', 'isActive', 'actions'];
    return this.lockedLocationCode ? base : ['locationCode', ...base];
  }

  ngOnInit(): void {
    if (!this.lockedLocationCode) {
      this.locationsApi.getAll().subscribe((locations) => this.locations.set(locations));
    }
    this.load();
  }

  load(): void {
    const code = this.lockedLocationCode ?? this.selectedLocation()?.locationCode;
    this.shiftsApi.getAll(code).subscribe((shifts) => this.shifts.set(shifts));
  }

  onLocationFilterChange(location: LocationDto | null): void {
    this.selectedLocation.set(location);
    this.load();
  }

  compareLocations = (a: LocationDto | null, b: LocationDto | null): boolean => a?.id === b?.id;

  startCreate(): void {
    this.editingId.set(null);
    this.form = emptyForm();
    this.error.set(null);
    this.showForm.set(true);
  }

  startEdit(shift: ShiftDto): void {
    this.editingId.set(shift.id);
    this.form = {
      name: shift.name,
      startTime: toInputTime(shift.startTime),
      endTime: toInputTime(shift.endTime),
      scheduledBreaks: rowsFromApi(shift.scheduledBreaks),
      isActive: shift.isActive,
    };
    this.error.set(null);
    this.showForm.set(true);
  }

  // Pre-fills the create form from an existing shift so the admin can
  // tweak the name/times and save it as a brand new shift.
  duplicate(shift: ShiftDto): void {
    this.editingId.set(null);
    this.form = {
      name: `${shift.name} (Copy)`,
      startTime: toInputTime(shift.startTime),
      endTime: toInputTime(shift.endTime),
      scheduledBreaks: rowsFromApi(shift.scheduledBreaks),
      isActive: true,
    };
    this.error.set(null);
    this.showForm.set(true);
  }

  cancel(): void {
    this.showForm.set(false);
  }

  addBreak(kind: BreakKind): void {
    const { start, end } = DEFAULT_WINDOW[kind];
    this.form.scheduledBreaks = [...this.form.scheduledBreaks, { kind, start, end }];
  }

  removeBreak(index: number): void {
    this.form.scheduledBreaks = this.form.scheduledBreaks.filter((_, i) => i !== index);
  }

  // Mirrors ShiftsController.ValidateScheduledBreaks server-side: every
  // window must fall within the shift's own span, and windows can't
  // overlap each other regardless of Kind.
  private validateScheduledBreaks(): string | null {
    for (const b of this.form.scheduledBreaks) {
      if (b.end <= b.start) {
        return `${b.kind} end time must be after its start time.`;
      }
      if (b.start < this.form.startTime || b.end > this.form.endTime) {
        return `${b.kind} (${b.start}–${b.end}) must fall within the shift's start and end time.`;
      }
    }

    const sorted = [...this.form.scheduledBreaks].sort((a, b) => a.start.localeCompare(b.start));
    for (let i = 0; i < sorted.length; i++) {
      for (let j = i + 1; j < sorted.length; j++) {
        if (sorted[i].start < sorted[j].end && sorted[j].start < sorted[i].end) {
          return 'Scheduled breaks and lunches can’t overlap.';
        }
      }
    }

    return null;
  }

  save(): void {
    this.error.set(null);
    const validationError = this.validateScheduledBreaks();
    if (validationError) {
      this.error.set(validationError);
      return;
    }

    const id = this.editingId();

    if (id === null) {
      const locationId = this.lockedLocationCode ? null : (this.selectedLocation()?.id ?? null);
      if (!this.lockedLocationCode && locationId === null) {
        this.error.set('Select a location before adding a shift.');
        return;
      }

      this.shiftsApi
        .create({
          name: this.form.name,
          startTime: toApiTime(this.form.startTime),
          endTime: toApiTime(this.form.endTime),
          scheduledBreaks: rowsToApi(this.form.scheduledBreaks),
          locationId,
        })
        .subscribe({
          next: () => {
            this.showForm.set(false);
            this.load();
          },
          error: (err) => this.error.set(err?.error ?? 'Failed to create shift.'),
        });
      return;
    }

    this.shiftsApi
      .update(id, {
        name: this.form.name,
        startTime: toApiTime(this.form.startTime),
        endTime: toApiTime(this.form.endTime),
        scheduledBreaks: rowsToApi(this.form.scheduledBreaks),
        isActive: this.form.isActive,
      })
      .subscribe({
        next: () => {
          this.showForm.set(false);
          this.load();
        },
        error: (err) => this.error.set(err?.error ?? 'Failed to update shift.'),
      });
  }

  remove(shift: ShiftDto): void {
    if (!confirm(`Delete shift "${shift.name}"?`)) {
      return;
    }
    this.shiftsApi.delete(shift.id).subscribe({
      next: () => this.load(),
      error: (err) => alert(err?.error ?? 'Failed to delete shift.'),
    });
  }
}
