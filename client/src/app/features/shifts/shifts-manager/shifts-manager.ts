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

import { ShiftsApi, ShiftDto } from '../../../core/shifts-api';
import { LocationsApi, LocationDto } from '../../../core/locations-api';

interface FormModel {
  name: string;
  startTime: string;
  endTime: string;
  isBreakRequired: boolean;
  isLunchRequired: boolean;
  isActive: boolean;
}

const emptyForm = (): FormModel => ({
  name: '',
  startTime: '09:00',
  endTime: '17:00',
  isBreakRequired: false,
  isLunchRequired: false,
  isActive: true,
});

// `<input type="time">` works with "HH:mm"; the API's TimeOnly fields
// serialize as "HH:mm:ss". Convert at the edges.
const toInputTime = (apiTime: string): string => apiTime.slice(0, 5);
const toApiTime = (inputTime: string): string => (inputTime.length === 5 ? `${inputTime}:00` : inputTime);

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

  get columns(): string[] {
    const base = ['name', 'startTime', 'endTime', 'isBreakRequired', 'isLunchRequired', 'isActive', 'actions'];
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
      isBreakRequired: shift.isBreakRequired,
      isLunchRequired: shift.isLunchRequired,
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
      isBreakRequired: shift.isBreakRequired,
      isLunchRequired: shift.isLunchRequired,
      isActive: true,
    };
    this.error.set(null);
    this.showForm.set(true);
  }

  cancel(): void {
    this.showForm.set(false);
  }

  save(): void {
    this.error.set(null);
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
          isBreakRequired: this.form.isBreakRequired,
          isLunchRequired: this.form.isLunchRequired,
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
        isBreakRequired: this.form.isBreakRequired,
        isLunchRequired: this.form.isLunchRequired,
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
