// "HH:mm:ss" (the API's TimeOnly serialization) to minutes-since-midnight.
export function toMinutes(time: string): number {
  const [h, m] = time.split(':').map(Number);
  return h * 60 + m;
}

interface TimedEvent {
  startMinutes: number;
  endMinutes: number;
}

export interface LaidOutEvent<T> {
  event: T;
  startMinutes: number;
  endMinutes: number;
  // Which of columnCount side-by-side lanes this event sits in, and how
  // many of those lanes (from `column` rightward) it can spread across
  // before hitting a real conflict — same "expand into free space" look
  // Google Calendar's day view uses for overlapping events.
  column: number;
  columnSpan: number;
  columnCount: number;
}

// Lays out potentially-overlapping events into side-by-side lanes for a
// single-day timeline. Events that don't overlap anything share lane 0 at
// full width; a run of mutually-overlapping events split into as many
// lanes as their peak concurrency, with each event stretching rightward
// through any lanes it doesn't actually conflict with.
export function layoutDayEvents<T extends TimedEvent>(events: T[]): LaidOutEvent<T>[] {
  const sorted = [...events].sort((a, b) => a.startMinutes - b.startMinutes || a.endMinutes - b.endMinutes);

  const result: LaidOutEvent<T>[] = [];
  let cluster: { event: T; column: number }[] = [];
  let columnEnds: number[] = [];
  let clusterEnd = -Infinity;

  const flushCluster = () => {
    const columnCount = columnEnds.length;
    for (const { event, column } of cluster) {
      let columnSpan = 1;
      for (let col = column + 1; col < columnCount; col++) {
        const conflicts = cluster.some(
          (other) =>
            other.column === col &&
            other.event.startMinutes < event.endMinutes &&
            other.event.endMinutes > event.startMinutes,
        );
        if (conflicts) {
          break;
        }
        columnSpan++;
      }
      result.push({ event, startMinutes: event.startMinutes, endMinutes: event.endMinutes, column, columnSpan, columnCount });
    }
    cluster = [];
    columnEnds = [];
    clusterEnd = -Infinity;
  };

  for (const event of sorted) {
    if (cluster.length > 0 && event.startMinutes >= clusterEnd) {
      flushCluster();
    }

    let column = columnEnds.findIndex((end) => end <= event.startMinutes);
    if (column === -1) {
      column = columnEnds.length;
      columnEnds.push(event.endMinutes);
    } else {
      columnEnds[column] = event.endMinutes;
    }

    cluster.push({ event, column });
    clusterEnd = Math.max(clusterEnd, event.endMinutes);
  }
  flushCluster();

  return result;
}
