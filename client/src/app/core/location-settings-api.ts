import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';

export type TimeFormat = 'TwelveHour' | 'TwentyFourHour';
export type DateFormat = 'MmDdYyyy' | 'DdMmYyyy' | 'YyyyMmDd' | 'DdMmmYyyy' | 'MmmDdYyyy';

export interface LocationSettingsDto {
  timeFormat: TimeFormat;
  dateFormat: DateFormat;
  timeZone: string;
  availabilityDays: number;
  clockInWindowMinutes: number;
  smtpHost: string | null;
  smtpPort: number | null;
  smtpUsername: string | null;
  smtpUseSsl: boolean;
  smtpFromAddress: string | null;
  smtpFromName: string | null;
  hasSmtpPassword: boolean;
}

export interface UpdateLocationSettingsRequest {
  timeFormat: TimeFormat;
  dateFormat: DateFormat;
  timeZone: string;
  availabilityDays: number;
  clockInWindowMinutes: number;
  smtpHost: string | null;
  smtpPort: number | null;
  smtpUsername: string | null;
  smtpPassword: string | null;
  smtpUseSsl: boolean;
  smtpFromAddress: string | null;
  smtpFromName: string | null;
}

// Minimal subset any signed-in account can read (see LocationSettingsController.GetMine).
export interface EmployeeLocationSettingsDto {
  clockInWindowMinutes: number;
}

@Service()
export class LocationSettingsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/location-settings`;

  get(locationCode?: string) {
    const params = locationCode ? `?locationCode=${encodeURIComponent(locationCode)}` : '';
    return this.http.get<LocationSettingsDto>(`${this.base}${params}`);
  }

  getMine() {
    return this.http.get<EmployeeLocationSettingsDto>(`${this.base}/mine`);
  }

  update(request: UpdateLocationSettingsRequest, locationCode?: string) {
    const params = locationCode ? `?locationCode=${encodeURIComponent(locationCode)}` : '';
    return this.http.put<LocationSettingsDto>(`${this.base}${params}`, request);
  }
}
