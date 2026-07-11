import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';

export interface TimeEntryDto {
  id: number;
  shiftAssignmentId: number;
  accountId: number;
  clockInAt: string;
  breakStartAt: string | null;
  breakEndAt: string | null;
  lunchStartAt: string | null;
  lunchEndAt: string | null;
  clockOutAt: string | null;
}

@Service()
export class TimeEntriesApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/time-entries`;

  getMine(date: string) {
    return this.http.get<TimeEntryDto[]>(`${this.base}/mine?date=${encodeURIComponent(date)}`);
  }

  clockIn(shiftAssignmentId: number) {
    return this.http.post<TimeEntryDto>(`${this.base}/clock-in`, { shiftAssignmentId });
  }

  breakStart(id: number) {
    return this.http.post<TimeEntryDto>(`${this.base}/${id}/break-start`, {});
  }

  breakEnd(id: number) {
    return this.http.post<TimeEntryDto>(`${this.base}/${id}/break-end`, {});
  }

  lunchStart(id: number) {
    return this.http.post<TimeEntryDto>(`${this.base}/${id}/lunch-start`, {});
  }

  lunchEnd(id: number) {
    return this.http.post<TimeEntryDto>(`${this.base}/${id}/lunch-end`, {});
  }

  clockOut(id: number) {
    return this.http.post<TimeEntryDto>(`${this.base}/${id}/clock-out`, {});
  }
}
