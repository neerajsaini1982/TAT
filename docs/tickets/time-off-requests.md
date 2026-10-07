# Time-off requests and approvals

## Summary

Let employees request time off from the employee portal, and let admins
approve or deny those requests. Each location's admin sets up their own list
of time-off types in location settings and chooses whether each type is paid
or unpaid. Approved time off blocks scheduling on those days, shows on the
schedule, and appears in the payroll hours report and payroll email alongside
sick time.

Today the only way to record time away is after the fact: an admin enters
sick hours on a shift (`ShiftAssignment.SickMinutes`) or on an unscheduled
day (`SickTimeEntry`), or marks a shift absent. Nothing lets an employee ask
in advance, and nothing stops a lead/admin from scheduling someone who is
known to be away.

## User stories

**Employee**
1. As an employee, I can submit a time-off request for one day or a range of
   days, choose a type, choose full day or partial day (with hours), and add
   an optional note.
2. As an employee, I can see all my requests and their status (Pending,
   Approved, Denied, Cancelled), including the reviewer's comment.
3. As an employee, I can cancel a request while it is Pending. Once Approved,
   I can ask to cancel it, which needs admin approval (or: only an admin can
   cancel it — see open questions).
4. As an employee, I see my approved time off on my schedule
   (`employee-schedule-page`) and on the current-week schedule.

**Admin** (`AdminOrAbove`)
5. As an admin, I see a list of pending requests for my location, with a
   count badge on admin home, and can filter by status, employee and date.
6. As an admin, I can approve or deny a request with an optional comment.
   When I approve, I can see if the employee already has published shifts
   in that range.
7. As an admin, I can create time off on an employee's behalf (already
   approved), e.g. when they phone in.
8. As an admin, I can cancel an approved request, with a reason.
9. Every change (submitted, approved, denied, cancelled, edited) is kept in
   the request's history with who and when — the same pattern as
   `WriteUpEvent`.

**Lead**
10. As a lead, I can see approved time off on the schedule so I don't plan
    around someone who's away. (Whether leads can approve is an open
    question.)

## Rules

- Requests are per employee, per location (`Account.LocationId`).
- Dates are interpreted in the location's time zone
  (`LocationSettings.TimeZone`), like everything else.
- A request can't start in the past, except when an admin creates it.
- A request can't overlap another Pending or Approved request for the same
  employee.
- **Scheduling:** `ShiftAssignmentsController` create/move must reject
  assigning an employee to a date covered by an Approved full-day request
  ("This employee has approved time off on this date."). Partial-day time off
  shows as a warning instead. Like the existing availability check, this is
  skipped in `DevelopmentMode`.
- **Approving over existing shifts:** if the employee already has assignments
  in the range, the approve dialog lists them. The admin chooses to remove
  them or keep them (keep = the admin will sort it out). Draft (unpublished)
  assignments should probably be removed automatically.
- **Types are set per location.** Each location has its own list of
  time-off types (see `TimeOffType` below), managed by the location's admin.
  New locations get default types, **all unpaid**; the admin turns on "Paid"
  for any type their policy pays.
- **Sick stays as it is.** Sick time keeps using the existing
  `ShiftAssignment.SickMinutes` / `SickTimeEntry` logic and is always paid.
  It is not one of the configurable types and can't be made unpaid. The
  settings page may show it as a locked, built-in "Sick (paid)" row for
  clarity.
- **Paid/unpaid is saved on the request when approved**
  (`TimeOffRequest.IsPaid`). Changing a type's Paid setting later only
  affects future approvals — it never changes past pay periods.
- **Types are never deleted once used** — they're deactivated, so old
  requests and reports keep their label. A type with no requests can be
  deleted.
- **Payroll report:** two new columns in the hours report
  (`ReportsController` hours) and payroll email (`PayrollHoursEmail`), next
  to Sick, per day and in total:
  - **Paid Time Off** — included in Total Paid.
  - **Unpaid Time Off** — for information only, not included in Total Paid.
  Each employee's daily detail and the payroll email also show a breakdown by
  type (e.g. "Vacation 8h, Bereavement 16h"), rather than one column per
  type, so the report keeps the same shape however many types a location
  has. Time off is not worked time, so `OvertimeCalculator` must ignore it
  (same as sick), paid or unpaid.
- **Full day length:** a full day off counts as the scheduled shift length if
  the employee had a shift that day, otherwise a default (e.g. 8h, setting
  below).
- Deactivating an employee (`IsActive = false`) leaves their history alone;
  their pending requests are cancelled automatically.

## Data model (server)

New `Server/Models/TimeOffRequest.cs`:

| Field | Type | Notes |
|---|---|---|
| Id | int | |
| AccountId / Account | int | the employee |
| LocationId | int | copied from the account when created, for filtering |
| TimeOffTypeId / TimeOffType | int | the location's type |
| StartDate / EndDate | DateOnly | inclusive range |
| IsPartialDay | bool | only allowed when StartDate == EndDate |
| PartialMinutes | int? | required when partial |
| Note | string? | from the employee |
| Status | `TimeOffStatus` enum | Pending, Approved, Denied, Cancelled |
| IsPaid | bool? | copied from the type when approved; null until then |
| ReviewedByAccountId / ReviewedAt / ReviewComment | | last decision |
| CreatedByAccountId / CreatedAt | | employee, or admin when created on their behalf |

New `Server/Models/TimeOffType.cs` (one list per location):

| Field | Type | Notes |
|---|---|---|
| Id | int | |
| LocationId | int | |
| Name | string | unique per location, e.g. "Vacation" |
| IsPaid | bool | default **false** |
| IsActive | bool | inactive types can't be picked for new requests |
| SortOrder | int | order in the employee's drop-down |
| Color | string? | optional, for the schedule |
| PayrollCode | string? | optional, e.g. ADP earnings code for a future export |

Default types created for each location (existing locations get them in the
migration; new locations when they're created), **all unpaid**: Vacation,
Personal, Bereavement, Jury Duty, Unpaid, Other.

New `TimeOffRequestEvent` (history, never deleted): Id, TimeOffRequestId,
Action (Submitted, Approved, Denied, Cancelled, Edited), ActorAccountId, At,
Comment.

New `LocationSettings` fields:
- `TimeOffEnabled` (bool, default true)
- `TimeOffMinNoticeDays` (int?, default null = no minimum)
- `TimeOffFullDayMinutes` (int, default 480)

Add an EF Core migration in `server/Data/Migrations` (which also seeds the
default types for existing locations) and register the `DbSet`s in
`AppDbContext`.

## API (server)

New `TimeOffRequestsController` (`[Authorize]`):

| Method | Route | Who | Purpose |
|---|---|---|---|
| GET | `/api/time-off-requests/mine` | any employee | my requests |
| POST | `/api/time-off-requests` | any employee | submit (Pending) |
| PUT | `/api/time-off-requests/{id}/cancel` | owner (Pending only) / admin | cancel |
| GET | `/api/time-off-requests?status=&accountId=&from=&to=` | AdminOrAbove | location's requests |
| POST | `/api/time-off-requests/admin` | AdminOrAbove | create for an employee (Approved) |
| PUT | `/api/time-off-requests/{id}/approve` | AdminOrAbove | body: comment, removeConflictingAssignments |
| PUT | `/api/time-off-requests/{id}/deny` | AdminOrAbove | body: comment (required) |
| GET | `/api/time-off-requests/{id}/conflicts` | AdminOrAbove | shift assignments in the range |
| GET | `/api/time-off-requests/calendar?from=&to=` | LeadOrAbove | approved time off for schedule views |

Time-off types (`TimeOffTypesController`):

| Method | Route | Who | Purpose |
|---|---|---|---|
| GET | `/api/time-off-types` | any signed-in user | active types for my location (request dialog) |
| GET | `/api/time-off-types/all` | AdminOrAbove | all types incl. inactive (settings) |
| POST | `/api/time-off-types` | AdminOrAbove | add a type |
| PUT | `/api/time-off-types/{id}` | AdminOrAbove | rename, paid/unpaid, active, order, colour, code |
| DELETE | `/api/time-off-types/{id}` | AdminOrAbove | only if never used; otherwise 409 — deactivate instead |

Admins only see requests for their own location; Sa sees all. An employee
only ever sees their own requests, and never the event history.

## Emails

Add keys to `EmailTemplateKeys` / `EmailTemplateCatalog` so they can be
edited in the email template editor:
- **Time Off Requested** — to the location's admins
- **Time Off Approved** / **Time Off Denied** — to the employee, with the
  reviewer's comment

Sent with `EmailSender` using the location's SMTP settings; skipped quietly
if SMTP isn't set up (same as current emails).

## UI (client)

Employee (`features/employee`):
- New `time-off-page` (route + account-menu link next to My Write-ups /
  My Documents): list of my requests with status chips, and a "Request
  time off" button opening `time-off-request-dialog` (type, date range,
  full/partial day, hours, note).
- Approved time off shown on `employee-schedule-page` and
  `current-week-schedule`.

Admin (`features/admin`):
- New `admin-time-off-page`: tabs Pending / Approved / History, filters,
  approve/deny actions. Approve opens a dialog listing conflicting shifts.
- "Pending time off (N)" card/badge on `admin-home`.
- `admin-schedule-assign-page`, `schedule-week-timeline`,
  `schedule-day-view`: show a "Time off" block for affected employees and
  disable assigning them on full days off.
- `admin-payroll-report-page` + `payroll-hours-chart`: new Paid Time Off and
  Unpaid Time Off columns, with the per-type breakdown in the daily detail.
- `admin-location-settings-page`: the new time-off settings, plus a "Time off
  types" table (add, rename, Paid toggle, Active toggle, reorder, colour,
  payroll code), with Sick shown as a locked "paid" row.

Kiosk: no change (optionally show "Time off" in `kiosk-schedule`).

## Tests (`server.Tests`)

- Submitting: overlap rejected, past dates rejected for employees,
  partial-day rules, min-notice rule.
- Permissions: employee can't see/approve others' requests; admin is
  limited to their location.
- Approving writes the event and status; denying requires a comment.
- `ShiftAssignmentsController` rejects assigning on an approved full day off
  (and allows it in DevelopmentMode).
- `HoursReportTests`: paid time off counts in Paid Time Off and Total Paid;
  unpaid counts in Unpaid Time Off only; neither counts towards overtime;
  per-type breakdown is correct.
- Changing a type's Paid setting doesn't change already-approved requests.
- Types: defaults created unpaid; used types can't be deleted; inactive
  types can't be requested; admins only manage their own location's types.
- `EmailHoursReportTests`: new columns and breakdown rendered.
- Migration test, like the existing `*MigrationTests`.

## Acceptance criteria

- [ ] Employee can submit, view and cancel (while pending) time-off requests.
- [ ] Admin can view, approve (with conflict handling), deny, create and
      cancel requests for their location.
- [ ] Full history kept for every request.
- [ ] Approved full days off block shift assignment; partial days warn.
- [ ] Approved time off is visible on admin, lead and employee schedules.
- [ ] Each location's admin can manage its time-off types (add, rename,
      paid/unpaid, deactivate, reorder). Defaults are all unpaid; Sick stays
      paid and unchanged.
- [ ] Paid and unpaid time off show as separate columns in the payroll
      report and payroll email, with a per-type breakdown; only paid counts in
      Total Paid; neither counts towards overtime.
- [ ] Request/approve/deny emails sent, editable in the template editor.
- [ ] Settings for enable/disable, minimum notice and full-day length.
- [ ] Server tests cover the cases above.

## Open questions

1. Can leads approve requests, or only admins? (Could reuse a per-account
   flag like `CanWriteUpOthers`.)
2. Should employees be able to cancel an already-approved request themselves?
3. Blackout dates (e.g. holidays when no time off allowed) — v1 or later?

## Decided

- Time-off types are configured per location by the location's admin.
- Sick stays on the existing logic and is always paid. All other default
  types start as unpaid; admins can mark any of them paid.

## Out of scope (follow-up tickets)

- Balances and accrual (hours earned per hour worked, yearly caps,
  carry-over), and blocking requests over the balance.
- Converting existing `SickMinutes` / `SickTimeEntry` data into requests.
- Limits on how many people can be off on the same day.
- Text/push notifications.
