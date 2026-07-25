import { Cell, Legend, Pie, PieChart, ResponsiveContainer, Tooltip } from 'recharts'
import type { SliceCount } from '@/types'
import { severityColors, tooltip } from './chartTheme'

/** Findings by severity as a filled pie (Critical / Warning / Info). */
export function SeverityPie({ data }: { data: SliceCount[] }) {
  return (
    <ResponsiveContainer width="100%" height={220}>
      <PieChart>
        <Pie data={data} dataKey="count" nameKey="label" outerRadius={80} stroke="var(--panel)" strokeWidth={2}>
          {data.map((s) => (
            <Cell key={s.label} fill={severityColors[s.label] ?? 'var(--muted)'} />
          ))}
        </Pie>
        <Tooltip {...tooltip} />
        <Legend
          iconType="circle"
          formatter={(value) => <span style={{ color: 'var(--muted)', fontSize: 12 }}>{value}</span>}
        />
      </PieChart>
    </ResponsiveContainer>
  )
}
