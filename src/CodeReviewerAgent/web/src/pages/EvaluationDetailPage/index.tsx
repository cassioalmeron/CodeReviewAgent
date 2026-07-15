import { useParams } from 'react-router-dom'
import styled from 'styled-components'
import { api } from '@/services/api'
import { Async } from '@/components/ui/Async'
import { DiffView } from '@/components/features/DiffView'
import { MetricStrip } from '@/components/ui/MetricStrip'
import { PageHeader } from '@/components/ui/PageHeader'
import { ScoreMeter } from '@/components/ui/ScoreMeter'
import { Eyebrow, Field, IdTag, Panel, Prose, Stack } from '@/components/ui/primitives'
import { useAsync } from '@/hooks/useAsync'
import { cost, dateTime, latency, tokens } from '@/utils/format'

const Scores = styled(Panel)`
  padding: 8px 20px;
`

export function EvaluationDetailPage() {
  const { id } = useParams()
  const evaluationId = Number(id)
  // The evaluation links to its analysis, which links to the reviewed diff — chain to show it.
  const state = useAsync(async () => {
    const evaluation = await api.evaluation(evaluationId)
    const analysis = await api.analysis(evaluation.analysisId)
    const diff = await api.diff(analysis.diffId)
    return { evaluation, diff }
  }, [evaluationId])

  return (
    <Async state={state}>
      {({ evaluation: e, diff }) => (
        <>
          <PageHeader
            crumbs={[
              { label: 'Evaluations', to: '/evaluations' },
              { label: `Analysis #${e.analysisId}`, to: `/analyses/${e.analysisId}` },
            ]}
            title={<IdTag>Evaluation #{e.id}</IdTag>}
          />
          <Stack $gap={32}>
            <MetricStrip
              metrics={[
                { key: 'Judge', value: e.judgeModel ?? '—' },
                { key: 'Rubric', value: e.rubricVersion ?? '—' },
                { key: 'Cost', value: cost(e.cost) },
                { key: 'Latency', value: latency(e.latencyMs) },
                { key: 'Tokens', value: `${tokens(e.inputTokens)} / ${tokens(e.outputTokens)}` },
                { key: 'Run', value: dateTime(e.createdAt) },
              ]}
            />

            <Field>
              <Eyebrow>Rubric scores</Eyebrow>
              <Scores>
                <ScoreMeter label="Correctness" value={e.correctness} />
                <ScoreMeter label="Actionability" value={e.actionability} />
                <ScoreMeter label="Calibration" value={e.calibration} />
                <ScoreMeter label="Signal-to-noise" value={e.signalToNoise} />
                <ScoreMeter label="Overall" value={e.overall} emphasis />
              </Scores>
            </Field>

            {e.rationale && (
              <Field>
                <Eyebrow>Rationale</Eyebrow>
                <Prose>{e.rationale}</Prose>
              </Field>
            )}

            <Field>
              <Eyebrow>Reviewed diff · #{diff.id}</Eyebrow>
              <DiffView content={diff.content} />
            </Field>
          </Stack>
        </>
      )}
    </Async>
  )
}
