import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';

export interface DailyHoursDto {
  date: string;
  workedMinutes: number | null;
  breakMinutes: number;
  lunchMinutes: number;
  netWorkedMinutes: number | null;
  isAbsent: boolean;
  absenceNote: string | null;
  stillClockedIn: boolean;
  hasLongBreak: boolean;
  hasLongLunch: boolean;
  notes: string[];
}

export interface EmployeeHoursReportDto {
  employeeId: number;
  fullName: string;
  totalWorkedMinutes: number;
  totalBreakMinutes: number;
  totalLunchMinutes: number;
  totalNetWorkedMinutes: number;
  absentDays: number;
  openEntryDays: number;
  days: DailyHoursDto[];
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
}
