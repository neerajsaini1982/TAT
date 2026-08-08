import { isDevMode } from '@angular/core';

// In dev (`ng serve` on :4200), the API runs as a separate process on
// :5235, so it needs an explicit cross-origin URL. Every production build
// (LAN or cloud) serves the UI and API from the same origin — see
// Program.cs's UseStaticFiles/MapControllers/MapFallbackToFile all on one
// Kestrel instance — so using the page's own origin there works
// regardless of host, port, or scheme (fixes an HTTPS mixed-content
// failure when the LAN-only hardcoded http://...:5235 form was used on a
// TLS-terminated deployment like Azure).
export const API_BASE_URL = isDevMode()
  ? `http://${window.location.hostname}:5235/api`
  : `${window.location.origin}/api`;
