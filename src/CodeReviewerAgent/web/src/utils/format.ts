export const shortHash = (hash: string) => hash.slice(0, 10)

export const cost = (value: number) => `$${value.toFixed(4)}`

export const latency = (ms: number) =>
  ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s`

export const tokens = (n: number) => n.toLocaleString('en-US')

export const dateTime = (iso: string) => {
  const d = new Date(iso)
  return d.toLocaleString('en-US', {
    year: 'numeric',
    month: 'short',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

export const relativeDay = (iso: string) => {
  const d = new Date(iso)
  return d.toLocaleDateString('en-US', { month: 'short', day: '2-digit' })
}

/** Wall-clock time of a run: 6 min 14 s, 1 h 58 min 25 s, 53 s. */
export const duration = (ms: number) => {
  const total = Math.round(ms / 1000)
  const hours = Math.floor(total / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  const seconds = total % 60
  return [
    hours > 0 ? `${hours} h` : '',
    hours > 0 || minutes > 0 ? `${minutes} min` : '',
    `${seconds} s`,
  ]
    .filter(Boolean)
    .join(' ')
}
