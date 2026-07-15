import styled from 'styled-components'

const Strip = styled.dl`
  display: flex;
  flex-wrap: wrap;
  gap: 1px;
  margin: 0;
  background: var(--border-soft);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  overflow: hidden;
`

const Cell = styled.div`
  flex: 1 1 auto;
  min-width: 120px;
  padding: 12px 16px;
  background: var(--panel);
`

const Key = styled.dt`
  font-family: var(--mono);
  font-size: 10px;
  letter-spacing: 0.12em;
  text-transform: uppercase;
  color: var(--faint);
  margin: 0 0 4px;
`

const Val = styled.dd`
  margin: 0;
  font-family: var(--mono);
  font-size: 15px;
  color: var(--text);
`

export interface Metric {
  key: string
  value: string
}

/** Instrument-panel readout of run metadata (cost / latency / tokens / model…). */
export function MetricStrip({ metrics }: { metrics: Metric[] }) {
  return (
    <Strip>
      {metrics.map((m) => (
        <Cell key={m.key}>
          <Key>{m.key}</Key>
          <Val>{m.value}</Val>
        </Cell>
      ))}
    </Strip>
  )
}
