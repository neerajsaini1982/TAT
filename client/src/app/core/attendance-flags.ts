import { parseDate } from './week-utils';
import { ShiftAssignmentDto } from './shift-assignments-api';
import { TimeEntryDto } from './time-entries-api';

function combineDateAndTime(dateIso: string, time: string): Date {
  const [hours, minutes] = time.slice(0, 5).split(':').map(Number);
  const date = parseDate(dateIso);
  date.setHours(hours, minutes, 0, 0);
  return date;
}

// A clock-in more than graceMinutes after the shift's scheduled start.
export function isLateClockIn(
  entry: Pick<TimeEntryDto, 'clockInAt'>,
  assignment: Pick<ShiftAssignmentDto, 'date' | 'shiftStartTime'>,
  graceMinutes: number,
): boolean {
  const latestOnTime = combineDateAndTime(assignment.date, assignment.shiftStartTime);
  latestOnTime.setMinutes(latestOnTime.getMinutes() + graceMinutes);
  return new Date(entry.clockInAt) > latestOnTime;
}

// Measured against `now` while still open, so a break/lunch flags live once
// it's run long, not only after it's closed out.
function isSegmentOverLimit(startAt: string | null, endAt: string | null, limitMinutes: number, now: Date): boolean {
  if (!startAt) {
    return false;
  }
  const start = new Date(startAt);
  const end = endAt ? new Date(endAt) : now;
  return (end.getTime() - start.getTime()) / 60_000 > limitMinutes;
}

export const isBreakOverLimit = (
  entry: Pick<TimeEntryDto, 'breakStartAt' | 'breakEndAt'>,
  limitMinutes: number,
  now: Date,
): boolean => isSegmentOverLimit(entry.breakStartAt, entry.breakEndAt, limitMinutes, now);

export const isLunchOverLimit = (
  entry: Pick<TimeEntryDto, 'lunchStartAt' | 'lunchEndAt'>,
  limitMinutes: number,
  now: Date,
): boolean => isSegmentOverLimit(entry.lunchStartAt, entry.lunchEndAt, limitMinutes, now);

export const isBreak2OverLimit = (
  entry: Pick<TimeEntryDto, 'break2StartAt' | 'break2EndAt'>,
  limitMinutes: number,
  now: Date,
): boolean => isSegmentOverLimit(entry.break2StartAt, entry.break2EndAt, limitMinutes, now);
