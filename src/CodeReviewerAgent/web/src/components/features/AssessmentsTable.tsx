import { cost, latency, relativeDay } from '@/utils/format'
import type { AnalysisListItem } from '@/types'
import { DataTable, type Column } from '@/components/ui/DataTable'
import { IdTag, Mono, Muted } from '@/components/ui/primitives'

/** Shared table of analyses — used both standalone and inside a diff's detail. */
export function AnalysesTable({
  rows,
  showDiff = true,
}: {
  rows: AnalysisListItem[]
  showDiff?: boolean
}) {
  const columns: Column<AnalysisListItem>[] = [
    { header: 'ID', width: '64px', render: (a) => <IdTag>#{a.id}</IdTag> },
    ...(showDiff
      ? [
          {
            header: 'Diff',
            width: '72px',
            render: (a: AnalysisListItem) => <Muted>#{a.diffId}</Muted>,
          },
        ]
      : []),
    { header: 'Model', render: (a) => <Mono>{a.model ?? a.engine ?? '—'}</Mono> },
    {
      header: 'Prompt',
      width: '80px',
      render: (a) => <Muted>{a.promptVersion ?? '—'}</Muted>,
    },
    {
      header: 'Findings',
      align: 'right',
      width: '90px',
      render: (a) => <Mono>{a.findingCount}</Mono>,
    },
    { header: 'Cost', align: 'right', width: '90px', render: (a) => <Muted>{cost(a.cost)}</Muted> },
    {
      header: 'Latency',
      align: 'right',
      width: '90px',
      render: (a) => <Muted>{latency(a.latencyMs)}</Muted>,
    },
    {
      header: 'Run',
      align: 'right',
      width: '96px',
      render: (a) => <Muted>{relativeDay(a.createdAt)}</Muted>,
    },
  ]

  return (
    <DataTable
      columns={columns}
      rows={rows}
      rowKey={(a) => a.id}
      rowHref={(a) => `/analyses/${a.id}`}
    />
  )
}
