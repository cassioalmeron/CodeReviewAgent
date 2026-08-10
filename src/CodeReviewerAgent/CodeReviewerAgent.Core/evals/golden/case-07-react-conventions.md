# case-07 — react-conventions

**Expectativa:** achado em `web/src/components/AlertList.tsx`
**Classe:** convenção de projeto — mede o harness de skills, não capacidade do modelo

## O que o diff faz

Acrescenta um componente com três problemas:

```tsx
export default function AlertList({ alerts }: { alerts: any[] }) {
  ...
      {alerts.map((alert) => (
        <li onClick={() => setOpen(!open)}>{alert.title}</li>
      ))}
```

1. **Export default** — o projeto exige exports nomeados.
2. **`key` ausente** na lista renderizada por `map`.
3. **`any[]`** na tipagem das props, sob TypeScript estrito.

## Por que conta como achado

O `key` faltando é o único dos três que o React aponta em runtime; os outros dois são convenção
deste projeto, que chega ao agente pela skill `react`. Mistura de regra universal com regra local, o
que torna o caso menos limpo que o 06 para comparar condições — a parte do `key` pode ser detectada
sem skill nenhuma.

## O que um bom achado diz

Nomeia a convenção e propõe a correção concreta: export nomeado, `key={alert.id}`, tipo real no
lugar de `any`.

## Ressalva conhecida

Como o `key` é detectável sem skill, este caso **não serve** para isolar o valor do harness. Para
essa leitura, o caso 06 é o limpo. Registrado para que o delta baseline↔harness não seja lido como
se os dois casos medissem a mesma coisa.
