import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';

export interface EmailTemplateDto {
  key: string;
  displayName: string;
  subject: string;
  bodyHtml: string;
  updatedAt: string;
}

export interface UpdateEmailTemplateRequest {
  subject: string;
  bodyHtml: string;
}

@Service()
export class EmailTemplatesApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/email-templates`;

  getAll(locationCode?: string) {
    const params = locationCode ? `?locationCode=${encodeURIComponent(locationCode)}` : '';
    return this.http.get<EmailTemplateDto[]>(`${this.base}${params}`);
  }

  update(key: string, request: UpdateEmailTemplateRequest, locationCode?: string) {
    const params = locationCode ? `?locationCode=${encodeURIComponent(locationCode)}` : '';
    return this.http.put<EmailTemplateDto>(`${this.base}/${key}${params}`, request);
  }
}
