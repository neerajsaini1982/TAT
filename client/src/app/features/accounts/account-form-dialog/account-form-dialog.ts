import { Component, ElementRef, Inject, OnDestroy, ViewChild, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';

import { AccountsApi, AccountDto, EmploymentType } from '../../../core/accounts-api';
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
  isOnShiftSchedule: boolean;
  canSeeAllSchedules: boolean;
  hourlyRate: number | null;
  // Write-only — always reset to '' when editing (see constructor); never
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
  isOnShiftSchedule: true,
  canSeeAllSchedules: false,
  hourlyRate: null,
  ssn: '',
  ssnMasked: null,
  dateOfBirth: null,
  hireDate: null,
  employmentType: null,
});

// data.account set means "edit that account"; data.duplicateFrom set means
// "create a new account pre-filled from that one"; neither set means a blank
// create form. locationId is the location to create the account under (only
// relevant for create, when lockedLocationCode is null).
export interface AccountFormDialogData {
  account: AccountDto | null;
  duplicateFrom: AccountDto | null;
  lockedLocationCode: string | null;
  locationId: number | null;
}

@Component({
  selector: 'app-account-form-dialog',
  imports: [
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
  ],
  templateUrl: './account-form-dialog.html',
  styleUrl: './account-form-dialog.scss',
})
export class AccountFormDialog implements OnDestroy {
  @ViewChild('cameraVideo') private readonly cameraVideoRef?: ElementRef<HTMLVideoElement>;

  protected readonly roles: Role[] = ['Admin', 'Lead', 'Employee'];
  protected readonly employmentTypes: EmploymentType[] = ['FullTime', 'PartTime'];
  protected readonly editingId: number | null;
  // The role the account had when the dialog was opened — used to tell
  // whether the admin is promoting away from Employee, which needs fresh
  // login credentials (see promotingFromEmployee/save).
  private readonly originalRole: Role | null;
  protected readonly error = signal<string | null>(null);
  protected form: FormModel;

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

  constructor(
    private readonly dialogRef: MatDialogRef<AccountFormDialog, boolean>,
    private readonly accountsApi: AccountsApi,
    @Inject(MAT_DIALOG_DATA) protected readonly data: AccountFormDialogData,
  ) {
    const account = data.account;
    this.editingId = account?.id ?? null;
    this.originalRole = account?.role ?? null;

    if (account) {
      this.form = { ...emptyForm(), ...account, password: '', ssn: '' };
      if (account.hasPhoto) {
        this.accountsApi.getPhoto(account.id).subscribe((blob) => {
          this.photoPreviewUrl.set(URL.createObjectURL(blob));
        });
      }
    } else if (data.duplicateFrom) {
      const a = data.duplicateFrom;
      this.form = {
        ...emptyForm(),
        firstName: `${a.firstName} (Copy)`,
        lastName: a.lastName,
        email: a.email,
        phone: a.phone,
        role: a.role,
      };
    } else {
      this.form = emptyForm();
    }
  }

  ngOnDestroy(): void {
    this.closeCamera();
    const current = this.photoPreviewUrl();
    if (current) {
      URL.revokeObjectURL(current);
    }
  }

  // True once the admin has changed the role away from Employee — the
  // account needs a real username/password at that point (see save()).
  protected promotingFromEmployee(): boolean {
    return this.originalRole === 'Employee' && this.form.role !== 'Employee';
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
    const id = this.editingId;

    if (id === null) {
      if (!this.data.lockedLocationCode && this.data.locationId === null) {
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
          locationId: this.data.locationId,
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
        isOnShiftSchedule: this.form.isOnShiftSchedule,
        canSeeAllSchedules: this.form.canSeeAllSchedules,
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
  // dialog and telling the caller to reload the table.
  private syncPhotoThenClose(accountId: number): void {
    const done = () => this.dialogRef.close(true);

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

  cancel(): void {
    this.dialogRef.close(false);
  }
}
