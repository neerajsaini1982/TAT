// Derived from whatever host the browser used to load the app, so the same
// build works from localhost, a LAN IP, or a hostname without editing this
// file per machine.
export const API_BASE_URL = `http://${window.location.hostname}:5235/api`;
