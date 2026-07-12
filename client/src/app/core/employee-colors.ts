// Deterministic, stable color per employee id — lets the admin schedule
// grid and the "This Week's Schedule" team view visually tell different
// employees apart (e.g. a colored dot next to their name, a matching
// border on their shift chips) without a color needing to be stored
// anywhere. Same account id always maps to the same color.
const PALETTE = [
  '#5B8DEF',
  '#F2994A',
  '#27AE60',
  '#BB6BD9',
  '#EB5757',
  '#2D9CDB',
  '#F2C94C',
  '#56CCF2',
  '#9B51E0',
  '#219653',
];

// Multiplying by a number coprime with the palette length scatters
// consecutive account ids across it, so e.g. accounts 2 and 3 don't end up
// with adjacent (similar-looking) colors just because they were created
// back to back.
export function employeeColor(accountId: number): string {
  return PALETTE[(accountId * 7) % PALETTE.length];
}
