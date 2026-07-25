import { useParams } from 'react-router-dom'
import { api } from '@/services/api'
import { AssessmentsTable } from '@/components/features/AssessmentsTable'
import { Async } from '@/components/ui/Async'
import { DiffView } from '@/components/features/DiffView'
import { EmptyState } from '@/components/ui/States'
import { MetricStrip } from '@/components/ui/MetricStrip'
import { PageHeader } from '@/components/ui/PageHeader'
import { Eyebrow, Field, IdTag, Mono, Stack } from '@/components/ui/primitives'
import { useAsync } from '@/hooks/useAsync'
import { dateTime } from '@/utils/format'

export function ReviewDetailPage() {
  const { id } = useParams()
  const reviewId = Number(id)
  const review = useAsync(() => api.review(reviewId), [reviewId])
  const assessments = useAsync(() => api.reviewAssessments(reviewId), [reviewId])

  return (
    <>
      <PageHeader
        crumbs={[{ label: 'Reviews', to: '/reviews' }]}
        title={<IdTag>Review #{reviewId}</IdTag>}
      />
      <Stack $gap={32}>
        <Async state={review}>
          {(r) => (
            <Stack $gap={16}>
              <MetricStrip
                metrics={[
                  { key: 'Source', value: r.source ?? '—' },
                  { key: 'Captured', value: dateTime(r.createdAt) },
                  { key: 'Content hash', value: r.contentHash.slice(0, 16) },
                ]}
              />
              <Field>
                <Eyebrow>Diff</Eyebrow>
                <DiffView content={r.content} />
              </Field>
            </Stack>
          )}
        </Async>

        <Field>
          <Eyebrow>Assessments of this review</Eyebrow>
          <Async state={assessments}>
            {(rows) =>
              rows.length === 0 ? (
                <EmptyState
                  title="No assessments"
                  hint={<>Analyze this review with <Mono>dotnet run -- assess {reviewId}</Mono>.</>}
                />
              ) : (
                <AssessmentsTable rows={rows} showReview={false} />
              )
            }
          </Async>
        </Field>
      </Stack>
    </>
  )
}
