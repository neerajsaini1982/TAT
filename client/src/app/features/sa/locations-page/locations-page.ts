import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatCardModule } from '@angular/material/card';

import { LocationsApi, LocationDto } from '../../../core/locations-api';

interface FormModel {
  name: string;
  address: string;
  locationCode: string;
  phone: string;
  email: string;
  isActive: boolean;
}

const emptyForm = (): FormModel => ({
  name: '',
  address: '',
  locationCode: '',
  phone: '',
  email: '',
  isActive: true,
});

@Component({
  selector: 'app-locations-page',
  imports: [
    FormsModule,
    RouterLink,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSlideToggleModule,
    MatCardModule,
  ],
  templateUrl: './locations-page.html',
  styleUrl: './locations-page.scss',
})
export class LocationsPage implements OnInit {
  private readonly api = inject(LocationsApi);

  protected readonly locations = signal<LocationDto[]>([]);
  protected readonly editingId = signal<number | null>(null);
  protected readonly showForm = signal(false);
  protected readonly error = signal<string | null>(null);
  protected form: FormModel = emptyForm();

  protected readonly columns = ['name', 'locationCode', 'phone', 'email', 'isActive', 'actions'];

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.api.getAll().subscribe((locations) => this.locations.set(locations));
  }

  startCreate(): void {
    this.editingId.set(null);
    this.form = emptyForm();
    this.error.set(null);
    this.showForm.set(true);
  }

  startEdit(location: LocationDto): void {
    this.editingId.set(location.id);
    this.form = { ...location };
    this.error.set(null);
    this.showForm.set(true);
  }

  cancel(): void {
    this.showForm.set(false);
  }

  save(): void {
    this.error.set(null);
    const id = this.editingId();

    const request$ =
      id === null
        ? this.api.create({
            name: this.form.name,
            address: this.form.address,
            locationCode: this.form.locationCode,
            phone: this.form.phone,
            email: this.form.email,
          })
        : this.api.update(id, {
            name: this.form.name,
            address: this.form.address,
            phone: this.form.phone,
            email: this.form.email,
            isActive: this.form.isActive,
          });

    request$.subscribe({
      next: () => {
        this.showForm.set(false);
        this.load();
      },
      error: (err) => this.error.set(err?.error ?? 'Failed to save location.'),
    });
  }

  remove(location: LocationDto): void {
    if (!confirm(`Delete location "${location.name}"?`)) {
      return;
    }
    this.api.delete(location.id).subscribe({
      next: () => this.load(),
      error: (err) => alert(err?.error ?? 'Failed to delete location.'),
    });
  }
}
