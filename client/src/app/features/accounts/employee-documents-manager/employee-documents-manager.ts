import { Component, Input, OnChanges, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';

import { EmployeeDocumentsApi, EmployeeDocumentDto } from '../../../core/employee-documents-api';

// Onboarding documents for one employee — used both by an admin (canDelete)
// and by the employee viewing their own documents (canDelete = false, per
// the issue's "admin can delete, others can't").
@Component({
  selector: 'app-employee-documents-manager',
  imports: [
    DatePipe,
    FormsModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatCardModule,
    MatProgressBarModule,
  ],
  templateUrl: './employee-documents-manager.html',
  styleUrl: './employee-documents-manager.scss',
})
export class EmployeeDocumentsManager implements OnChanges {
  @Input({ required: true }) accountId!: number;
  @Input() canDelete = false;

  private readonly api = inject(EmployeeDocumentsApi);

  protected readonly documents = signal<EmployeeDocumentDto[]>([]);
  protected readonly showForm = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly uploading = signal(false);
  protected readonly renamingId = signal<number | null>(null);
  protected readonly viewingId = signal<number | null>(null);
  protected readonly deletingId = signal<number | null>(null);

  protected readonly columns = ['name', 'uploadedBy', 'uploadedAt', 'size', 'actions'];

  protected name = '';
  protected renameValue = '';
  private selectedFile: File | null = null;
  protected selectedFileName: string | null = null;

  ngOnChanges(): void {
    if (this.accountId) {
      this.load();
    }
  }

  load(): void {
    this.api.list(this.accountId).subscribe((docs) => this.documents.set(docs));
  }

  startAdd(): void {
    this.name = '';
    this.selectedFile = null;
    this.selectedFileName = null;
    this.error.set(null);
    this.showForm.set(true);
  }

  cancelAdd(): void {
    this.showForm.set(false);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.[0] ?? null;
    this.selectedFileName = this.selectedFile?.name ?? null;
  }

  upload(): void {
    if (!this.name.trim()) {
      this.error.set('Enter a document name.');
      return;
    }
    if (!this.selectedFile) {
      this.error.set('Choose a file to upload.');
      return;
    }

    this.uploading.set(true);
    this.error.set(null);
    this.api.upload(this.accountId, this.name.trim(), this.selectedFile).subscribe({
      next: () => {
        this.uploading.set(false);
        this.showForm.set(false);
        this.load();
      },
      error: (err) => {
        this.uploading.set(false);
        this.error.set(err?.error ?? 'Failed to upload document.');
      },
    });
  }

  view(doc: EmployeeDocumentDto): void {
    this.viewingId.set(doc.id);
    this.api.download(this.accountId, doc.id).subscribe({
      next: (blob) => {
        this.viewingId.set(null);
        const url = URL.createObjectURL(blob);
        window.open(url, '_blank');
      },
      error: () => {
        this.viewingId.set(null);
        alert('Failed to open document.');
      },
    });
  }

  startRename(doc: EmployeeDocumentDto): void {
    this.renamingId.set(doc.id);
    this.renameValue = doc.name;
  }

  cancelRename(): void {
    this.renamingId.set(null);
  }

  saveRename(doc: EmployeeDocumentDto): void {
    if (!this.renameValue.trim()) {
      return;
    }
    this.api.rename(this.accountId, doc.id, this.renameValue.trim()).subscribe({
      next: () => {
        this.renamingId.set(null);
        this.load();
      },
      error: (err) => alert(err?.error ?? 'Failed to rename document.'),
    });
  }

  remove(doc: EmployeeDocumentDto): void {
    if (!confirm(`Delete "${doc.name}"? This cannot be undone.`)) {
      return;
    }
    this.deletingId.set(doc.id);
    this.api.remove(this.accountId, doc.id).subscribe({
      next: () => {
        this.deletingId.set(null);
        this.load();
      },
      error: (err) => {
        this.deletingId.set(null);
        alert(err?.error ?? 'Failed to delete document.');
      },
    });
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }
    if (bytes < 1024 * 1024) {
      return `${(bytes / 1024).toFixed(1)} KB`;
    }
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
