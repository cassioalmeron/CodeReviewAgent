import styled from 'styled-components'
import { cost, relativeDay } from '@/utils/format'
import type { Evaluation } from '@/types'
import { DataTable, type Column } from '@/components/ui/DataTable'
import { IdTag, Mono, Muted } from '@/components/ui/primitives'

const Overall = styled.span`
  display: inline-flex;
  align-items: baseline;
  gap: 3px;
  font-family: var(--mono);
`

const Big = styled.span`
  color: var(--gold);
  font-size: 15px;
  font-weight: 700;
`

/** Shared table of judge evaluations — used standalone and inside an assessment's detail. */
export function EvaluationsTable({
  rows,
  showAssessment = true,
}: {
  rows: Evaluation[]
  showAssessment?: boolean
}) {
  const columns: Column<Evaluation>[] = [
    { header: 'ID', width: '64px', render: (e) => <IdTag>#{e.id}</IdTag> },
    ...(showAssessment
      ? [
          {
            header: 'Assessment',
            width: '96px',
            render: (e: Evaluation) => <Muted>#{e.assessmentId}</Muted>,
          },
        ]
      : []),
    { header: 'Judge', render: (e) => <Mono>{e.judgeModel ?? '—'}</Mono> },
    {
      header: 'Rubric',
      width: '80px',
      render: (e) => <Muted>{e.rubricVersion ?? '—'}</Muted>,
    },
    {
      header: 'Overall',
      align: 'right',
      width: '90px',
      render: (e) => (
        <Overall>
          <Big>{e.overall}</Big>
          <Muted>/5</Muted>
        </Overall>
      ),
    },
    { header: 'Cost', align: 'right', width: '90px', render: (e) => <Muted>{cost(e.cost)}</Muted> },
    {
      header: 'Run',
      align: 'right',
      width: '96px',
      render: (e) => <Muted>{relativeDay(e.createdAt)}</Muted>,
    },
  ]

  return (
    <DataTable
      columns={columns}
      rows={rows}
      rowKey={(e) => e.id}
      rowHref={(e) => `/evaluations/${e.id}`}
    />
  )
}
