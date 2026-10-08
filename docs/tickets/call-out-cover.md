# Call-outs: mark absent and assign cover in one step

## Summary

When an employee calls out at the last minute, let an admin mark them absent
and put someone else on the shift from a single dialog. The cover shift is
live straight away (the cover person can clock in without the week being
reposted), it remembers whose shift it covers, and the cover person can be
emailed.

Today this takes three separate steps, and the last one is easy to miss:

1. Mark the shift absent (`ShiftAssignmentsController.MarkAbsent`) from
   `schedule-day-view` or `admin-schedule-assign-page`.
2. Add the cover person to the same shift on `admin-schedule-assign-page`
   (`ShiftAssignmentsController.Create`).
3. Click Post Schedule again. The new assignment is a draft, and
   `TimeEntryPunchService` and `KioskController` both ignore unpublished
   assignments, so until the week is reposted the cover person can't clock in
   and doesn't see the shift. `Publish` reposts the whole week and, with
   Send Email ticked, emails everyone on it.

Nothing links the two assignments, so the schedule can't show who covered
for whom. The other shortcut — dragging the shift to the cover person
(`Move`) — overwrites `AccountId` and loses the absence altogether.

## User stories

**Admin or lead** (`LeadOrAbove`)
1. As an admin or lead, I can pick "Mark Absent & Assign Cover" on a shift and, in
   one dialog, enter the absence note, optionally record sick hours, and
   choose who covers.
2. The cover picker shows who can take the shift, each person's hours this
   week so I can avoid overtime, and a warning on anyone who said they were
   unavailable or who already has a shift that day.
3. When I pick someone who already has a shift that day, their shift is
   replaced by one combined shift covering both, and I see exactly what
   that shift will be before I confirm.
4. I can mark the employee absent now and assign cover later: an absent
   shift with no cover shows a "Find Cover" action.
5. I can change or remove the cover person.
6. I can choose to email the cover person about the shift.
7. As an admin, I see uncovered absences for today on the dashboard.

Leads can do all of the above except record sick hours, which stays
admin-only as it is today (`SetSickMinutes`).

**Employee**
8. As the cover employee, the shift shows on my schedule and the kiosk
   immediately, labelled "Covering for Sam R.", and I can clock into it.
   If I was already working that day I still have one shift, just longer,
   and any punches I've made carry over.
9. As the absent employee, the shift stays on my schedule marked Absent.

## Rules

- **The absent assignment is kept.** It stays on the original employee with
  `IsAbsent`, the note and any sick hours, exactly as `MarkAbsent` and
  `SetSickMinutes` leave it today.
- **One shift per person per day.** Someone with the day off gets a new
  assignment on the absent shift. Someone already working that day keeps
  their one assignment, switched to a combined shift — see "Combined
  shifts" below. Nobody ends up with two assignments on a date.
- **Cover is published on creation** (`IsPublished = true`,
  `PublishedAt = now`) when the absent assignment is published. If the absent
  assignment is still a draft, the cover assignment is a draft too and goes
  out with the normal Post Schedule.
- **Who can cover** — same location, role is Employee/Lead/Admin, and
  active. Unlike `Create`, two things are warnings rather than blocks:
  - **Unavailable that day.** The picker lists them under the available
    people with an "Unavailable" label; choosing one asks for confirmation.
  - **Already has a shift that day.** Their shift is replaced by a combined
    one — see "Combined shifts" below.

  The server re-checks everything and only accepts an unavailable or
  already-scheduled person when the request says the warning was confirmed.
  `Create` and `Move` keep their existing blocks; the relaxed rules apply to
  cover assignments only.
- **Whole shift only.** Cover is always for the full shift. If the cover
  person arrives late, their punches show it.
- **Hours shown in the picker** are the employee's scheduled hours for that
  workweek including this shift, flagged when they'd pass the overtime
  threshold `OvertimeCalculator` uses. It's a warning, not a block.
- **One cover per absent assignment.** Assigning a new cover person replaces
  the previous one. A previous cover who had the day off loses the
  assignment, which is refused if they have already clocked in; one whose
  shift was combined goes back to the shift they had, punches and all.
- **All or nothing.** Marking absent, saving sick hours and creating the
  cover assignment happen in one transaction. If the cover person fails a
  check, nothing is saved.
- **Clearing the absence** (`MarkAbsent` with `IsAbsent = false`, or the
  original employee clocking in, which already clears `IsAbsent`) does not
  remove the cover assignment — both people may end up working. The cover
  assignment keeps its link and the admin removes it by hand if it isn't
  wanted.
- **Deleting the absent assignment** clears the link on the cover assignment
  rather than deleting it.
- **Payroll rules are unchanged.** The cover person's hours come from their own
  punches on their own assignment; the absent employee's sick hours stay on
  theirs. No report column changes.
- Marking absent still wipes an existing `TimeEntry` on the absent
  assignment, as today.

## Combined shifts

When the cover employee already has a shift that day, they are not given a
second one. Their existing assignment is switched, in the same save, to a
single shift for the day. One assignment, one clock-in, one lunch.

The assigner types that shift into the dialog's "New shift" box, which
works like the schedule builder's Add Shift box (a start time, colon
optional, or a shift name narrows the list; Enter takes the top match):

- **One of the location's active shifts** (`coverShiftId`). Nothing is
  created; the assignment simply moves to that shift, with its own lunch
  and breaks. The dialog preselects a location shift whose times exactly
  match the two shifts end to end, when there is one.
- **Left blank**, the fallback when no location shift fits: a shift is
  built from the two, as described below.

For a combined shift:

- **Span:** from the earlier start to the later end. Back-to-back shifts
  (7–3 and 3–11) become 7–11. Overlapping shifts (9–5 covering 2–10) become
  9–10, which is how "stay late to cover the closer" is handled. A gap
  between the two is taken in and worked through; the dialog says so.
- **Lunch and breaks:** the combined shift keeps the cover employee's own
  lunch (or the covered shift's, if theirs has none) and the short breaks
  from both shifts.
- **It is a real `Shift` row**, created on demand, named after both
  ("Morning + Evening") and inactive, so it never appears in the schedule
  builder's shift list. The same pair of shifts reuses the same row.
- **Punches carry over.** The assignment keeps its id, so a `TimeEntry`
  already on it (they clocked in this morning) stays where it is.
- **The assignment remembers its original shift** (`OriginalShiftId`), which
  is what Remove Cover and Change Cover put back.
Either way:

- **Blocked** when the person has already clocked out for the day, is
  already covering another shift that day, or the two shifts together would
  run 24 hours or more.
- **The assigner has to tick a confirmation** that names the shift being
  replaced and the new one.
- **Overtime.** The picker's weekly hours and overtime warning use the
  combined shift in place of the person's own.

## Data model (server)

New fields on `ShiftAssignment`:

| Field | Type | Notes |
|---|---|---|
| CoversAssignmentId / CoversAssignment | int? | the absent assignment this one covers; null for ordinary shifts |
| CoverAssignedByAccountId / CoverAssignedByAccount | int? | who assigned the cover |
| CoverAssignedAt | DateTime? | |
| OriginalShiftId / OriginalShift | int? | the shift the cover employee had before it was combined; null when covering was their only shift that day |

`CoversAssignmentId` is a self-reference with a unique index (one cover per
absent assignment) and `OnDelete(SetNull)`.

Add an EF Core migration in `server/Data/Migrations` and configure the
relationship in `AppDbContext`.

`ShiftAssignmentDto` gains:
- `CoversAssignmentId`, `CoversAccountFirstName`, `CoversAccountLastName` —
  set on the cover assignment.
- `CoveredByAssignmentId`, `CoveredByAccountFirstName`,
  `CoveredByAccountLastName` — set on the absent assignment.
- `OriginalShiftName` — set on a cover assignment that was combined.

## API (server)

In `ShiftAssignmentsController`:

| Method | Route | Who | Purpose |
|---|---|---|---|
| GET | `/api/shift-assignments/{id}/cover-candidates` | LeadOrAbove | everyone at the location who could cover, each with: scheduled hours for the week, overtime minutes, isAvailable, their own shift that day and the combined shift it would become, and a blocked reason when they can't be picked; also the location's active shifts for the "New shift" list. `?shiftId=` previews that shift as the replacement |
| PUT | `/api/shift-assignments/{id}/call-out` | LeadOrAbove | body: note (required), sickMinutes? (admins only), coverAccountId?, confirmUnavailable, confirmCombine, coverShiftId?, sendEmail — marks absent, records sick hours, assigns cover |
| PUT | `/api/shift-assignments/{id}/cover` | LeadOrAbove | body: coverAccountId, confirmUnavailable, confirmCombine, coverShiftId?, sendEmail — assign or replace cover on an assignment that is already absent |
| DELETE | `/api/shift-assignments/{id}/cover` | LeadOrAbove | remove cover; 409 if a cover person who had the day off has clocked in |

A lead sending `sickMinutes` gets 403.

`call-out` and `cover` return both assignments so the client can update the
schedule without a refetch. `MarkAbsent` and `SetSickMinutes` stay as they
are. All routes use the existing `CanAccess` location check and call
`notifier.NotifyLocationChanged`.

## Emails

Add a key to `EmailTemplateKeys` / `EmailTemplateCatalog` so it can be edited
in the email template editor:
- **Cover Shift Assigned** — to the cover employee, with the date, shift name
  and times, and who they're covering for.

Sent with `EmailSender` using the location's SMTP settings when Send Email is
ticked. Sending is best-effort: a failure doesn't undo the cover assignment,
and the box is disabled when SMTP isn't set up or the employee has no email.

## UI (client)

Admin (`features/admin`):
- New `call-out-dialog`: absence note, optional sick hours (same input as
  `sick-hours-dialog`; hidden for leads), cover picker, "No cover yet"
  option, and the Send Email checkbox.
  - Picker order: available with no shift that day, then people already
    working that day, then unavailable people. Each row shows hours this
    week and any overtime. People who can't be picked are greyed out with
    the reason.
  - Choosing someone already working shows the "New shift" type-ahead box.
  - Choosing someone unavailable or already working needs a confirm tick
    before Save.
  - In cover mode the dialog also offers Remove Cover.
- `schedule-day-view` gear menu and the `admin-schedule-assign-page` row
  actions, for leads as well as admins wherever Mark Absent shows today:
  Mark Absent opens the dialog (cover is optional in it); an absent shift
  shows "Find Cover", or "Change Cover" once it has one.
- `schedule-day-view`, `schedule-week-timeline`,
  `schedule-week-days-timeline`, `admin-schedule-assign-page`: the absent
  shift shows "Covered by Alex M."; the cover shift shows a "Covering for
  Sam R." badge.
- `admin-dashboard`: the Absent stat and Today's Schedule mark absences that
  have no cover; Needs Attention lists them with a link that opens the
  dialog.
- `shift-assignments-api.ts`: the new calls and DTO fields.

Employee: `employee-schedule-page` and `current-week-schedule` show
"Covering for Sam R." on a cover shift.

Kiosk: `kiosk-schedule` lists the cover shift like any other published
shift; no change.

## Tests (`server.Tests`)

- `call-out` with a cover person: absent assignment keeps its employee and
  gets the note and sick minutes; a published, linked cover assignment is
  created; the cover person can clock in through `TimeEntryPunchService`
  without `Publish` being called.
- `call-out` without a cover person behaves like `MarkAbsent` (+ sick hours).
- Cover on a draft absent assignment is created as a draft.
- Cover person rejected when in another location, inactive, or the wrong
  role — and nothing is saved, including the absence.
- Unavailable cover person: rejected without `confirmUnavailable`, accepted
  with it. `Create` still blocks unavailable employees.
- Already working: rejected without `confirmCombine`; with it, their one
  assignment moves to a combined shift with the right span, name, lunch and
  breaks, and remembers the original shift. `Create` still blocks a second
  assignment on the same date.
- A picked location shift is used as-is and no combined shift is created;
  an inactive shift or another location's is rejected; the pick can be
  changed for someone already covering.
- Combined span for back-to-back, overlapping, gapped and overnight shifts.
- Existing punches stay on the combined assignment.
- Blocked when clocked out for the day or already covering.
- The same pair of shifts reuses one combined `Shift` row.
- Removing or changing cover restores the original shift, even after
  clock-in.
- `cover-candidates` returns correct weekly hours and overtime (using the
  combined shift), availability, own and combined shift, and blocks.
- Replacing cover deletes the old cover assignment; replacing or removing is
  refused once the cover person has clocked in.
- Clearing the absence, and the original employee clocking in, both leave
  the cover assignment in place.
- Deleting the absent assignment nulls the link on the cover assignment.
- Permissions: leads can call the new routes but not send sick minutes;
  employees can't call them; leads and admins are limited to their location.
- `HoursReportTests`: cover hours count for the cover employee only; sick
  hours stay with the absent employee.
- Migration test, like the existing `*MigrationTests`.

## Acceptance criteria

- [ ] An admin or lead can mark an employee absent and assign cover from
      one dialog, saved together or not at all. Admins can also record sick
      hours there.
- [ ] The cover picker shows weekly hours with an overtime warning, and
      allows unavailable people after a confirmation.
- [ ] Someone already working that day covers by having their shift
      replaced with one combined shift — one assignment, one clock-in, one
      lunch — previewed in the dialog and undone by Remove Cover.
- [ ] A cover shift on a published shift is live immediately: visible to the
      cover employee and on the kiosk, and clockable, without reposting the
      week.
- [ ] The absent shift stays with the original employee and shows who
      covered; the cover shift shows who it covers.
- [ ] Cover can be assigned later, changed or removed.
- [ ] The cover employee can be emailed, with a template editable in the
      template editor.
- [ ] The dashboard highlights today's absences with no cover.
- [ ] Payroll report totals are unchanged in shape; cover hours and sick
      hours land on the right employees.
- [ ] Server tests cover the cases above.

## Open questions

1. Should a lead's cover assignment that pushes someone into overtime need
   an admin's approval, or is the warning enough?
2. Should combined shifts be hidden from Manage Shifts altogether? Today
   they are inactive rows, visible only with "show inactive".

## Decided

- Leads can assign cover. Recording sick hours stays admin-only.
- Someone who already has a shift that day covers through one combined
  shift that replaces their own; there are no second assignments and no
  lunch choice.
- Availability can be overridden for cover, with a warning.
- Cover is for the whole shift; partial cover is left to actual punches.

## Out of scope (follow-up tickets)

- Employees offering up or swapping shifts themselves.
- Broadcasting an open shift to all eligible employees to claim.
- Text/push notifications.
- A call-out report (counts per employee over time). The link added here
  makes it possible later.
- Tying call-outs to time-off requests (see `time-off-requests.md`).
