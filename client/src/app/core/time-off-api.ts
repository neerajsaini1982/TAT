import { Service, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';

import { API_BASE_URL } from './api-config';

export type TimeOffStatus = 'Pending' | 'Approved' | 'Denied' | 'Cancelled';
export type TimeOffEventAction = 'Submitted' | 'Approved' | 'Denied' | 'Cancelled' | 'Edited';

export interface TimeOffTypeDto {
  id: number;
  name: string;
  isPaid: boolean;
  isActive: boolean;
  sortOrder: number;
  color: string | null;
  payrollCode: string | null;
  isUsed: boolean;
}

export type SaveTimeOffType = Omit<TimeOffTypeDto, 'id' | 'isUsed'>;

export interface TimeOffBlackoutDto {
  id: number;
  name: string;
  startDate: string;
  endDate: string;
}

export interface TimeOffSettingsDto {
  timeOffEnabled: boolean;
  timeOffMinNoticeDays: number | null;
  timeOffFullDayMinutes: number;
}

export interface TimeOffDayDto {
  date: string;
  minutes: number;
}

export interface TimeOffRequestDto {
  id: number;
  accountId: number;
  employeeName: string;
  timeOffTypeId: number;
  typeName: string;
  typeColor: string | null;
  startDate: string;
  endDate: string;
  isPartialDay: boolean;
  partialMinutes: number | null;
  note: string | null;
  status: TimeOffStatus;
  isPaid: boolean | null;
  reviewedByName: string | null;
  reviewedAt: string | null;
  reviewComment: string | null;
  createdAt: string;
  totalMinutes: number;
  days: TimeOffDayDto[];
  // Admins only.
  history: { action: TimeOffEventAction; actorName: string; at: string; comment: string | null }[] | null;
}

export interface SubmitTimeOff {
  timeOffTypeId: number;
  startDate: string;
  endDate: string;
  isPartialDay: boolean;
  partialMinutes: number | null;
  note: string | null;
}

export interface AdminCreateTimeOff extends SubmitTimeOff {
  accountId: number;
  removeConflictingAssignments: boolean;
  days: TimeOffDayDto[] | null;
}

export interface TimeOffPreviewDto {
  conflicts: { shiftAssignmentId: number; date: string; shiftName: string; startTime: string; endTime: string; isPublished: boolean }[];
  suggestedDays: TimeOffDayDto[];
}

export interface TimeOffCalendarEntryDto {
  requestId: number;
  accountId: number;
  employeeName: string;
  date: string;
  typeName: string;
  typeColor: string | null;
  isPartialDay: boolean;
  minutes: number;
}

// Time-off requests (issue #100). Only the location's admin reviews; anyone
// signed in can request, see their own, and cancel their own.
@Service()
export class TimeOffApi {
  private readonly http = inject(HttpClient);
  private readonly base = API_BASE_URL;

  mine() {
    return this.http.get<TimeOffRequestDto[]>(`${this.base}/time-off-requests/mine`);
  }

  submit(request: SubmitTimeOff) {
    return this.http.post<TimeOffRequestDto>(`${this.base}/time-off-requests`, request);
  }

  cancel(id: number, comment: string | null = null) {
    return this.http.put<TimeOffRequestDto>(`${this.base}/time-off-requests/${id}/cancel`, { comment });
  }

  forLocation(locationCode: string, status?: TimeOffStatus) {
    let params = new HttpParams().set('locationCode', locationCode);
    if (status) params = params.set('status', status);
    return this.http.get<TimeOffRequestDto[]>(`${this.base}/time-off-requests`, { params });
  }

  pendingCount(locationCode: string) {
    return this.http.get<number>(`${this.base}/time-off-requests/pending-count`, { params: { locationCode } });
  }

  preview(id: number) {
    return this.http.get<TimeOffPreviewDto>(`${this.base}/time-off-requests/${id}/preview`);
  }

  previewNew(request: AdminCreateTimeOff) {
    return this.http.post<TimeOffPreviewDto>(`${this.base}/time-off-requests/preview`, request);
  }

  approve(id: number, comment: string | null, removeConflictingAssignments: boolean, days: TimeOffDayDto[] | null) {
    return this.http.put<TimeOffRequestDto>(`${this.base}/time-off-requests/${id}/approve`, {
      comment,
      removeConflictingAssignments,
      days,
    });
  }

  deny(id: number, comment: string) {
    return this.http.put<TimeOffRequestDto>(`${this.base}/time-off-requests/${id}/deny`, { comment });
  }

  adminCreate(request: AdminCreateTimeOff) {
    return this.http.post<TimeOffRequestDto>(`${this.base}/time-off-requests/admin`, request);
  }

  calendar(locationCode: string, from: string, to: string) {
    return this.http.get<TimeOffCalendarEntryDto[]>(`${this.base}/time-off-requests/calendar`, {
      params: { locationCode, from, to },
    });
  }

  types(locationCode: string, all = false) {
    return this.http.get<TimeOffTypeDto[]>(`${this.base}/time-off-types${all ? '/all' : ''}`, { params: { locationCode } });
  }

  createType(locationCode: string, type: SaveTimeOffType) {
    return this.http.post<TimeOffTypeDto>(`${this.base}/time-off-types`, type, { params: { locationCode } });
  }

  updateType(id: number, type: SaveTimeOffType) {
    return this.http.put<TimeOffTypeDto>(`${this.base}/time-off-types/${id}`, type);
  }

  deleteType(id: number) {
    return this.http.delete<void>(`${this.base}/time-off-types/${id}`);
  }

  blackouts(locationCode: string) {
    return this.http.get<TimeOffBlackoutDto[]>(`${this.base}/time-off-blackouts`, { params: { locationCode } });
  }

  createBlackout(locationCode: string, blackout: Omit<TimeOffBlackoutDto, 'id'>) {
    return this.http.post<TimeOffBlackoutDto>(`${this.base}/time-off-blackouts`, blackout, { params: { locationCode } });
  }

  deleteBlackout(id: number) {
    return this.http.delete<void>(`${this.base}/time-off-blackouts/${id}`);
  }

  settings(locationCode: string) {
    return this.http.get<TimeOffSettingsDto>(`${this.base}/time-off-settings`, { params: { locationCode } });
  }

  saveSettings(locationCode: string, settings: TimeOffSettingsDto) {
    return this.http.put<TimeOffSettingsDto>(`${this.base}/time-off-settings`, settings, { params: { locationCode } });
  }
}

// "8h", "7h 30m", "45m".
export function formatTimeOffMinutes(minutes: number): string {
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  if (h === 0) return `${m}m`;
  return m === 0 ? `${h}h` : `${h}h ${m}m`;
}

export function timeOffDateRange(r: { startDate: string; endDate: string }): string {
  const fmt = (iso: string) => {
    const [y, mo, d] = iso.split('-').map(Number);
    return new Date(y, mo - 1, d).toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' });
  };
  return r.startDate === r.endDate ? fmt(r.startDate) : `${fmt(r.startDate)} – ${fmt(r.endDate)}`;
}

export function apiError(err: unknown, fallback: string): string {
  const body = (err as { error?: unknown })?.error;
  if (typeof body === 'string' && body.trim()) return body;
  const title = (body as { title?: string })?.title;
  return title ?? fallback;
}
