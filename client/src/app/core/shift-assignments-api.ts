import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';
import { ScheduledBreakDto } from './shifts-api';

export interface ShiftAssignmentDto {
  id: number;
  shiftId: number;
  shiftName: string;
  shiftStartTime: string;
  shiftEndTime: string;
  scheduledBreaks: ScheduledBreakDto[];
  hours: number;
  accountId: number;
  accountFirstName: string;
  accountLastName: string;
  date: string;
  isPublished: boolean;
  isAbsent: boolean;
  absenceNote: string | null;
  absentMarkedByAccountId: number | null;
  absentMarkedAt: string | null;
  sickMinutes: number;
  sickHoursRecordedByAccountId: number | null;
  sickHoursRecordedAt: string | null;
}

export interface CreateShiftAssignmentRequest {
  shiftId: number;
  accountId: number;
  date: string;
}

export interface MoveShiftAssignmentRequest {
  accountId: number;
  date: string;
}

export interface MarkAbsentRequest {
  isAbsent: boolean;
  note: string | null;
}

export interface SetSickMinutesRequest {
  sickMinutes: number;
}

@Service()
export class ShiftAssignmentsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/shift-assignments`;

  getMine() {
    return this.http.get<ShiftAssignmentDto[]>(`${this.base}/mine`);
  }

  getForWeek(weekStartDate: string, locationCode?: string) {
    const params = new URLSearchParams({ weekStartDate });
    if (locationCode) {
      params.set('locationCode', locationCode);
    }
    return this.http.get<ShiftAssignmentDto[]>(`${this.base}?${params.toString()}`);
  }

  create(request: CreateShiftAssignmentRequest) {
    return this.http.post<ShiftAssignmentDto>(this.base, request);
  }

  move(id: number, request: MoveShiftAssignmentRequest) {
    return this.http.put<ShiftAssignmentDto>(`${this.base}/${id}/move`, request);
  }

  delete(id: number) {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  markAbsent(id: number, request: MarkAbsentRequest) {
    return this.http.put<ShiftAssignmentDto>(`${this.base}/${id}/absent`, request);
  }

  setSickMinutes(id: number, request: SetSickMinutesRequest) {
    return this.http.put<ShiftAssignmentDto>(`${this.base}/${id}/sick-hours`, request);
  }

  publish(weekStartDate: string, locationCode?: string, sendEmail = false) {
    return this.http.post<void>(`${this.base}/publish`, { weekStartDate, locationCode, sendEmail });
  }
}
