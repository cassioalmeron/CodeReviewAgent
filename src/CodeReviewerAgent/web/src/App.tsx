import { Route, Routes } from 'react-router-dom'
import { Layout } from '@/components/layout/Layout'
import { RequireProject } from '@/components/RequireProject'
import { HomePage } from '@/pages/HomePage'
import { ReviewsPage } from '@/pages/ReviewsPage'
import { ReviewDetailPage } from '@/pages/ReviewDetailPage'
import { AssessmentsPage } from '@/pages/AssessmentsPage'
import { AssessmentDetailPage } from '@/pages/AssessmentDetailPage'
import { EvaluationsPage } from '@/pages/EvaluationsPage'
import { EvaluationDetailPage } from '@/pages/EvaluationDetailPage'
import { RunsPage } from '@/pages/RunsPage'
import { RunDetailPage } from '@/pages/RunDetailPage'

export function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<HomePage />} />
        {/* A golden run measures a model against the set, not work on a repository: no project needed. */}
        <Route path="/runs" element={<RunsPage />} />
        <Route path="/runs/:id" element={<RunDetailPage />} />
        <Route element={<RequireProject />}>
          <Route path="/reviews" element={<ReviewsPage />} />
          <Route path="/reviews/:id" element={<ReviewDetailPage />} />
          <Route path="/assessments" element={<AssessmentsPage />} />
          <Route path="/assessments/:id" element={<AssessmentDetailPage />} />
          <Route path="/evaluations" element={<EvaluationsPage />} />
          <Route path="/evaluations/:id" element={<EvaluationDetailPage />} />
        </Route>
        <Route path="*" element={<HomePage />} />
      </Route>
    </Routes>
  )
}
