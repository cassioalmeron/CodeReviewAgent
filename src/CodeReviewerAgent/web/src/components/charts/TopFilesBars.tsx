import {
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { SliceCount } from '@/types'
import { axisProps, chartColors, tooltip } from './chartTheme'

const shortFile = (path: string) => {
  const name = path.split(/[\\/]/).pop() ?? path
  return name.length > 22 ? `…${name.slice(-21)}` : name
}

/** Top files by finding count, as horizontal bars. */
export function TopFilesBars({ data }: { data: SliceCount[] }) {
  const rows = data.map((d) => ({ ...d, name: shortFile(d.label) }))
  return (
    <ResponsiveContainer width="100%" height={Math.max(160, rows.length * 34)}>
      <BarChart data={rows} layout="vertical" margin={{ top: 4, right: 12, bottom: 4, left: 8 }}>
        <CartesianGrid stroke={chartColors.grid} horizontal={false} />
        <XAxis type="number" {...axisProps} allowDecimals={false} />
        <YAxis type="category" dataKey="name" {...axisProps} width={150} />
        <Tooltip {...tooltip} />
        <Bar dataKey="count" fill={chartColors.gold} radius={[0, 3, 3, 0]} maxBarSize={20} />
      </BarChart>
    </ResponsiveContainer>
  )
}
