# ADR-002: Decisão de usar workflow (não agent) para o agente de code review como primeiro padrão arquitetural de IA aplicada

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

Definir o padrão arquitetural do primeiro sistema de IA aplicada — um exemplo simples o suficiente para começar a internalizar os conceitos de IA aplicada na prática, e ao mesmo tempo útil e aplicável na [REDACTED] como agente de code review.

### 3.2 — Por que essa decisão importa agora

Essa escolha dá o direcionamento para as próximas semanas da mentoria. Escolher o padrão errado agora significa retrabalho no meio da execução. Além disso, é um caso potencialmente aplicável na [REDACTED] — o que reforça a necessidade de uma decisão consciente e fundamentada.

### 3.3 — Constraints/restrições

- Tempo: ~10h efetivas/semana, 4 semanas até Marco 1 (10/07/2026)
- Custo: cap mensal de [REDACTED]
- Performance: é um POC, nada crítico
- Outros: MVP 100% independente de aprovação da [REDACTED]

---

## Bloco 2 — Alternativas Consideradas

### Alternativa A — RAG (ir direto para um chatbot com banco de dados vetorial)

**Descrição:** Construir um chatbot como primeiro sistema, usando RAG com banco de dados vetorial em vez de um workflow de code review.

**Prós:**
- Contemplaria um projeto de chatbot com uso de bancos de dados vetoriais — tecnologia relevante no ecossistema de IA

**Contras:**
- Passaria do tempo disponível na mentoria
- Não cobriria o padrão workflow, que é o objetivo do aprendizado neste momento

**Custo estimado:** alto em tempo — inviável dentro da janela atual

---

### Alternativa B — Workflow de code review

**Descrição:** Construir um agente de code review como workflow determinístico: diff entra, LLM processa, achados estruturados saem.

**Prós:**
- Contempla um caso de uso real e aplicável na [REDACTED]

**Contras:**
- Nenhum identificado

**Custo estimado:** 4 semanas (Marco 1 em 10/07/2026)

---

## Bloco 3 — Decisão

### Alternativa escolhida

**Escolhi:** Alternativa B — Workflow de code review

### Justificativa

É viável dentro do tempo estimado de 4 semanas e tem potencial real de se tornar um caso de uso na [REDACTED]. O RAG, embora relevante, extrapolaria a janela disponível na mentoria e desviaria do objetivo de aprender o padrão workflow primeiro.

### Trade-offs aceitos

Adiar o início de um chatbot com RAG, que é outro projeto em potencial aplicável na [REDACTED] — deixado para uma próxima etapa após o Marco 1.

### O que NÃO consideramos (e por quê)

Continuar ou expandir projetos pessoais existentes — como a lista de mercado com reconhecimento de voz por IA. Ficou fora de cogitação porque o objetivo da mentoria é construir algo aplicável profissionalmente, com caso de uso real e documentável como portfolio.

---

## Bloco 4 — Consequências

### Consequências positivas esperadas

- Implementar um caso real de IA aplicada, utilizável em produção
- Compreender e internalizar na prática os conceitos de workflows e agentes

### Consequências negativas conhecidas

- Gasto real com tokens durante o desenvolvimento e testes

### Riscos identificados

| Risco | Probabilidade | Impacto | Mitigação |
|---|---|---|---|
| Custo acima do cap de [REDACTED] | Alta | Médio | Usar Ollama para testes locais, reservando a API da Anthropic para validações reais |
| Scope creep — tentar ir além do MVP | Média | Alto | Voltar ao plano e ao escopo definido no 3.1 |
| Documentação escassa em C# para IA | Baixa | Baixo | Recorrer ao Claude Code para exemplos e suporte |
| Bloqueio técnico em alguma implementação | Média | Alto | Recorrer ao Claude Code para destravar |
| Imprevistos na [REDACTED] consumindo as 14h semanais | Baixa | Médio | Diminuir o escopo da sprint daquela semana |

### Quando reavaliar essa decisão

- [ ] Se o projeto for concluído antes do prazo esperado — avaliar expandir o escopo ou iniciar um novo projeto
- [ ] Quando o domínio sobre workflows e IA aplicada estiver internalizado — nesse ponto, o projeto pode não fazer mais sentido como exercício de aprendizado e pode ser abandonado em favor de um desafio mais avançado

---

## Bloco 5 — Validação Pós-Decisão

_(Preencher após Marco 1 — 10/07/2026)_

### A decisão se mostrou correta?

- [ ] Sim, sem questionamento
- [ ] Sim, mas com ajustes
- [ ] Não — vou criar ADR-003 pra reverter

### O que aprendi com essa decisão

______

### O que faria diferente em retrospectiva

______
