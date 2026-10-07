import { Service, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Observable, from, map, switchMap } from 'rxjs';

import { ShiftAssignmentDto } from '../../../core/shift-assignments-api';
import type { CallOutDialogData } from './call-out-dialog';

// Opens the call-out dialog for the schedule screens that offer it, so each
// one only has to reload when the returned observable emits true. The dialog
// is loaded on demand (see lazy-dialogs.ts) — only leads and admins ever
// open it.
@Service()
export class CallOutActions {
  private readonly dialog = inject(MatDialog);

  // Mark absent, with or without cover.
  callOut(assignment: ShiftAssignmentDto): Observable<boolean> {
    return this.open({ assignment, mode: 'call-out' });
  }

  // Assign, change or remove cover on a shift that's already absent.
  cover(assignment: ShiftAssignmentDto): Observable<boolean> {
    return this.open({ assignment, mode: 'cover' });
  }

  private open(data: CallOutDialogData): Observable<boolean> {
    return from(import('../lazy-dialogs')).pipe(
      switchMap(({ CallOutDialog }) =>
        this.dialog
          .open<InstanceType<typeof CallOutDialog>, CallOutDialogData, boolean>(CallOutDialog, {
            data,
            autoFocus: 'first-tabbable',
          })
          .afterClosed(),
      ),
      map((changed) => changed === true),
    );
  }
}
