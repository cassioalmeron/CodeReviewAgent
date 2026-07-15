import type { ReactNode } from 'react'
import { ErrorState, Loading } from './States'

interface State<T> {
  data: T | null
  error: string | null
  loading: boolean
}

/** Renders children with loaded data, or the matching loading/error state. */
export function Async<T>({
  state,
  children,
}: {
  state: State<T>
  children: (data: T) => ReactNode
}) {
  if (state.loading) return <Loading />
  if (state.error) return <ErrorState message={state.error} />
  if (state.data === null) return <ErrorState message="No data" />
  return <>{children(state.data)}</>
}
