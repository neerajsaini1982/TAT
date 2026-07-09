import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';

import { AccountsManager } from '../../accounts/accounts-manager/accounts-manager';

@Component({
  selector: 'app-sa-accounts-page',
  imports: [RouterLink, MatIconModule, MatButtonModule, AccountsManager],
  templateUrl: './sa-accounts-page.html',
  styleUrl: './sa-accounts-page.scss',
})
export class SaAccountsPage {}
