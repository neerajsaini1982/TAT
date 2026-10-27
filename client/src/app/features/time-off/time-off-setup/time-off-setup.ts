import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';

import {
  TimeOffApi,
  TimeOffBlackoutDto,
  TimeOffSettingsDto,
  TimeOffTypeDto,
  apiError,
  timeOffDateRange,
} from '../../../core/time-off-api';
import { formatDate } from '../../../core/week-utils';

// The location's time-off setup: on/off and limits, the types employees can
// pick (each paid or unpaid), and blackout dates. Sick is shown as a fixed
// paid row — it keeps its own built-in handling.
@Component({
  selector: 'app-time-off-setup',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSlideToggleModule],
  templateUrl: './time-off-setup.html',
  styleUrl: './time-off-setup.scss',
})
export class TimeOffSetup implements OnInit {
  private readonly api = inject(TimeOffApi);
  readonly locationCode = input.required<string>();

  protected readonly types = signal<TimeOffTypeDto[]>([]);
  protected readonly blackouts = signal<TimeOffBlackoutDto[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly saved = signal<string | null>(null);
  protected settings: TimeOffSettingsDto = { timeOffEnabled: true, timeOffMinNoticeDays: null, timeOffFullDayMinutes: 480 };
  protected fullDayHours = 8;
  protected newTypeName = '';
  protected newBlackout = { name: '', startDate: formatDate(new Date()), endDate: formatDate(new Date()) };
  protected readonly range = timeOffDateRange;

  ngOnInit(): void {
    const code = this.locationCode();
    this.api.settings(code).subscribe((s) => {
      this.settings = s;
      this.fullDayHours = s.timeOffFullDayMinutes / 60;
    });
    this.loadTypes();
    this.api.blackouts(code).subscribe((b) => this.blackouts.set(b));
  }

  private loadTypes(): void {
    this.api.types(this.locationCode(), true).subscribe({
      next: (t) => this.types.set(t),
      error: (err) => this.error.set(apiError(err, 'Failed to load types.')),
    });
  }

  private fail(fallback: string) {
    return (err: unknown) => this.error.set(apiError(err, fallback));
  }

  private flash(message: string): void {
    this.error.set(null);
    this.saved.set(message);
    setTimeout(() => this.saved.set(null), 2500);
  }

  protected saveSettings(): void {
    this.api
      .saveSettings(this.locationCode(), {
        ...this.settings,
        timeOffMinNoticeDays: this.settings.timeOffMinNoticeDays || null,
        timeOffFullDayMinutes: Math.round(this.fullDayHours * 60),
      })
      .subscribe({ next: (s) => ((this.settings = s), this.flash('Settings saved.')), error: this.fail('Failed to save settings.') });
  }

  protected updateType(type: TimeOffTypeDto, change: Partial<TimeOffTypeDto>): void {
    const next = { ...type, ...change };
    this.api.updateType(type.id, next).subscribe({
      next: () => (this.loadTypes(), this.flash(`${next.name} saved.`)),
      error: (err) => (this.fail('Failed to save the type.')(err), this.loadTypes()),
    });
  }

  protected move(type: TimeOffTypeDto, delta: number): void {
    const list = [...this.types()];
    const i = list.indexOf(type);
    const j = i + delta;
    if (j < 0 || j >= list.length) return;
    [list[i], list[j]] = [list[j], list[i]];
    list.forEach((t, index) => t.sortOrder !== index && this.api.updateType(t.id, { ...t, sortOrder: index }).subscribe());
    this.types.set(list.map((t, index) => ({ ...t, sortOrder: index })));
  }

  protected addType(): void {
    const name = this.newTypeName.trim();
    if (!name) return;
    this.api
      .createType(this.locationCode(), { name, isPaid: false, isActive: true, sortOrder: this.types().length, color: null, payrollCode: null })
      .subscribe({ next: () => ((this.newTypeName = ''), this.loadTypes(), this.flash(`${name} added.`)), error: this.fail('Failed to add the type.') });
  }

  protected deleteType(type: TimeOffTypeDto): void {
    if (!confirm(`Delete "${type.name}"?`)) return;
    this.api.deleteType(type.id).subscribe({ next: () => this.loadTypes(), error: this.fail('Failed to delete the type.') });
  }

  protected addBlackout(): void {
    const b = { ...this.newBlackout, name: this.newBlackout.name.trim() };
    if (!b.name) {
      this.error.set('Give the blackout a name, e.g. "Thanksgiving week".');
      return;
    }
    this.api.createBlackout(this.locationCode(), b).subscribe({
      next: (created) => {
        this.blackouts.set([...this.blackouts(), created].sort((x, y) => x.startDate.localeCompare(y.startDate)));
        this.newBlackout.name = '';
        this.flash('Blackout added.');
      },
      error: this.fail('Failed to add the blackout.'),
    });
  }

  protected deleteBlackout(b: TimeOffBlackoutDto): void {
    if (!confirm(`Remove "${b.name}"?`)) return;
    this.api.deleteBlackout(b.id).subscribe({
      next: () => this.blackouts.set(this.blackouts().filter((x) => x.id !== b.id)),
      error: this.fail('Failed to remove the blackout.'),
    });
  }
}
