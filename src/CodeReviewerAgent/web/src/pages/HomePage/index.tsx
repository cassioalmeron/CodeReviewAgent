import { lazy, Suspense } from 'react'
import styled from 'styled-components'
import { useProject } from '@/contexts/ProjectContext'
import { ProjectCard } from '@/components/features/ProjectCard'
import { PageHeader } from '@/components/ui/PageHeader'
import { EmptyState, Loading } from '@/components/ui/States'

// The dashboard pulls in recharts — load it only when a project is opened, so the
// picker and the list pages don't pay for the chart library up front.
const ProjectDashboard = lazy(() =>
  import('@/components/features/ProjectDashboard').then((m) => ({ default: m.ProjectDashboard })),
)

const Grid = styled.div`
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 16px;
`

export function HomePage() {
  const { projects, project, loading, select } = useProject()

  if (loading) return <Loading />

  // A project is selected → its dashboard (lazy: recharts loads on demand).
  if (project)
    return (
      <Suspense fallback={<Loading />}>
        <ProjectDashboard project={project} />
      </Suspense>
    )

  // Otherwise the picker.
  return (
    <>
      <PageHeader
        title="Projects"
        sub="Choose a repository to see its reviews, assessments, and an overview of what was found."
      />
      {projects && projects.length > 0 ? (
        <Grid>
          {projects.map((p) => (
            <ProjectCard key={p.id} project={p} onSelect={select} />
          ))}
        </Grid>
      ) : (
        <EmptyState
          title="No projects stored yet"
          hint="Run the CLI in a repository (e.g. dotnet run -- review) to capture its first review."
        />
      )}
    </>
  )
}
