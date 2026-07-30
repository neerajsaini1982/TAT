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

export interface UpdateAccountRequest {
  firstName: string;
  lastName: string;
  email: string;
  phone: string;
  isActive: boolean;
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

  // Emails the employee their login link and user code (LoginCredentials
  // template). loginLink is built by the caller from window.location.origin.
  sendCredentials(id: number, loginLink: string) {
    return this.http.post<void>(`${this.base}/${id}/send-credentials`, { loginLink });
  }
}
