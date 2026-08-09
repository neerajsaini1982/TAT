import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';

export interface AllowedPunchDeviceDto {
  id: number;
  ipAddress: string;
  label: string;
  createdAt: string;
}

export interface CreateAllowedPunchDeviceRequest {
  ipAddress: string;
  label: string;
}

@Service()
export class AllowedPunchDevicesApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/allowed-punch-devices`;

  getAll(locationCode?: string) {
    const params = locationCode ? `?locationCode=${encodeURIComponent(locationCode)}` : '';
    return this.http.get<AllowedPunchDeviceDto[]>(`${this.base}${params}`);
  }

  create(request: CreateAllowedPunchDeviceRequest, locationCode?: string) {
    const params = locationCode ? `?locationCode=${encodeURIComponent(locationCode)}` : '';
    return this.http.post<AllowedPunchDeviceDto>(`${this.base}${params}`, request);
  }

  delete(id: number, locationCode?: string) {
    const params = locationCode ? `?locationCode=${encodeURIComponent(locationCode)}` : '';
    return this.http.delete<void>(`${this.base}/${id}${params}`);
  }
}
