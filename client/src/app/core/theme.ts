import { Service, signal } from '@angular/core';

export type ThemeName = 'azure' | 'green' | 'violet' | 'orange' | 'rose';
export type ColorScheme = 'light' | 'dark';

const THEME_STORAGE_KEY = 'tat-theme';
const SCHEME_STORAGE_KEY = 'tat-color-scheme';

export interface ThemeOption {
  name: ThemeName;
  label: string;
}

// Keep in sync with the `$themes` map in `src/styles.scss`.
export const THEMES: ThemeOption[] = [
  { name: 'azure', label: 'Azure' },
  { name: 'green', label: 'Green' },
  { name: 'violet', label: 'Violet' },
  { name: 'orange', label: 'Orange' },
  { name: 'rose', label: 'Rose' },
];

@Service()
export class Theme {
  readonly theme = signal<ThemeName>(this.readStored(THEME_STORAGE_KEY, 'azure'));
  readonly scheme = signal<ColorScheme>(this.readStored(SCHEME_STORAGE_KEY, 'light'));

  constructor() {
    this.applyTheme(this.theme());
    this.applyScheme(this.scheme());
  }

  setTheme(name: ThemeName): void {
    this.theme.set(name);
    this.applyTheme(name);
    localStorage.setItem(THEME_STORAGE_KEY, name);
  }

  setScheme(scheme: ColorScheme): void {
    this.scheme.set(scheme);
    this.applyScheme(scheme);
    localStorage.setItem(SCHEME_STORAGE_KEY, scheme);
  }

  toggleScheme(): void {
    this.setScheme(this.scheme() === 'light' ? 'dark' : 'light');
  }

  private applyTheme(name: ThemeName): void {
    document.documentElement.setAttribute('data-theme', name);
  }

  private applyScheme(scheme: ColorScheme): void {
    document.documentElement.style.colorScheme = scheme;
  }

  private readStored<T extends string>(key: string, fallback: T): T {
    return (localStorage.getItem(key) as T | null) ?? fallback;
  }
}
