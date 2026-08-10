# case-05 — quadratic-loop

**Expectativa:** achado em `src/Services/DuplicateFinder.cs`
**Classe:** bug de manual — piso de comparação, não discrimina modelo

## O que o diff faz

Troca `items.Distinct().Count() != items.Count` por dois laços aninhados sobre a mesma lista.

## Por que é problema

O código **é correto** — devolve a resposta certa. O defeito é de custo: O(n²) comparações onde havia
O(n). Com 10 mil itens são até 100 milhões de comparações, contra 10 mil.

Isso torna o caso ligeiramente diferente dos outros quatro do piso: exige notar que correto e
adequado não são a mesma coisa. Ainda assim não discrimina, porque laço aninhado sobre a mesma
coleção é padrão visualmente óbvio.

## O que um bom achado diz

Nomeia a complexidade quadrática e propõe estrutura de hash (`HashSet<string>`, ou o `Distinct()`
que estava ali antes). Achado que diz apenas "poderia ser mais eficiente", sem dizer por quê nem o
quê, não é acionável.

## Cuidado de medição

As keywords incluem `o(n`, que casa com `O(n²)` e `O(n)`. É régua grossa de propósito: aqui o
vocabulário é previsível. Não copiar esse critério para os casos sutis da fase 2.
