import { useParams } from 'react-router-dom'
import { api } from '@/services/api'
import { AnalysesTable } from '@/components/features/AnalysesTable'
import { Async } from '@/components/ui/Async'
import { DiffView } from '@/components/features/DiffView'
import { EmptyState } from '@/components/ui/States'
import { MetricStrip } from '@/components/ui/MetricStrip'
import { PageHeader } from '@/components/ui/PageHeader'
import { Eyebrow, Field, IdTag, Mono, Stack } from '@/components/ui/primitives'
import { useAsync } from '@/hooks/useAsync'
import { dateTime } from '@/utils/format'

export function DiffDetailPage() {
  const { id } = useParams()
  const diffId = Number(id)
  const diff = useAsync(() => api.diff(diffId), [diffId])
  const analyses = useAsync(() => api.diffAnalyses(diffId), [diffId])

  return (
    <>
      <PageHeader
        crumbs={[{ label: 'Diffs', to: '/diffs' }]}
        title={<IdTag>Diff #{diffId}</IdTag>}
      />
      <Stack $gap={32}>
        <Async state={diff}>
          {(d) => (
            <Stack $gap={16}>
              <MetricStrip
                metrics={[
                  { key: 'Source', value: d.source ?? '—' },
                  { key: 'Captured', value: dateTime(d.createdAt) },
                  { key: 'Content hash', value: d.contentHash.slice(0, 16) },
                ]}
              />
              <Field>
                <Eyebrow>Diff</Eyebrow>
                <DiffView content={d.content} />
              </Field>
            </Stack>
          )}
        </Async>

        <Field>
          <Eyebrow>Analyses of this diff</Eyebrow>
          <Async state={analyses}>
            {(rows) =>
              rows.length === 0 ? (
                <EmptyState
                  title="No analyses"
                  hint={<>Review this diff with <Mono>dotnet run -- review {diffId}</Mono>.</>}
                />
              ) : (
                <AnalysesTable rows={rows} showDiff={false} />
              )
            }
          </Async>
        </Field>
      </Stack>
    </>
  )
}
