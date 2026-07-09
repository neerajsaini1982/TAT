import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { Auth } from './auth';

export const saGuard: CanActivateFn = () => {
  const auth = inject(Auth);
  const router = inject(Router);
  return (auth.isAuthenticated() && auth.role() === 'Sa') || router.parseUrl('/sa');
};

export const adminGuard: CanActivateFn = (route) => {
  const auth = inject(Auth);
  const router = inject(Router);
  const locationCode = route.paramMap.get('locationCode');

  const ok =
    auth.isAuthenticated() &&
    (auth.role() === 'Admin' || auth.role() === 'Lead') &&
    auth.locationCode() === locationCode;

  return ok || router.parseUrl(`/${locationCode}/admin`);
};

// Any authenticated role (Employee, Lead, Admin, Sa all clock in the same
// way) can access their own location's employee sub-routes.
export const employeeGuard: CanActivateFn = (route) => {
  const auth = inject(Auth);
  const router = inject(Router);
  const locationCode = route.paramMap.get('locationCode');

  const ok = auth.isAuthenticated() && auth.locationCode() === locationCode;

  return ok || router.parseUrl(`/${locationCode}/employee`);
};
