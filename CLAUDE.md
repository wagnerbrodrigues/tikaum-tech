# CLAUDE.md — Contexto do Projeto Tikaum-Tech

Este arquivo é lido automaticamente pelo Claude Code. Ele existe pra evitar que decisões já tomadas sejam reabertas ou contornadas durante o desenvolvimento.

## Filosofia do projeto

**Spec-first, código gerado.**
Toda necessidade nova deve ser descrita primeiro em `TIKAUM_SPEC.md` ou aqui, antes de qualquer implementação. O código é consequência da especificação — nunca o contrário. Se algo não está nos arquivos de especificação, não deve existir no código.

Antes de implementar qualquer feature:
1. Verifique `TIKAUM_SPEC.md` — está especificado?
2. Verifique `STATUS.md` — já foi feito? Está em andamento?
3. Se não está na spec, discuta primeiro e atualize a spec, depois implemente.

## O que é o projeto

Sistema web local (Blazor Server/.NET 9) de controle de atendimentos para um estúdio de tatuagem e piercing: cadastro de pessoas, produtos, serviços, registro de vendas e relatórios. Roda na máquina do estúdio, acessado via navegador em `http://localhost:5000` (endereço padrão do atalho e da auto-abertura desde 2026-07-03; o alias `http://tikaum-tech.local:5000` continua configurado no hosts como alternativa, mas não é mais o caminho padrão — `.local` é reservado para mDNS e Chrome/Windows às vezes resolvem fora do arquivo hosts, gerando "não foi possível encontrar" com o app no ar). Veja `TIKAUM_SPEC.md` para a especificação completa.

## Decisões já tomadas (não reabrir sem perguntar)

- **Projeto único Blazor Server.** Não criar camadas separadas Domain/Application/Infrastructure/Api.
- **Sem JWT, sem API versionada, sem frontend separado.** Blazor Server usa sessão/cookie nativo.
- **ASP.NET Core Identity para autenticação.** Usar `UserManager`/`SignInManager`/`ChangePasswordAsync`/`ResetPasswordAsync` — nunca implementar hash de senha manual. Sem registro público: contas só são criadas pelo próprio app (seed inicial ou tela `/usuarios`).
- **Login sempre sem persistência — sem opção "Lembrar-me" (decisão de 2026-07-06).** `Login.razor` chama `PasswordSignInAsync(..., isPersistent: false, ...)` fixo; o checkbox "Lembrar-me" foi removido da UI. Motivo: relato do usuário de a aplicação abrir já autenticada, sem tela de login — investigação encontrou que `Program.cs` não define `ExpireTimeSpan`/`SlidingExpiration` (usa o padrão do Identity, 14 dias com sliding), e um cookie persistente (`isPersistent: true`) sobrevive ao navegador fechar. Terminal do estúdio é compartilhado; cookie de sessão (não persistente) deveria sumir ao fechar o navegador de verdade. **Não resolve sozinho** o caso do navegador que mantém processo em segundo plano mesmo "fechado" (ex.: Edge com "continuar executando apps em segundo plano" ligado) — se o problema persistir, é configuração do navegador/SO, não do app.
- **Multiusuário sem papéis (decisão de 2026-07-01, reabre a exclusão original da spec).** Vários usuários podem existir, todos com acesso total — sem permissões diferenciadas. Gestão via tela `/usuarios` (`UsuarioService`, CRUD sobre `UserManager`). Exclusão bloqueada para o próprio usuário logado e para o último usuário do sistema (evita lockout).
- **Usuários/senhas padrão `admin`/`admin` E `tikaum`/`admin` (decisão de 2026-07-01, ampliada em 2026-07-05; reabre "nenhuma senha hardcoded").** `Program.cs` garante os dois usuários a cada start — cada um é criado apenas se ainda não existir; senha de conta existente nunca é tocada. Se `data/setup.json` existir (`install.bat` ainda grava esse arquivo), a senha dele vale só para o `admin`; o arquivo é sempre apagado depois. `tikaum` é o profissional do estúdio, pré-selecionado no lançamento de vendas. Troque as senhas padrão via `/usuarios` assim que possível — os valores são públicos (estão neste repositório).
- **`admin` é recriado mesmo em bancos com outros usuários (decisão de 2026-07-02).** A checagem deixou de ser "só cria se a tabela de usuários está vazia" — agora é "só cria se `admin` especificamente não existir". Isso cobre bancos antigos que já têm `tikaum` (de antes da renomeação): o app garante `admin`/`admin` também, sem tocar na senha de nenhum usuário já existente.
- **SQLite é o único banco.** Não sugerir Postgres/MySQL. A escala é de ~1000 pessoas.
- **Nunca colocar o `.db` ativo numa pasta sincronizada** (Drive Desktop, OneDrive, Dropbox). Backup sempre via `VACUUM INTO` → snapshot estático copiado para fora.
- **Volume de pen drive ausente nunca falha silenciosamente.** Mantém aviso vermelho fixo visível em todas as telas até resolver — desde 2026-07-05 via `BackupBanner` (banner entre topbar e conteúdo, com alerta independente amarelo para Google Drive não configurado; substitui o toast `BackupWarning` que sobrepunha conteúdo). O [×] fecha só temporariamente (reaparece na navegação seguinte).
- **MudBlazor para todos os componentes visuais.** Não usar Bootstrap ou componentes Blazor puros quando MudBlazor resolve — `MudTable`, `MudDialog`, `MudForm`, `MudTextField`. Exceção registrada em 2026-07-03: o shell global (sidebar/topbar/rodapé decorativo) é HTML/CSS próprio, porque nenhum componente MudBlazor reproduz o layout do mockup.
- **Tema escuro por padrão — identidade visual Tikaum (decisão de 2026-07-03, substitui a paleta `#121212` anterior).** Design tokens normativos em `TIKAUM_SPEC.md` §13 (fundo `#111110`, superfícies `#1C1C1A`/`#242420`, acento dourado `#C4A265`, texto `#E8DDC8`; fontes Cinzel/Inter/JetBrains Mono via Google Fonts **com fallback local** — o app precisa continuar usável offline). Zero branco ou cinza claro em qualquer tela/modal/toast. A referência visual são os tokens/regras da §13 da spec (os mockups originais foram incorporados lá e removidos do repositório em 2026-07-05); branding sempre Tikaum, nunca "InkHaus". A arte de skull usada na sidebar/hero vive em `wwwroot/images/skull_sidebar.png`.
- **Vendas nunca são apagadas nem alteradas in-place (decisão de 2026-07-03).** Editar = desabilita o original (`status='disabled'`, `adjusted_at`, `adjusted_reason='edit'`) e cria substituto com `origin_id`; excluir = `status='deleted'` (permanece no banco). Todo relatório/total/métrica filtra `status='active'`. Colunas de auditoria em inglês (`status`, `origin_id`, `adjusted_at`, `adjusted_reason`) por definição explícita da spec — exceção pontual à convenção de nomes em português.
- **Venda registra só a data, sem hora (decisão de 2026-07-05).** A UI de venda (nova e edição) não pede hora; `DataHora` é gravada com 00:00 e toda exibição usa `dd/MM/yyyy`. Coluna/propriedade mantêm o nome `DataHora` (sem migration); ordenações por data desempatam por `Id` DESC.
- **Venda registra quem vendeu (decisão de 2026-07-05).** Coluna `usuario` em `vendas` guarda o **username em texto** (snapshot — sobrevive a renomeação/exclusão da conta; não é FK para o Identity). A tela de venda pré-carrega o primeiro usuário ≠ `admin` em ordem alfabética (na prática, `tikaum`); o valor aparece nos relatórios (tela + Excel) e na listagem de vendas.
- **Cadastro do cliente: nome, data de nascimento, celular e CPF (decisão de 2026-07-05).** `Email` foi removido do modelo/banco/telas. O CPF distingue homônimos (aparece na busca e nos autocompletes); continua opcional, mas **desde a revisão de 2026-07-05 é validado quando preenchido** (dígitos verificadores — `CpfValidador`, no formulário e no `PessoaService`; substitui o "não é validado" original, a pedido do usuário). Celular com máscara `(00) 0000-0000`/`(00) 00000-0000` e CPF com máscara `000.000.000-00` — valores gravados já formatados. A coluna `telefone` mantém o nome no banco — só o rótulo da UI virou "Celular" (mesma lógica da decisão Pessoa/Cliente).
- **O app roda em Windows E Linux (decisão de 2026-07-05).** Código específico de plataforma sempre atrás de `OperatingSystem.IsWindows()`/`IsLinux()` — nunca assumir Windows. No Linux: `start_linux.sh`, navegador via `xdg-open`, pen drive via `/media`//`/run/media` (último segmento do ponto de montagem = nome do volume). Ver `TIKAUM_SPEC.md` §10.
- **Backup idempotente por dia, cada um na sua própria subpasta (decisão de 2026-07-06, amplia a de 2026-07-05).** Um arquivo por data (`tikaum_AAAA-MM-DD.db`) dentro de uma subpasta com o nome da data (`TikaumBackup/AAAA-MM-DD/`), em vez de solto direto em `TikaumBackup/` — maior isolamento entre backups de dias diferentes. Rodar de novo no mesmo dia sobrescreve o arquivo da subpasta do dia. Disparo automático em background na inicialização + a cada 24h (`BackupAutomaticoService`, 2026-07-05) — só tenta destino disponível. O botão manual "Fazer backup agora" continua na tela `/backup` como reforço opcional (não foi removido — o automático não substitui a possibilidade de disparo manual).
- **Retenção de 120 dias é por destino (ampliada de 30 — decisão de 2026-07-06)** — pen drive e Drive contam separadamente; a retenção apaga a subpasta do dia inteira, não arquivo a arquivo.
- **Controle de progresso: `STATUS.md`.** Mantenha atualizado continuamente. Antes de continuar em sessão nova, valide o `STATUS.md` contra o estado real do código.

## Convenções de código

- **Nomes de domínio em português, sem acento** nos identificadores C# e no banco (ex.: `Servico`, não `Serviço`; `PessoaId`). Acentos só em strings de UI.
- Entidades: `Pessoa`, `Produto`, `Servico`, `Venda`, `ItemVenda` — não renomear sem confirmação (o banco SQLite já tem essas tabelas).
- **UI chama `Pessoa` de "Cliente" (decisão de 2026-07-02).** Todo texto visível ao usuário
  (menu, títulos de tela, botões, mensagens de erro, cabeçalhos de relatório/Excel) usa
  "Cliente"/"Clientes", não "Pessoa"/"Pessoas". A entidade C# (`Pessoa`), a tabela (`pessoas`),
  o `PessoaService` e as rotas (`/pessoas`, `/pessoas/{id}`) continuam com o nome original —
  só o texto voltado ao usuário mudou. Não renomear a classe/tabela/rotas sem confirmação (seria
  reabrir a decisão de não renomear entidades, acima).
- `ItemVenda` usa `ProdutoId`/`ServicoId` nullable. Regra: se ambos são nulos, `DescricaoLivre` é obrigatória.
- `ValorUnitario` sempre pré-preenchido do cadastro, mas **editável** no formulário de venda.
- Busca incremental é o padrão em **todo** campo de seleção.
- Não adicionar dependências NuGet silenciosamente — avisar no resumo da mudança.

## Arquivos de especificação

| Arquivo | Propósito |
|---------|-----------|
| `TIKAUM_SPEC.md` | Especificação completa: modelo de dados, telas, regras, stack |
| `CLAUDE.md` | Decisões arquiteturais e convenções para o assistente de IA |
| `STATUS.md` | Estado atual do desenvolvimento — sempre atualizado |
| `README.md` | Documentação para humanos: setup, deploy, uso |

Qualquer feature não descrita nesses arquivos não deve ser implementada sem antes atualizar a especificação.

## Comandos úteis

```bash
dotnet run --project src/TikaumTech          # desenvolvimento
dotnet build src/TikaumTech                  # build
dotnet test                                   # testes (xUnit, src/TikaumTech.Tests)
dotnet run --project src/TikaumTech.Seed      # massa de testes (só em banco VAZIO — spec §11)
sqlite3 data/tikaum.db                        # inspecionar banco manualmente
dotnet ef migrations add <Nome> --project src/TikaumTech
dotnet ef database update --project src/TikaumTech
```

## Onde olhar antes de implementar

1. `STATUS.md` — exatamente onde o desenvolvimento parou
2. `TIKAUM_SPEC.md` — modelo de dados, telas, regras, stack completa
3. Se algo não está documentado → atualizar a spec primeiro, depois implementar
