import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';

export interface SickTimeEntryDto {
  id: number;
  accountId: number;
  accountFirstName: string;
  accountLastName: string;
  date: string;
  minutes: number;
  note: string | null;
  recordedByAccountId: number;
  recordedAt: string;
}

export interface CreateSickTimeEntryRequest {
  accountId: number;
  date: string;
  minutes: number;
  note: string | null;
}

// Admin/Sa only — records sick hours for a day the employee had no shift
// assignment at all. See ShiftAssignmentsApi.setSickMinutes for the
// already-scheduled case. ReportsApi.getHoursReport folds both into the
// same per-day/per-employee sick totals.
@Service()
export class SickTimeEntriesApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/sick-time-entries`;

  create(request: CreateSickTimeEntryRequest) {
    return this.http.post<SickTimeEntryDto>(this.base, request);
  }
}
