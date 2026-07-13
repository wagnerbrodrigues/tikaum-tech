# Especificação Técnica — Tikaum-Tech

> **Versão 3 (Blazor Server) — Spec-first.**
> Este projeto segue a filosofia spec-first: toda necessidade de negócio é especificada aqui antes de qualquer implementação. O código é gerado a partir desta especificação — nunca o contrário. Mudanças de comportamento devem ser descritas neste arquivo primeiro.
>
> Substitui a versão Python/Flet desktop. O banco de dados e a lógica de negócio são preservados da versão anterior.

---

## 1. Visão Geral

**Tikaum-Tech** é o sistema de controle de atendimentos de um estúdio de tatuagem e piercing: cadastro de clientes, produtos e serviços, registro de vendas e relatórios financeiros por período.

- **Usuários:** múltiplos, todos com o mesmo nível de acesso (equipe do estúdio) — sem papéis/permissões diferenciadas, acesso local via navegador
- **Plataforma:** Web local — roda em Windows (qualquer edição, inclusive Home) e em **Linux**
  (suporte adicionado em 2026-07-05: `start_linux.sh` na raiz, auto-abertura via `xdg-open`,
  detecção de pen drive em `/media`//`/run/media`), acessado via `http://localhost:5000`
  (alias alternativo no Windows: `http://tikaum-tech.local:5000`)
- **Cliente final:** dono(a)/atendente do estúdio — o sistema deve funcionar sozinho, com mínimo de configuração

---

## 2. Princípios de Design

1. **Projeto único.** Sem separação Domain/Application/Infrastructure/Api — para esse tamanho, só adiciona navegação entre arquivos sem ganho de manutenção.
2. **Dependência mínima.** SQLite local, sem servidor de banco externo, sem internet para uso diário.
3. **Resiliente a erro de uso.** Busca incremental em todo campo de seleção, valores sempre sugeridos do cadastro (mas editáveis).
4. **Autostart transparente.** Sobe com o Windows via Tarefa Agendada ou Windows Service — sem intervenção manual diária.

---

## 3. Stack Tecnológica

| Camada | Escolha | Por quê |
|---|---|---|
| Framework | Blazor Server (.NET 9) | C# do início ao fim, sem frontend separado; funciona em Windows Home |
| Componentes UI | MudBlazor | Material Design, tabelas, formulários e diálogos prontos; tema escuro nativo |
| Banco de dados | SQLite (modo WAL) | Arquivo local único, mesmo banco do legado Python |
| ORM | EF Core 9 | Migrations, LINQ, integração nativa com Identity |
| Autenticação | ASP.NET Core Identity | Hash PBKDF2 nativo; `UserManager`/`SignInManager`/`ChangePasswordAsync` |
| Excel | ClosedXML ou similar | Geração de relatório .xlsx |
| PDF | QuestPDF ou similar | Geração de relatório PDF |
| Backup — pen drive | `DriveInfo` (.NET BCL) | Detecção de unidades removíveis sem dependência externa |
| Backup — Google Drive | `Google.Apis.Drive.v3` | Mesmo fluxo do legado Python |

---

## 4. Arquitetura do Projeto

```
TikaumTech/
  TikaumTech.sln
  src/
    TikaumTech/                  # projeto único Blazor Server
      Components/
        Pages/                   # telas: Login, Pessoas, Vendas, Produtos, Serviços, Relatórios, Backup
        Layout/                  # MainLayout, NavMenu, BackupBanner (componente global)
      Models/                    # entidades de domínio (EF Core + Identity User)
      Data/                      # TikaumDbContext, configurações EF Core, seed
      Services/                  # regras de negócio, acesso a dados
      wwwroot/                   # static files (CSS, logo)
      Program.cs
      appsettings.json
      TikaumTech.csproj
  assets/                        # ícone do app (embutido no .exe via ApplicationIcon)
  data/                          # tikaum.db + backups/ (gitignored)
  config/                        # credentials.json, token.json (gitignored)
  install.bat                    # instalador standalone (copia, hosts, tarefa agendada)
  build_release.bat              # dotnet publish win-x64 + copia install.bat p/ publish\
  TIKAUM_SPEC.md
  CLAUDE.md
```

---

## 5. Modelo de Dados

Schema preservado do legado Python. Migrations EF Core geradas sobre o banco SQLite existente.

### `pessoas`
| Campo | Tipo | Regra |
|---|---|---|
| Id | int, PK | auto |
| Nome | string | obrigatório |
| DataNascimento | DateTime, nullable | coluna `data_nascimento`; opcional |
| Telefone | string | opcional — **exibido como "Celular" na UI** (coluna mantém o nome `telefone`); campo com **máscara** `(00) 0000-0000`/`(00) 00000-0000` (muda para 9 dígitos quando o número após o DDD começa com 9) — o valor é gravado já formatado. Implementação em `TelefoneMascara.Criar()` (correção de 2026-07-08: o `MultiMask` do MudBlazor casa o regex das opções contra o texto **já formatado**, com parênteses — o regex antigo `^\d{2}9` nunca ativava a máscara de 9 dígitos e o último dígito do celular era descartado) |
| Cpf | string | coluna `cpf`; opcional — usado para **distinguir homônimos** (aparece na busca e nos autocompletes de cliente); com **máscara** `000.000.000-00`, **validado** quando preenchido (revisão 2026-07-05, abaixo) e **único** quando preenchido (revisão 2026-07-11, abaixo) |
| Observacoes | string | opcional |
| CriadoEm | DateTime | default now |

> **Revisão do cadastro (2026-07-05):** o campo `Email` foi removido do modelo, do banco
> (migration com DropColumn — dados de e-mail existentes são descartados) e de todas as telas.
> O cadastro do cliente passa a ser: **nome, data de nascimento, celular e CPF**. O CPF é o
> desempate para clientes com o mesmo nome; quando presente aparece ao lado do nome nas buscas.
>
> **Validação de CPF (revisão 2026-07-05, a pedido do usuário — substitui o "não é validado"
> da revisão anterior):** o CPF continua **opcional**, mas quando preenchido é validado pelos
> **dígitos verificadores** (mod 11; rejeita também sequências de dígitos iguais). A validação
> vale no formulário (`PessoaDialog`, bloqueia o salvar com "CPF inválido.") e no service
> (`PessoaService.CriarAsync`/`AtualizarAsync` lançam exceção — defesa em profundidade).
> Helper: `CpfValidador.EhValido()` em `Services`. CPFs inválidos já existentes no banco não
> são tocados — a regra só barra gravação nova/edição.
>
> **Unicidade de CPF (revisão 2026-07-11):** nomes iguais continuam permitidos (o CPF é o
> desempate), mas **dois clientes não podem ter o mesmo CPF**. Defesa em duas camadas:
> `PessoaService.CriarAsync`/`AtualizarAsync` verificam duplicidade antes de gravar (excluindo
> o próprio registro em edição) e lançam "Este CPF já está cadastrado para o cliente [Nome].";
> no banco, índice **único parcial** `ix_pessoas_cpf` (`CREATE UNIQUE INDEX IF NOT EXISTS ...
> ON pessoas (cpf) WHERE cpf IS NOT NULL AND cpf != ''`) — nulo/vazio fica fora do índice,
> então clientes sem CPF seguem ilimitados. Vale também para o cadastro rápido durante a venda
> (mesmo `PessoaService`; o erro aparece no diálogo). **Atenção em bancos antigos:** se já
> houver CPFs duplicados, a migration falha no start — resolver os duplicados pela tela de
> Clientes (na versão anterior) antes de atualizar.

### `produtos`
| Campo | Tipo | Regra |
|---|---|---|
| Id | int, PK | auto |
| Nome | string | obrigatório |
| PrecoPadrao | decimal | obrigatório |
| Ativo | bool | default true |

### `servicos`
| Campo | Tipo | Regra |
|---|---|---|
| Id | int, PK | auto |
| Nome | string | obrigatório |
| PrecoPadrao | decimal | obrigatório |
| Ativo | bool | default true |

### `vendas`
| Campo | Tipo | Regra |
|---|---|---|
| Id | int, PK | auto |
| PessoaId | int, FK → pessoas, nullable | nulo = venda avulsa/anônima |
| DataHora | DateTime | **só a data importa (revisão 2026-07-05)** — a UI não pede mais hora; grava-se a data com hora 00:00. Coluna e propriedade mantêm o nome `DataHora` (sem migration); vendas antigas continuam com a hora gravada, mas toda exibição usa só `dd/MM/yyyy`. Ordenações por `DataHora` desempatam por `Id` DESC (vendas do mesmo dia aparecem da mais recente para a mais antiga) |
| Observacao | string | opcional |
| ValorTotal | decimal | calculado a partir dos itens |
| Usuario | string, nullable | coluna `usuario`; **username do usuário que fez a venda** (snapshot em texto — sobrevive a renomeação/exclusão da conta). Pré-carregado na tela de venda com o primeiro usuário que não seja `admin` (ordem alfabética, a mesma da tela `/usuarios`); aparece nos relatórios |
| Status | string (`active`/`disabled`/`deleted`) | coluna `status`, NOT NULL, default `active` |
| OriginId | int, FK → vendas, nullable | coluna `origin_id`; NULL no registro original, preenchido no substituto criado por edição (aponta para o registro substituído) |
| AdjustedAt | DateTime, nullable | coluna `adjusted_at`; preenchido quando o registro é desabilitado ou excluído |
| AdjustedReason | string (`edit`/`delete`), nullable | coluna `adjusted_reason`; razão pela qual foi desabilitado |

> **Rastreabilidade (decisão de 2026-07-03):** venda nunca é apagada nem alterada in-place.
> *Editar* = original recebe `status='disabled'`, `adjusted_at=now`, `adjusted_reason='edit'` e um
> registro novo é criado com os dados alterados e `origin_id` apontando para o original.
> *Excluir* = registro recebe `status='deleted'`, `adjusted_at=now`, `adjusted_reason='delete'` e
> permanece no banco. Registros `disabled`/`deleted` são somente leitura (sem editar/excluir de novo).
> **Toda consulta exibida ao usuário (relatórios, dashboard, totais, ficha do cliente) filtra
> `status='active'`** — o histórico só aparece na página de Vendas com o toggle
> "Mostrar histórico de ajustes". Os nomes das colunas de auditoria são em inglês por definição
> explícita desta spec (exceção pontual à convenção de nomes em português).

### `itens_venda`
| Campo | Tipo | Regra |
|---|---|---|
| Id | int, PK | auto |
| VendaId | int, FK → vendas | obrigatório |
| ProdutoId | int, FK → produtos, nullable | preenchido quando item é produto |
| ServicoId | int, FK → servicos, nullable | preenchido quando item é serviço |
| DescricaoLivre | string | obrigatório quando ProdutoId e ServicoId são nulos |
| Quantidade | decimal | default 1 |
| ValorUnitario | decimal | pré-preenchido do cadastro, editável na venda |
| ValorTotal | decimal | calculado (Quantidade × ValorUnitario) |

> Regra validada no service layer: se `ProdutoId` e `ServicoId` são ambos nulos, `DescricaoLivre` é obrigatória. Essa é a herança do design polimórfico do schema SQLite legado — não alterar sem migration.

### Identity
Tabelas padrão do ASP.NET Core Identity criadas via migration. Sem registro público — contas só são criadas pelo próprio app.

A cada start, o `Program.cs` garante que existam **dois usuários padrão** (cada um criado apenas se ainda não existir; a senha de contas já existentes nunca é alterada):

- **`admin`** com senha padrão `admin` — se existir `data/setup.json` (`{"AdminPassword":"..."}`, gravado pelo `install.bat`), a senha desse arquivo é usada no lugar da padrão; o arquivo é sempre apagado depois, exista ou não usuário para criar.
- **`tikaum`** com senha padrão `admin` (decisão de 2026-07-05) — é o profissional do estúdio e o usuário pré-selecionado ao lançar vendas.

Depois do primeiro start, a tela `/usuarios` permite criar/editar/excluir outras contas — todas com o mesmo nível de acesso, sem papéis. Troque as senhas padrão assim que possível (são públicas, estão neste repositório).

---

## 6. Páginas Blazor

### Login (`/login`)
- Formulário usuário/senha via Identity (`SignInManager`)
- Redirect para `/` após login bem-sucedido
- Troca de senha disponível no perfil (`ChangePasswordAsync`) — `/Account/Manage/ChangePassword`

### Usuários (`/usuarios`)
- `MudTable` listando usuário e e-mail de todas as contas
- Botão "Novo Usuário" abre `MudDialog` com usuário, e-mail e senha (via `UserManager.CreateAsync`)
- Editar: mesmo diálogo, permite renomear (`SetUserNameAsync`), trocar e-mail e opcionalmente redefinir a senha
  (`ResetPasswordAsync` com token gerado internamente — não exige a senha antiga, pois é uma ação administrativa)
- Excluir: bloqueado para o próprio usuário logado e para o último usuário restante no sistema
  (proteção contra lockout total)

### Clientes (`/pessoas`)
- Tela e menu chamados "Clientes" na UI (decisão de 2026-07-02); entidade/tabela no banco
  continuam `Pessoa`/`pessoas` — ver `CLAUDE.md`
- `MudTable` com busca incremental (filtra por nome/celular/CPF a cada tecla)
- Botão "Novo Cliente" abre `MudDialog` com formulário: nome, data de nascimento, celular,
  CPF e observações (sem e-mail — revisão de 2026-07-05)
- Colunas da tabela: Nome, Celular, CPF, Nascimento, Cliente desde, Ações
- Clique na linha → `/pessoas/{id}`

### Detalhe do Cliente (`/pessoas/{id}`)
- Dados cadastrais + botão Editar
- Tabela de vendas: data, itens, valor — ordenada da mais recente para mais antiga
- Rodapé: total gasto, ticket médio

### Vendas — listagem (`/vendas`)
- Tabela com filtros de período/cliente; por padrão exibe **apenas** registros `status='active'`
- Toggle "Mostrar histórico de ajustes": exibe também `disabled` (linha âmbar sutil, opacity 0.7,
  badge `EDITADO`, coluna "Substituído por" com link para o registro novo) e `deleted` (linha
  avermelhada sutil, opacity 0.6, valores riscados, badge `EXCLUÍDO`)
- Registro ativo criado por edição exibe badge `AJUSTE` e, com histórico ligado, link "Origem"
  para o registro original desabilitado
- Ações por status: `active` = editar/excluir/visualizar; `disabled`/`deleted` = só visualizar
  (somente leitura)
- Editar abre modal com os dados preenchidos (cliente — inclusive trocar —, data, observação,
  itens); confirmar segue o fluxo de substituição da seção 5 e mostra toast
  "Venda atualizada com sucesso."
- Excluir abre modal de confirmação ("Tem certeza que deseja excluir esta venda? Esta ação não
  pode ser desfeita."); confirmar marca `deleted` e mostra toast "Venda excluída."

### Vendas — nova (`/vendas/nova`)
- Busca incremental de cliente ou toggle "Venda avulsa" — o dropdown do autocomplete mostra
  CPF/celular abaixo do nome para distinguir homônimos
- **Cadastro rápido de cliente (2026-07-11):** quando a busca de cliente não retorna nenhum
  resultado, o próprio dropdown mostra a opção **"+ Cadastrar [texto digitado] como novo
  cliente"** (item sentinela com `Id = -1`). Selecioná-la abre o **mesmo** `PessoaDialog` de
  `/pessoas` com o Nome já preenchido; ao salvar, o cliente recém-criado fica selecionado na
  venda e o carrinho é preservado; cancelar volta ao campo de busca sem selecionar ninguém.
  Passa pelas mesmas validações do `PessoaService` (CPF válido e único — o erro aparece no
  diálogo). Para devolver o cliente criado, o `PessoaDialog` fecha com
  `DialogResult.Ok(pessoa)` (compatível com os chamadores existentes, que só checam `Canceled`)
- Select "Usuário" com todas as contas, **pré-carregado com o primeiro usuário ≠ `admin`**
  (na prática, `tikaum`); gravado na venda (coluna `usuario`) e editável antes de salvar
- Adicionar item em abas na ordem **Serviço → Produto → Item Livre** (revisão 2026-07-08:
  Serviço vem primeiro e é a aba inicial — é o carro-chefe do estúdio; mesma ordem no diálogo
  de edição de venda), cada uma com busca incremental; Item Livre = descrição manual
- Valor pré-preenchido do cadastro, sempre editável
- Total calculado em tempo real
- Data editável (lançamento retroativo) — **sem campo de hora** (revisão 2026-07-05)

### Produtos (`/produtos`)
- `MudTable` com busca incremental
- CRUD via `MudDialog`: nome, preço padrão, ativo/inativo (soft delete)

### Serviços (`/servicos`)
- `MudTable` com busca incremental
- CRUD via `MudDialog`: nome, preço padrão, ativo/inativo

### Relatórios (`/relatorios`)
**Dois relatórios em abas (revisão 2026-07-05):**
- **Aba "Vendas"** (comportamento original):
  - Filtros: período (data início + fim) e cliente (busca incremental, opcional)
  - Resultado: vendas no filtro (com coluna **Usuário** — quem fez a venda), subtotais por
    cliente, total geral
  - Exportação PDF e Excel com os filtros aplicados (Excel também traz a coluna Usuário)
- **Aba "Aniversariantes"**:
  - Filtro: **mês** (select com os 12 meses), pré-carregado com o **mês atual** —
    alterável como nos demais filtros do app
  - Resultado: clientes com `DataNascimento` no mês, ordenados pelo **dia** do aniversário;
    colunas Dia, Cliente (link para a ficha `/pessoas/{id}`), Nascimento (com a idade que
    completa), Celular, Mensagem; clientes sem data de nascimento ficam de fora
  - Botão Imprimir/PDF (mesmo `window.print` da aba de vendas)
  - **Coluna Mensagem (2026-07-06):** ícone por linha abre `MudDialog` com um campo de texto
    editável pré-preenchido com uma mensagem de aniversário e sugestão de brinde ou desconto
    (`RelatorioService.GerarMensagemAniversario`, texto fixo/genérico — não depende de
    histórico de compras); botão "Copiar" usa a área de transferência do navegador
    (`navigator.clipboard`). O app só ajuda a compor o texto — não envia nada (sem
    integração de WhatsApp/SMS/e-mail); o atendente copia e envia manualmente.

### Backup (`/backup`)
- Status de último backup por destino (pen drive / Google Drive)
- Botão backup manual
- Seleção/reconfiguração do volume do pen drive
- **Seção "Restaurar Backup" (2026-07-11):** aviso fixo em vermelho ("restaurar um backup
  substituirá todos os dados atuais..."), botão **"Fazer backup agora antes de restaurar"**
  (dispara o backup normal nos destinos disponíveis) e dois botões de origem — **Google Drive**
  (primário) e **pen drive** (secundário) — que listam os backups disponíveis do mais recente
  para o mais antigo (nome, data, tamanho). "Restaurar" em uma linha abre `ConfirmDialog` com
  o aviso em vermelho; confirmado, o arquivo é baixado/copiado para
  `data/tikaum_restore_pending.db` (com progress indicator) e a troca acontece **no próximo
  start** (ver §9). Enquanto houver restore pendente, a seção mostra o estado pendente com
  botão "Cancelar restauração pendente" (apaga o arquivo — o banco atual segue em uso)

### BackupBanner (componente global no Layout — substitui o BackupWarning em 2026-07-05)
- Banner fixo **entre o topbar e o conteúdo** (largura da área de conteúdo, não sobrepõe nada;
  `position: sticky` abaixo do topbar) — substitui o toast flutuante anterior que cobria conteúdo
- **Dois alertas independentes**, empilhados quando ambos ativos:
  - **Pen drive (vermelho, borda esquerda `#BF5D5D`):** aparece quando o pen drive não está
    conectado/configurado — "Pen drive de backup não encontrado..."
  - **Google Drive (amarelo, borda esquerda `#D4A017`):** aparece quando o Google Drive não está
    configurado (token ausente) — "Backup no Google Drive não configurado..."
  - Extra (mesma garantia do BackupWarning antigo): Google Drive **configurado mas com erro** no
    último backup também gera alerta vermelho — falha nunca é silenciosa
- Cada alerta tem link "→ `/backup`" e botão [×] que fecha **temporariamente** (reaparece na
  próxima navegação enquanto a condição persistir)
- **Restore pendente (2026-07-11):** alerta amarelo "Restauração de backup pendente. Reinicie
  o sistema..." enquanto existir `data/tikaum_restore_pending.db`; e, no primeiro render após
  um restore aplicado no startup, toast de sucesso "Banco restaurado com sucesso de [origem]
  ([arquivo])" (consome `data/restore_done.json`)
- Visível em **todas as telas** — não bloqueia uso; recheca a cada 30s

---

## 7. Autenticação

- **ASP.NET Core Identity** — sem implementação manual de hash
- Seed do admin no `Program.cs` via `UserManager.CreateAsync`: primeiro start sem usuários cria `admin`/`admin`
  (ou a senha de `data/setup.json`, se presente)
- Login: `SignInManager.PasswordSignInAsync`, sempre `isPersistent: false` (sem opção "Lembrar-me" —
  removida em 2026-07-06; ver decisão em `CLAUDE.md`)
- Troca de senha (próprio usuário): `UserManager.ChangePasswordAsync`
- Gestão de usuários (`/usuarios`, qualquer usuário logado): `UserManager.CreateAsync` / `SetUserNameAsync` /
  `ResetPasswordAsync` / `DeleteAsync` via `UsuarioService`
- Multiusuário sem papéis/permissões — todo usuário autenticado tem acesso total ao sistema
- Sem JWT, sem registro público, sem refresh token

---

## 8. Configuração

`appsettings.json` (valores reais em produção; `appsettings.Development.json` só troca o
banco para `data/tikaum-dev.db`):
```json
{
  "Urls": "http://localhost:5000",
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=data/tikaum.db;Cache=Shared"
  },
  "AllowedHosts": "tikaum-tech.local;localhost",
  "BackupConfig": {
    "LocalDir": "data/backups",
    "PenDriveVolumeName": "TIKAUM_BACKUP",
    "GoogleDriveAccountEmail": "tikaumtech@gmail.com",
    "RetencaoDias": 120
  }
}
```

Não há `AdminSeedPassword` (chave prevista originalmente, nunca implementada): a senha
inicial do `admin` vem de `data/setup.json` (gravado pelo `install.bat` e apagado após o
primeiro start) — ver decisão de usuários padrão em `CLAUDE.md`.

Credenciais Google Drive ficam em `config/` — gitignored por completo:
- `config/credentials.json` — OAuth client ("Aplicativo para computador") baixado do Google
  Cloud Console pelo usuário; identifica o app, não contém a senha da conta.
- `config/google_token_*.enc` — token OAuth (a credencial real de acesso), armazenado
  **criptografado em repouso** via ASP.NET Data Protection (decisão de 2026-07-03; substitui
  o `token.json` em texto plano previsto originalmente).
- `config/keys/` — chaves do Data Protection; no Windows são protegidas adicionalmente com
  DPAPI (vinculadas à conta do Windows da máquina do estúdio).

> **A senha da conta Google nunca é armazenada nem usada pelo app.** A API do Drive não
> aceita autenticação por e-mail/senha (o Google desativou login por senha em apps); o único
> caminho suportado é OAuth2 com consentimento único no navegador. `BackupConfig.
> GoogleDriveAccountEmail` (appsettings) guarda apenas o e-mail **esperado** da conta
> (`tikaumtech@gmail.com`) para exibição e conferência pós-conexão — não é segredo.

---

## 9. Backup — Estratégia

Preserva a lógica do legado Python, reescrita em C#:

- **Snapshot:** `VACUUM INTO` via SQLite direto (ADO.NET ou EF Core), um arquivo por dia (`tikaum_AAAA-MM-DD.db`) — rodar novamente no mesmo dia sobrescreve
- **Cada backup na sua própria subpasta (decisão de 2026-07-06, para maior segurança/isolamento
  entre dias):** dentro da pasta raiz `TikaumBackup`, cada dia ganha uma subpasta com o nome da
  data (`AAAA-MM-DD`) contendo o `tikaum_AAAA-MM-DD.db` daquele dia — ex.
  `TikaumBackup/2026-07-06/tikaum_2026-07-06.db`. Vale para pen drive e Google Drive. Substitui
  o layout anterior (arquivos soltos direto em `TikaumBackup/`).
- **Pen drive:** detectado via `DriveInfo.GetDrives()`, filtrando por `VolumeLabel == PenDriveVolumeName`; nunca gravar em qualquer pen drive conectado
- **Google Drive (detalhado em 2026-07-03):** upload via `Google.Apis.Drive.v3` com escopo
  mínimo `drive.file` (o app só enxerga arquivos criados por ele). Fluxo na tela `/backup`:
  "Conectar Google Drive" dispara o consentimento OAuth2 no navegador da máquina (uma única
  vez; token fica em `config/google_token_*.enc`, criptografado — ver seção 8); "Testar
  conexão" chama `about.get` e mostra conta conectada + uso da cota, provando o acesso;
  "Desconectar" apaga o token. Snapshots vão para a subpasta do dia dentro de `TikaumBackup`
  no Drive (pastas criadas pelo app conforme necessário), retenção de 120 dias aplicada no
  Drive após cada upload (apaga a subpasta do dia expirado inteira). Se a conta conectada for
  diferente de `BackupConfig.GoogleDriveAccountEmail`, a UI avisa (não bloqueia).
- **Retenção:** 120 dias por destino (ampliada de 30 — decisão de 2026-07-06), aplicada após
  cada entrega bem-sucedida; apaga a subpasta do dia inteira, não arquivo a arquivo
- **Disparo automático (implementado em 2026-07-05):** `BackupAutomaticoService`
  (BackgroundService) tenta o backup ~20s após a inicialização e a cada 24h a partir daí,
  sem travar a UI. Só tenta pen drive quando o volume configurado está conectado e Google
  Drive quando o token existe (destino indisponível não gera erro repetido — o `BackupBanner`
  já avisa). Como o snapshot é idempotente por dia, repetição é inofensiva.
  *Substitui a "fila de pendências" (`pendentes_pendrive/`/`pendentes_drive/`) planejada
  originalmente: a tentativa diária + um arquivo por data cobre o mesmo objetivo (falha de
  conexão não perde backup — o do dia seguinte inclui tudo) com bem menos peças móveis.*
- **Nunca colocar o `.db` ativo em pasta sincronizada** (Drive Desktop, OneDrive etc.)

### Restore de backup (2026-07-11)

A lógica de backup acima não muda — o restore é só um caminho de volta:

- **Agendamento (tela `/backup`, seção "Restaurar Backup"):** o snapshot escolhido é
  baixado do Google Drive (`BaixarBackupGoogleDriveAsync(fileId)`) ou copiado do pen drive
  (`CopiarBackupPenDriveAsync(filePath)`) para `data/tikaum_restore_pending.db` (escrita via
  arquivo `.part` + rename; o conteúdo é validado pelo cabeçalho mágico do SQLite antes de
  virar pendente). A listagem usa `ListarBackupsGoogleDriveAsync()` (uma busca só por padrão
  de nome — o escopo `drive.file` já limita aos arquivos do app; cobre também backups antigos
  soltos) e `ListarBackupsPenDriveAsync()` (`TikaumBackup/**/*.db` — subpastas por dia e
  layout legado), ambas do mais recente para o mais antigo.
- **Aplicação (próximo start, `Program.cs` → `BackupService.AplicarRestorePendente()`, ANTES
  das migrations e de qualquer conexão):** `tikaum.db` vira
  `tikaum_pre_restore_TIMESTAMP.db` — os arquivos `-wal`/`-shm` são renomeados junto
  (pertencem ao banco antigo; um WAL órfão seria aplicado pelo SQLite sobre o banco
  restaurado = corrupção) — e `tikaum_restore_pending.db` assume como `tikaum.db`. As
  migrations rodam em seguida e atualizam o snapshot para o schema corrente. O evento é
  logado; `restore_pending.json` (origem/arquivo) vira `restore_done.json`, que o
  `BackupBanner` consome no primeiro render para o toast de sucesso.
- **Guarda de instância dupla (verificada em teste real):** uma segunda instância morre em
  silêncio só em `app.Run()` (porta ocupada) — tarde demais para o restore. Se a porta
  configurada já responde, o restore **não** é aplicado (fica pendente, com aviso no log);
  qualquer exceção na aplicação do restore também não impede o app de subir (o banco atual
  segue em uso e a pendência continua).
- O banco substituído **nunca é apagado** — `tikaum_pre_restore_*.db` fica em `data/` como
  arrependimento de última instância.

### Recuperação total — máquina formatada/trocada (2026-07-13)

Cenário diferente do restore acima: o app em si não existe mais na máquina (formatação,
troca de computador), não só o banco. Passo a passo completo no `README.md` (seção
"Recuperação total"); resumo: `install.bat` numa instalação nova (banco vazio) →
disponibilizar a origem (pen drive: só conectar; Google Drive: `credentials.json` +
"Conectar Google Drive" de novo, porque `config/` — o token — **não é copiado em nenhum
backup, de propósito**, é credencial e não dado do negócio) → usar a seção "Restaurar
Backup" normalmente. Pen drive é a via mais simples (sem credencial para reconectar) —
por isso deve ficar fisicamente separado do computador do estúdio.

### Resiliência da migration do índice de CPF (2026-07-13)

Reabre parcialmente o "Pontos de atenção conhecidos" original (STATUS.md): um banco de
produção com CPFs duplicados de antes da regra de unicidade (2026-07-05) travava
`db.Database.Migrate()` — sem try/catch em `Program.cs`, isso impedia o app inteiro de
subir ("sistema não abre" / erro em várias telas — incidente de 2026-07-13). Correção:

- A migration `AddCpfUniqueIndex` teve o `CREATE UNIQUE INDEX` removido do `Up()` (virou
  no-op, mantida só por histórico/consistência com o model snapshot).
- A criação do índice agora é um passo **idempotente pós-migration** em `Program.cs`
  (`CREATE UNIQUE INDEX IF NOT EXISTS`, tentado em todo start, dentro de try/catch) — nunca
  impede o app de subir. A validação de duplicidade em `PessoaService` (nível aplicação)
  continua ativa independente do índice existir ou não.
- Falha nessa criação (duplicados presentes) é logada e populada em `AvisoSistemaService`
  (`CpfsDuplicados`, registrado como singleton), exibida pelo `BackupBanner` em todas as
  telas com a lista "CPF (Nome1, Nome2)" — o índice se cria sozinho no restart seguinte
  assim que os duplicados forem corrigidos em `/pessoas`, sem qualquer intervenção manual
  além de corrigir o cadastro.
- `ArquivoLoggerProvider` (log em arquivo, `data/logs/tikaum_AAAA-MM-DD.log`, retenção de
  30 dias) foi registrado em `builder.Logging` — a máquina do estúdio roda por Tarefa
  Agendada sem console visível; sem log em arquivo, uma falha fatal de startup era
  invisível e virava só "o sistema não abre", sem nenhum rastro para diagnosticar.

---

## 10. Autostart no Windows

Funciona em Windows Home, Pro e Enterprise.

**Opção 1 — Tarefa Agendada (mais simples, é a usada pelo `install.bat`):**
```
schtasks /create /tn "TikaumTech" /tr "caminho\TikaumTech.exe" /sc onlogon /ru CURRENTUSER
```
- O app abre o navegador sozinho ao subir (registrado em `ApplicationStarted`, não um
  delay fixo) — evita abrir a página antes do Kestrel estar pronto para aceitar conexões.
- **Endereço padrão é `http://localhost:5000` (decisão de 2026-07-03):** a auto-abertura
  (`Program.cs`) e o atalho da área de trabalho usam `localhost`, que funciona sempre,
  sem depender do arquivo hosts nem do resolvedor de nomes. O alias
  `http://tikaum-tech.local:5000` continua sendo adicionado ao hosts e funciona como
  endereço alternativo, mas `.local` é um TLD reservado para mDNS e Chrome/Windows às
  vezes o resolvem fora do hosts (Secure DNS, mDNS), causando "não foi possível
  encontrar" no navegador mesmo com o app rodando.
- `install.bat` cria um atalho `.lnk` na área de trabalho de todos os usuários
  (`CommonDesktopDirectory`) apontando para um `launch.vbs` que garante o `TikaumTech.exe`
  em execução (inicia sem janela se preciso) e abre `http://localhost:5000`. O usuário
  acessa o site, nunca o processo/servidor; o ícone vem do próprio `TikaumTech.exe`
  (`assets/icon.ico` via `ApplicationIcon` no `.csproj`).
- `install.bat` roda `ipconfig /flushdns` após editar o hosts, pois o Windows pode manter
  em cache uma resolução negativa anterior de `tikaum-tech.local` e continuar reportando
  "página não encontrada" mesmo com o hosts já corrigido.
- `install.bat` apaga a instalação anterior em `C:\TikaumTech` (preservando `data\`)
  antes de copiar, e `build_release.bat` apaga `publish\` antes de publicar — evita
  misturar arquivos de versões diferentes após reinstalações sucessivas.

### Scripts de build e implantação com responsabilidades separadas (revisão 2026-07-11)

Substitui o modelo anterior em que o `install.bat` acumulava instalação e atualização e
verificava/instalava o .NET (verificação da Rodada 38 — movida). Regra geral: **build e
preparação do pacote acontecem na máquina do DESENVOLVEDOR** (Windows ou Linux); a máquina
do estúdio só recebe o pacote pronto e executa `install.bat`/`update.bat`:

- **`build_release.bat` — máquina do DESENVOLVEDOR (Windows).** Único que precisa de .NET:
  verifica o SDK 9 (e instala via `dotnet-install.ps1` em `C:\Program Files\dotnet` + `setx
  PATH` se ausente — lógica movida do `install.bat`, onde não pertencia), publica Release
  win-x64 self-contained em `publish\` (limpa a anterior) e copia `install.bat` **e**
  `update.bat` para dentro dela.
- **`build_release.sh` — máquina do DESENVOLVEDOR (Linux; criado em 2026-07-11).** O .NET
  faz **cross-publish win-x64 a partir do Linux** (self-contained, com o ícone embutido no
  apphost — verificado em build real), então build e preparação do deploy acontecem na
  máquina do dev; **a máquina do estúdio nunca compila** (antes, sem caminho Linux→Windows,
  o build acabava sendo feito na máquina do estúdio). Mesmo fluxo do `.bat`: localiza o SDK
  (PATH → `~/.dotnet`; instala em `~/.dotnet` se ausente; ativa modo invariante só no
  processo de build se faltar libicu), limpa e publica `publish/`, copia
  `install.bat`/`update.bat` **normalizando para CRLF** (working tree Linux costuma tê-los
  em LF, e `cmd.exe` quebra com LF puro). Extras: argumento opcional com um diretório de
  destino copia o pacote pronto como `TikaumTech_AAAA-MM-DD/` (ex.: pen drive montado —
  o "deploy" sai da máquina do dev); `--linux` publica linux-x64 em `publish-linux/` com o
  `install.sh` dentro (o instalador Linux já referenciava esse fluxo).
- **`install.bat` — máquina do ESTÚDIO, PRIMEIRA instalação.** Não compila nem instala .NET
  (o publish é self-contained). Se `C:\TikaumTech` já tem instalação, avisa e sugere o
  `update.bat` (prossegue só com confirmação explícita). A cópia usa `robocopy /E /XD data
  config /XF install.bat update.bat` — uma pasta `data\`/`config\` presente por engano na
  origem **nunca** sobrescreve o banco/config do estúdio (antes o `xcopy /E` copiaria).
  Mantém o restante do fluxo: senha do admin mascarada → `data\setup.json`, hosts +
  `flushdns`, Tarefa Agendada no logon, atalho (`launch.vbs` + `.lnk`), inicia o app.
- **`update.bat` — máquina do ESTÚDIO, ATUALIZAÇÕES (novo).** Aborta se não há instalação.
  Para a tarefa/processo (`Stop-ScheduledTask` + `taskkill`, espera 3s), faz backup do banco
  como `data\tikaum_pre_update_AAAAMMDD.db` **antes de qualquer cópia** (aborta se o backup
  falhar), copia com o mesmo `robocopy` acima (`/R:3 /W:2` — sem o retry quase infinito
  padrão do robocopy em arquivo travado; códigos 0–7 = sucesso), **verifica que
  `data\tikaum.db` ainda existe** (se sumiu: restaura o backup e aborta com erro crítico) e
  reinicia via `Start-ScheduledTask` (fallback: inicia o `.exe` direto se a tarefa não
  existir). Nunca toca em `data\` nem `config\` — senha, hosts, tarefa e atalho não são
  refeitos.

**Opção 2 — Windows Service (mais robusto):**
- Instalar `Microsoft.Extensions.Hosting.WindowsServices`
- Chamar `.UseWindowsService()` em `Program.cs`
- `sc create TikaumTech binPath= "caminho\TikaumTech.exe"`

### Execução no Linux (2026-07-05)

O mesmo código roda no Linux sem alteração — os pontos específicos de plataforma são
condicionais (`OperatingSystem.IsWindows()`/`IsLinux()`):

- **Rodar:** `./start_linux.sh` na raiz do repositório (usa `dotnet run` se houver SDK, ou o
  publish se existir); ou `dotnet publish -c Release -r linux-x64 --self-contained` e executar
  `./TikaumTech` da pasta publicada. Acesso em `http://localhost:5000`.
- **Roda em segundo plano (decisão de 2026-07-06, paridade com o Windows):** no Windows o
  `.exe` já roda sem janela de console (subsistema `WinExe`, seção 10 acima) em qualquer
  caminho de início (Tarefa Agendada, atalho, `install.bat`). `start_linux.sh` agora faz o
  equivalente no Linux — `nohup ... & disown`, devolvendo o terminal na hora; log completo em
  `logs/tikaum.log` (pasta local, fora do git). Para encerrar, `./stop_linux.sh` (ou
  `pkill -f TikaumTech`, que também funciona sozinho).
- **Guard de instância já no ar (2026-07-08):** uma segunda instância morre em silêncio por
  design (Rodada 19) — rodar `start_linux.sh` com o app já no ar deixava o navegador preso na
  versão antiga, parecendo que a atualização "não pegou" (foi a causa provável do relato "o
  site não renderizou corretamente no Linux"). O script agora testa a porta 5000 antes de
  lançar (`/dev/tcp`, sem dependências) e, se ocupada, orienta a rodar `./stop_linux.sh` +
  recarregar com Ctrl+F5, saindo com erro em vez de fingir que iniciou.
- **Auto-abertura do navegador:** `xdg-open` (se ausente — servidor sem interface gráfica —
  só loga a URL, sem falhar).
- **Pen drive:** montagens automáticas aparecem em `/media/<usuário>/<RÓTULO>` ou
  `/run/media/<usuário>/<RÓTULO>`; a detecção usa o último segmento do ponto de montagem como
  "nome do volume" (equivalente ao `VolumeLabel` do Windows). O pen drive precisa estar
  montado (clicar nele no gerenciador de arquivos basta).
- **Data Protection:** sem DPAPI (exclusivo do Windows) — as chaves em `config/keys/` ficam
  protegidas apenas pelas permissões do filesystem, comportamento padrão do ASP.NET no Linux.
- **ICU:** distribuições comuns já têm libicu (pt-BR funciona nativo); em ambientes mínimos
  sem libicu, rodar com `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` (moeda sai como `¤` e
  datas em `MM/dd`). A detecção do `start_linux.sh` não pode depender de `ldconfig` estar
  no PATH (no Debian ele mora em `/sbin`, fora do PATH de usuário comum — um teste ingênuo
  falha em silêncio e força o modo invariante mesmo com libicu instalada): tenta `ldconfig`
  também por caminho absoluto e, na ausência, procura `libicuuc.so*` nos diretórios de lib
  usuais. O app fixa `pt-BR` como cultura padrão no startup (independe do `LANG` da máquina)
  e os `MudDatePicker` usam `DateFormat="dd/MM/yyyy"` explícito, garantindo dd/MM até em
  modo invariante.
- `install.bat`/hosts/Tarefa Agendada são exclusivos do Windows; no Linux o autostart fica a
  cargo do usuário (systemd/autostart do desktop), fora do escopo.

---

## 11. Fases de Entrega

1. **Fase 1:** `dotnet new blazor`, MudBlazor, EF Core + SQLite, Identity + seed, migration inicial, Login, Pessoas
2. **Fase 2:** Vendas, Produtos, Serviços
3. **Fase 3:** Relatórios com exportação PDF/Excel
4. **Fase 4:** Backup + BackupWarning global, autostart Windows
5. **Fase 5:** Testes de integração, validação final, documentação de deploy

### Massa de testes para homologação (2026-07-05)

Ferramenta de linha de comando `src/TikaumTech.Seed` (projeto console separado — não faz
parte do app publicado) que popula um banco **vazio** com dados realistas de estúdio para
testar o software de ponta a ponta:

- **Só roda em banco sem dados de domínio.** Se `pessoas`, `produtos`, `servicos` ou
  `vendas` tiver qualquer linha, aborta sem tocar em nada — impossível poluir um banco
  real. Para re-semear, apague o arquivo `.db` (o schema é recriado via migrations pela
  própria ferramenta).
- **Usa os services reais do app** (`PessoaService`, `ProdutoService`, `ServicoService`,
  `VendaService`) — a massa passa pelas mesmas validações e regras de domínio da UI,
  incluindo a trilha de auditoria (vendas editadas viram `disabled` + substituta com
  `origin_id`; excluídas viram `deleted`), venda avulsa, item livre e usuário na venda.
- **Conteúdo:** ~20 clientes (com par de homônimos distinguíveis por CPF, cadastros
  incompletos e cliente sem compras), ~10 produtos e ~12 serviços dos três tipos (com
  inativos), e ~320 vendas espalhadas pelos últimos ~2 anos (730 dias) — incluindo hoje e o
  mês corrente, para os painéis "Vendas do Dia"/"Vendas do Mês" terem dados e para permitir
  testar relatórios/filtros de período longo (decisão de 2026-07-06, ampliada dos ~90 dias
  originais).
- **Determinística:** semente fixa de aleatoriedade — rodar de novo num banco limpo gera
  a mesma massa.
- **Uso:** `dotnet run --project src/TikaumTech.Seed` (alvo padrão: o banco de
  desenvolvimento `src/TikaumTech/bin/Debug/net9.0/data/tikaum-dev.db`) ou
  `--db <caminho>` para outro arquivo. Nunca apontar para o banco de produção do estúdio.

---

## 12. Fora de Escopo

- API separada / React / TypeScript / frontend desacoplado
- JWT / OAuth2 / rotas versionadas `/api/v1/...`
- Papéis/permissões diferenciadas por usuário (todo usuário tem acesso total; ver seção 7)
- Emissão de nota fiscal
- Controle de estoque
- Integração com meios de pagamento

---

## 13. Identidade Visual (2026-07-03)

Os tokens e regras abaixo são **a fonte de verdade visual** (branding **Tikaum** — nunca
"InkHaus"). Os mockups/briefs que os originaram (`reference_visual.png`,
`tikaum_visual_prompt.md`) foram incorporados aqui e removidos do repositório
(2026-07-03/2026-07-05, a pedido do usuário — implementação concluída):

### Design tokens (CSS custom properties em `wwwroot/app.css`)
- Fundo `#111110`, superfícies `#1C1C1A`/`#242420`, borda `rgba(196,162,101,0.15)`
- Acento dourado `#C4A265` (+ dim `rgba(196,162,101,0.12)`)
- Texto `#E8DDC8` (primário) / `#7A7060` (secundário) / `#4A4640` (muted)
- Semânticas: sucesso `#1E3D28`/`#5DBF7A`, perigo `#3D1E1E`/`#BF5D5D`, aviso `#3D300A`/`#D4A017`
- **Zero branco ou cinza claro** em qualquer tela, modal, drawer, toast ou tooltip

### Tipografia (Google Fonts, com fallback local para uso offline)
- `Cinzel`: títulos, logo e valores de métricas; `Cinzel Decorative`: taglines decorativas
- `Inter`: corpo e UI; `JetBrains Mono`: valores monetários e datas

### Shell global (idêntico em todas as telas)
- **Sidebar fixa 240px**: logo (agulhas cruzadas SVG + TIKAUM em Cinzel + "TATTOO STUDIO" +
  tagline "ARTE NA PELE ◆ PARA SEMPRE"), navegação (Dashboard, Clientes, Produtos, Serviços,
  Vendas, Relatórios ─ Usuários, Backup, Perfil, Sair), skull realista com flores e borboletas
  (`images/skull_sidebar.png`, opacity 0.22, filtro sépia/dourado — substitui o SVG simples
  anterior em 2026-07-05) no rodapé. Item ativo: fundo `rgba(196,162,101,0.14)` + borda esquerda dourada.
  Colapsável (hamburger); em telas estreitas vira off-canvas.
  *Adaptação registrada:* o mockup lista "Perfil/Configurações"; o Tikaum-Tech mantém as telas
  reais já especificadas (Relatórios, Usuários, Backup) — Perfil = `/Account/Manage/ChangePassword`.
- **Topbar 64px**: hamburger + boas-vindas em duas linhas; à direita avatar circular com iniciais
  (borda dourada) + nome do usuário logado + chevron.
- **Rodapé decorativo em todas as páginas**: "TATTOO É EXPRESSÃO. ◆ SEU TRABALHO. ◆ SUA HISTÓRIA."
  em Cinzel Decorative + agulha SVG vertical + linha `--color-border`.

### Dashboard (`/`) — Revisão 2 (2026-07-05)
- Hero (`--color-surface`, gradiente): "Olá, {usuário}" + ⚡ dourado; **skull com flores**
  (`images/skull_sidebar.png`) como fundo sutil à direita (opacity 0.07, filtro sépia/dourado) —
  substitui o SVG de fênix/águia; subtítulo "Aqui está o resumo do seu estúdio hoje."
- **2 cards de métricas** (50%/50%): Clientes cadastrados e Vendas realizadas (**vendas conta
  só `status='active'`**) — cards de Produtos e Serviços removidos. **Revisão 3 (2026-07-05):**
  card compacto em linha única — ícone + rótulo/link à esquerda e o **valor à direita**
  (Cinzel 40px, alinhado à direita); acabou o card alto com área vazia sob o número
- **"Vendas do Dia"** (50%): vendas de hoje (`DataHora >= hoje 00:00`, `status='active'`,
  DESC), máx. 8 linhas, colunas Cliente/Itens/Total/Status (coluna Hora removida na revisão
  2026-07-05 — venda não registra mais hora), botão "Nova venda →" no header,
  rodapé com "Total do dia: R$ X | N vendas" (soma TODAS as vendas do dia, não só as 8 exibidas),
  estado vazio "Nenhuma venda hoje ainda."
- **"Vendas do Mês"** (50%): vendas do mês atual (`DataHora >= dia 1`, `status='active'`, DESC),
  máx. 8 linhas com scroll interno (max-height 420px), botão "Ver todas →" no header, rodapé com
  total do mês + quantidade + ticket médio, estado vazio "Nenhuma venda este mês."
- Rodapés dos painéis em JetBrains Mono, cor `--color-accent`
- "Acesso Rápido" removido — Nova venda/Ver todas vivem nos headers dos painéis; demais atalhos
  ficam na navegação lateral

### Componentes
- Botão primário: fundo dourado, texto `#111110`; secundário: borda/texto dourado, fundo transparente
- Inputs: fundo `--color-surface-alt`, foco com borda dourada + glow `rgba(196,162,101,0.2)`
- Modais: overlay `rgba(0,0,0,0.7)`, container `--color-surface` radius 12px, título em Cinzel
- Toasts: `--color-surface-alt` com borda esquerda 3px verde/vermelha/dourada, canto inferior direito
- Scrollbar fina (6px) com thumb dourado translúcido
- Badges de status em tabelas (ATIVO/INATIVO, tipo de serviço, EDITADO/EXCLUÍDO/AJUSTE em vendas)
- Estados vazios com ícone + mensagem "Nenhum(a) ... ainda."
- *Adaptação registrada:* o mockup genérico cita colunas Categoria/Estoque em Produtos e badge de
  estoque — fora de escopo (seção 12, "Controle de estoque"); Produtos usa os campos reais
  (Nome, Preço, Ativo) com badge de status.

MudBlazor continua sendo a base dos componentes (tabelas, diálogos, formulários, snackbar) — a
identidade é aplicada via `MudTheme` (paleta escura com os tokens acima) + CSS em `app.css`.
O shell (sidebar/topbar/rodapé) é HTML/CSS próprio, pois não há componente MudBlazor que
reproduza o layout do mockup.
