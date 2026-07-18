import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';

export interface ShiftAssignmentDto {
  id: number;
  shiftId: number;
  shiftName: string;
  shiftStartTime: string;
  shiftEndTime: string;
  hours: number;
  accountId: number;
  accountFirstName: string;
  accountLastName: string;
  date: string;
  isPublished: boolean;
  isAbsent: boolean;
  absenceNote: string | null;
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

export interface TodayScheduleEntryDto {
  shiftName: string;
  shiftStartTime: string;
  shiftEndTime: string;
  employeeName: string;
  isClockedIn: boolean;
  isClockedOut: boolean;
}

@Service()
export class ShiftAssignmentsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/shift-assignments`;

  getMine() {
    return this.http.get<ShiftAssignmentDto[]>(`${this.base}/mine`);
  }

  getToday(locationCode: string) {
    const params = new URLSearchParams({ locationCode });
    return this.http.get<TodayScheduleEntryDto[]>(`${this.base}/today?${params.toString()}`);
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

  publish(weekStartDate: string, locationCode?: string) {
    return this.http.post<void>(`${this.base}/publish`, { weekStartDate, locationCode });
  }
}
