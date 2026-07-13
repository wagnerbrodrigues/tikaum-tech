using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using TikaumTech.Components;
using TikaumTech.Components.Account;
using TikaumTech.Data;
using TikaumTech.Services;

// Ancora o diretório de trabalho na pasta do executável, independente de
// como o processo foi iniciado (Tarefa Agendada, atalho da área de
// trabalho, ou install.bat via "Executar como administrador" — a elevação
// do UAC troca o diretório de trabalho para C:\Windows\System32). Sem
// isso, appsettings.json e os caminhos relativos "data/tikaum.db" e
// "data/backups" resolvem contra o CWD herdado, não contra C:\TikaumTech.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

// Fixa pt-BR como cultura do app inteiro, independente do locale da máquina (LANG=C,
// en_US etc. deixavam datas MM/dd e formatação errada em qualquer componente que usa a
// cultura corrente, como o MudDatePicker). Com ICU presente isso garante R$ e dd/MM em
// todas as telas; em modo invariante (sem libicu) a cultura "pt-BR" existe mas carrega
// dados invariantes (moeda ¤, MM/dd) — por isso os MudDatePicker também fixam
// DateFormat="dd/MM/yyyy" e a detecção de libicu do start_linux.sh precisa ser precisa.
var ptBR = new System.Globalization.CultureInfo("pt-BR");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = ptBR;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = ptBR;

var builder = WebApplication.CreateBuilder(args);

// Rodar em Production SEM publish (caso do start_linux.sh, que usa `dotnet run`) servia
// CSS/JS com 200 e corpo VAZIO — interface "crua", só os controles nativos: fora de
// Development o host não carrega o manifesto de static web assets do build, e os
// endpoints do MapStaticAssets ficam sem conteúdo. A chamada é no-op quando o manifesto
// não existe (app publicado — instalação do estúdio — segue servindo do wwwroot/ local).
builder.WebHost.UseStaticWebAssets();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Data Protection com chaves persistidas em config/keys — usadas para criptografar o
// token OAuth do Google Drive em repouso (TIKAUM_SPEC.md §8). No Windows as chaves são
// protegidas adicionalmente com DPAPI (vinculadas à conta do Windows da máquina).
var dataProtectionKeysDir = Path.Combine(builder.Environment.ContentRootPath, "config", "keys");
Directory.CreateDirectory(dataProtectionKeysDir);
var dataProtectionBuilder = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDir))
    .SetApplicationName("TikaumTech");
if (OperatingSystem.IsWindows())
    dataProtectionBuilder.ProtectKeysWithDpapi();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
})
.AddIdentityCookies(opt =>
    opt.ApplicationCookie!.Configure(c =>
        c.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 4;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddSignInManager()
.AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddMudServices(config =>
{
    // Toasts no canto inferior direito, empilhados (TIKAUM_SPEC.md §13)
    config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomRight;
});
builder.Services.AddScoped<PessoaService>();
builder.Services.AddScoped<ProdutoService>();
builder.Services.AddScoped<ServicoService>();
builder.Services.AddScoped<VendaService>();
builder.Services.AddScoped<RelatorioService>();
builder.Services.AddScoped<UsuarioService>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService<BackupAutomaticoService>();

var app = builder.Build();

// Aplica migrations e processa setup de primeiro uso
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // Localiza data/setup.json no mesmo diretório do banco de dados
    var dbSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
    var dataDir = Path.GetDirectoryName(Path.GetFullPath(dbSource)) ?? ".";
    var setupPath = Path.Combine(dataDir, "setup.json");

    // SQLite não cria o diretório sozinho — precisa existir antes de abrir a conexão
    Directory.CreateDirectory(dataDir);

    // Restore pendente (agendado pela tela /backup) é aplicado AQUI, antes das migrations
    // e de qualquer conexão: o banco atual vira tikaum_pre_restore_TIMESTAMP.db e o
    // snapshot baixado assume como tikaum.db (TIKAUM_SPEC.md §9).
    //
    // Guarda de instância dupla: uma segunda instância só morre lá em app.Run() (porta
    // ocupada) — tarde demais para o restore. Se outra instância está com o banco aberto,
    // trocar os arquivos aqui daria exceção de arquivo em uso (Windows) ou trocaria o
    // banco POR BAIXO da instância ativa (Linux). Porta já respondendo = deixa o restore
    // pendente para o próximo start de verdade (verificado em teste real: sem a guarda,
    // a segunda instância aplicava o restore com a primeira no ar).
    var backupService = app.Services.GetRequiredService<BackupService>();
    if (backupService.RestorePendente)
    {
        if (OutraInstanciaNoAr(app.Configuration["Urls"]))
        {
            app.Logger.LogWarning(
                "Restauração pendente NÃO aplicada: já existe uma instância do TikaumTech em " +
                "execução. Feche o TikaumTech por completo e abra de novo para completar a restauração.");
        }
        else
        {
            try
            {
                var (restoreAplicado, restoreMensagem) = backupService.AplicarRestorePendente();
                if (restoreAplicado)
                    app.Logger.LogWarning("{Mensagem}", restoreMensagem);
            }
            catch (Exception ex)
            {
                // Nunca impedir o app de subir por causa do restore: o banco atual continua
                // em uso e a restauração segue pendente (o banner continua avisando).
                app.Logger.LogError(ex,
                    "Falha ao aplicar a restauração pendente — o banco atual permanece em uso.");
            }
        }
    }

    db.Database.Migrate();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL");

    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    const string senhaPadrao = "admin";

    // A senha de data/setup.json (gravada pelo install.bat) vale só para o 'admin'
    var senhaAdmin = senhaPadrao;
    if (File.Exists(setupPath))
    {
        var json = await File.ReadAllTextAsync(setupPath);
        using var doc = JsonDocument.Parse(json);
        var senhaSetup = doc.RootElement.TryGetProperty("AdminPassword", out var v) ? v.GetString() : null;
        if (!string.IsNullOrWhiteSpace(senhaSetup))
            senhaAdmin = senhaSetup;
    }

    // Usuários padrão (TIKAUM_SPEC.md §5 Identity): 'admin' (gestão) e 'tikaum'
    // (profissional do estúdio, pré-selecionado no lançamento de vendas). Cada um é
    // criado apenas se ainda não existir — a senha de contas existentes nunca é tocada.
    foreach (var (username, senha) in new[] { ("admin", senhaAdmin), ("tikaum", senhaPadrao) })
    {
        if (await userManager.FindByNameAsync(username) is not null) continue;

        var user = new ApplicationUser { UserName = username, Email = $"{username}@tikaum.local", EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, senha);

        if (!result.Succeeded)
        {
            var erros = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Falha ao criar usuário '{username}': {erros}");
        }

        app.Logger.LogInformation("Usuário '{User}' criado.", username);
    }

    if (File.Exists(setupPath))
        File.Delete(setupPath); // Senha não fica armazenada em disco após o primeiro uso
}

if (app.Environment.IsDevelopment())
    app.UseMigrationsEndPoint();
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapAdditionalIdentityEndpoints();

// Abre o navegador automaticamente em produção (instalado via install.bat).
// Usa ApplicationStarted em vez de um delay fixo: só abre quando o Kestrel
// já está de fato aceitando conexões, evitando "página não encontrada" em
// máquinas mais lentas onde a migration inicial demora mais que o esperado.
// localhost em vez de tikaum-tech.local: o alias depende do arquivo hosts e
// do resolvedor (.local é reservado para mDNS — Chrome/Windows às vezes o
// resolvem fora do hosts), o que causava "não foi possível encontrar" no
// navegador mesmo com o app no ar. localhost funciona sempre; o alias segue
// válido para quem digitar manualmente.
if (!app.Environment.IsDevelopment())
{
    app.Lifetime.ApplicationStarted.Register(() => AbrirNavegador(app.Logger, "http://localhost:5000"));
}

// Testa se a porta configurada já responde (outra instância no ar). Conexão recusada
// ou timeout curto = porta livre. Usado só como guarda do restore pendente, acima.
static bool OutraInstanciaNoAr(string? urls)
{
    try
    {
        var url = (urls ?? "http://localhost:5000").Split(';')[0].Trim();
        var uri = new Uri(url.Replace("*", "localhost").Replace("+", "localhost"));
        using var client = new System.Net.Sockets.TcpClient();
        return client.ConnectAsync(uri.Host, uri.Port).Wait(500) && client.Connected;
    }
    catch
    {
        return false; // conexão recusada = ninguém escutando
    }
}

// UseShellExecute só resolve URL→navegador no Windows; no Linux/macOS o equivalente
// é xdg-open/open. Num servidor sem interface gráfica a abertura falha — loga e segue.
static void AbrirNavegador(ILogger logger, string url)
{
    try
    {
        if (OperatingSystem.IsWindows())
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        else if (OperatingSystem.IsLinux())
            System.Diagnostics.Process.Start("xdg-open", url);
        else if (OperatingSystem.IsMacOS())
            System.Diagnostics.Process.Start("open", url);
    }
    catch (Exception ex)
    {
        logger.LogInformation("Não foi possível abrir o navegador automaticamente ({Motivo}). Acesse {Url} manualmente.",
            ex.Message, url);
    }
}

try
{
    app.Run();
}
catch (IOException ex) when (ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase))
{
    // Já existe uma instância do TikaumTech rodando (Tarefa Agendada no
    // logon + atalho da área de trabalho clicado por cima, por exemplo).
    // Antes isso derrubava o processo com um crash visível do Windows
    // (WerFault); agora a segunda instância, redundante, só encerra em
    // silêncio.
    app.Logger.LogInformation("Já existe uma instância do TikaumTech em execução nesta porta. Encerrando esta segunda instância.");
}
