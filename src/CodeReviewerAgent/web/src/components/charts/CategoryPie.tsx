import { Cell, Legend, Pie, PieChart, ResponsiveContainer, Tooltip } from 'recharts'
import type { SliceCount } from '@/types'
import { categoryColor, tooltip } from './chartTheme'

/** Findings by category as a filled pie (Bug / Security / Performance / Style / Maintainability…). */
export function CategoryPie({ data }: { data: SliceCount[] }) {
  return (
    <ResponsiveContainer width="100%" height={220}>
      <PieChart>
        <Pie data={data} dataKey="count" nameKey="label" outerRadius={80} stroke="var(--panel)" strokeWidth={2}>
          {data.map((s, i) => (
            <Cell key={s.label} fill={categoryColor(s.label, i)} />
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
