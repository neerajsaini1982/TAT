import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';

export interface EmployeeDocumentDto {
  id: number;
  accountId: number;
  name: string;
  fileName: string;
  contentType: string;
  fileSizeBytes: number;
  uploadedAt: string;
  uploadedByAccountId: number;
  uploadedByName: string;
}

@Service()
export class EmployeeDocumentsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/accounts`;

  list(accountId: number) {
    return this.http.get<EmployeeDocumentDto[]>(`${this.base}/${accountId}/documents`);
  }

  upload(accountId: number, name: string, file: File) {
    const formData = new FormData();
    formData.append('name', name);
    formData.append('file', file);
    return this.http.post<EmployeeDocumentDto>(`${this.base}/${accountId}/documents`, formData);
  }

  rename(accountId: number, documentId: number, name: string) {
    return this.http.put<EmployeeDocumentDto>(`${this.base}/${accountId}/documents/${documentId}`, { name });
  }

  remove(accountId: number, documentId: number) {
    return this.http.delete<void>(`${this.base}/${accountId}/documents/${documentId}`);
  }

  // Fetched as a blob (not a plain <a href>) because the auth token is only
  // attached to HttpClient requests via auth-interceptor.ts.
  download(accountId: number, documentId: number) {
    return this.http.get(`${this.base}/${accountId}/documents/${documentId}/download`, { responseType: 'blob' });
  }
}
