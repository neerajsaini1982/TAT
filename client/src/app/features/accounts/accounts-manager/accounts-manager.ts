import { Component, ElementRef, Input, OnDestroy, OnInit, ViewChild, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatCardModule } from '@angular/material/card';

import { AccountsApi, AccountDto, EmploymentType } from '../../../core/accounts-api';
import { LocationsApi, LocationDto } from '../../../core/locations-api';
import { Role } from '../../../core/auth';

interface FormModel {
  username: string;
  password: string;
  firstName: string;
  lastName: string;
  email: string;
  phone: string;
  role: Role;
  isActive: boolean;
  hourlyRate: number | null;
  // Write-only — always reset to '' when editing (see startEdit); never
  // pre-filled with the real value. Blank means "leave unchanged" on update.
  ssn: string;
  // Read-only display value from the server, e.g. "***-**-1234".
  ssnMasked: string | null;
  dateOfBirth: string | null;
  hireDate: string | null;
  employmentType: EmploymentType | null;
}

const emptyForm = (): FormModel => ({
  username: '',
  password: '',
  firstName: '',
  lastName: '',
  email: '',
  phone: '',
  role: 'Employee',
  isActive: true,
  hourlyRate: null,
  ssn: '',
  ssnMasked: null,
  dateOfBirth: null,
  hireDate: null,
  employmentType: null,
});

// Used both at /sa/accounts (lockedLocationCode = null, shows a location
// picker and every location's accounts) and at /:locationCode/admin/accounts
// (lockedLocationCode set, server auto-scopes everything to that location).
@Component({
  selector: 'app-accounts-manager',
  imports: [
    RouterLink,
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
  templateUrl: './accounts-manager.html',
  styleUrl: './accounts-manager.scss',
})
export class AccountsManager implements OnInit, OnDestroy {
  @Input() lockedLocationCode: string | null = null;
  @ViewChild('cameraVideo') private readonly cameraVideoRef?: ElementRef<HTMLVideoElement>;

  private readonly accountsApi = inject(AccountsApi);
  private readonly locationsApi = inject(LocationsApi);

  protected readonly roles: Role[] = ['Admin', 'Lead', 'Employee'];
  protected readonly employmentTypes: EmploymentType[] = ['FullTime', 'PartTime'];
  protected readonly accounts = signal<AccountDto[]>([]);
  protected readonly locations = signal<LocationDto[]>([]);
  protected readonly selectedLocation = signal<LocationDto | null>(null);
  protected readonly editingId = signal<number | null>(null);
  // The role the account had when the edit form was opened — used to tell
  // whether the admin is promoting away from Employee, which needs fresh
  // login credentials (see startEdit/save).
  protected readonly originalRole = signal<Role | null>(null);
  protected readonly showForm = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly resettingId = signal<number | null>(null);
  protected readonly sendingId = signal<number | null>(null);
  protected form: FormModel = emptyForm();

  // Local object URL for whatever photo is currently shown in the preview
  // box — either the account's existing photo (fetched as a blob, since the
  // auth token only attaches via HttpClient) or a freshly picked file.
  protected readonly photoPreviewUrl = signal<string | null>(null);
  private selectedPhotoFile: File | null = null;
  private photoRemoved = false;

  // Live camera capture (works on both a phone's camera and a desktop
  // webcam via getUserMedia — no separate mobile-only code path needed).
  protected readonly cameraActive = signal(false);
  protected readonly cameraError = signal<string | null>(null);
  private mediaStream: MediaStream | null = null;

  // Hides terminated/inactive employees by default — a location can pick up
  // 100+ inactive rows from an ADP import, and most day-to-day account work
  // (scheduling, resets) only ever cares about who's currently active.
  protected readonly showInactive = signal(false);
  protected readonly searchTerm = signal('');

  protected readonly filteredAccounts = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    return this.accounts().filter((a) => {
      if (!this.showInactive() && !a.isActive) {
        return false;
      }
      if (!term) {
        return true;
      }
      return a.firstName.toLowerCase().includes(term) || a.lastName.toLowerCase().includes(term);
    });
  });

  get columns(): string[] {
    const base = ['serial', 'username', 'firstName', 'lastName', 'role', 'userCode', 'isActive', 'actions'];
    return this.lockedLocationCode ? base : ['serial', 'locationCode', ...base.slice(1)];
  }

  ngOnInit(): void {
    if (!this.lockedLocationCode) {
      this.locationsApi.getAll().subscribe((locations) => this.locations.set(locations));
    }
    this.load();
  }

  ngOnDestroy(): void {
    this.closeCamera();
  }

  load(): void {
    const code = this.lockedLocationCode ?? this.selectedLocation()?.locationCode;
    this.accountsApi.getAll(code).subscribe((accounts) => this.accounts.set(accounts));
  }

  onLocationFilterChange(location: LocationDto | null): void {
    this.selectedLocation.set(location);
    this.load();
  }

  compareLocations = (a: LocationDto | null, b: LocationDto | null): boolean => a?.id === b?.id;

  startCreate(): void {
    this.editingId.set(null);
    this.originalRole.set(null);
    this.form = emptyForm();
    this.error.set(null);
    this.resetPhotoState();
    this.showForm.set(true);
  }

  startEdit(account: AccountDto): void {
    this.editingId.set(account.id);
    this.originalRole.set(account.role);
    this.form = { ...emptyForm(), ...account, password: '', ssn: '' };
    this.error.set(null);
    this.resetPhotoState();
    if (account.hasPhoto) {
      this.accountsApi.getPhoto(account.id).subscribe((blob) => {
        this.photoPreviewUrl.set(URL.createObjectURL(blob));
      });
    }
    this.showForm.set(true);
  }

  // True once the admin has changed the role away from Employee — the
  // account needs a real username/password at that point (see save()).
  protected promotingFromEmployee(): boolean {
    return this.originalRole() === 'Employee' && this.form.role !== 'Employee';
  }

  // Pre-fills the create form from an existing account so the admin can
  // adjust it and save it as a brand new account. Username/password/user
  // code are never copied — those get (re)generated on save.
  duplicate(account: AccountDto): void {
    this.editingId.set(null);
    this.form = {
      ...emptyForm(),
      firstName: `${account.firstName} (Copy)`,
      lastName: account.lastName,
      email: account.email,
      phone: account.phone,
      role: account.role,
    };
    this.error.set(null);
    this.resetPhotoState();
    this.showForm.set(true);
  }

  cancel(): void {
    this.closeCamera();
    this.showForm.set(false);
  }

  private resetPhotoState(): void {
    this.closeCamera();
    const current = this.photoPreviewUrl();
    if (current) {
      URL.revokeObjectURL(current);
    }
    this.photoPreviewUrl.set(null);
    this.selectedPhotoFile = null;
    this.photoRemoved = false;
  }

  onPhotoSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    if (file) {
      this.setPhotoFile(file);
    }
    // Reset so picking the same file again still fires a change event.
    input.value = '';
  }

  removePhoto(): void {
    const current = this.photoPreviewUrl();
    if (current) {
      URL.revokeObjectURL(current);
    }
    this.photoPreviewUrl.set(null);
    this.selectedPhotoFile = null;
    this.photoRemoved = true;
  }

  private setPhotoFile(file: File): void {
    const current = this.photoPreviewUrl();
    if (current) {
      URL.revokeObjectURL(current);
    }
    this.selectedPhotoFile = file;
    this.photoRemoved = false;
    this.photoPreviewUrl.set(URL.createObjectURL(file));
  }

  // Opens a live camera preview in the photo box itself (see the template's
  // always-present <video #cameraVideo>, hidden via CSS until active — this
  // sidesteps ViewChild timing issues around @if-driven creation). Works the
  // same way for a phone's camera and a desktop webcam.
  openCamera(): void {
    this.cameraError.set(null);
    if (!navigator.mediaDevices?.getUserMedia) {
      this.cameraError.set('Camera access is not supported in this browser.');
      return;
    }

    navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user' }, audio: false }).then(
      (stream) => {
        this.mediaStream = stream;
        this.cameraActive.set(true);
        const video = this.cameraVideoRef?.nativeElement;
        if (video) {
          video.srcObject = stream;
          void video.play();
        }
      },
      () => this.cameraError.set('Could not access the camera. Check permissions and try again.'),
    );
  }

  capturePhoto(): void {
    const video = this.cameraVideoRef?.nativeElement;
    if (!video || !video.videoWidth) {
      return;
    }

    const canvas = document.createElement('canvas');
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    canvas.getContext('2d')?.drawImage(video, 0, 0, canvas.width, canvas.height);
    canvas.toBlob(
      (blob) => {
        if (!blob) {
          return;
        }
        this.setPhotoFile(new File([blob], `photo-${Date.now()}.jpg`, { type: 'image/jpeg' }));
        this.closeCamera();
      },
      'image/jpeg',
      0.92,
    );
  }

  closeCamera(): void {
    this.mediaStream?.getTracks().forEach((track) => track.stop());
    this.mediaStream = null;
    this.cameraActive.set(false);
    const video = this.cameraVideoRef?.nativeElement;
    if (video) {
      video.srcObject = null;
    }
  }

  save(): void {
    this.error.set(null);
    const id = this.editingId();

    if (id === null) {
      const locationId = this.lockedLocationCode ? null : (this.selectedLocation()?.id ?? null);
      if (!this.lockedLocationCode && locationId === null) {
        this.error.set('Select a location before adding an account.');
        return;
      }

      const isEmployee = this.form.role === 'Employee';
      this.accountsApi
        .create({
          username: isEmployee ? undefined : this.form.username,
          password: isEmployee ? undefined : this.form.password,
          firstName: this.form.firstName,
          lastName: this.form.lastName,
          email: this.form.email,
          phone: this.form.phone,
          role: this.form.role,
          locationId,
          hourlyRate: this.form.hourlyRate,
          ssn: this.form.ssn || undefined,
          dateOfBirth: this.form.dateOfBirth,
          hireDate: this.form.hireDate,
          employmentType: this.form.employmentType,
        })
        .subscribe({
          next: (account) => this.syncPhotoThenClose(account.id),
          error: (err) => this.error.set(err?.error ?? 'Failed to create account.'),
        });
      return;
    }

    if (this.promotingFromEmployee() && (!this.form.username || !this.form.password)) {
      this.error.set('Username and password are required when changing role away from Employee.');
      return;
    }

    this.accountsApi
      .update(id, {
        firstName: this.form.firstName,
        lastName: this.form.lastName,
        email: this.form.email,
        phone: this.form.phone,
        isActive: this.form.isActive,
        role: this.form.role,
        // Sent whenever the field is editable (promoting off Employee, or
        // renaming an Employee's username in place) — when it's disabled
        // (already-Admin/Lead/Sa, not promoting) this is just their
        // unchanged existing value, a no-op on the server.
        username: this.form.username,
        password: this.promotingFromEmployee() ? this.form.password : undefined,
        hourlyRate: this.form.hourlyRate,
        ssn: this.form.ssn || undefined,
        dateOfBirth: this.form.dateOfBirth,
        hireDate: this.form.hireDate,
        employmentType: this.form.employmentType,
      })
      .subscribe({
        next: () => this.syncPhotoThenClose(id),
        error: (err) => this.error.set(err?.error ?? 'Failed to update account.'),
      });
  }

  // After the account itself is saved, apply whatever photo change the admin
  // made (a new file, an explicit removal, or nothing) before closing the
  // form and reloading the table.
  private syncPhotoThenClose(accountId: number): void {
    const done = () => {
      this.showForm.set(false);
      this.load();
    };

    if (this.selectedPhotoFile) {
      this.accountsApi.uploadPhoto(accountId, this.selectedPhotoFile).subscribe({
        next: done,
        error: (err) => this.error.set(err?.error ?? 'Account saved, but the photo failed to upload.'),
      });
    } else if (this.photoRemoved) {
      this.accountsApi.deletePhoto(accountId).subscribe({
        next: done,
        error: (err) => this.error.set(err?.error ?? 'Account saved, but the photo failed to remove.'),
      });
    } else {
      done();
    }
  }

  remove(account: AccountDto): void {
    if (!confirm(`Delete account "${account.username}"?`)) {
      return;
    }
    this.accountsApi.delete(account.id).subscribe({
      next: () => this.load(),
      error: (err) => alert(err?.error ?? 'Failed to delete account.'),
    });
  }

  resetCode(account: AccountDto): void {
    if (!confirm(`Generate a new user code for ${account.firstName} ${account.lastName}? The old code will stop working immediately.`)) {
      return;
    }
    this.resettingId.set(account.id);
    this.accountsApi.resetCode(account.id).subscribe({
      next: () => {
        this.resettingId.set(null);
        this.load();
      },
      error: (err) => {
        this.resettingId.set(null);
        alert(err?.error ?? 'Failed to reset user code.');
      },
    });
  }

  canSendCredentials(account: AccountDto): boolean {
    return account.role === 'Employee' && !!account.email && !!account.userCode;
  }

  sendCredentials(account: AccountDto): void {
    if (!this.canSendCredentials(account)) {
      return;
    }
    const locationCode = this.lockedLocationCode ?? account.locationCode;
    const loginLink = `${window.location.origin}/${locationCode}/employee`;

    this.sendingId.set(account.id);
    this.accountsApi.sendCredentials(account.id, loginLink).subscribe({
      next: () => {
        this.sendingId.set(null);
        alert(`Login credentials sent to ${account.firstName} ${account.lastName}.`);
      },
      error: (err) => {
        this.sendingId.set(null);
        alert(err?.error ?? 'Failed to send login credentials.');
      },
    });
  }
}
