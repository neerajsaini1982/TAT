import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';

export interface LocationDto {
  id: number;
  name: string;
  address: string;
  locationCode: string;
  phone: string;
  email: string;
  isActive: boolean;
}

export type CreateLocationRequest = Omit<LocationDto, 'id' | 'isActive'>;
export type UpdateLocationRequest = Omit<LocationDto, 'id' | 'locationCode'>;

@Service()
export class LocationsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/locations`;

  getAll() {
    return this.http.get<LocationDto[]>(this.base);
  }

  create(request: CreateLocationRequest) {
    return this.http.post<LocationDto>(this.base, request);
  }

  update(id: number, request: UpdateLocationRequest) {
    return this.http.put<LocationDto>(`${this.base}/${id}`, request);
  }

  delete(id: number) {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}
