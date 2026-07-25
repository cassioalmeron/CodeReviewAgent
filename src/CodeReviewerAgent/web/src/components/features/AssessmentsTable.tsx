import { cost, latency, relativeDay } from '@/utils/format'
import type { AssessmentListItem } from '@/types'
import { DataTable, type Column } from '@/components/ui/DataTable'
import { IdTag, Mono, Muted } from '@/components/ui/primitives'

/** Shared table of assessments — used both standalone and inside a review's detail. */
export function AssessmentsTable({
  rows,
  showReview = true,
}: {
  rows: AssessmentListItem[]
  showReview?: boolean
}) {
  const columns: Column<AssessmentListItem>[] = [
    { header: 'ID', width: '64px', render: (a) => <IdTag>#{a.id}</IdTag> },
    ...(showReview
      ? [
          {
            header: 'Review',
            width: '72px',
            render: (a: AssessmentListItem) => <Muted>#{a.reviewId}</Muted>,
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
      rowHref={(a) => `/assessments/${a.id}`}
    />
  )
}
