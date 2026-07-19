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
  break2StartAt: string | null;
  break2EndAt: string | null;
  clockOutAt: string | null;
  clockedOutByAccountId: number | null;
  note: string | null;
  editedByAccountId: number | null;
  editedAt: string | null;
}

export interface AdminEditTimeEntryRequest {
  clockInAt: string;
  breakStartAt: string | null;
  breakEndAt: string | null;
  lunchStartAt: string | null;
  lunchEndAt: string | null;
  break2StartAt: string | null;
  break2EndAt: string | null;
  clockOutAt: string | null;
  note: string;
}

@Service()
export class TimeEntriesApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/time-entries`;

  getMine(date: string) {
    return this.http.get<TimeEntryDto[]>(`${this.base}/mine?date=${encodeURIComponent(date)}`);
  }

  // Lead/Admin only: every entry for a location/date, so the admin
  // schedule grid can see who's clocked in without being limited to the
  // caller's own entries (see TimeEntriesController.GetForLocation).
  getForLocation(locationCode: string, date: string) {
    const params = new URLSearchParams({ locationCode, date });
    return this.http.get<TimeEntryDto[]>(`${this.base}?${params.toString()}`);
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

  break2Start(id: number) {
    return this.http.post<TimeEntryDto>(`${this.base}/${id}/break2-start`, {});
  }

  break2End(id: number) {
    return this.http.post<TimeEntryDto>(`${this.base}/${id}/break2-end`, {});
  }

  clockOut(id: number) {
    return this.http.post<TimeEntryDto>(`${this.base}/${id}/clock-out`, {});
  }

  // Lead/Admin only: closes someone else's entry out with a required note
  // explaining why (left early, etc.) — see TimeEntriesController.AdminClockOut.
  adminClockOut(id: number, note: string) {
    return this.http.post<TimeEntryDto>(`${this.base}/${id}/admin-clock-out`, { note });
  }

  // Lead/Admin only: sets every punch on a shift's entry directly, creating
  // it if the employee never clocked in at all — see
  // TimeEntriesController.AdminEditTimes.
  adminEditTimes(shiftAssignmentId: number, request: AdminEditTimeEntryRequest) {
    return this.http.put<TimeEntryDto>(`${this.base}/by-assignment/${shiftAssignmentId}/admin-edit`, request);
  }
}
