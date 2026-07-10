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
