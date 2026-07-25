import {
  PolarAngleAxis,
  PolarGrid,
  Radar,
  RadarChart,
  ResponsiveContainer,
  Tooltip,
} from 'recharts'
import type { JudgeAverages } from '@/types'
import { tooltip } from './chartTheme'

/** The four rubric criteria (1–5) as a radar; overall is shown separately as a number. */
export function JudgeRadar({ judge }: { judge: JudgeAverages }) {
  const data = [
    { axis: 'Correctness', value: judge.correctness },
    { axis: 'Actionability', value: judge.actionability },
    { axis: 'Calibration', value: judge.calibration },
    { axis: 'Signal/noise', value: judge.signalToNoise },
  ]
  return (
    <ResponsiveContainer width="100%" height={220}>
      <RadarChart data={data} outerRadius="72%">
        <PolarGrid stroke="var(--border-soft)" />
        <PolarAngleAxis
          dataKey="axis"
          tick={{ fill: 'var(--muted)', fontSize: 11, fontFamily: 'var(--mono)' }}
        />
        <Radar
          dataKey="value"
          stroke="var(--gold)"
          fill="var(--gold)"
          fillOpacity={0.18}
          strokeWidth={2}
        />
        <Tooltip {...tooltip} />
      </RadarChart>
    </ResponsiveContainer>
  )
}
