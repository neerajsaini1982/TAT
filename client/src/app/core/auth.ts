import { Service, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

import { API_BASE_URL } from './api-config';

export type Role = 'Sa' | 'Admin' | 'Lead' | 'Employee';

export interface AuthResponse {
  token: string;
  accountId: number;
  username: string;
  firstName: string;
  lastName: string;
  role: Role;
  locationCode: string | null;
  locationName: string | null;
}

const STORAGE_KEY = 'tat-auth';

@Service()
export class Auth {
  private readonly http = inject(HttpClient);

  readonly session = signal<AuthResponse | null>(this.readStored());
  readonly isAuthenticated = computed(() => this.session() !== null);
  readonly accountId = computed(() => this.session()?.accountId ?? null);
  readonly role = computed(() => this.session()?.role ?? null);
  readonly locationCode = computed(() => this.session()?.locationCode ?? null);
  readonly locationName = computed(() => this.session()?.locationName ?? null);
  readonly token = computed(() => this.session()?.token ?? null);

  async saLogin(username: string, password: string): Promise<void> {
    const res = await firstValueFrom(
      this.http.post<AuthResponse>(`${API_BASE_URL}/auth/sa-login`, { username, password })
    );
    this.setSession(res);
  }

  async adminLogin(locationCode: string, username: string, password: string): Promise<void> {
    const res = await firstValueFrom(
      this.http.post<AuthResponse>(`${API_BASE_URL}/auth/admin-login`, { locationCode, username, password })
    );
    this.setSession(res);
  }

  async employeeLogin(locationCode: string, userCode: string): Promise<void> {
    const res = await firstValueFrom(
      this.http.post<AuthResponse>(`${API_BASE_URL}/auth/employee-login`, { locationCode, userCode })
    );
    this.setSession(res);
  }

  logout(): void {
    this.session.set(null);
    localStorage.removeItem(STORAGE_KEY);
  }

  private setSession(res: AuthResponse): void {
    this.session.set(res);
    localStorage.setItem(STORAGE_KEY, JSON.stringify(res));
  }

  private readStored(): AuthResponse | null {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return null;
    }

    // A session cached before accountId was added to AuthResponse (see
    // #36) would otherwise silently resolve accountId() to null, making
    // every isMine check in CurrentWeekSchedule false — discard it and
    // fall back to signed-out so the user re-logs-in and gets a complete
    // session instead.
    const parsed = JSON.parse(raw) as Partial<AuthResponse>;
    if (typeof parsed.accountId !== 'number') {
      localStorage.removeItem(STORAGE_KEY);
      return null;
    }

    return parsed as AuthResponse;
  }
}
