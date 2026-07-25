import { Component, computed, input } from '@angular/core';

import { ShiftAssignmentDto } from '../../../core/shift-assignments-api';
import { employeeColor } from '../../../core/employee-colors';
import { LaidOutEvent, formatClockTime, hourLabel, layoutDayEvents, toMinutes } from '../../../core/day-view-layout';

export interface WeekTimelineDay {
  label: string;
  dateLabel: string;
  assignments: ShiftAssignmentDto[];
}

// Same fixed-height-scaled-to-hours-span approach as ScheduleDayView (see
// its comment on TIMELINE_HEIGHT_PX), but shorter — this view packs 7 day
// columns side by side and is meant to print on a single landscape A4
// page, so it trades some of the day view's vertical room for width.
const TIMELINE_HEIGHT_PX = 420;
const FALLBACK_START_HOUR = 9;
const FALLBACK_END_HOUR = 17;

@Component({
  selector: 'app-schedule-week-timeline',
  imports: [],
  templateUrl: './schedule-week-timeline.html',
  styleUrl: './schedule-week-timeline.scss',
})
export class ScheduleWeekTimeline {
  readonly days = input<WeekTimelineDay[]>([]);

  protected readonly employeeColor = employeeColor;
  protected readonly hourLabel = hourLabel;

  // One shared hour axis for the whole week, spanning the earliest start /
  // latest end across every day — so every column's shifts line up against
  // the same gridlines instead of each day picking its own range.
  protected readonly timeRange = computed(() => {
    const events = this.days().flatMap((d) => d.assignments);
    if (events.length === 0) {
      return { startHour: FALLBACK_START_HOUR, endHour: FALLBACK_END_HOUR };
    }

    const starts = events.map((a) => toMinutes(a.shiftStartTime));
    const ends = events.map((a) => toMinutes(a.shiftEndTime));
    const startHour = Math.floor(Math.min(...starts) / 60);
    const endHour = Math.ceil(Math.max(...ends) / 60);
    return { startHour, endHour: Math.max(endHour, startHour + 1) };
  });

  protected readonly hours = computed(() => {
    const { startHour, endHour } = this.timeRange();
    return Array.from({ length: endHour - startHour }, (_, i) => startHour + i);
  });

  protected readonly hourHeightPx = computed(() => {
    const { startHour, endHour } = this.timeRange();
    return TIMELINE_HEIGHT_PX / (endHour - startHour);
  });

  protected readonly totalHeightPx = TIMELINE_HEIGHT_PX;

  // Each day's events are laid out independently (a Tuesday overlap
  // shouldn't steal lane width from Wednesday), then positioned against
  // the shared timeRange in eventStyle below.
  protected readonly laidOutDays = computed<LaidOutEvent<ShiftAssignmentDto>[][]>(() =>
    this.days().map((day) =>
      layoutDayEvents(
        day.assignments.map((a) => ({
          ...a,
          startMinutes: toMinutes(a.shiftStartTime),
          endMinutes: toMinutes(a.shiftEndTime),
        })),
      ),
    ),
  );

  eventStyle(laidOut: LaidOutEvent<ShiftAssignmentDto>): Record<string, string> {
    const { startHour } = this.timeRange();
    const hourHeight = this.hourHeightPx();
    const top = ((laidOut.startMinutes - startHour * 60) / 60) * hourHeight;
    const height = ((laidOut.endMinutes - laidOut.startMinutes) / 60) * hourHeight;
    const width = (100 / laidOut.columnCount) * laidOut.columnSpan;
    const left = (100 / laidOut.columnCount) * laidOut.column;
    return {
      top: `${top}px`,
      height: `${Math.max(height, 26)}px`,
      left: `calc(${left}% + 2px)`,
      width: `calc(${width}% - 4px)`,
    };
  }

  timeRangeLabel(a: ShiftAssignmentDto): string {
    return `${formatClockTime(a.shiftStartTime)}–${formatClockTime(a.shiftEndTime)}`;
  }
}
