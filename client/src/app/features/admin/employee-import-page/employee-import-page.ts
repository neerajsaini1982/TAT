import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressBarModule } from '@angular/material/progress-bar';

import {
  EmployeeImportApi,
  EmployeeImportCommitResult,
  EmployeeImportRowDto,
} from '../../../core/employee-import-api';

type Phase = 'pick' | 'reviewing' | 'done';

interface ReviewRow extends EmployeeImportRowDto {
  selected: boolean;
}

// Bulk-uploads employees from an ADP "Employee Directory" .xlsx export.
// Preview-then-commit: the file is parsed and every row's new-vs-existing
// status is shown before anything touches the database (see
// EmployeeImportController) — creating dozens of accounts in one shot is
// exactly the kind of thing you want to review first, not fire-and-forget.
@Component({
  selector: 'app-employee-import-page',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatCardModule, MatCheckboxModule, MatProgressBarModule],
  templateUrl: './employee-import-page.html',
  styleUrl: './employee-import-page.scss',
})
export class EmployeeImportPage {
  private readonly api = inject(EmployeeImportApi);
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;

  protected readonly phase = signal<Phase>('pick');
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly rows = signal<ReviewRow[]>([]);
  protected readonly totalRows = signal(0);
  protected readonly newCount = signal(0);
  protected readonly skippedCount = signal(0);
  protected readonly committing = signal(false);
  protected readonly commitResult = signal<EmployeeImportCommitResult | null>(null);

  protected readonly selectedCount = computed(() => this.rows().filter((r) => r.selected).length);
  protected readonly allSelectableChecked = computed(() => {
    const selectable = this.rows().filter((r) => r.willCreate);
    return selectable.length > 0 && selectable.every((r) => r.selected);
  });

  private selectedFile: File | null = null;
  protected selectedFileName: string | null = null;

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.[0] ?? null;
    this.selectedFileName = this.selectedFile?.name ?? null;
    this.error.set(null);
  }

  runPreview(): void {
    if (!this.selectedFile) {
      this.error.set('Choose a file first.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);
    this.api.preview(this.selectedFile, this.locationCode).subscribe({
      next: (result) => {
        this.rows.set(result.rows.map((r) => ({ ...r, selected: r.willCreate })));
        this.totalRows.set(result.totalRows);
        this.newCount.set(result.newCount);
        this.skippedCount.set(result.skippedCount);
        this.loading.set(false);
        this.phase.set('reviewing');
      },
      error: (err) => {
        this.error.set(err?.error ?? 'Failed to read that file.');
        this.loading.set(false);
      },
    });
  }

  toggleRow(row: ReviewRow, checked: boolean): void {
    if (!row.willCreate) {
      return;
    }
    this.rows.update((rows) => rows.map((r) => (r === row ? { ...r, selected: checked } : r)));
  }

  toggleAll(checked: boolean): void {
    this.rows.update((rows) => rows.map((r) => (r.willCreate ? { ...r, selected: checked } : r)));
  }

  runCommit(): void {
    const selected = this.rows().filter((r) => r.selected);
    if (selected.length === 0) {
      return;
    }

    this.committing.set(true);
    this.error.set(null);
    this.api.commit(selected, this.locationCode).subscribe({
      next: (result) => {
        this.commitResult.set(result);
        this.committing.set(false);
        this.phase.set('done');
      },
      error: (err) => {
        this.error.set(err?.error ?? 'Failed to import employees.');
        this.committing.set(false);
      },
    });
  }

  startOver(): void {
    this.phase.set('pick');
    this.rows.set([]);
    this.commitResult.set(null);
    this.selectedFile = null;
    this.selectedFileName = null;
    this.error.set(null);
  }

  address(row: EmployeeImportRowDto): string {
    const line1 = [row.address1, row.address2].filter(Boolean).join(', ');
    const line2 = [row.city, row.state, row.zipcode].filter(Boolean).join(', ');
    return [line1, line2].filter(Boolean).join(' — ') || '—';
  }
}
