import { Component, computed, input } from '@angular/core';

import { ShiftAssignmentDto } from '../../../core/shift-assignments-api';
import { employeeColor } from '../../../core/employee-colors';
import { LaidOutEvent, layoutDayEvents, toMinutes } from '../../../core/day-view-layout';

const HOUR_HEIGHT_PX = 60;
// Padding around the day's actual shifts so a light day doesn't render a
// timeline that's mostly cut off, and a normal day still shows the whole
// business day for context — same reasoning as the vertical padding
// google calendar's day view always keeps around the day's events.
const DEFAULT_START_HOUR = 8;
const DEFAULT_END_HOUR = 20;

@Component({
  selector: 'app-schedule-day-view',
  imports: [],
  templateUrl: './schedule-day-view.html',
  styleUrl: './schedule-day-view.scss',
})
export class ScheduleDayView {
  readonly assignments = input<ShiftAssignmentDto[]>([]);

  protected readonly employeeColor = employeeColor;

  protected readonly timeRange = computed(() => {
    const events = this.assignments();
    if (events.length === 0) {
      return { startHour: DEFAULT_START_HOUR, endHour: DEFAULT_END_HOUR };
    }

    const starts = events.map((a) => toMinutes(a.shiftStartTime));
    const ends = events.map((a) => toMinutes(a.shiftEndTime));
    return {
      startHour: Math.min(DEFAULT_START_HOUR, Math.floor(Math.min(...starts) / 60)),
      endHour: Math.max(DEFAULT_END_HOUR, Math.ceil(Math.max(...ends) / 60)),
    };
  });

  protected readonly hours = computed(() => {
    const { startHour, endHour } = this.timeRange();
    return Array.from({ length: endHour - startHour }, (_, i) => startHour + i);
  });

  protected readonly totalHeightPx = computed(() => {
    const { startHour, endHour } = this.timeRange();
    return (endHour - startHour) * HOUR_HEIGHT_PX;
  });

  protected readonly laidOutEvents = computed<LaidOutEvent<ShiftAssignmentDto>[]>(() =>
    layoutDayEvents(
      this.assignments().map((a) => ({
        ...a,
        startMinutes: toMinutes(a.shiftStartTime),
        endMinutes: toMinutes(a.shiftEndTime),
      })),
    ),
  );

  eventStyle(laidOut: LaidOutEvent<ShiftAssignmentDto>): Record<string, string> {
    const { startHour } = this.timeRange();
    const top = ((laidOut.startMinutes - startHour * 60) / 60) * HOUR_HEIGHT_PX;
    const height = ((laidOut.endMinutes - laidOut.startMinutes) / 60) * HOUR_HEIGHT_PX;
    const width = (100 / laidOut.columnCount) * laidOut.columnSpan;
    const left = (100 / laidOut.columnCount) * laidOut.column;
    return {
      top: `${top}px`,
      height: `${Math.max(height, 28)}px`,
      left: `calc(${left}% + 4px)`,
      width: `calc(${width}% - 8px)`,
    };
  }

  hourLabel(hour: number): string {
    const h = hour % 24;
    const period = h < 12 ? 'AM' : 'PM';
    const display = h % 12 === 0 ? 12 : h % 12;
    return `${display} ${period}`;
  }

  timeRangeLabel(a: ShiftAssignmentDto): string {
    return `${formatClockTime(a.shiftStartTime)} – ${formatClockTime(a.shiftEndTime)}`;
  }
}

// "13:30:00" -> "1:30pm"; drops :00 minutes the way Google Calendar's day
// view does ("12 – 1pm" rather than "12:00pm – 1:00pm").
function formatClockTime(time: string): string {
  const [hStr, mStr] = time.split(':');
  const h24 = Number(hStr);
  const period = h24 < 12 ? 'am' : 'pm';
  const h = h24 % 12 === 0 ? 12 : h24 % 12;
  return mStr === '00' ? `${h}${period}` : `${h}:${mStr}${period}`;
}
