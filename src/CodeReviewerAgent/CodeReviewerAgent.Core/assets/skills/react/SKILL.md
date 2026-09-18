---
name: react
description: React + TypeScript conventions for the web viewer — functional components with hooks only, named exports, the @ alias for src/, styled-components, strict typing, stable keys and complete useEffect dependencies. Use when the diff changes React or TypeScript code (.tsx / .ts files), to report convention violations.
metadata:
  applies-to: "*.tsx,*.ts"
---
# React + TypeScript conventions

- Functional components with hooks only; no class components.
- Prefer named exports over default exports.
- Use the `@` alias for `src/` imports.
- Style with styled-components; avoid inline styles and CSS modules.
- Strict TypeScript: no `any` without justification; type component props with an interface.
- Give every mapped element a stable `key`.
- List all dependencies in `useEffect`; return a cleanup function when needed.
- Call hooks only at the top level of a component or another hook.
