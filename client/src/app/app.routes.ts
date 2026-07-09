import { Routes } from '@angular/router';

import { Home } from './features/home/home';
import { LocationHome } from './features/location-home/location-home';
import { SaHome } from './features/sa/sa-home/sa-home';
import { LocationsPage } from './features/sa/locations-page/locations-page';
import { SaAccountsPage } from './features/sa/sa-accounts-page/sa-accounts-page';
import { SaShiftsPage } from './features/sa/sa-shifts-page/sa-shifts-page';
import { AdminHome } from './features/admin/admin-home/admin-home';
import { AdminAccountsPage } from './features/admin/admin-accounts-page/admin-accounts-page';
import { AdminShiftsPage } from './features/admin/admin-shifts-page/admin-shifts-page';
import { AdminAvailabilityPage } from './features/admin/admin-availability-page/admin-availability-page';
import { AdminSchedulePage } from './features/admin/admin-schedule-page/admin-schedule-page';
import { EmployeeHome } from './features/employee/employee-home/employee-home';
import { AvailabilityPage } from './features/employee/availability-page/availability-page';
import { AvailabilityCalendarPage } from './features/employee/availability-calendar-page/availability-calendar-page';
import { saGuard, adminGuard, employeeGuard } from './core/guards';

export const routes: Routes = [
  { path: '', component: Home, pathMatch: 'full' },

  // Order matters: these static 'sa' paths must come before the bare
  // `:locationCode` route below, or the router would treat "sa" as a
  // location code instead.
  { path: 'sa', component: SaHome },
  { path: 'sa/locations', component: LocationsPage, canActivate: [saGuard] },
  { path: 'sa/accounts', component: SaAccountsPage, canActivate: [saGuard] },
  { path: 'sa/shifts', component: SaShiftsPage, canActivate: [saGuard] },

  { path: ':locationCode/admin', component: AdminHome },
  { path: ':locationCode/admin/accounts', component: AdminAccountsPage, canActivate: [adminGuard] },
  { path: ':locationCode/admin/shifts', component: AdminShiftsPage, canActivate: [adminGuard] },
  { path: ':locationCode/admin/availability', component: AdminAvailabilityPage, canActivate: [adminGuard] },
  { path: ':locationCode/admin/schedule', component: AdminSchedulePage, canActivate: [adminGuard] },

  { path: ':locationCode/employee', component: EmployeeHome },
  { path: ':locationCode/employee/availability', component: AvailabilityPage, canActivate: [employeeGuard] },
  { path: ':locationCode/employee/availability2', component: AvailabilityCalendarPage, canActivate: [employeeGuard] },

  { path: ':locationCode', component: LocationHome, pathMatch: 'full' },
];
