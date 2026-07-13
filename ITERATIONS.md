# ITERATIONS.md — Histórico de Ciclos de Desenvolvimento

Registro detalhado de decisões, trade-offs e contexto das iterações de maior impacto.
Para o histórico completo por rodada ver `STATUS.md`.

---

## Ciclo 1 — Identidade Visual Tikaum (Rodada 17, 2026-07-03)

**Objetivo:** substituir a paleta `#121212` por tokens normativos da marca Tikaum.

**Decisões:**
- Tokens CSS centralizados em `app.css` (`--tk-bg`, `--tk-surface-*`, `--tk-gold`, `--tk-text`).
- Shell global (sidebar/topbar/rodapé) em HTML/CSS próprio — nenhum componente MudBlazor
  reproduz o layout do mockup sem tradeoffs maiores.
- Fontes Cinzel/Inter/JetBrains Mono com fallback local para uso offline.
- Branding sempre "Tikaum-Tech", nunca "InkHaus".

**Impacto:** `app.css` reescrito, `MainLayout.razor` refeito, `MudTheme` mapeado para os tokens.

---

## Ciclo 2 — Backup Google Drive (Rodada 18, 2026-07-03)

**Objetivo:** backup automático no Google Drive além do pen drive.

**Decisões:**
- OAuth2 (`drive.file`) — Google não aceita e-mail/senha em apps.
- Token criptografado via ASP.NET Data Protection + DPAPI no Windows.
- Escopo mínimo: o app só enxerga arquivos criados por ele.
- `credentials.json` fora do repositório (`.gitignore`).

**Impacto:** `BackupService` ampliado, `EncryptedFileDataStore` novo, `Program.cs` com
`AddDataProtection`, tela `/backup` com três estados, `install.bat` preserva `config\`.

---

## Ciclo 3 — Correções na Máquina do Estúdio (Rodada 19, 2026-07-05)

**Objetivo:** resolver cinco causas raiz encontradas na instalação de produção.

**Decisões:**
- `Directory.SetCurrentDirectory(AppContext.BaseDirectory)` como primeira linha do `Program.cs`
  — elimina dependência do CWD herdado do UAC.
- try/catch de `IOException` em `app.Run()` — segunda instância encerra silenciosamente.
- Interatividade por página (não global) — compatibilidade com `[ExcludeFromInteractiveRouting]`
  das páginas de Identity.
- `InteractiveProviders.razor` como ilha interativa no `MainLayout` estático.

---

## Ciclo 4 — Identidade Visual Revisão 2 (Rodada 20, 2026-07-05)

**Objetivo:** skull real, dashboard enxuto, BackupBanner no fluxo do layout.

**Decisões:**
- `skull_sidebar.png` otimizado (6,2 MB → 228 KB) — equilibra fidelidade e carregamento.
- Dashboard com 2 cards (Clientes + Vendas) e duas listas (Dia + Mês) — elimina a área vazia.
- `BackupBanner` como ilha `InteractiveServer` própria — necessário porque o `MainLayout` é
  estático desde a Rodada 19; sem a ilha, o [×] não funciona.

---

## Ciclo 5 — Linux, Usuário nas Vendas, Cadastro de Cliente (Rodada 21, 2026-07-05)

**Objetivo:** suporte completo a Linux + seis pedidos do usuário.

**Decisões:**
- `start_linux.sh` com detecção automática de libicu — evita crash em ambientes mínimos.
- Coluna `usuario` em `vendas` como snapshot de texto (não FK) — histórico sobrevive a
  renomeações e exclusões de conta.
- `Email` removido do modelo — migration corrigida manualmente (scaffold gerava RenameColumn).
- Hamburger 100% em JS puro com delegação no `document` — sobrevive à navegação aprimorada
  do Blazor sem depender de rendermode interativo no `MainLayout`.

---

## Ciclo 6 — Verificação Automática do .NET 9 nos Instaladores (Rodada 38, 2026-07-10)

**Objetivo:** garantir que o .NET 9 esteja disponível antes de qualquer ação dos instaladores.

**Decisões:**
- **`install.bat`:** verificação inserida logo após `@echo off`, antes do `setlocal` e de
  qualquer lógica de negócio — garante que `dotnet` esteja no PATH antes de ser usado.
  Download via `Invoke-WebRequest` + `dotnet-install.ps1`, instalação em
  `C:\Program Files\dotnet`, PATH atualizado com `setx /M`.
- **`install.sh`:** arquivo criado do zero como instalador Linux análogo ao `install.bat`.
  Verificação de .NET inserida logo após o bloco de verificação de root, seguindo o mesmo
  padrão: `dotnet-install.sh` → `/usr/share/dotnet`, symlink em `/usr/local/bin/dotnet`.
  Serviço systemd como mecanismo de autostart (análogo à Tarefa Agendada do Windows).
- A app é publicada self-contained (dispensando .NET no destino em produção normal), mas a
  verificação cobre cenários atípicos: instalação sem build prévio, atualização manual, ou
  máquina sem .NET por outra razão.

**Arquivos alterados:** `install.bat` (inserção no início), `install.sh` (criado).
**Arquivos não alterados:** `build_release.bat`, `start_linux.sh`, `stop_linux.sh`, projeto.
**Build:** 0 warnings, 0 erros.

---

## Ciclo 7 — Cadastro Rápido, Instalador em Três Scripts, Restore e CPF Único (Rodada 39, 2026-07-11)

**Objetivo:** quatro entregas do usuário — cadastro rápido de cliente na venda, separação
build/install/update, restore de backup e unicidade de CPF.

**Decisões:**
- **CPF único:** checagem no `PessoaService` (mensagem com o nome do cliente que já usa o
  CPF) + índice único parcial `ix_pessoas_cpf` via `migrationBuilder.Sql` com
  `IF NOT EXISTS` (SQL exato pedido na tarefa; nulo/vazio fora do índice). Banco antigo com
  duplicados: a migration falha no start por design — nunca deduplicar dados em silêncio.
- **Cadastro rápido na venda:** item sentinela (`Id = -1`) devolvido pelo próprio
  `SearchFunc` quando a busca não retorna nada — integra com o fluxo de seleção do
  `MudAutocomplete` sem componente novo. `PessoaDialog` passou a fechar com
  `DialogResult.Ok(pessoa)` para o chamador receber o cliente criado (compatível com os
  chamadores antigos, que só checam `Canceled`).
- **Restore em dois tempos:** a tela `/backup` só deixa `data/tikaum_restore_pending.db`
  (+ `restore_pending.json` com origem/arquivo); a troca acontece no próximo start, antes
  das migrations. `-wal`/`-shm` são renomeados junto com o banco antigo (WAL órfão seria
  aplicado sobre o banco restaurado = corrupção). Download/cópia via `.part` + rename e
  validação do cabeçalho mágico SQLite antes de virar pendente.
- **Guarda de instância dupla no restore (achado de teste real):** a segunda instância só
  morre em `app.Run()` (porta ocupada) — nos testes ela chegou a aplicar o restore com a
  primeira instância no ar. `Program.cs` agora testa a porta configurada antes: se responde,
  o restore fica pendente para o próximo start de verdade; exceções na aplicação do restore
  também não derrubam o app.
- **Listagem do Drive em uma busca só** (padrão de nome, sem descer pasta a pasta): o escopo
  `drive.file` já limita a visão aos arquivos do app, e isso cobre backups legados soltos.
- **Instalador em três scripts:** verificação/instalação do SDK movida para o
  `build_release.bat` (único que compila; o publish é self-contained). `install.bat` virou
  só-primeira-instalação (com aviso/confirmação se já existe) e trocou `xcopy /E` por
  `robocopy /E /XD data config` — uma `data\` presente por engano na origem nunca mais
  sobrescreve o banco do estúdio. `update.bat` novo: backup `tikaum_pre_update_AAAAMMDD.db`
  antes de copiar, verificação de que o banco sobreviveu (senão restaura e aborta) e
  `/R:3 /W:2` no robocopy (o padrão é ~1 milhão de tentativas — travaria para sempre num
  arquivo em uso).

**Arquivos alterados:** `PessoaService`, `ApplicationDbContext`, migration
`AddCpfUniqueIndex`, `Vendas.razor`, `PessoaDialog.razor`, `BackupService`, `Backup.razor`,
`BackupBanner.razor`, `Program.cs`, `install.bat`, `build_release.bat`, `update.bat` (novo),
testes (`PessoaServiceTests` +4, `BackupServiceTests` +5).
**Build:** 0 warnings, 0 erros. **Testes:** 109/109.
**Verificação real:** app subiu no sandbox (login via curl, telas alteradas 200); restore
aplicado num start de verdade (marcador sumiu, `pre_restore` criado, toast info gravada);
guarda de instância dupla exercitada com duas instâncias reais.
**Nota:** `STATUS.md` não existia mais no repositório (referenciado por `CLAUDE.md`/
`ITERATIONS.md`) — recriado nesta rodada a partir do estado real do código. Causa raiz
encontrada depois: `STATUS.md` está no `.gitignore` (bloco "controle de sessão") — ele
nunca foi versionado e existia só no working tree de quem o mantinha.

---

## Ciclo 8 — Build e Deploy na Máquina do Dev (Rodada 40, 2026-07-11)

**Objetivo:** parar de compilar/preparar o deploy na máquina do estúdio. Causa raiz: a
máquina de desenvolvimento é Linux e o único caminho de publicação era o
`build_release.bat` (Windows) — na prática o repo era levado à máquina do estúdio, que
instalava SDK e compilava.

**Decisões:**
- **`build_release.sh` (novo):** o .NET faz cross-publish **win-x64 self-contained a
  partir do Linux** — verificado em build real neste sandbox (394 arquivos, apphost
  `TikaumTech.exe` com o `icon.ico` embutido — conferido byte a byte no PE). A máquina do
  estúdio passa a receber só o pacote pronto.
- **CRLF nos `.bat` garantido pelo script** (`sed` remove CR e recoloca, idempotente): o
  working tree Linux costuma ter LF e `cmd.exe` quebra com LF puro (continuações `^`);
  não dá para depender só do `.gitattributes` (`eol=crlf`), que atua no checkout, não em
  cópias diretas do working tree.
- **Deploy opcional no mesmo passo:** argumento com diretório de destino (ex.: pen drive
  montado) copia o pacote como `TikaumTech_AAAA-MM-DD/` + `sync`.
- **`--linux` gera `publish-linux/` com `install.sh`** — o instalador Linux (Rodada 38)
  já referenciava um `build_release.sh` que não existia; agora existe.
- Detecção de ambiente igual à dos scripts existentes (dotnet no PATH → `~/.dotnet` →
  instala em `~/.dotnet`); sem libicu, o modo invariante é ativado **só para o processo
  de build** (o app publicado roda com ICU completo no destino). Supressão da mensagem
  "Aborted" do teste de ICU exige forçar fork no `bash -c` (`; exit $?`) — com comando
  único o bash faz exec e o sinal aparece no stderr do script.

**Arquivos:** `build_release.sh` (novo), `.gitignore` (`publish-linux/`), mensagens de
erro de `install.bat`/`update.bat`, `TIKAUM_SPEC.md` §10, `README.md`, `CLAUDE.md`.
**Sem mudança no lado do estúdio:** `install.bat`/`update.bat` continuam os mesmos — uma
nova implantação sobre a instalação existente não muda em nada.
**Verificação real:** `./build_release.sh` executado no sandbox (publish win-x64 completo,
`.bat` conferidos em CRLF via `od`, ícone presente no `.exe`); variante `--linux` e cópia
para destino (pen drive simulado) também exercitadas.
