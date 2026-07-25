import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api } from '@/services/api'
import type { ProjectListItem } from '@/types'

const STORAGE_KEY = 'crv.project-id'

interface ProjectContextValue {
  /** Every project (for the picker); null while loading. */
  projects: ProjectListItem[] | null
  /** The selected project, or null when none is chosen. */
  project: ProjectListItem | null
  projectId: number | null
  select: (id: number) => void
  clear: () => void
  /** Still resolving the persisted selection against the API. */
  loading: boolean
}

const ProjectContext = createContext<ProjectContextValue | undefined>(undefined)

function readStoredId(): number | null {
  const raw = localStorage.getItem(STORAGE_KEY)
  if (raw == null) return null
  const id = Number(raw)
  return Number.isInteger(id) ? id : null
}

export function ProjectProvider({ children }: { children: ReactNode }) {
  const [projects, setProjects] = useState<ProjectListItem[] | null>(null)
  const [projectId, setProjectId] = useState<number | null>(null)
  const [loading, setLoading] = useState(true)

  // On boot, load the projects and validate the persisted selection against them —
  // a stale id (store recreated with different ids) is dropped instead of showing an empty view.
  useEffect(() => {
    let active = true
    api
      .projects()
      .then((list) => {
        if (!active) return
        setProjects(list)
        const stored = readStoredId()
        setProjectId(stored != null && list.some((p) => p.id === stored) ? stored : null)
      })
      .catch(() => active && setProjects([]))
      .finally(() => active && setLoading(false))
    return () => {
      active = false
    }
  }, [])

  const value = useMemo<ProjectContextValue>(() => {
    const select = (id: number) => {
      localStorage.setItem(STORAGE_KEY, String(id))
      setProjectId(id)
    }
    const clear = () => {
      localStorage.removeItem(STORAGE_KEY)
      setProjectId(null)
    }
    return {
      projects,
      project: projects?.find((p) => p.id === projectId) ?? null,
      projectId,
      select,
      clear,
      loading,
    }
  }, [projects, projectId, loading])

  return <ProjectContext.Provider value={value}>{children}</ProjectContext.Provider>
}

export function useProject(): ProjectContextValue {
  const context = useContext(ProjectContext)
  if (!context) throw new Error('useProject must be used within a ProjectProvider')
  return context
}
