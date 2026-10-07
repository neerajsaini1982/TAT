// The dialogs that are loaded on demand rather than shipped in the initial
// bundle, which sits right at its size budget. They share this one entry
// point on purpose: each separate import() target becomes its own lazy
// chunk, and from three lazy chunks up the Angular build switches on its
// chunk optimizer, which re-bundles the initial code about 40 kB larger and
// over the budget. Add further on-demand dialogs here rather than importing
// them directly.
export { CallOutDialog } from './call-out-dialog/call-out-dialog';
export { PayrollEmailDialog } from './payroll-email-dialog/payroll-email-dialog';
