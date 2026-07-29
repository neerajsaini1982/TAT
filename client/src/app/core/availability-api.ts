import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';

export interface AvailabilityDayDto {
  date: string;
  isAvailable: boolean;
  startTime: string | null;
  endTime: string | null;
}

export interface AvailabilityDto {
  id: number;
  accountId: number;
  username: string;
  firstName: string;
  lastName: string;
  weekStartDate: string;
  isSubmitted: boolean;
  submittedAt: string | null;
  days: AvailabilityDayDto[];
}

export interface SaveAvailabilityRequest {
  weekStartDate: string;
  days: AvailabilityDayDto[];
  submit: boolean;
}

export interface CopyPreviousWeekResult {
  copied: number;
  skipped: number;
}

@Service()
export class AvailabilityApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/availability`;

  getMine(weekStartDate: string) {
    return this.http.get<AvailabilityDto>(`${this.base}/mine?weekStartDate=${weekStartDate}`);
  }

  saveMine(request: SaveAvailabilityRequest) {
    return this.http.put<AvailabilityDto>(`${this.base}/mine`, request);
  }

  // Admin/Sa override: bypasses the employee-facing submit/deadline lock.
  saveForAccount(accountId: number, request: SaveAvailabilityRequest) {
    return this.http.put<AvailabilityDto>(`${this.base}/${accountId}`, request);
  }

  getForLocation(weekStartDate: string, locationCode?: string) {
    const params = new URLSearchParams({ weekStartDate });
    if (locationCode) {
      params.set('locationCode', locationCode);
    }
    return this.http.get<AvailabilityDto[]>(`${this.base}?${params.toString()}`);
  }

  // Admin bulk action: fills every schedulable employee's weekStartDate
  // availability from the week before, in one call. Already-submitted
  // target weeks are left untouched server-side; see CopyPreviousWeek in
  // AvailabilityController.
  copyPreviousWeekForLocation(weekStartDate: string, locationCode?: string) {
    const params = new URLSearchParams({ weekStartDate });
    if (locationCode) {
      params.set('locationCode', locationCode);
    }
    return this.http.post<CopyPreviousWeekResult>(`${this.base}/copy-previous-week?${params.toString()}`, {});
  }
}
