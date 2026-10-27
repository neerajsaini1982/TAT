import { Service, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Observable, from, map, switchMap } from 'rxjs';

import type { CopyWeekDialogData } from './copy-week-dialog';

// Opens the copy-previous-week dialog for both schedule pages; emits true
// when shifts were copied so the page reloads. Loaded on demand (see
// lazy-dialogs.ts) to keep it out of the initial bundle.
@Service()
export class CopyWeekActions {
  private readonly dialog = inject(MatDialog);

  open(data: CopyWeekDialogData): Observable<boolean> {
    return from(import('../lazy-dialogs')).pipe(
      switchMap(({ CopyWeekDialog }) =>
        this.dialog
          .open<InstanceType<typeof CopyWeekDialog>, CopyWeekDialogData, boolean>(CopyWeekDialog, {
            data,
            autoFocus: 'first-tabbable',
          })
          .afterClosed(),
      ),
      map((changed) => changed === true),
    );
  }
}
