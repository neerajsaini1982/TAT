import { Component, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';

import { AccountsManager } from '../../accounts/accounts-manager/accounts-manager';

@Component({
  selector: 'app-admin-accounts-page',
  imports: [RouterLink, MatIconModule, MatButtonModule, AccountsManager],
  templateUrl: './admin-accounts-page.html',
  styleUrl: './admin-accounts-page.scss',
})
export class AdminAccountsPage {
  private readonly route = inject(ActivatedRoute);
  protected readonly locationCode = this.route.snapshot.paramMap.get('locationCode')!;
}
