import { useEffect, useState } from 'react'

interface AsyncState<T> {
  data: T | null
  error: string | null
  loading: boolean
}

/** Runs an async loader on mount (and when `deps` change), tracking loading/error. */
export function useAsync<T>(loader: () => Promise<T>, deps: unknown[]): AsyncState<T> {
  const [state, setState] = useState<AsyncState<T>>({
    data: null,
    error: null,
    loading: true,
  })

  useEffect(() => {
    let active = true
    setState({ data: null, error: null, loading: true })
    loader()
      .then((data) => active && setState({ data, error: null, loading: false }))
      .catch((e: unknown) =>
        active &&
        setState({
          data: null,
          error: e instanceof Error ? e.message : String(e),
          loading: false,
        }),
      )
    return () => {
      active = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)

  return state
}
