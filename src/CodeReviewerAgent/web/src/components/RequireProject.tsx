import { Navigate, Outlet } from 'react-router-dom'
import { useProject } from '@/contexts/ProjectContext'
import { Loading } from '@/components/ui/States'

/** Gate for the list/detail routes: with no project selected, send the user back to the Home picker. */
export function RequireProject() {
  const { projectId, loading } = useProject()
  if (loading) return <Loading />
  if (projectId == null) return <Navigate to="/" replace />
  return <Outlet />
}
