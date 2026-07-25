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

export function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<HomePage />} />
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
