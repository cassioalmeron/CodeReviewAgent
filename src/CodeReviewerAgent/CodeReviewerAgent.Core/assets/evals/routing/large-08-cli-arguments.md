# large-08 — cli-arguments

**Expectativa:** achado em `src/CodeReviewerAgent/CodeReviewerAgent.Core/ProcessRunner.cs`
**Classe:** segurança · **Tamanho:** 388 linhas alteradas em 7 arquivos
**Origem:** commit `313de28b`, "Claude Code Signature Implementation.", com 2 linhas trocadas

## O que o diff faz

Acrescenta as duas engines de assinatura, que falam com o Claude pela linha de comando em vez de
pela API, e o jeito de rodar processo passando o diff pela entrada padrão.

## O defeito plantado

Os argumentos pararam de ser passados um a um e passaram a ser juntados num texto só:

```
// one string instead of the list: the CLI parses it the same way
startInfo.Arguments = string.Join(" ", arguments);
```

## Por que é problema

Juntar com espaço e sem citação quebra todo argumento que tenha espaço dentro, e o `--system-prompt`
deste código é um texto de várias linhas. Ele chega partido em dezenas de argumentos.

O lado de segurança é o mesmo mecanismo: qualquer valor que venha de fora e contenha espaço, aspas
ou um caractere que o shell entenda deixa de ser um valor e passa a ser estrutura do comando. É o
caminho clássico de injeção por linha de comando.

O comentário do próprio método, no mesmo diff, promete o contrário: "with each argument passed
separately".

## O que um bom achado diz

Que juntar os argumentos num texto perde a citação, e que a lista de argumentos existe para isso.
Citar o prompt de várias linhas como o caso que quebra primeiro é bom sinal de leitura.

## Cuidado de medição

Este diff não tem arquivo de teste nenhum, então não há o que falhar.
