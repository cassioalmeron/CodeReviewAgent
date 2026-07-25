import { api } from '@/services/api'
import { useProject } from '@/contexts/ProjectContext'
import { Async } from '@/components/ui/Async'
import { DataTable, type Column } from '@/components/ui/DataTable'
import { EmptyState } from '@/components/ui/States'
import { PageHeader } from '@/components/ui/PageHeader'
import { IdTag, Mono, Muted } from '@/components/ui/primitives'
import { useAsync } from '@/hooks/useAsync'
import { relativeDay, shortHash } from '@/utils/format'
import type { ReviewListItem } from '@/types'

const columns: Column<ReviewListItem>[] = [
  { header: 'ID', width: '64px', render: (r) => <IdTag>#{r.id}</IdTag> },
  { header: 'Source', render: (r) => <Mono>{r.source ?? '—'}</Mono> },
  { header: 'Content hash', render: (r) => <Muted>{shortHash(r.contentHash)}</Muted> },
  {
    header: 'Assessments',
    align: 'right',
    width: '110px',
    render: (r) => <Mono>{r.assessmentCount}</Mono>,
  },
  {
    header: 'Captured',
    align: 'right',
    width: '110px',
    render: (r) => <Muted>{relativeDay(r.createdAt)}</Muted>,
  },
]

export function ReviewsPage() {
  const { projectId, project } = useProject()
  const state = useAsync(() => api.reviews(projectId ?? undefined), [projectId])
  return (
    <>
      <PageHeader
        title="Reviews"
        sub={`Every captured diff in ${project?.name ?? 'this project'}. Open one to see its assessments.`}
      />
      <Async state={state}>
        {(reviews) =>
          reviews.length === 0 ? (
            <EmptyState
              title="No reviews stored yet"
              hint="Run the CLI (e.g. dotnet run -- review) to capture diffs into the store."
            />
          ) : (
            <DataTable
              columns={columns}
              rows={reviews}
              rowKey={(r) => r.id}
              rowHref={(r) => `/reviews/${r.id}`}
            />
          )
        }
      </Async>
    </>
  )
}
