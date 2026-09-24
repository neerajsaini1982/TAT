import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';

// Least to most serious — matches the server's WriteUpSeverity enum names.
export type WriteUpSeverity = 'Low' | 'Normal' | 'High' | 'Critical';

export const WRITE_UP_SEVERITIES: WriteUpSeverity[] = ['Low', 'Normal', 'High', 'Critical'];

export const DEFAULT_WRITE_UP_SEVERITY: WriteUpSeverity = 'Normal';

// The step of progressive discipline (matches the server's WriteUpType).
// Separate from severity: severity is how serious the incident was, type is
// which step of discipline this write-up is.
export type WriteUpType = 'Note' | 'Verbal' | 'Written' | 'Final';

export const WRITE_UP_TYPES: { value: WriteUpType; label: string }[] = [
  { value: 'Note', label: 'Note (not disciplinary)' },
  { value: 'Verbal', label: 'Verbal warning' },
  { value: 'Written', label: 'Written warning' },
  { value: 'Final', label: 'Final warning' },
];

export const DEFAULT_WRITE_UP_TYPE: WriteUpType = 'Written';

export function writeUpTypeLabel(type: WriteUpType): string {
  return WRITE_UP_TYPES.find((t) => t.value === type)?.label.replace(' (not disciplinary)', '') ?? type;
}

// Whether the employee has confirmed receiving it ("received", not "agree").
export type WriteUpAcknowledgment = 'Pending' | 'Acknowledged' | 'Declined';

export type WriteUpEventAction = 'Created' | 'Edited' | 'Voided' | 'Acknowledged' | 'AcknowledgmentDeclined';

// Server-side caps (WriteUpsController.MaxDescriptionLength / MaxVoidReasonLength).
export const WRITE_UP_MAX_DESCRIPTION_LENGTH = 2000;
export const WRITE_UP_MAX_VOID_REASON_LENGTH = 500;

export interface WriteUpEventDto {
  id: number;
  action: WriteUpEventAction;
  byAccountId: number;
  byName: string;
  at: string;
  detail: string | null;
  // Set on an Acknowledged entry: the signature given at that moment.
  signatureId: number | null;
}

export interface WriteUpDto {
  id: number;
  accountId: number;
  date: string;
  description: string;
  severity: WriteUpSeverity;
  type: WriteUpType;
  createdByAccountId: number;
  createdByName: string;
  createdAt: string;
  acknowledgmentStatus: WriteUpAcknowledgment;
  acknowledgmentAt: string | null;
  // What the employee typed to acknowledge; null until they do.
  acknowledgmentSignedName: string | null;
  // The drawn signature currently on the write-up (fetch it with
  // WriteUpsApi.signature); null unless it is acknowledged with a drawing.
  acknowledgmentSignatureId: number | null;
  // The signature of whoever recorded it, given when creating it (optional);
  // their name and the time are createdByName / createdAt.
  authorSignatureId: number | null;
  isVoided: boolean;
  voidedAt: string | null;
  // Only sent to whoever manages the write-up (an admin), never to the
  // employee it's about.
  voidReason: string | null;
  history: WriteUpEventDto[] | null;
}

export interface WriteUpRequest {
  date: string;
  description: string;
  severity: WriteUpSeverity;
  type: WriteUpType;
}

// Signatures given in person when the write-up is created, both optional
// ("data:image/png;base64,..." from SignaturePad). An employee signature
// acknowledges the write-up on the spot.
export interface CreateWriteUpRequest extends WriteUpRequest {
  employeeSignature?: string;
  authorSignature?: string;
}

// Any signed-in employee can list and acknowledge their own write-ups;
// everything else (another employee's list, create, edit, void, recording a
// declined acknowledgment) is Admin/Sa only — and never for their own
// account — and 404s for anyone else. There's deliberately no delete: a
// mistaken or rescinded write-up is voided, with a reason.
@Service()
export class WriteUpsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/accounts`;

  list(accountId: number) {
    return this.http.get<WriteUpDto[]>(`${this.base}/${accountId}/write-ups`);
  }

  create(accountId: number, request: CreateWriteUpRequest) {
    return this.http.post<WriteUpDto>(`${this.base}/${accountId}/write-ups`, request);
  }

  update(accountId: number, writeUpId: number, request: WriteUpRequest) {
    return this.http.put<WriteUpDto>(`${this.base}/${accountId}/write-ups/${writeUpId}`, request);
  }

  void(accountId: number, writeUpId: number, reason: string) {
    return this.http.post<WriteUpDto>(`${this.base}/${accountId}/write-ups/${writeUpId}/void`, { reason });
  }

  // typedName has to match the employee's name on their account (the server
  // ignores case and extra spaces and answers 400 with what to type if not).
  // signature is the drawing as a "data:image/png;base64,..." URL.
  acknowledge(accountId: number, writeUpId: number, typedName: string, signature: string) {
    return this.http.post<WriteUpDto>(`${this.base}/${accountId}/write-ups/${writeUpId}/acknowledge`, {
      typedName,
      signature,
    });
  }

  // Fetched as a blob (not a plain <img src>) because the auth token is only
  // attached to HttpClient requests via auth-interceptor.ts.
  signature(accountId: number, writeUpId: number, signatureId: number) {
    return this.http.get(`${this.base}/${accountId}/write-ups/${writeUpId}/signatures/${signatureId}`, {
      responseType: 'blob',
    });
  }

  recordDeclined(accountId: number, writeUpId: number) {
    return this.http.post<WriteUpDto>(`${this.base}/${accountId}/write-ups/${writeUpId}/decline`, {});
  }
}
