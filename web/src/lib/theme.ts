const STORAGE_KEY = 'p27.theme'

/** The DS (tokens/theme.css) is dark-first: :root is Gray 100 by default.
 *  'white' is its light override; there's no separate attribute for dark since that's the default. */
export type Theme = 'light' | 'dark'

export function loadTheme(): Theme | null {
  const stored = localStorage.getItem(STORAGE_KEY)
  return stored === 'light' || stored === 'dark' ? stored : null
}

export function applyTheme(theme: Theme | null): void {
  if (theme === 'light') document.documentElement.dataset.theme = 'white'
  else delete document.documentElement.dataset.theme
}

export function saveTheme(theme: Theme | null): void {
  if (theme === null) localStorage.removeItem(STORAGE_KEY)
  else localStorage.setItem(STORAGE_KEY, theme)
  applyTheme(theme)
}
