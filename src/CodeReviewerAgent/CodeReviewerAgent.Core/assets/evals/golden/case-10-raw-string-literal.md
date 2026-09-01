# case-10 — raw-string-literal

**Expectativa:** `ExpectNoFinding` — isca em `public const string PendingInvoices = """`
**Classe:** armadilha de falso positivo · **Exige:** C# 11 (2022)

## Por que este código está correto

`"""` abre um **raw string literal**. O conteúdo vai até a sequência de fechamento de mesmo tamanho;
a indentação da linha de fechamento define quanto recuo é removido de cada linha. Aspas simples
dentro do texto — `'Pending'` — não precisam de escape, que é justamente o motivo de existir da
sintaxe para SQL embutido.

O `@tenantId` é um **parâmetro nomeado**, não interpolação: não há `$` antes das aspas. Nada é
concatenado, nada vem de fora.

## Como um modelo antigo erra

Duas formas. Marca as três aspas como string malformada, ou — pior e mais interessante — confunde
`@tenantId` com concatenação e aponta **injeção de SQL** num literal constante. O segundo é falso
positivo de segurança, o tipo mais caro para quem revisa.

## Verificação de honestidade

Compilado e executado em .NET 10, **sem aviso**. O literal tem 82 caracteres após remoção do recuo.

## O que a skill diz hoje sobre isto

Nada sobre raw strings. A skill exige interpolação em vez de concatenação (`$"..."`), o que é
vocabulário vizinho e pode empurrar o modelo a comentar sobre strings neste arquivo. Vale acompanhar:
se este caso for o único das três armadilhas a piorar na condição harness, a regra de interpolação da
skill é a suspeita.

## O que conta como queda

Só achado na linha da isca — a linha que abre o literal. Um achado que cite a linha do `SELECT` ou a
do `WHERE` não conta, mesmo dizendo bobagem sobre injeção. Isso é limitação conhecida do desenho e
está registrada: a decisão 2 troca sensibilidade por honestidade de propósito.
