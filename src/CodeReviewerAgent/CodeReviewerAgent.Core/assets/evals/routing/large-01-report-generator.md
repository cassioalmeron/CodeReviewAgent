# large-01 — report-generator

**Expectativa:** achado em `src/CodeReviewerAgent/CodeReviewerAgent.Core/ReportGenerator.cs`
**Classe:** performance · **Tamanho:** 684 linhas alteradas em 8 arquivos
**Origem:** commit `1b4ebc85`, "New prompt version and Findings Report", com 4 linhas trocadas

## O que o diff faz

Acrescenta o gerador de relatório em Markdown, o validador de achados e os testes dos dois. É um
diff de feature inteira: 229 linhas novas só no gerador.

## O defeito plantado

Em `SplitDiffByFile`, que parte o diff recebido em blocos por arquivo, o acumulador deixou de ser
`StringBuilder` e passou a ser `string`:

```
string? current = null;
...
current += line + Environment.NewLine;
```

Quatro linhas trocadas, nenhuma mudança de comportamento: o relatório sai idêntico.

## Por que é problema

A concatenação acontece uma vez por linha do diff, e cada uma copia tudo que já foi acumulado. O
custo é quadrático no tamanho do diff. Num diff de 5 mil linhas isso é da ordem de milhões de
cópias de string, para um resultado que o `StringBuilder` daria em uma passada.

O agravante está no contexto: a entrada desse método é o diff inteiro da revisão, que é exatamente
o que pode ser grande.

## O que um bom achado diz

Nomeia a concatenação em laço, diz que o custo cresce com o quadrado do tamanho do diff, e propõe o
`StringBuilder` de volta. Achado que só diz "poderia ser mais eficiente" não é acionável.

## Cuidado de medição

Os testes do diff continuam passando, porque a saída não mudou. Isso é de propósito: o caso mede se
o modelo enxerga custo, não se ele lê o resultado de um teste.
