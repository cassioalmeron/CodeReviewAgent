# case-08 — extension-block

**Expectativa:** `ExpectNoFinding` — isca em `extension(decimal value)`
**Classe:** armadilha de falso positivo · **Exige:** C# 14 (2025)

## Por que este código está correto

`extension(decimal value) { ... }` é a sintaxe de **extension members**, introduzida no C# 14. Um
bloco `extension` declara membros de extensão — aqui duas propriedades e um método — sobre o tipo
receptor declarado no cabeçalho do bloco. Substitui a forma antiga `this decimal value` no parâmetro,
que continua válida e aparece na última linha do arquivo (`ToDisplay`), de propósito: as duas formas
convivem.

O arquivo mistura as duas sintaxes justamente para que um modelo que só conhece a antiga tenha um
contraste diante dos olhos.

## Como um modelo antigo erra

Chama de sintaxe inválida — "`extension` não é palavra-chave", "falta `static`", "isto não compila".
É o candidato mais recente da escada e portanto o mais discriminante.

## Verificação de honestidade

Compilado e executado em .NET 10 / C# 14 num projeto descartável, **sem aviso**:

```
12,34        // 12.345m.RoundedToCents
```

## O que a skill diz sobre isto

**Mudou em 08/08/2026, e isso muda o que o caso mede.**

Antes: nada. A skill `csharp` trazia só *"Prefer modern C#: records, pattern matching..."*, sem citar
bloco `extension`, e a `csharp-modern` v1/v2 pedia postura genérica diante de sintaxe desconhecida.
Nas duas versões o Haiku caiu 0/3, repetindo *"Invalid syntax: `extension(decimal value)` is not a
recognized C# construct"* mesmo com a frase explicitamente proibida na skill.

Depois: a `csharp-modern` passou a **mostrar a sintaxe** num exemplo (`extension(string text)` dentro
de uma classe estática), por decisão do Cassio — dar o conhecimento à skill em vez de pedir cautela.

**Consequência para a medição:** na condição harness este caso não mede mais o corte de conhecimento
do modelo, e sim se ele **aplica** o que a skill mostrou — o estágio "aplicação", que não tinha
medida nenhuma até aqui. A leitura de capacidade continua válida **só na condição baseline**
(`SKILLS=off`), que por isso deixa de ser opcional para este caso.

## O que conta como queda

Só achado que cite a linha da isca. Comentário legítimo sobre outra coisa do diff — nomes, o `1 + rate`
sem validação — é ignorado por desenho (decisão 2), senão a armadilha viraria teste de silêncio.
