import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';

import { AccountsApi, AccountDto } from '../../../core/accounts-api';
import { EmployeeDocumentsManager } from '../../accounts/employee-documents-manager/employee-documents-manager';

@Component({
  selector: 'app-employee-documents-page',
  imports: [RouterLink, MatIconModule, MatButtonModule, EmployeeDocumentsManager],
  templateUrl: './employee-documents-page.html',
  styleUrl: './employee-documents-page.scss',
})
export class EmployeeDocumentsPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly accountsApi = inject(AccountsApi);

  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;
  protected readonly accountId = Number(this.route.snapshot.paramMap.get('id'));
  protected readonly account = signal<AccountDto | null>(null);

  ngOnInit(): void {
    this.accountsApi.getOne(this.accountId).subscribe((account) => this.account.set(account));
  }
}
