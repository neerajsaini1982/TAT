import { TimeFormat } from './location-settings-api';

// Formats a TimeOnly-shaped "HH:mm" or "HH:mm:ss" string (a shift's
// scheduled start/end, which is already wall-clock in the location's own
// timezone — no conversion needed) per the location's configured
// TimeFormat. "15:30" -> "3:30 PM" (TwelveHour) or "15:30" (TwentyFourHour).
export function formatTimeOnly(hhmm: string, timeFormat: TimeFormat): string {
  const [hStr, mStr] = hhmm.split(':');
  if (timeFormat === 'TwentyFourHour') {
    return `${hStr.padStart(2, '0')}:${mStr}`;
  }
  const h = Number(hStr);
  const period = h >= 12 ? 'PM' : 'AM';
  const h12 = h % 12 === 0 ? 12 : h % 12;
  return `${h12}:${mStr} ${period}`;
}

// Formats a UTC ISO instant (a punch timestamp) as wall-clock time in the
// location's own timezone — not the viewing browser's — per its configured
// TimeFormat. This is the one place punch times get converted for display;
// every other date/time helper in this app operates in the browser's local
// zone, which is wrong for punches since an admin or kiosk screen might not
// be physically in the same timezone as the location itself.
export function formatInstant(iso: string, timeZone: string, timeFormat: TimeFormat): string {
  return new Intl.DateTimeFormat('en-US', {
    timeZone,
    hour: 'numeric',
    minute: '2-digit',
    hour12: timeFormat === 'TwelveHour',
  }).format(new Date(iso));
}
