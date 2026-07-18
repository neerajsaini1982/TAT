import { Service } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState } from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';

import { API_BASE_URL } from './api-config';

const HUB_URL = `${API_BASE_URL.replace(/\/api$/, '')}/hubs/schedule`;

// Thin wrapper around a SignalR connection to the server's ScheduleHub.
// Screens call connect(locationCode) and re-run their existing load() on
// every emission, instead of polling on a timer. The hub never carries the
// actual data — see IScheduleNotifier server-side — so it's fine to stay
// anonymous and just signal "something changed, go refetch".
@Service()
export class ScheduleRealtime {
  private connection: HubConnection | null = null;
  private startPromise: Promise<void> | null = null;
  private lastLocationCode: string | null = null;
  private readonly changed$ = new Subject<void>();

  connect(locationCode: string): Observable<void> {
    this.lastLocationCode = locationCode;

    if (!this.connection) {
      this.connection = new HubConnectionBuilder()
        .withUrl(HUB_URL, { withCredentials: false })
        .withAutomaticReconnect()
        .build();
      this.connection.on('scheduleChanged', () => this.changed$.next());
      // Automatic reconnect gets a new connection id, so any previous group
      // membership on the server is gone — rejoin every time.
      this.connection.onreconnected(() => this.join());
      this.startPromise = this.connection
        .start()
        .then(() => this.join())
        .catch(() => {
          // Best-effort: if the hub connection fails, screens simply fall
          // back to their initial load instead of live-updating.
        });
    } else {
      this.join();
    }

    return this.changed$.asObservable();
  }

  private join(): void {
    if (this.connection?.state === HubConnectionState.Connected && this.lastLocationCode) {
      this.connection.invoke('JoinLocation', this.lastLocationCode).catch(() => {});
    }
  }
}
