import { NavLink, Outlet } from 'react-router-dom'
import styled from 'styled-components'
import { DiamondIcon } from '@/components/icons'

const Shell = styled.div`
  display: grid;
  grid-template-columns: 232px 1fr;
  min-height: 100%;

  @media (max-width: 820px) {
    grid-template-columns: 1fr;
  }
`

const Sidebar = styled.aside`
  border-right: 1px solid var(--border);
  padding: 22px 16px;
  display: flex;
  flex-direction: column;
  gap: 28px;
  position: sticky;
  top: 0;
  align-self: start;
  height: 100vh;

  @media (max-width: 820px) {
    position: static;
    height: auto;
    border-right: none;
    border-bottom: 1px solid var(--border);
  }
`

const Brand = styled.div`
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 0 8px;
`

const Mark = styled.div`
  width: 30px;
  height: 30px;
  border-radius: 7px;
  display: grid;
  place-items: center;
  background: var(--gold-soft);
  border: 1px solid color-mix(in srgb, var(--gold) 40%, transparent);
  color: var(--gold);
  font-family: var(--mono);
  font-weight: 700;
  font-size: 15px;
`

const BrandText = styled.div`
  display: flex;
  flex-direction: column;
  line-height: 1.2;
`

const BrandName = styled.span`
  font-weight: 600;
  font-size: 14px;
`

const BrandSub = styled.span`
  font-family: var(--mono);
  font-size: 10px;
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--faint);
`

const Nav = styled.nav`
  display: flex;
  flex-direction: column;
  gap: 2px;
`

const NavLabel = styled.span`
  font-family: var(--mono);
  font-size: 10px;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: var(--faint);
  padding: 0 10px 8px;
`

const Item = styled(NavLink)`
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 9px 10px;
  border-radius: var(--radius-sm);
  color: var(--muted);
  font-size: 14px;
  transition: background 0.12s ease, color 0.12s ease;

  &:hover {
    background: var(--panel);
    color: var(--text);
  }

  &.active {
    background: var(--panel);
    color: var(--text);
  }

  &.active::before {
    content: '';
    width: 3px;
    height: 15px;
    border-radius: 2px;
    background: var(--gold);
    margin-left: -10px;
  }
`

const Main = styled.main`
  padding: 40px clamp(20px, 5vw, 56px) 80px;
  max-width: 1100px;
  width: 100%;
`

export function Layout() {
  return (
    <Shell>
      <Sidebar>
        <Brand>
          <Mark>
            <DiamondIcon size={16} />
          </Mark>
          <BrandText>
            <BrandName>Review Console</BrandName>
            <BrandSub>read-only</BrandSub>
          </BrandText>
        </Brand>
        <Nav>
          <NavLabel>Browse</NavLabel>
          <Item to="/diffs">Diffs</Item>
          <Item to="/analyses">Analyses</Item>
          <Item to="/evaluations">Evaluations</Item>
        </Nav>
      </Sidebar>
      <Main>
        <Outlet />
      </Main>
    </Shell>
  )
}
