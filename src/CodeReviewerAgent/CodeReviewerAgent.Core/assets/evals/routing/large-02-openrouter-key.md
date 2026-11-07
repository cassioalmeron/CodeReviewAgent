# large-02 — openrouter-key

**Expectativa:** achado em `src/CodeReviewerAgent/CodeReviewerAgent.Infra/LlmClientFactory.cs`
**Classe:** segurança · **Tamanho:** 582 linhas alteradas em 17 arquivos
**Origem:** commit `682d6edb`, "Implement OpenRouter Client.", com 1 linha trocada

## O que o diff faz

Acrescenta o cliente do OpenRouter, move os clientes para o projeto de infraestrutura e remove o
`ILlmServer` antigo. Mexe em 17 arquivos, a maioria por mudança de namespace.

## O defeito plantado

Na fábrica, o endereço base do cliente passou a carregar a chave de API:

```
BaseAddress = new Uri($"https://openrouter.ai?key={apiKey}"),
```

O cabeçalho `Authorization` continua ali, duas linhas abaixo, então a chamada funciona. A chave na
URL é redundante, e é o defeito.

## Por que é problema

Toda URL é registrada em algum lugar: no log do servidor, no proxy do meio, no histórico da própria
biblioteca de HTTP e em qualquer rastro de erro. Uma chave que viaja no cabeçalho não aparece
nesses lugares. Na URL, aparece em todos.

É vazamento de credencial por um caminho que ninguém olha depois.

## O que um bom achado diz

Que a chave está na URL e por isso cai em log, e que o cabeçalho `Authorization` que já existe
basta. Achado que só diz "cuidado com a chave", sem dizer onde ela está exposta, não é acionável.

## Cuidado de medição

Nenhum teste do diff toca a fábrica, então nada falha por causa do plantio. O caso mede atenção a
credencial num diff grande, não leitura de teste.
