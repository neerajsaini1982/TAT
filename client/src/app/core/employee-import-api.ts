import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';
import { AccountDto } from './accounts-api';

// One row parsed from an ADP Employee Directory export. willCreate/skipReason
// are set server-side — never true for a row matching an existing account.
export interface EmployeeImportRowDto {
  firstName: string;
  lastName: string;
  birthDate: string | null;
  jobTitle: string | null;
  address1: string | null;
  address2: string | null;
  city: string | null;
  state: string | null;
  zipcode: string | null;
  phone: string | null;
  supervisor: string | null;
  adpStatus: string | null;
  isActive: boolean;
  willCreate: boolean;
  skipReason: string | null;
}

export interface EmployeeImportPreviewResult {
  rows: EmployeeImportRowDto[];
  totalRows: number;
  newCount: number;
  skippedCount: number;
}

export interface EmployeeImportCommitResult {
  createdCount: number;
  skippedCount: number;
  created: AccountDto[];
}

@Service()
export class EmployeeImportApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/employee-import`;

  preview(file: File, locationCode?: string) {
    const formData = new FormData();
    formData.append('file', file);
    const params = locationCode ? `?locationCode=${encodeURIComponent(locationCode)}` : '';
    return this.http.post<EmployeeImportPreviewResult>(`${this.base}/preview${params}`, formData);
  }

  commit(rows: EmployeeImportRowDto[], locationCode?: string) {
    return this.http.post<EmployeeImportCommitResult>(`${this.base}/commit`, { rows, locationCode });
  }
}
