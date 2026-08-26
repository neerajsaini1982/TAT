import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';
import { BreakKind } from './shifts-api';
import { ShiftAssignmentDto } from './shift-assignments-api';
import { TimeEntryDto } from './time-entries-api';

// Backs the kiosk device's own session (see Auth.kioskLogin) — every call
// here is scoped server-side to that session's own locationCode claim, and
// every punch additionally verifies a per-employee PIN (KioskPinService),
// since the kiosk's own token never identifies one employee.
@Service()
export class KioskApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/kiosk`;

  getSchedule() {
    return this.http.get<ShiftAssignmentDto[]>(`${this.base}/schedule`);
  }

  getTimeEntries() {
    return this.http.get<TimeEntryDto[]>(`${this.base}/time-entries`);
  }

  clockIn(shiftAssignmentId: number, accountId: number, pin: string) {
    return this.http.post<TimeEntryDto>(`${this.base}/clock-in`, { shiftAssignmentId, accountId, pin });
  }

  startSegment(entryId: number, accountId: number, pin: string, kind: BreakKind) {
    return this.http.post<TimeEntryDto>(`${this.base}/${entryId}/segments/start`, { accountId, pin, kind });
  }

  endSegment(entryId: number, accountId: number, pin: string) {
    return this.http.post<TimeEntryDto>(`${this.base}/${entryId}/segments/end`, { accountId, pin });
  }

  clockOut(entryId: number, accountId: number, pin: string) {
    return this.http.post<TimeEntryDto>(`${this.base}/${entryId}/clock-out`, { accountId, pin });
  }
}
