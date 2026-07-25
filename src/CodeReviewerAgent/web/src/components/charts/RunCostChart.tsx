import {
  Bar,
  CartesianGrid,
  ComposedChart,
  Line,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import styled from 'styled-components'
import type { AssessmentRunPoint } from '@/types'
import { cost as fmtCost, latency as fmtLatency, tokens as fmtTokens } from '@/utils/format'
import { axisProps, chartColors } from './chartTheme'

const Box = styled.div`
  background: var(--panel-2);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 10px 12px;
  font-family: var(--mono);
  font-size: 12px;
`
const Row = styled.div`
  display: flex;
  justify-content: space-between;
  gap: 18px;
  color: var(--muted);
`

interface RunTooltipProps {
  active?: boolean
  payload?: Array<{ payload: AssessmentRunPoint }>
}

function RunTooltip({ active, payload }: RunTooltipProps) {
  if (!active || !payload?.length) return null
  const p = payload[0].payload
  return (
    <Box>
      <Row>
        <span>Assessment</span>
        <span style={{ color: 'var(--gold)' }}>#{p.assessmentId}</span>
      </Row>
      <Row>
        <span>Cost</span>
        <span style={{ color: 'var(--text)' }}>{fmtCost(p.cost)}</span>
      </Row>
      <Row>
        <span>Tokens in / out</span>
        <span style={{ color: 'var(--text)' }}>
          {fmtTokens(p.inputTokens)} / {fmtTokens(p.outputTokens)}
        </span>
      </Row>
      <Row>
        <span>Latency</span>
        <span style={{ color: 'var(--text)' }}>{fmtLatency(p.latencyMs)}</span>
      </Row>
      <Row>
        <span>Findings</span>
        <span style={{ color: 'var(--text)' }}>{p.findingCount}</span>
      </Row>
    </Box>
  )
}

/** Cost (bars) and latency (line) per assessment over time; tokens/findings in the tooltip. */
export function RunCostChart({ data }: { data: AssessmentRunPoint[] }) {
  const rows = data.map((r) => ({ ...r, label: `#${r.assessmentId}` }))
  return (
    <ResponsiveContainer width="100%" height={240}>
      <ComposedChart data={rows} margin={{ top: 8, right: 8, bottom: 0, left: -8 }}>
        <CartesianGrid stroke={chartColors.grid} vertical={false} />
        <XAxis dataKey="label" {...axisProps} />
        <YAxis yAxisId="cost" {...axisProps} width={54} tickFormatter={(v) => `$${Number(v).toFixed(3)}`} />
        <YAxis yAxisId="latency" orientation="right" {...axisProps} width={44} tickFormatter={(v) => `${Math.round(Number(v) / 1000)}s`} />
        <Tooltip content={<RunTooltip />} cursor={{ fill: 'rgba(255,255,255,0.04)' }} />
        <Bar yAxisId="cost" dataKey="cost" fill={chartColors.gold} radius={[3, 3, 0, 0]} maxBarSize={26} />
        <Line
          yAxisId="latency"
          type="monotone"
          dataKey="latencyMs"
          stroke={chartColors.warning}
          strokeWidth={2}
          dot={{ r: 3, fill: chartColors.warning }}
        />
      </ComposedChart>
    </ResponsiveContainer>
  )
}
