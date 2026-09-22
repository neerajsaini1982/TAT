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

// Stricter than adminGuard: Admin (or Sa) only, no Lead — for pages backed
// by endpoints that are already AdminOrAbove-only server-side (account
// management, location settings), so a Lead can't land on a page that can
// only 403 for them. Sa isn't expected to hit these directly (it has no
// LocationId of its own), but is included since it can act as any role.
export const adminOnlyGuard: CanActivateFn = (route) => {
  const auth = inject(Auth);
  const router = inject(Router);
  const locationCode = route.paramMap.get('locationCode');

  const ok =
    auth.isAuthenticated() &&
    (auth.role() === 'Admin' || auth.role() === 'Sa') &&
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

// A kiosk session isn't any one employee — the device itself logs in (see
// Auth.kioskLogin) and individual punches identify themselves separately
// with a PIN. Not applied to the top-level /:locationCode/kiosk route
// itself (KioskHome handles its own signed-in/out branching, same as
// EmployeeHome/AdminHome); only relevant if kiosk grows sub-routes later.
export const kioskGuard: CanActivateFn = (route) => {
  const auth = inject(Auth);
  const router = inject(Router);
  const locationCode = route.paramMap.get('locationCode');

  const ok = auth.isAuthenticated() && auth.role() === 'Kiosk' && auth.locationCode() === locationCode;

  return ok || router.parseUrl(`/${locationCode}/kiosk`);
};
