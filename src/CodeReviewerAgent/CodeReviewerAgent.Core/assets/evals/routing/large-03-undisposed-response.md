# large-03 — undisposed-response

**Expectativa:** achado em `src/CodeReviewerAgent/CodeReviewerAgent.Core/HttpResilience.cs`
**Classe:** bug de recurso · **Tamanho:** 578 linhas alteradas em 14 arquivos
**Origem:** commit `40554021`, "Publish Findins to PR and HttpResilience.", com 1 linha trocada

## O que o diff faz

Acrescenta a publicação dos achados no pull request e a camada de resiliência de HTTP, com retry e
espera exponencial. São 14 arquivos, incluindo o divisor de diff e o formatador de comentário.

## O defeito plantado

No laço de retry, a resposta descartada deixou de ser liberada. A linha que fazia isso virou
comentário:

```
// a transient status: the loop below sends a fresh request
```

## Por que é problema

Cada tentativa que falha com status temporário deixa uma `HttpResponseMessage` viva, com o fluxo de
conteúdo dela aberto. Numa rajada de 429, que é justamente quando o retry entra em ação, isso
acumula conexão presa até o coletor de lixo passar. O laço roda até 3 vezes por chamada, e a rodada
do golden set faz chamadas em paralelo.

## O que um bom achado diz

Que a resposta temporária não é liberada antes da próxima tentativa, e que isso prende conexão. O
conserto é uma linha: o `Dispose` de volta, ou um `using` no escopo da tentativa.

## Cuidado de medição

Nenhum teste do diff verifica liberação de recurso, então o plantio não quebra nada. É um defeito
que só se enxerga lendo o laço, que é o ponto do caso.
