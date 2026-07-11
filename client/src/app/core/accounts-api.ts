import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';
import { Role } from './auth';

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
}

export interface UpdateAccountRequest {
  firstName: string;
  lastName: string;
  email: string;
  phone: string;
  isActive: boolean;
}

@Service()
export class AccountsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/accounts`;

  getAll(locationCode?: string) {
    const url = locationCode ? `${this.base}?locationCode=${encodeURIComponent(locationCode)}` : this.base;
    return this.http.get<AccountDto[]>(url);
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
}
