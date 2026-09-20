import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';

import { AccountsApi, AccountDto } from '../../../core/accounts-api';
import { WriteUpsManager } from '../../accounts/write-ups-manager/write-ups-manager';

@Component({
  selector: 'app-employee-write-ups-page',
  imports: [RouterLink, MatIconModule, MatButtonModule, WriteUpsManager],
  templateUrl: './employee-write-ups-page.html',
  styleUrl: './employee-write-ups-page.scss',
})
export class EmployeeWriteUpsPage implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly accountsApi = inject(AccountsApi);

  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;
  protected readonly accountId = Number(this.route.snapshot.paramMap.get('id'));
  protected readonly account = signal<AccountDto | null>(null);

  ngOnInit(): void {
    this.accountsApi.getOne(this.accountId).subscribe((account) => this.account.set(account));
  }
}
