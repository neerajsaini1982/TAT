import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';
import { Role } from './auth';

export type EmploymentType = 'FullTime' | 'PartTime';

export interface AccountDto {
  id: number;
  username: string;
  firstName: string;
  lastName: string;
  email: string;
  phone: string;
  role: Role;
  isActive: boolean;
  // Whether this employee shows up as a row on the schedule screens — see
  // AvailabilityApi.getForLocation's onShiftScheduleOnly param.
  isOnShiftSchedule: boolean;
  // Per-employee override that grants a roster view of everyone's shifts
  // regardless of role/location Schedule Visibility settings. Defaults false;
  // does not affect clock-in/out, which always stays self-service only.
  canSeeAllSchedules: boolean;
  userCode: string | null;
  locationCode: string | null;
  // Populated by the ADP employee-directory import (see employee-import-api.ts);
  // null for accounts created by hand.
  birthDate: string | null;
  jobTitle: string | null;
  address1: string | null;
  address2: string | null;
  city: string | null;
  state: string | null;
  zipcode: string | null;
  supervisor: string | null;
  adpStatus: string | null;
  hourlyRate: number | null;
  // Masked ("***-**-1234") — the real SSN never leaves the server.
  ssnMasked: string | null;
  dateOfBirth: string | null;
  hireDate: string | null;
  employmentType: EmploymentType | null;
  hasPhoto: boolean;
}

export interface CreateAccountRequest {
  // Ignored (and not required) when role is 'Employee'; the server generates
  // both since employees log in with a UserCode instead.
  username?: string;
  password?: string;
  firstName: string;
  lastName: string;
  email: string;
  phone: string;
  role: Role;
  locationId: number | null;
  hourlyRate: number | null;
  // 9 digits, or blank/undefined to leave unset.
  ssn?: string;
  dateOfBirth: string | null;
  hireDate: string | null;
  employmentType: EmploymentType | null;
}

export interface UpdateMineRequest {
  email: string;
  phone: string;
}

export interface UpdateAccountRequest {
  firstName: string;
  lastName: string;
  email: string;
  phone: string;
  isActive: boolean;
  isOnShiftSchedule: boolean;
  canSeeAllSchedules: boolean;
  role: Role;
  // Only required when role moves away from Employee.
  username?: string;
  password?: string;
  hourlyRate: number | null;
  // 9 digits to replace the stored SSN; blank/undefined leaves it unchanged.
  ssn?: string;
  dateOfBirth: string | null;
  hireDate: string | null;
  employmentType: EmploymentType | null;
}

@Service()
export class AccountsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/accounts`;

  getAll(locationCode?: string) {
    const url = locationCode ? `${this.base}?locationCode=${encodeURIComponent(locationCode)}` : this.base;
    return this.http.get<AccountDto[]>(url);
  }

  getOne(id: number) {
    return this.http.get<AccountDto>(`${this.base}/${id}`);
  }

  create(request: CreateAccountRequest) {
    return this.http.post<AccountDto>(this.base, request);
  }

  update(id: number, request: UpdateAccountRequest) {
    return this.http.put<AccountDto>(`${this.base}/${id}`, request);
  }

  delete(id: number) {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  resetCode(id: number) {
    return this.http.post<AccountDto>(`${this.base}/${id}/reset-code`, {});
  }

  resetMyCode() {
    return this.http.post<AccountDto>(`${this.base}/mine/reset-code`, {});
  }

  // Lets the signed-in account pick its own 6-digit login code instead of a
  // random one. Server validates the digit-count/format and uniqueness.
  setMyCode(userCode: string) {
    return this.http.post<AccountDto>(`${this.base}/mine/set-code`, { userCode });
  }

  getMine() {
    return this.http.get<AccountDto>(`${this.base}/mine`);
  }

  updateMine(request: UpdateMineRequest) {
    return this.http.put<AccountDto>(`${this.base}/mine`, request);
  }

  // Emails the employee their login link and user code (LoginCredentials
  // template). loginLink is built by the caller from window.location.origin.
  sendCredentials(id: number, loginLink: string) {
    return this.http.post<void>(`${this.base}/${id}/send-credentials`, { loginLink });
  }

  // Fetched as a blob (not a plain <img src>) because the auth token is only
  // attached to HttpClient requests via auth-interceptor.ts.
  getPhoto(id: number) {
    return this.http.get(`${this.base}/${id}/photo`, { responseType: 'blob' });
  }

  uploadPhoto(id: number, file: File) {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<AccountDto>(`${this.base}/${id}/photo`, formData);
  }

  deletePhoto(id: number) {
    return this.http.delete<AccountDto>(`${this.base}/${id}/photo`);
  }
}
