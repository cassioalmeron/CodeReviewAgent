# large-05 — diff-parser

**Expectativa:** achado em `src/CodeReviewerAgent/CodeReviewerAgent.Core/DiffParser.cs`
**Classe:** bug de numeração · **Tamanho:** 512 linhas alteradas em 10 arquivos
**Origem:** commit `0b8df610`, "Expland diff source to staged and specific files", com 1 linha trocada

## O que o diff faz

Acrescenta o leitor de diff que resolve o número absoluto de cada linha, mais três novas origens de
diff: arquivos específicos, área de staging e o diff local.

## O defeito plantado

A guarda que ignora o marcador de "sem nova linha no fim do arquivo" passou a olhar a posição
errada do texto:

```
if (line.Length > 2 && line[1] == '\\') // not a real line.
```

O marcador tem a barra invertida na primeira posição, não na segunda. A guarda nunca dispara.

## Por que é problema

Sem a guarda, o marcador cai no caso padrão e é contado como linha de contexto, somando 1 nos dois
contadores. Toda linha depois dele, no mesmo bloco, recebe número errado.

O número da linha é o que ancora o achado no código. Quando ele está errado, a conferência descarta
um achado correto por não encontrar a linha citada, e o modelo é penalizado por acerto.

## O que um bom achado diz

Que a condição olha a segunda posição em vez da primeira, e que por isso o marcador é tratado como
linha real e desloca a numeração. O comentário ao lado, que diz o que a linha deveria fazer, está
no mesmo diff.

## Cuidado de medição

Os 8 testes do leitor que estão no diff continuam passando: nenhum deles usa um diff com esse
marcador.
