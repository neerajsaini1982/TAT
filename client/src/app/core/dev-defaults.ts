// Dev-only convenience so you don't have to retype credentials while
// testing locally. Edit these to match whatever accounts exist in your
// local database. Only ever applied when `isDevMode()` is true (never in
// a production build), see usages in the sa/admin/employee login forms.
export const DEV_DEFAULTS = {
  sa: { username: 'sa', password: 'ChangeMe123!' },
  admin: { username: 'admin1', password: 'AdminPass1!' },
  employeeCode: '496780',
  locationCode: 'l2psj',
};
