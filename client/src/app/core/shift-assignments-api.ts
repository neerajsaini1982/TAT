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
  // Set on a cover shift: whose absent shift it stands in for.
  coversAssignmentId: number | null;
  coversAccountFirstName: string | null;
  coversAccountLastName: string | null;
  // Set on an absent shift that has cover: who is covering it.
  coveredByAssignmentId: number | null;
  coveredByAccountFirstName: string | null;
  coveredByAccountLastName: string | null;
  // Set on a cover shift whose employee was already working that day: the
  // shift they had before it was combined with the one they're covering
  // (the shift* fields above describe the combined shift).
  originalShiftName: string | null;
}

// "Covering for Sam R." on a cover shift, "Covered by Alex M." on an absent
// shift that has cover, otherwise null.
export function coverLabel(a: ShiftAssignmentDto): string | null {
  if (a.coversAssignmentId !== null && a.coversAccountFirstName) {
    return `Covering for ${shortName(a.coversAccountFirstName, a.coversAccountLastName)}`;
  }
  if (a.coveredByAssignmentId !== null && a.coveredByAccountFirstName) {
    return `Covered by ${shortName(a.coveredByAccountFirstName, a.coveredByAccountLastName)}`;
  }
  return null;
}

// An absent shift nobody has been put on yet.
export function needsCover(a: ShiftAssignmentDto): boolean {
  return a.isAbsent && a.coveredByAssignmentId === null;
}

function shortName(first: string, last: string | null): string {
  return last ? `${first} ${last.charAt(0)}.` : first;
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

// Mark absent and, optionally, put coverAccountId on the shift — saved
// together or not at all. sickMinutes is admin-only; null leaves it alone.
// confirmCombine acknowledges that someone already working that day has
// their shift replaced: by coverShiftId (one of the location's shifts), or
// when that's null by one combined shift spanning both.
export interface CallOutRequest {
  note: string;
  sickMinutes: number | null;
  coverAccountId: number | null;
  confirmUnavailable: boolean;
  confirmCombine: boolean;
  sendEmail: boolean;
  coverShiftId: number | null;
}

export interface AssignCoverRequest {
  coverAccountId: number;
  confirmUnavailable: boolean;
  confirmCombine: boolean;
  sendEmail: boolean;
  coverShiftId: number | null;
}

export interface CallOutResultDto {
  absent: ShiftAssignmentDto;
  cover: ShiftAssignmentDto | null;
  emailSent: boolean;
}

export interface CoverShiftPreviewDto {
  shiftName: string;
  startTime: string;
  endTime: string;
  scheduledBreaks: ScheduledBreakDto[];
  hours: number;
}

export interface CoverShiftOptionDto {
  id: number;
  name: string;
  startTime: string;
  endTime: string;
}

// ownShift is set when they already work that day; combinedShift is then
// the single shift that would replace it — the one the candidates were
// requested with, or by default their own and the covered one end to end,
// in which case bridgedGapMinutes is any time between the two that it takes
// in. suggestedShiftId is a location shift with exactly that default span. weekScheduledHours has the cover
// applied; overtimeMinutes is how much of that week would be paid at a
// premium. blockedReason is set when they can't be picked at all.
export interface CoverCandidateDto {
  accountId: number;
  firstName: string;
  lastName: string;
  isAvailable: boolean;
  hasEmail: boolean;
  weekScheduledHours: number;
  overtimeMinutes: number;
  ownShift: CoverShiftPreviewDto | null;
  combinedShift: CoverShiftPreviewDto | null;
  bridgedGapMinutes: number;
  suggestedShiftId: number | null;
  blockedReason: string | null;
}

export interface CoverCandidatesDto {
  emailConfigured: boolean;
  currentCoverAccountId: number | null;
  // The location shift the current cover was moved onto; null for an
  // automatically combined one, or when they had the day off.
  currentCoverShiftId: number | null;
  shifts: CoverShiftOptionDto[];
  candidates: CoverCandidateDto[];
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

  // shiftId previews that location shift as the replacement for people
  // already working that day, in place of the automatic combined one.
  getCoverCandidates(id: number, shiftId?: number) {
    const query = shiftId ? `?shiftId=${shiftId}` : '';
    return this.http.get<CoverCandidatesDto>(`${this.base}/${id}/cover-candidates${query}`);
  }

  callOut(id: number, request: CallOutRequest) {
    return this.http.put<CallOutResultDto>(`${this.base}/${id}/call-out`, request);
  }

  assignCover(id: number, request: AssignCoverRequest) {
    return this.http.put<CallOutResultDto>(`${this.base}/${id}/cover`, request);
  }

  removeCover(id: number) {
    return this.http.delete<ShiftAssignmentDto>(`${this.base}/${id}/cover`);
  }

  publish(weekStartDate: string, locationCode?: string, sendEmail = false) {
    return this.http.post<void>(`${this.base}/publish`, { weekStartDate, locationCode, sendEmail });
  }
}
