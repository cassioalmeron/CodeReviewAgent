# ADR-001: Decisão de usar workflow com C#/.NET + Claude para construir um agente de code review como primeiro sistema de IA aplicada em produção

**Data:** 12/06/2026

**Status:**
- [ ] Proposed (em discussão)
- [x] Accepted (decisão tomada)
- [ ] Deprecated
- [ ] Superseded by ADR-___

**Decisor:** Cassio Almeron

**Stakeholders consultados:** Rodrigo (Rambo)

---

## Bloco 1 — Contexto

### 3.1 — Qual o problema/necessidade

Este ADR faz parte de um exercício do método EPC para desenvolver habilidades de Applied AI Engineer. A necessidade é colocar em prática e quebrar as primeiras barreiras de aprendizado. O objetivo concreto é construir um agente de code review para aplicar na [REDACTED].

### 3.2 — Por que essa decisão importa agora

O foco do aprendizado deve ser Applied AI, não adaptação de linguagem. Usar Python adicionaria sobrecarga de adaptação a uma nova linguagem, desviando energia do que realmente importa aprender. Decidir a stack agora garante que todo o atrito vai para o lugar certo: o domínio de IA aplicada.

### 3.3 — Constraints/restrições

- Tempo: ~10h efetivas/semana, 4 semanas até Marco 1 (10/07/2026)
- Custo: cap mensal de [REDACTED] em API de LLM
- Performance: é um POC, nada crítico
- Outros: MVP 100% independente de aprovação da [REDACTED]

---

## Bloco 2 — Alternativas Consideradas

### Alternativa A — Python

**Descrição:** Usar Python como linguagem principal para o agente de code review.

**Prós:**
- É a linguagem na qual a IA está mais madura — ecossistema rico, mais exemplos e comunidade

**Contras:**
- Adicionaria mais um item de adaptação, desviando o foco do aprendizado de IA aplicada

**Custo estimado:** zero

---

### Alternativa B — C#/.NET

**Descrição:** Usar C#/.NET como linguagem principal para o agente de code review.

**Prós:**
- Anos de experiência e alta familiaridade com a linguagem — zero atrito

**Contras:**
- Menos maduro no ecossistema de IA em comparação ao Python

**Custo estimado:** zero

---

## Bloco 3 — Decisão

### Alternativa escolhida

**Escolhi:** Alternativa B — C#/.NET

### Justificativa

A familiaridade com C#/.NET elimina o atrito de linguagem e permite que o foco vá inteiramente para o que realmente precisa ser aprendido: IA aplicada. Usar Python adicionaria uma segunda curva de aprendizado paralela, dividindo a atenção entre linguagem e domínio.

### Trade-offs aceitos

Nenhum identificado para este contexto.

### O que NÃO consideramos (e por quê)

Todas as demais linguagens (Java, Go, Ruby, etc.) ficaram fora de cogitação — a escolha se reduziu a Python vs C#/.NET pela relevância no contexto de IA e pela familiaridade já existente.

---

## Bloco 4 — Consequências

### Consequências positivas esperadas

- Compreender os conceitos iniciais de IA aplicada de forma prática
- Internalizar de forma mais profunda a diferença entre Agentes e Workflows
- Ganhar consciência real sobre custo, latência e observabilidade de LLM
- Desenvolver senso crítico sobre quais LLMs são melhores para cada tipo de tarefa
- Implementar um agente de code review funcional que possa ser usado em produção e implantado na [REDACTED]

### Consequências negativas conhecidas

- Testes na API da Anthropic têm custo monetário real — aceito como parte do processo de estudo e aprendizado.

### Riscos identificados

| Risco | Probabilidade | Impacto | Mitigação |
|---|---|---|---|
| Custo acima do cap de [REDACTED] | Alta | Médio | Usar Ollama para testes locais, reservando a API da Anthropic para validações reais |
| Scope creep — tentar ir além do MVP | Média | Alto | Voltar ao plano e ao escopo definido no 3.1 |
| Documentação escassa em C# para IA | Baixa | Baixo | Recorrer ao Claude Code para exemplos e suporte |
| Bloqueio técnico em alguma implementação | Média | Alto | Recorrer ao Claude Code para destravar |
| Imprevistos na [REDACTED] consumindo as 14h semanais | Baixa | Médio | Diminuir o escopo da sprint daquela semana |

### Quando reavaliar essa decisão

- [ ] Quando os conceitos básicos de IA aplicada estiverem internalizados — nesse ponto, avaliar se faz sentido explorar Python para ampliar o alcance no ecossistema de IA.

---

## Bloco 5 — Validação Pós-Decisão

_(Preencher após Marco 1 — 10/07/2026)_

### A decisão se mostrou correta?

- [ ] Sim, sem questionamento
- [ ] Sim, mas com ajustes
- [ ] Não — vou criar ADR-002 pra reverter

### O que aprendi com essa decisão

______

### O que faria diferente em retrospectiva

______
