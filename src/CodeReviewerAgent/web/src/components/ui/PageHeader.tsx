import { Fragment, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import styled from 'styled-components'

const Head = styled.header`
  margin-bottom: 28px;
`

const Crumbs = styled.div`
  display: flex;
  align-items: center;
  gap: 8px;
  font-family: var(--mono);
  font-size: 12px;
  color: var(--faint);
  margin-bottom: 12px;
`

const CrumbLink = styled(Link)`
  color: var(--muted);
  &:hover {
    color: var(--gold);
  }
`

const Sep = styled.span`
  color: var(--border);
`

const TitleRow = styled.div`
  display: flex;
  align-items: baseline;
  gap: 14px;
  flex-wrap: wrap;
`

const Title = styled.h1`
  margin: 0;
  font-size: 26px;
  font-weight: 600;
  letter-spacing: -0.01em;
`

const Sub = styled.p`
  margin: 8px 0 0;
  color: var(--muted);
  font-size: 14px;
`

export interface Crumb {
  label: string
  to: string
}

export function PageHeader({
  crumbs,
  title,
  aside,
  sub,
}: {
  crumbs?: Crumb[]
  title: ReactNode
  aside?: ReactNode
  sub?: ReactNode
}) {
  return (
    <Head>
      {crumbs && crumbs.length > 0 && (
        <Crumbs>
          {crumbs.map((c, i) => (
            <Fragment key={i}>
              {i > 0 && <Sep>/</Sep>}
              <CrumbLink to={c.to}>{c.label}</CrumbLink>
            </Fragment>
          ))}
        </Crumbs>
      )}
      <TitleRow>
        <Title>{title}</Title>
        {aside}
      </TitleRow>
      {sub && <Sub>{sub}</Sub>}
    </Head>
  )
}
