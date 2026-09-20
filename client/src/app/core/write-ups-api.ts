import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';

// Least to most serious — matches the server's WriteUpSeverity enum names.
export type WriteUpSeverity = 'Low' | 'Normal' | 'High' | 'Critical';

export const WRITE_UP_SEVERITIES: WriteUpSeverity[] = ['Low', 'Normal', 'High', 'Critical'];

export const DEFAULT_WRITE_UP_SEVERITY: WriteUpSeverity = 'Normal';

// Server-side cap (WriteUpsController.MaxDescriptionLength).
export const WRITE_UP_MAX_DESCRIPTION_LENGTH = 2000;

export interface WriteUpDto {
  id: number;
  accountId: number;
  date: string;
  description: string;
  severity: WriteUpSeverity;
  createdByAccountId: number;
  createdByName: string;
  createdAt: string;
}

export interface WriteUpRequest {
  date: string;
  description: string;
  severity: WriteUpSeverity;
}

// Any signed-in employee can list their own write-ups; everything else
// (another employee's list, create, edit, delete) is Admin/Sa only and
// 404s for anyone else.
@Service()
export class WriteUpsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/accounts`;

  list(accountId: number) {
    return this.http.get<WriteUpDto[]>(`${this.base}/${accountId}/write-ups`);
  }

  create(accountId: number, request: WriteUpRequest) {
    return this.http.post<WriteUpDto>(`${this.base}/${accountId}/write-ups`, request);
  }

  update(accountId: number, writeUpId: number, request: WriteUpRequest) {
    return this.http.put<WriteUpDto>(`${this.base}/${accountId}/write-ups/${writeUpId}`, request);
  }

  remove(accountId: number, writeUpId: number) {
    return this.http.delete<void>(`${this.base}/${accountId}/write-ups/${writeUpId}`);
  }
}
