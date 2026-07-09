import { Routes } from '@angular/router';

import { Dashboard } from './features/dashboard/dashboard';
import { SaHome } from './features/sa/sa-home/sa-home';
import { LocationsPage } from './features/sa/locations-page/locations-page';
import { SaAccountsPage } from './features/sa/sa-accounts-page/sa-accounts-page';
import { AdminHome } from './features/admin/admin-home/admin-home';
import { AdminAccountsPage } from './features/admin/admin-accounts-page/admin-accounts-page';
import { EmployeeHome } from './features/employee/employee-home/employee-home';
import { saGuard, adminGuard } from './core/guards';

export const routes: Routes = [
  { path: '', component: Dashboard, pathMatch: 'full' },

  { path: 'sa', component: SaHome },
  { path: 'sa/locations', component: LocationsPage, canActivate: [saGuard] },
  { path: 'sa/accounts', component: SaAccountsPage, canActivate: [saGuard] },

  { path: ':locationCode/admin', component: AdminHome },
  { path: ':locationCode/admin/accounts', component: AdminAccountsPage, canActivate: [adminGuard] },

  { path: ':locationCode/employee', component: EmployeeHome },
];
