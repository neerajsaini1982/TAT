// yyyy-MM-dd, matching the API's DateOnly serialization.
export function formatDate(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

export function parseDate(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d);
}

export function mondayOf(date: Date): Date {
  const result = new Date(date);
  const day = result.getDay(); // 0 = Sunday
  const diff = day === 0 ? -6 : 1 - day;
  result.setDate(result.getDate() + diff);
  result.setHours(0, 0, 0, 0);
  return result;
}

export function addDays(date: Date, days: number): Date {
  const result = new Date(date);
  result.setDate(result.getDate() + days);
  return result;
}

export const DAY_LABELS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
export const DAY_LABELS_SHORT = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

export function formatWeekRange(weekStart: Date): string {
  const weekEnd = addDays(weekStart, 6);
  const opts: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric' };
  return `${weekStart.toLocaleDateString(undefined, opts)} – ${weekEnd.toLocaleDateString(undefined, opts)}`;
}

// Plain ISO-date (yyyy-MM-dd) string comparison, no Date parsing needed.
export const isPastDate = (isoDate: string): boolean => isoDate < formatDate(new Date());

// Employees submit availability for exactly one week ahead, during the
// week before it: the only week ever open for self-service edits is next
// week, and only through Saturday of the current week. Mirrored
// server-side in AvailabilityController.SaveMine.
export function editableAvailabilityWeekStart(): Date {
  return addDays(mondayOf(new Date()), 7);
}

export function availabilitySubmissionDeadline(): Date {
  return addDays(mondayOf(new Date()), 5); // Saturday of the current week
}

export const isAvailabilitySubmissionOpen = (): boolean =>
  formatDate(new Date()) <= formatDate(availabilitySubmissionDeadline());

// Whether a given date falls within the one week employees can currently
// submit availability for (does not by itself account for the deadline or
// an already-submitted week — see isAvailabilitySubmissionOpen for that).
export const isEditableAvailabilityDate = (isoDate: string): boolean => {
  const start = formatDate(editableAvailabilityWeekStart());
  const end = formatDate(addDays(editableAvailabilityWeekStart(), 6));
  return isoDate >= start && isoDate <= end;
};

export function dayOfWeekLabel(isoDate: string): string {
  const jsDay = parseDate(isoDate).getDay(); // 0 = Sunday
  const mondayFirstIndex = (jsDay + 6) % 7; // 0 = Monday
  return DAY_LABELS[mondayFirstIndex];
}

export const toMmDdYyyy = (isoDate: string): string => {
  const [y, m, d] = isoDate.split('-');
  return `${m}/${d}/${y}`;
};

// `<input type="time">` works with "HH:mm"; the API's TimeOnly fields
// serialize as "HH:mm:ss". Convert at the edges.
export const toInputTime = (apiTime: string | null): string => (apiTime ? apiTime.slice(0, 5) : '09:00');
export const toApiTime = (inputTime: string): string => (inputTime.length === 5 ? `${inputTime}:00` : inputTime);

// Combines a yyyy-MM-dd date with an "HH:mm" time into a local-time Date —
// used both to compute clock-in windows and to turn an admin-entered punch
// time back into a UTC instant before sending it to the API.
export function combineDateAndTime(dateIso: string, time: string): Date {
  const [hours, minutes] = time.slice(0, 5).split(':').map(Number);
  const date = parseDate(dateIso);
  date.setHours(hours, minutes, 0, 0);
  return date;
}

export function formatHHmm(date: Date): string {
  return `${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;
}

// Every plain-text time field in the app (Edit Punch Times, availability's
// day dialog and weekly editor) uses these three together: type what you
// want (parseTimeInput), get it normalized back for display
// (formatDisplayTime), and a <datalist> of common times (TIME_SUGGESTIONS)
// for autocomplete — a real native <input type="time"> is deliberately not
// used here. That control has a well-documented, unfixable-from-JS quirk:
// its three segments (hour/minute/AM-PM) can visually show a complete value
// while the browser's own .value getter still reports "", most often when
// typing over a value that was already there. That's how Clock Out times
// (and, by the same mechanism, availability Start/End) have silently gone
// missing or gotten stuck behind a confusing "looks filled in but isn't"
// state. A plain text field has no such hidden state: whatever's typed is
// exactly what .value reports, always.

// Accepts "3:35 PM", "3:35pm", or 24-hour "15:35". Returns "HH:mm" (24-hour,
// the canonical form used for storage/comparison), '' for a blank field, or
// null if the text doesn't parse as a time at all.
export function parseTimeInput(raw: string): string | null {
  const trimmed = raw.trim();
  if (!trimmed) {
    return '';
  }

  const twelveHour = trimmed.match(/^(\d{1,2}):(\d{2})\s*(am|pm)$/i);
  if (twelveHour) {
    const minute = Number(twelveHour[2]);
    let hour = Number(twelveHour[1]);
    if (hour < 1 || hour > 12 || minute > 59) {
      return null;
    }
    const isPm = twelveHour[3].toLowerCase() === 'pm';
    if (isPm && hour !== 12) {
      hour += 12;
    } else if (!isPm && hour === 12) {
      hour = 0;
    }
    return `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`;
  }

  const twentyFourHour = trimmed.match(/^(\d{1,2}):(\d{2})$/);
  if (twentyFourHour) {
    const hour = Number(twentyFourHour[1]);
    const minute = Number(twentyFourHour[2]);
    if (hour > 23 || minute > 59) {
      return null;
    }
    return `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`;
  }

  return null;
}

// "HH:mm" (24-hour) -> "h:mm AM/PM" (what's actually shown in the text field).
export function formatDisplayTime(hhmm: string): string {
  if (!hhmm) {
    return '';
  }
  const [h, m] = hhmm.split(':').map(Number);
  const period = h < 12 ? 'AM' : 'PM';
  const displayHour = h % 12 === 0 ? 12 : h % 12;
  return `${displayHour}:${String(m).padStart(2, '0')} ${period}`;
}

// Every quarter-hour across a day, in the same "h:mm AM/PM" form
// parseTimeInput/formatDisplayTime use — bound to each time field's
// <datalist> for autocomplete. This is a suggestion list, not a constraint:
// a <datalist> never blocks typing something that isn't in it (unlike a
// <select>), so an exact time like 7:32 PM is always still enterable by hand.
export const TIME_SUGGESTIONS: string[] = (() => {
  const options: string[] = [];
  for (let totalMinutes = 0; totalMinutes < 24 * 60; totalMinutes += 15) {
    const hour24 = Math.floor(totalMinutes / 60);
    const minute = totalMinutes % 60;
    options.push(formatDisplayTime(`${String(hour24).padStart(2, '0')}:${String(minute).padStart(2, '0')}`));
  }
  return options;
})();

// H.MM display for a decimal-hours total (e.g. 17.25 decimal hours -> "17.15"
// for 17h15m) — matches how shift names are already written (e.g. "4.45 hrs"
// for 4h45m). Plain decimal hours reads as "17 hours 25" at a glance, which
// is wrong.
export function hoursMinutesLabel(hours: number): string {
  const totalMinutes = Math.round(hours * 60);
  const h = Math.floor(totalMinutes / 60);
  const m = totalMinutes % 60;
  return `${h}.${m.toString().padStart(2, '0')}`;
}

export function startOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

export function addMonths(date: Date, months: number): Date {
  return new Date(date.getFullYear(), date.getMonth() + months, 1);
}

export function formatMonthLabel(monthAnchor: Date): string {
  return monthAnchor.toLocaleDateString(undefined, { month: 'long', year: 'numeric' });
}

// Full weeks (Monday-Sunday) covering every day of the given month, the
// same grid shape Google Calendar's month view uses.
export function monthGridDays(monthAnchor: Date): Date[] {
  const firstOfMonth = startOfMonth(monthAnchor);
  const lastOfMonth = new Date(monthAnchor.getFullYear(), monthAnchor.getMonth() + 1, 0);
  const gridStart = mondayOf(firstOfMonth);
  const gridEnd = addDays(mondayOf(lastOfMonth), 6);

  const days: Date[] = [];
  for (let d = gridStart; d <= gridEnd; d = addDays(d, 1)) {
    days.push(d);
  }
  return days;
}
