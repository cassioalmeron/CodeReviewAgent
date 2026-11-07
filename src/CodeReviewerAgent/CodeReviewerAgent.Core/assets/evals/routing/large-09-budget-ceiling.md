# large-09 — budget-ceiling

**Expectativa:** achado em `src/CodeReviewerAgent/CodeReviewerAgent.Core/Llm/BudgetedLlmClient.cs`
**Classe:** bug de concorrência · **Tamanho:** 357 linhas alteradas em 3 arquivos
**Origem:** commit `b3ccf888`, "PR review CI", com 2 linhas trocadas

## O que o diff faz

Acrescenta o teto de gasto por execução: um decorador que confere o acumulado antes de cada chamada
e soma o custo dela depois que ela volta. É o diff mais estreito do conjunto, 3 arquivos, e mais da
metade dele é teste.

## O defeito plantado

A soma do custo perdeu o `lock`:

```
// a single decimal increment needs no lock
_spend += cost;
```

As outras duas operações da classe, a leitura e a conferência, continuam protegidas.

## Por que é problema

`decimal` tem 128 bits, e somar não é operação atômica: ler, somar e gravar são três passos. Duas
chamadas voltando juntas podem ler o mesmo valor e uma sobrescrever a outra, perdendo um custo.

Isso não é hipótese neste código. O comentário do próprio arquivo diz que o golden set dispara até
`GOLDEN_PARALLELISM` chamadas ao mesmo tempo sobre o mesmo teto. O efeito é um teto de gasto que
deixa passar mais do que devia, em dinheiro.

## O que um bom achado diz

Que a soma não está protegida, que `decimal` não é atômico e que o resto da classe protege as outras
operações. Propor o `lock` de volta, ou um `Interlocked` sobre outro tipo, serve.

## Cuidado de medição

Os testes deste diff rodam numa linha de execução só, então nenhum deles falha por causa do
plantio. É um defeito que só a leitura pega, que é o ponto do caso.
