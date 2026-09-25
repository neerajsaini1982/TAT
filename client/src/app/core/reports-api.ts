import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';

export interface DailyHoursDto {
  date: string;
  workedMinutes: number | null;
  breakMinutes: number;
  lunchMinutes: number;
  netWorkedMinutes: number | null;
  scheduledMinutes: number | null;
  // netWorkedMinutes split by pay rate: regular + overtime (1.5x) + double
  // time (2x). All 0 while the day is still clocked in.
  regularMinutes: number;
  overtimeMinutes: number;
  doubleTimeMinutes: number;
  isAbsent: boolean;
  absenceNote: string | null;
  leftEarly: boolean;
  leftEarlyNote: string | null;
  stillClockedIn: boolean;
  hasLongBreak: boolean;
  hasLongLunch: boolean;
  notes: string[];
  sickMinutes: number;
  shiftAssignmentId: number | null;
  manualSickNotes: string[];
}

export interface EmployeeHoursReportDto {
  employeeId: number;
  fullName: string;
  // All of this employee's worked time is regular, whatever the location's
  // overtime rules say (see AccountDto.isOvertimeExempt).
  isOvertimeExempt: boolean;
  totalWorkedMinutes: number;
  totalBreakMinutes: number;
  totalLunchMinutes: number;
  totalNetWorkedMinutes: number;
  totalScheduledMinutes: number;
  totalRegularMinutes: number;
  totalOvertimeMinutes: number;
  totalDoubleTimeMinutes: number;
  absentDays: number;
  openEntryDays: number;
  totalSickMinutes: number;
  days: DailyHoursDto[];
}

// See ReportsController.EmailHoursReport. employeeIds null = everyone in the
// report; testToAddress set = send one [TEST] copy there instead.
export interface EmailHoursReportRequest {
  locationCode: string;
  startDate: string;
  endDate: string;
  employeeIds: number[] | null;
  testToAddress: string | null;
}

export interface EmailHoursReportResultDto {
  sent: string[];
  skippedNoEmail: string[];
  failed: string[];
}

@Service()
export class ReportsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/reports`;

  // Admin/Sa only — nested by-employee/by-day worked/break/lunch/absence
  // breakdown for a location over a date range (issue #18). See
  // ReportsController.GetHoursReport.
  getHoursReport(locationCode: string, startDate: string, endDate: string) {
    const params = new URLSearchParams({ locationCode, startDate, endDate });
    return this.http.get<EmployeeHoursReportDto[]>(`${this.base}/hours?${params.toString()}`);
  }

  // Admin/Sa only — emails each employee their own hours for the range using
  // the PayrollHours template.
  emailHoursReport(request: EmailHoursReportRequest) {
    return this.http.post<EmailHoursReportResultDto>(`${this.base}/hours/email`, request);
  }
}
