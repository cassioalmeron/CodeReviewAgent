import styled from 'styled-components'
import { api } from '@/services/api'
import type { ProjectListItem } from '@/types'
import { Async } from '@/components/ui/Async'
import { MetricStrip } from '@/components/ui/MetricStrip'
import { PageHeader } from '@/components/ui/PageHeader'
import { Stack } from '@/components/ui/primitives'
import { ChartCard } from '@/components/charts/ChartCard'
import { SeverityPie } from '@/components/charts/SeverityPie'
import { CategoryPie } from '@/components/charts/CategoryPie'
import { RunCostChart } from '@/components/charts/RunCostChart'
import { JudgeRadar } from '@/components/charts/JudgeRadar'
import { TopFilesBars } from '@/components/charts/TopFilesBars'
import { useAsync } from '@/hooks/useAsync'
import { cost, latency } from '@/utils/format'

const Grid = styled.div`
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 16px;

  @media (max-width: 780px) {
    grid-template-columns: 1fr;
  }
`

const Overall = styled.div`
  display: flex;
  align-items: center;
  gap: 20px;
`

const RadarWrap = styled.div`
  flex: 1;
  min-width: 0;
`

const Big = styled.div`
  font-family: var(--mono);
  font-size: 30px;
  color: var(--gold);
`

const OverallLabel = styled.div`
  font-family: var(--mono);
  font-size: 10px;
  letter-spacing: 0.12em;
  text-transform: uppercase;
  color: var(--faint);
`

export function ProjectDashboard({ project }: { project: ProjectListItem }) {
  const state = useAsync(() => api.projectStats(project.id), [project.id])

  return (
    <>
      <PageHeader title={project.name} sub={project.folder} />
      <Async state={state}>
        {(stats) => (
          <Stack $gap={28}>
            <MetricStrip
              metrics={[
                { key: 'Reviews', value: String(stats.reviewCount) },
                { key: 'Assessments', value: String(stats.assessmentCount) },
                { key: 'Findings', value: String(stats.findingCount) },
                { key: 'Total cost', value: cost(stats.totalCost) },
                { key: 'Avg latency', value: latency(Math.round(stats.avgLatencyMs)) },
              ]}
            />
            <Grid>
              <ChartCard title="Findings by severity" hint={`${stats.findingCount} total`} empty={stats.bySeverity.length === 0}>
                <SeverityPie data={stats.bySeverity} />
              </ChartCard>

              <ChartCard title="Findings by category" hint={`${stats.findingCount} total`} empty={stats.byCategory.length === 0}>
                <CategoryPie data={stats.byCategory} />
              </ChartCard>

              <ChartCard title="Cost, tokens & latency per assessment" wide empty={stats.runs.length === 0}>
                <RunCostChart data={stats.runs} />
              </ChartCard>

              <ChartCard
                title="Judge scores"
                hint={stats.judge ? `${stats.judge.evaluationCount} evaluations` : undefined}
                empty={!stats.judge}
              >
                {stats.judge && (
                  <Overall>
                    <RadarWrap>
                      <JudgeRadar judge={stats.judge} />
                    </RadarWrap>
                    <div>
                      <Big>{stats.judge.overall.toFixed(1)}</Big>
                      <OverallLabel>avg overall</OverallLabel>
                    </div>
                  </Overall>
                )}
              </ChartCard>

              <ChartCard title="Files with most findings" hint={`top ${stats.topFiles.length}`} empty={stats.topFiles.length === 0}>
                <TopFilesBars data={stats.topFiles} />
              </ChartCard>
            </Grid>
          </Stack>
        )}
      </Async>
    </>
  )
}
