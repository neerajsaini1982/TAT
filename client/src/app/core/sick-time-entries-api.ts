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

// Admin/Sa only — the one way sick hours are recorded (the payroll report's
// "Add Sick Hours Manually" button), for any date whether or not the
// employee had a shift. ReportsApi.getHoursReport adds these to any sick
// minutes already stored on the day's shift assignment.
@Service()
export class SickTimeEntriesApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/sick-time-entries`;

  create(request: CreateSickTimeEntryRequest) {
    return this.http.post<SickTimeEntryDto>(this.base, request);
  }
}
