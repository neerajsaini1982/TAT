import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';

import { Auth } from '../../../core/auth';
import { EmployeeDocumentsManager } from '../../accounts/employee-documents-manager/employee-documents-manager';

@Component({
  selector: 'app-my-documents-page',
  imports: [RouterLink, MatIconModule, MatButtonModule, EmployeeDocumentsManager],
  templateUrl: './my-documents-page.html',
  styleUrl: './my-documents-page.scss',
})
export class MyDocumentsPage {
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(Auth);

  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;
  protected readonly accountId = this.auth.accountId()!;
}
