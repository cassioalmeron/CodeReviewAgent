// The single source of chart colour + axis/tooltip styling, so every chart reads as one system.
// Severity reuses the same tokens as the SeverityTag; categories get their own palette.

export const chartColors = {
  gold: 'var(--gold)',
  info: 'var(--info)',
  add: 'var(--add)',
  warning: 'var(--warning)',
  grid: 'var(--border-soft)',
  axis: 'var(--faint)',
}

export const severityColors: Record<string, string> = {
  Critical: 'var(--critical)',
  Warning: 'var(--warning)',
  Info: 'var(--info)',
}

// Distinguishable in the dark theme and in greyscale; extend if a new Category is added.
export const categoryColors: Record<string, string> = {
  Bug: '#e5484d',
  Security: '#d98a3d',
  Performance: '#c9a227',
  Style: '#6b93c0',
  Maintainability: '#7a8bd0',
  Convention: '#4a9e6b',
}

export const categoryPalette = ['#e5484d', '#d98a3d', '#c9a227', '#6b93c0', '#7a8bd0', '#4a9e6b']

export const categoryColor = (label: string, index: number) =>
  categoryColors[label] ?? categoryPalette[index % categoryPalette.length]

export const axisProps = {
  stroke: 'var(--faint)',
  tick: { fill: 'var(--faint)', fontSize: 11, fontFamily: 'var(--mono)' },
  tickLine: false,
} as const

// Styling passed to recharts <Tooltip> so it matches the app's panels.
export const tooltip = {
  contentStyle: {
    background: 'var(--panel-2)',
    border: '1px solid var(--border)',
    borderRadius: 8,
    fontFamily: 'var(--mono)',
    fontSize: 12,
  },
  labelStyle: { color: 'var(--muted)' },
  itemStyle: { color: 'var(--text)' },
  cursor: { fill: 'rgba(255,255,255,0.04)' },
} as const
