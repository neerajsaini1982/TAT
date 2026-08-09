import { Service, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { API_BASE_URL } from './api-config';

export interface SyncDbFromLiveResult {
  message: string;
  backupPath: string | null;
  bytesDownloaded: number;
}

// Backs the local-only "Sync DB from Live" button — see DevToolsController,
// which 404s outside the Development environment regardless of who calls it.
@Service()
export class DevToolsApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_BASE_URL}/dev-tools`;

  syncDbFromLive() {
    return this.http.post<SyncDbFromLiveResult>(`${this.base}/sync-db-from-live`, {});
  }
}
