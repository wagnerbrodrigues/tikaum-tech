# Tikaum Tech

Sistema web **local** de controle de atendimentos para estúdio de tatuagem e piercing:
clientes, produtos, serviços, vendas, relatórios e backups automáticos. Roda como um
processo na própria máquina do estúdio (Windows ou Linux) e é acessado pelo navegador
em **`http://localhost:5000`** — sem internet, sem servidor externo, sem mensalidade.

> **Projeto spec-first — código gerado.**
> Toda necessidade deve estar descrita em `TIKAUM_SPEC.md`, `CLAUDE.md` ou `README.md`
> **antes** de ser implementada. O código é consequência da especificação. Para pedir
> uma feature nova ao assistente de IA, descreva-a na spec primeiro.

## Índice

- [Funcionalidades](#funcionalidades)
- [Stack](#stack)
- [Rodando em desenvolvimento](#rodando-em-desenvolvimento)
- [Instalação em produção (Windows)](#instalação-em-produção-windows)
- [Rodando no Linux](#rodando-no-linux)
- [Primeiro login](#primeiro-login)
- [Backup](#backup)
- [Configuração](#configuração)
- [Estrutura do repositório](#estrutura-do-repositório)
- [Comandos úteis](#comandos-úteis)
- [Solução de problemas](#solução-de-problemas)
- [Documentação e convenções](#documentação-e-convenções)

---

## Funcionalidades

| Tela | O que faz |
|------|-----------|
| **Dashboard** | Visão do dia: clientes cadastrados, vendas realizadas, vendas do dia e do mês |
| **Clientes** | Cadastro com nome, data de nascimento, celular e CPF (validado quando preenchido; distingue homônimos na busca) |
| **Produtos / Serviços** | Catálogo com preço e ativação/desativação |
| **Vendas** | Lançamento com busca incremental em todos os campos; itens de produto, serviço ou descrição livre; valor unitário pré-preenchido mas editável; registra a data (sem hora) e **quem vendeu** |
| **Relatórios** | Abas de **Vendas** e **Aniversariantes** do período, com exportação para Excel e mensagem de parabéns pronta para o aniversariante |
| **Backup** | Pen drive + Google Drive, com disparo manual e automático |
| **Usuários** | Multiusuário sem papéis (todos com acesso total), gestão de senhas |

**Auditoria de vendas:** venda nunca é apagada nem alterada in-place. Editar desabilita
o registro original e cria um substituto vinculado; excluir apenas marca como excluída.
Todos os relatórios consideram só as vendas ativas, mas o histórico completo permanece
no banco.

**Identidade visual:** tema escuro dark/gold próprio (tokens normativos em
`TIKAUM_SPEC.md` §13). Nenhuma tela usa fundo claro.

## Stack

| Camada | Tecnologia |
|--------|-----------|
| UI | Blazor Server (.NET 9) + MudBlazor 9.6 |
| Autenticação | ASP.NET Core Identity (cookie de sessão, sem JWT) |
| Banco | SQLite via EF Core 9 (único banco suportado — escala de ~1000 pessoas) |
| Exportação | ClosedXML (Excel) |
| Backup em nuvem | Google Drive API (OAuth `drive.file`) |

---

## Rodando em desenvolvimento

Pré-requisito: [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9).
O SQLite vem embutido via NuGet — não precisa instalar nada além do SDK.

```bash
git clone <url-do-repo> tikaum-tech
cd tikaum-tech
dotnet run --project src/TikaumTech
```

Acesse `http://localhost:5000` (o app também tenta abrir o navegador sozinho assim
que o servidor sobe).

**Onde fica o banco:** o diretório de trabalho é ancorado na pasta do executável,
então `data/…` resolve para `src/TikaumTech/bin/<Config>/net9.0/data/` ao rodar via
SDK, e para a pasta da instalação em produção. Banco de dev: `data/tikaum-dev.db`;
produção: `data/tikaum.db` — ambos criados (com migrations aplicadas) na primeira
execução.

### Massa de testes (homologação)

Popula o banco com dados realistas: clientes com homônimos e cadastros incompletos,
produtos/serviços com inativos, ~50 vendas nos últimos 90 dias incluindo avulsas,
itens livres e histórico de edição/exclusão.

```bash
dotnet run --project src/TikaumTech.Seed              # banco dev padrão
dotnet run --project src/TikaumTech.Seed -- --db x.db # outro arquivo
```

**Só funciona em banco vazio** — havendo qualquer cadastro ou venda, aborta sem tocar
em nada. Para re-semear, apague o `.db` e rode de novo. Nunca aponte para o banco de
produção do estúdio. Detalhes em `TIKAUM_SPEC.md` §11.

---

## Instalação em produção (Windows)

**Caminho padrão (git pull direto na máquina do estúdio):** o repositório é clonado ali
mesmo, e o build acontece localmente antes de instalar/atualizar.

```bat
REM No computador do estúdio, dentro do clone do repositório:
git pull
build_release.bat   REM verifica/instala o SDK se faltar, gera publish\ com os dois .bat dentro

cd publish
install.bat   REM PRIMEIRA instalação
update.bat    REM ATUALIZAÇÕES (preserva todos os dados)
```

**Alternativa (pacote preparado numa máquina separada):** se o build acontecer longe do
computador do estúdio (ex.: numa máquina Linux de desenvolvimento), o `.NET` publica
`win-x64` a partir do Linux — pacote idêntico:

```bash
./build_release.sh                        # gera publish/ com install.bat e update.bat
./build_release.sh /media/user/PENDRIVE   # idem, e já copia o pacote para o pen drive
                                          #   (deploy: TikaumTech_AAAA-MM-DD/)
```

Leve o pacote ao computador do estúdio (pen drive, rede) e rode `install.bat`/`update.bat`
de dentro dele, como Administrador.

O `install.bat` (primeira instalação) faz automaticamente:

1. Copia os arquivos para `C:\TikaumTech\` (nunca inclui `data\`/`config\` da origem)
2. Coleta a senha do administrador (input mascarado; vale para o usuário `admin`)
3. Adiciona `tikaum-tech.local` ao arquivo hosts (endereço alternativo)
4. Cria tarefa no Agendador de Tarefas — o sistema inicia junto com o logon do Windows
5. Cria atalho na área de trabalho e abre o app + navegador em `http://localhost:5000`

Se já houver instalação, o `install.bat` avisa e sugere o `update.bat`, que atualiza
preservando tudo: para o app, faz backup do banco (`data\tikaum_pre_update_AAAAMMDD.db`)
antes de copiar, nunca toca em `data\` (banco e backups) nem `config\` (credenciais do
Google Drive), confere que o banco sobreviveu à cópia e reinicia o sistema.

## Rodando no Linux

```bash
./start_linux.sh          # modo produção (roda em segundo plano, abre o navegador)
./start_linux.sh --dev    # modo desenvolvimento
./stop_linux.sh           # encerra a instância iniciada em segundo plano
```

Ou publique um binário fixo e execute direto:

```bash
./build_release.sh --linux        # gera publish-linux/ (com install.sh dentro)
./publish-linux/TikaumTech        # executar direto, ou: sudo ./publish-linux/install.sh
```

**Pen drive de backup no Linux:** basta montar o volume (clicar nele no gerenciador de
arquivos). A detecção procura em `/media/<usuário>/` e `/run/media/<usuário>/`, usando
o nome da pasta de montagem como nome do volume. Detalhes em `TIKAUM_SPEC.md` §10.

---

## Primeiro login

Dois usuários padrão são garantidos a cada start — cada um é criado **apenas se ainda
não existir**; senha de conta existente nunca é alterada:

| Usuário | Senha padrão | Papel |
|---------|-------------|-------|
| `admin` | `admin` | gestão |
| `tikaum` | `admin` | profissional do estúdio — pré-selecionado no lançamento de vendas |

No fluxo do `install.bat`, a senha digitada na instalação vale para o `admin`
(via `data/setup.json`, apagado após o primeiro start).

> ⚠️ **Troque as senhas padrão na tela `/usuarios` assim que possível** — os valores
> acima são públicos (estão neste repositório).

O login **não** oferece "Lembrar-me": a sessão usa cookie não persistente e termina
quando o navegador é fechado de verdade (decisão de 2026-07-06 — terminal compartilhado).

---

## Backup

Snapshot consistente do banco via `VACUUM INTO` (nunca cópia do `.db` em uso).
Cada backup de cada dia vive na **sua própria subpasta**:

```
TikaumBackup/
└── 2026-07-06/
    └── tikaum_2026-07-06.db
```

Um backup por dia — rodar de novo no mesmo dia sobrescreve o arquivo daquele dia.
**Retenção de 120 dias por destino** (pen drive e Drive contam separadamente); a
retenção apaga a subpasta inteira do dia expirado.

- **Disparo automático:** ~20s após o app subir e a cada 24h, um serviço em background
  faz backup para os destinos disponíveis (pen drive conectado / Google Drive conectado).
- **Disparo manual:** botão "Fazer backup agora" na tela **Backup**, como reforço.
- **Aviso permanente:** um banner fixo em todas as telas alerta enquanto o pen drive
  configurado não estiver conectado (vermelho) ou o Google Drive não estiver
  configurado (amarelo). O [×] fecha só até a próxima navegação — indisponibilidade
  de backup nunca falha silenciosamente.

> **Nunca coloque o `.db` ativo em pasta sincronizada** (Google Drive Desktop, OneDrive,
> Dropbox) — corrompe o banco. O backup para o Drive é sempre um snapshot estático
> enviado pela API.

### Pen drive

Configure o nome do volume na tela **Backup** (padrão: `TIKAUM_BACKUP`, ajustável em
tempo de execução — salvo em `data/backups/runtime_config.json`). Os arquivos vão para
`[PenDrive]\TikaumBackup\<data>\`.

### Google Drive

Snapshot enviado para a pasta `TikaumBackup` no Drive da conta configurada
(`GoogleDriveAccountEmail` no `appsettings.json`).

**Configuração (uma única vez, na máquina do estúdio):**

1. Acesse `console.cloud.google.com` logado na conta do estúdio e crie um projeto
   (ex.: "TikaumTech").
2. Em *APIs e serviços → Biblioteca*, ative a **Google Drive API**.
3. Em *Tela de permissão OAuth*, configure como **Externo** e adicione o e-mail da
   conta como usuário de teste.
4. Em *Credenciais*, crie um **ID do cliente OAuth** do tipo **"Aplicativo para
   computador"** e baixe o JSON.
5. Salve o arquivo como `C:\TikaumTech\config\credentials.json`.
6. Na tela **Backup**, clique em **Conectar Google Drive** — o navegador abre para
   autorizar (uma única vez). Depois use **Testar conexão** para confirmar.

**Segurança:** a senha da conta Google **nunca** é digitada nem armazenada no sistema.
A autorização gera um token OAuth de escopo mínimo (`drive.file` — o app só enxerga
arquivos criados por ele), gravado **criptografado** em `config/google_token_*.enc`
via ASP.NET Data Protection; no Windows, as chaves (`config/keys/`) são protegidas
adicionalmente com DPAPI, vinculadas à conta do Windows da máquina. O `install.bat` e o
`update.bat` preservam a pasta `config\` — não é preciso reconectar após atualizar.

### Restaurar um backup

Na tela **Backup**, seção **Restaurar Backup**: escolha a origem (Google Drive ou pen
drive), selecione o arquivo na lista (mais recente primeiro) e confirme — o snapshot fica
agendado e a troca acontece no **próximo início do sistema** (feche e abra o TikaumTech).
O banco substituído é preservado como `data/tikaum_pre_restore_[data].db`; um toast
confirma a restauração após o reinício. Atenção: restaurar substitui todos os dados
atuais — faça um backup antes (há um botão para isso na própria seção).

### Recuperação total (computador formatado ou trocado)

A seção acima (**Restaurar um backup**) parte de uma instalação já funcionando — ela só
troca o banco. Se o computador do estúdio for formatado, quebrar, ou os dados precisarem
ir para uma máquina nova (o app em si não existe mais ali), o caminho é:

1. **Instale o TikaumTech do zero** na máquina nova: `install.bat` (como Administrador) a
   partir do pacote gerado por `build_release.bat`/`build_release.sh` na máquina do
   desenvolvedor — ver [Instalação em produção](#instalação-em-produção-windows). Isso cria
   um banco vazio e os usuários padrão (`admin`/`admin`, `tikaum`/`admin`).
2. **Disponibilize a origem do backup:**
   - **Pen drive:** basta conectar o pen drive `TIKAUM_BACKUP` (ou o nome configurado) —
     os arquivos já estão nele, nada a reconfigurar.
   - **Google Drive:** o token de acesso fica em `config/` (criptografado, e no Windows
     vinculado à conta do Windows via DPAPI) — **não é copiado em nenhum backup, de
     propósito** (é credencial, não dado do negócio). Numa máquina nova, refaça a conexão:
     salve `credentials.json` de novo em `C:\TikaumTech\config\` (guarde uma cópia dele
     fora da máquina — ex. no Google Drive da própria conta, ou anexado a este
     repositório fora do controle de versão — senão será preciso gerar um novo Client ID
     em `console.cloud.google.com`) e clique em **Conectar Google Drive** na tela
     **Backup** para autorizar de novo.
3. **Restaure o backup mais recente:** abra `http://localhost:5000`, faça login e siga
   **Restaurar um backup** normalmente (acima) — liste os backups da origem disponível e
   escolha o mais recente. Feche e abra o TikaumTech de novo para aplicar.
4. **Confira os dados** (contagem de clientes/vendas em `/relatorios`, por exemplo) antes
   de voltar a usar o sistema no dia a dia.

O pen drive é a via mais simples para este cenário — não depende de reconectar nenhuma
credencial. Por isso vale manter o pen drive de backup fisicamente separado do computador
do estúdio (numa gaveta, na casa de alguém de confiança), não preso à máquina o tempo
todo — um incêndio ou roubo que leva o computador não pode levar o único backup junto.

---

## Configuração

`src/TikaumTech/appsettings.json` (valores reais do projeto):

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

`PenDriveVolumeName` pode ser alterado sem reiniciar, pela tela **Backup**
(`data/backups/runtime_config.json` sobrescreve o valor do appsettings).
Em desenvolvimento, `appsettings.Development.json` troca o banco para
`data/tikaum-dev.db`.

### Endereço alternativo `tikaum-tech.local`

O endereço padrão (atalho e auto-abertura) é `http://localhost:5000` — funciona sempre,
sem depender do arquivo hosts. O alias `http://tikaum-tech.local:5000` continua
disponível se estiver no hosts (o `install.bat` configura), mas não é o caminho padrão
— veja [Solução de problemas](#solução-de-problemas).

```
# Windows: C:\Windows\System32\drivers\etc\hosts
127.0.0.1   tikaum-tech.local
```

### Banco vindo da versão Python (legado)

Um `data/tikaum.db` da versão Python antiga já tem as tabelas de domínio criadas — a
migration `AddDomainTables` **não** deve ser reaplicada. Marque-a como aplicada, uma
única vez:

```sql
INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
VALUES ('<id-real-da-migration>', '9.0.0');
```

Substitua `<id-real-da-migration>` pelo nome do arquivo em `Data/Migrations/`
(ex.: `20250101000001_AddDomainTables`). A migration de Identity aplica normalmente.

---

## Estrutura do repositório

```
tikaum-tech/
├── src/
│   ├── TikaumTech/             # Projeto Blazor Server (único projeto da aplicação)
│   │   ├── Components/
│   │   │   ├── Layout/         # Shell da UI: sidebar, topbar, BackupBanner
│   │   │   ├── Pages/          # Dashboard, Clientes, Produtos, Serviços,
│   │   │   │                   #   Vendas, Relatórios, Backup, Usuários
│   │   │   └── Account/        # Login/logout (ASP.NET Core Identity)
│   │   ├── Data/               # ApplicationDbContext + Migrations/
│   │   ├── Models/             # Pessoa, Produto, Servico, Venda, ItemVenda…
│   │   ├── Services/           # PessoaService, VendaService, RelatorioService,
│   │   │                       #   BackupService, BackupAutomaticoService…
│   │   └── wwwroot/            # app.css, js/, images/ (skull da identidade visual)
│   ├── TikaumTech.Seed/        # Massa de testes (só roda em banco vazio)
│   └── TikaumTech.Tests/       # Testes automatizados
├── start_linux.sh              # Iniciar no Linux (segundo plano + navegador)
├── stop_linux.sh               # Parar a instância iniciada pelo start_linux.sh
├── build_release.sh            # Dev Linux: publica win-x64 (ou --linux) e monta o pacote
├── build_release.bat           # Dev Windows: idem (gera publish\ + install/update.bat)
├── install.bat                 # Estúdio: PRIMEIRA instalação (roda de dentro do pacote)
├── update.bat                  # Estúdio: ATUALIZAÇÃO preservando dados (idem)
├── TIKAUM_SPEC.md              # Especificação completa (fonte da verdade)
├── STATUS.md                   # Estado atual do desenvolvimento
└── CLAUDE.md                   # Decisões e convenções para o assistente de IA

# data/ (banco + backups) e config/ (credenciais) ficam ao lado do executável,
# não na raiz do repo:
#   dev: src/TikaumTech/bin/<Config>/net9.0/   ·   prod Windows: C:\TikaumTech\
```

## Comandos úteis

```bash
# Rodar em dev
dotnet run --project src/TikaumTech

# Build
dotnet build src/TikaumTech

# Testes
dotnet test src/TikaumTech.Tests

# Publicar para Windows (self-contained; funciona também a partir do Linux)
dotnet publish src/TikaumTech -c Release -r win-x64 --self-contained -o publish/

# Adicionar migration
dotnet ef migrations add NomeDaMigration \
    --project src/TikaumTech --startup-project src/TikaumTech

# Aplicar migrations (também aplicadas automaticamente no start do app)
dotnet ef database update --project src/TikaumTech

# Inspecionar o banco manualmente
sqlite3 data/tikaum.db
```

Em Linux mínimo (sem libicu), prefixe os comandos `dotnet` com
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` — veja
[Solução de problemas](#solução-de-problemas).

---

## Solução de problemas

**"Não foi possível encontrar tikaum-tech.local" com o app no ar.**
Use `http://localhost:5000`. O sufixo `.local` é reservado para mDNS, e
Chrome/Windows às vezes resolvem fora do arquivo hosts. Por isso o atalho e a
auto-abertura usam `localhost` desde 2026-07-03.

**O app abre já autenticado, sem tela de login.**
O cookie é de sessão e morre quando o navegador fecha **de verdade**. Navegadores que
mantêm processo em segundo plano (ex.: Edge com "continuar executando aplicativos em
segundo plano" ligado) preservam a sessão mesmo com a janela fechada — desative essa
opção no navegador; não é configuração do app.

**Valores monetários aparecem como `¤ 100.00` e/ou datas em formato americano
(`07/08/2026` para 8 de julho) no Linux.**
O .NET está em modo de globalização invariante. Duas causas possíveis: (1) a biblioteca
ICU realmente não está instalada — instale o pacote `libicu` da sua distribuição; ou
(2) você está numa versão do `start_linux.sh` anterior a 2026-07-08, cuja detecção de
libicu dependia de `ldconfig` no PATH (no Debian ele fica em `/sbin`, fora do PATH de
usuário comum) e por isso ativava o modo invariante mesmo com libicu presente —
atualize o script e reinicie com `./stop_linux.sh && ./start_linux.sh`. Como paliativo
em máquinas genuinamente sem ICU, o app roda normalmente com
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, apenas sem o símbolo `R$` (as datas nas
telas continuam dd/MM/yyyy, fixado no código).

**Banner vermelho "pen drive não conectado" em todas as telas.**
Comportamento intencional: conecte/monte o pen drive configurado (nome do volume na
tela **Backup**) ou ajuste o nome do volume. O aviso amarelo equivalente indica
Google Drive ainda não conectado.

**Pen drive conectado mas não detectado (Linux).**
O volume precisa estar **montado** (clicar nele no gerenciador de arquivos basta).
A detecção usa `/media/<usuário>/` e `/run/media/<usuário>/`.

**Instalei uma versão nova e os dados sumiram?**
Não somem: tanto o `update.bat` (caminho recomendado — ainda faz backup
`tikaum_pre_update_AAAAMMDD.db` antes de copiar) quanto o `install.bat` preservam
`data\` (banco + backups) e `config\` (credenciais do Drive). Se algo parecer
faltando, confira `C:\TikaumTech\data\`.

---

## Documentação e convenções

| Arquivo | Propósito |
|---------|-----------|
| `TIKAUM_SPEC.md` | Especificação completa: modelo de dados, telas, regras, stack |
| `CLAUDE.md` | Decisões arquiteturais fechadas e convenções para o assistente de IA |
| `STATUS.md` | Estado atual do desenvolvimento — sempre atualizado |
| `README.md` | Este arquivo: setup, deploy e operação, para humanos |

Convenções essenciais (lista completa e decisões fechadas em `CLAUDE.md`):

- Nomes de domínio em **português sem acentos** nos identificadores (`Servico`,
  `PessoaId`) — acentos só em texto de UI.
- A UI chama `Pessoa` de **"Cliente"** em todo texto visível; classe, tabela e rotas
  mantêm o nome original.
- MudBlazor para todos os componentes visuais; tema escuro dark/gold — nunca
  introduzir fundo claro.
- Busca incremental em todo campo de seleção — filtra a cada tecla, sem Enter.
- Não adicionar dependências NuGet silenciosamente — sempre avisar no resumo da
  mudança.
