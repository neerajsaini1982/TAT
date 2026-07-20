import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';

export type BreakKind = 'Break' | 'Lunch';

// StartTime/EndTime are "HH:mm:ss" (API's TimeOnly shape) — wall-clock in
// the shift's own location, no timezone conversion needed.
export interface ScheduledBreakDto {
  kind: BreakKind;
  startTime: string;
  endTime: string;
}

export interface ShiftDto {
  id: number;
  name: string;
  startTime: string;
  endTime: string;
  scheduledBreaks: ScheduledBreakDto[];
  isActive: boolean;
  locationCode: string;
}

export interface CreateShiftRequest {
  name: string;
  startTime: string;
  endTime: string;
  scheduledBreaks: ScheduledBreakDto[];
  locationId: number | null;
}

export interface UpdateShiftRequest {
  name: string;
  startTime: string;
  endTime: string;
  scheduledBreaks: ScheduledBreakDto[];
  isActive: boolean;
}

@Service()
export class ShiftsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/shifts`;

  getAll(locationCode?: string) {
    const url = locationCode ? `${this.base}?locationCode=${encodeURIComponent(locationCode)}` : this.base;
    return this.http.get<ShiftDto[]>(url);
  }

  create(request: CreateShiftRequest) {
    return this.http.post<ShiftDto>(this.base, request);
  }

  update(id: number, request: UpdateShiftRequest) {
    return this.http.put<ShiftDto>(`${this.base}/${id}`, request);
  }

  delete(id: number) {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
