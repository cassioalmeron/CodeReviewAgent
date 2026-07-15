import { Navigate, Route, Routes } from 'react-router-dom'
import { Layout } from '@/components/layout/Layout'
import { DiffsPage } from '@/pages/DiffsPage'
import { DiffDetailPage } from '@/pages/DiffDetailPage'
import { AnalysesPage } from '@/pages/AnalysesPage'
import { AnalysisDetailPage } from '@/pages/AnalysisDetailPage'
import { EvaluationsPage } from '@/pages/EvaluationsPage'
import { EvaluationDetailPage } from '@/pages/EvaluationDetailPage'

export function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<Navigate to="/diffs" replace />} />
        <Route path="/diffs" element={<DiffsPage />} />
        <Route path="/diffs/:id" element={<DiffDetailPage />} />
        <Route path="/analyses" element={<AnalysesPage />} />
        <Route path="/analyses/:id" element={<AnalysisDetailPage />} />
        <Route path="/evaluations" element={<EvaluationsPage />} />
        <Route path="/evaluations/:id" element={<EvaluationDetailPage />} />
        <Route path="*" element={<Navigate to="/diffs" replace />} />
      </Route>
    </Routes>
  )
}
