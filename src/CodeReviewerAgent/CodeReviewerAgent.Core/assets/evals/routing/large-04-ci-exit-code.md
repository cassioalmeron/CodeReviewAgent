# large-04 — ci-exit-code

**Expectativa:** achado em `src/CodeReviewerAgent/CodeReviewerAgent.Console/Program.cs`
**Classe:** bug de fluxo · **Tamanho:** 524 linhas alteradas em 12 arquivos
**Origem:** commit `04a11e0d`, "Golden set CI", com 2 linhas trocadas

## O que o diff faz

Liga o golden set ao CI: guarda uma referência de como cada caso foi, compara a rodada nova com ela
e reprova a execução quando um caso piora além da tolerância. Acrescenta a temperatura à chave de
retomada das rodadas.

## O defeito plantado

Quando a comparação acusa piora, o processo passou a apenas imprimir, em vez de terminar com código
de erro:

```
if (report.Failed)
    Console.WriteLine("Regression detected: see the table above.");
```

## Por que é problema

O CI decide se o passo falhou pelo código de saída do processo. Sem ele, a tabela de piora é
impressa, o passo fica verde e a piora entra no repositório.

O comentário quatro linhas acima, no mesmo diff, promete o contrário: "fails the process when a
case regressed: this is the check CI runs".

É o pior tipo de defeito numa régua. Ela continua medindo e para de reprovar, sem fazer barulho.

## O que um bom achado diz

Que o código de saída não é mais alterado, e que por isso o CI não reprova mais nada. Citar o
comentário que promete o contrário é bom sinal de leitura, e não é obrigatório.

## Cuidado de medição

O `Program.cs` não tem teste neste repositório, então nada falha por causa do plantio.
