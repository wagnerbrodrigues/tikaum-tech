claude --model claude-sonnet-4-6 --dangerously-skip-permissions "
## Correção — Máscara automática nos campos de data

Verificar STATUS.md antes de começar. Não alterar nenhuma outra funcionalidade.
Build deve passar com todos os testes ao final. Atualizar STATUS.md e ITERATIONS.md.

### Problema

Os campos de data no sistema não têm máscara automática — o usuário digita livremente
sem formatação DD/MM/AAAA, causando confusão e erros de entrada.

### Campos afetados

1. Campo de data na tela /vendas/nova (data da venda)
2. Campo DataNascimento no dialog de cadastro de cliente (PessoaDialog)
3. Qualquer outro campo de data existente no sistema

### Solução

Implementar máscara automática DD/MM/AAAA via JavaScript interop ou componente Blazor.

#### Opção preferida: atributo de máscara no MudDatePicker

Verificar se o MudDatePicker 9.x suporta Editable=true com máscara.
Se suportar, usar:
- Editable=true
- DateFormat='dd/MM/yyyy'
- Mask='@(new DateMask('dd/MM/yyyy'))'

#### Opção alternativa: MudTextField com máscara via MudBlazor IMask

Se MudDatePicker não suportar edição com máscara adequada, substituir por
MudTextField com máscara numérica:
- Mask='@(new PatternMask('00/00/0000'))'
- Placeholder='DD/MM/AAAA'
- Converter o valor string para DateOnly/DateTime ao sair do campo (OnBlur ou bind)
- Validar a data resultante — se inválida, exibir erro inline no campo

#### Comportamento esperado

- Usuário digita: 1 → campo mostra: 1_/__/____
- Usuário digita: 10 → campo mostra: 10/__/____
- Usuário digita: 1007 → campo mostra: 10/07/____
- Usuário digita: 100720 → campo mostra: 10/07/20__
- Usuário digita: 10072026 → campo mostra: 10/07/2026
- Se data inválida (ex: 32/13/2026): exibir 'Data inválida'
- Campo aceita colar data no formato DD/MM/AAAA diretamente

#### Comportamento do picker

Manter o ícone de calendário para abrir o picker visual — a máscara é para quem
prefere digitar, o picker continua disponível para quem prefere clicar.

### Restrições

- Não alterar nenhum outro campo além dos de data
- Não quebrar a lógica de salvamento existente
- O valor salvo no banco continua sendo DATE ISO (YYYY-MM-DD)
- Não adicionar dependências externas além do que o MudBlazor já oferece
"
