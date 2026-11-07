# large-10 — round-store

**Expectativa:** `ExpectNoFinding` — isca em `private readonly Lock _writeLock = new();`
**Classe:** armadilha de falso positivo · **Exige:** C# 13 (2024)
**Tamanho:** 596 linhas alteradas em 15 arquivos
**Origem:** commit `ad6bd347`, "Include EVAL_OUTPUT_DIR environment variable.", sem nenhuma alteração

## Por que este diff está aqui

É o único caso do conjunto sem defeito plantado. O código é o commit real, letra por letra, e está
correto. Ele existe para a precisão ter onde se apoiar na rota forte: nos 9 casos de detecção,
achado não previsto não conta nem a favor nem contra, então sem uma armadilha a precisão de um diff
grande não é medida em lugar nenhum.

## O que o diff faz

Acrescenta a variável de ambiente que escolhe onde a medição escreve, o armazenamento durável das
rodadas pagas em JSON Lines, e a retomada de uma rodada que morreu no meio.

## A isca

```
private readonly Lock _writeLock = new();
```

`System.Threading.Lock` é um tipo que chegou no .NET 9, com o C# 13. Ele substitui o `object` que se
usava como objeto de trava desde sempre, e o compilador reconhece `lock (x)` sobre ele.

Um modelo que não conhece o tipo chama isso de erro: diz que `Lock` não existe, que falta um `using`,
ou que o certo seria `private readonly object _writeLock = new();`.

## Como um modelo erra aqui

O erro que conta é só o que cita a linha da isca. Observação legítima sobre outra coisa do diff é
ignorada por desenho, senão a armadilha viraria teste de silêncio num diff de 596 linhas, o que
nenhum modelo passa.

## Cuidado de medição

Esta é a mesma família de armadilha que o golden set já usa com o bloco `extension` do C# 14, e vale
a mesma ressalva: se uma skill passar a mostrar o tipo `Lock`, o caso para de medir o corte de
conhecimento do modelo e passa a medir se ele aplica o que a skill mostrou. Hoje nenhuma skill cita
esse tipo.
