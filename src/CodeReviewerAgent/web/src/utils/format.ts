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
