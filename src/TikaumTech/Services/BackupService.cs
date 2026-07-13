using System.Globalization;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using TikaumTech.Models;

namespace TikaumTech.Services;

public class BackupConfig
{
    public string LocalDir { get; set; } = "data/backups";
    public string PenDriveVolumeName { get; set; } = "";
    public string GoogleDriveAccountEmail { get; set; } = "";
    public string GoogleDriveEmailConectado { get; set; } = "";
    public int RetencaoDias { get; set; } = 120;
}

/// <summary>Um arquivo de backup disponível para restauração (TIKAUM_SPEC.md §9).</summary>
/// <param name="Id">fileId no Google Drive; caminho completo no pen drive.</param>
public record BackupDisponivel(string Id, string Nome, DateTime? Modificado, long? TamanhoBytes);

/// <summary>Metadados do restore (gravados junto do pending e do concluído).</summary>
public record RestoreInfo(string Origem, string Backup, DateTime Quando);

public class BackupService(
    IConfiguration configuration,
    IWebHostEnvironment env,
    ILogger<BackupService> logger,
    IDataProtectionProvider dataProtection)
{
    private readonly string _contentRoot = env.ContentRootPath;
    private readonly string _connString = configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=data/tikaum.db";
    private readonly string _configPath = Path.Combine(env.ContentRootPath, "data", "backups", "runtime_config.json");
    private readonly string _statusPath = Path.Combine(env.ContentRootPath, "data", "backups", "backup_status.json");
    private readonly string _googleConfigDir = Path.Combine(env.ContentRootPath, "config");
    private static readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private const string PastaDriveNome = "TikaumBackup";

    // --- Configuração em tempo de execução ---

    public BackupConfig CarregarConfig()
    {
        // Mescla: appsettings → runtime_config.json (runtime sobrescreve)
        var cfg = configuration.GetSection("BackupConfig").Get<BackupConfig>() ?? new BackupConfig();
        var rt = LerRuntimeConfig();
        if (rt.TryGetValue("pen_drive_volume_name", out var v) && !string.IsNullOrWhiteSpace(v))
            cfg.PenDriveVolumeName = v;
        if (rt.TryGetValue("google_drive_email_conectado", out var g) && !string.IsNullOrWhiteSpace(g))
            cfg.GoogleDriveEmailConectado = g;
        return cfg;
    }

    private Dictionary<string, string> LerRuntimeConfig()
    {
        if (!File.Exists(_configPath)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_configPath)) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao ler {Path}, usando configuração padrão.", _configPath);
            return [];
        }
    }

    private void SalvarRuntimeConfig(string chave, string valor)
    {
        // Merge: preserva as demais chaves (pen drive e Google Drive convivem no mesmo arquivo)
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        var obj = LerRuntimeConfig();
        obj[chave] = valor;
        File.WriteAllText(_configPath, JsonSerializer.Serialize(obj, _jsonOpts));
    }

    public void SalvarVolumePenDrive(string volumeName) =>
        SalvarRuntimeConfig("pen_drive_volume_name", volumeName);

    // --- Detecção de pen drive ---

    public List<DriveInfo> ObterUnidadesRemoviveis()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return [.. DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Removable && d.IsReady)];

            // Linux: mídia removível montada pelo desktop aparece em /media/<usuário>/<RÓTULO>
            // ou /run/media/<usuário>/<RÓTULO>; DriveType.Removable quase nunca é reportado,
            // então o filtro é pelo ponto de montagem (TIKAUM_SPEC.md §10).
            return [.. DriveInfo.GetDrives()
                .Where(d => d.IsReady &&
                            (d.Name.StartsWith("/media/", StringComparison.Ordinal) ||
                             d.Name.StartsWith("/run/media/", StringComparison.Ordinal)))];
        }
        catch (Exception ex)
        {
            logger.LogWarning("Erro ao listar unidades removíveis: {Msg}", ex.Message);
            return [];
        }
    }

    /// <summary>
    /// "Nome do volume" multiplataforma: VolumeLabel no Windows; no Linux, o último
    /// segmento do ponto de montagem (que é o rótulo do volume nas montagens automáticas).
    /// </summary>
    public static string NomeVolume(DriveInfo drive) =>
        OperatingSystem.IsWindows()
            ? drive.VolumeLabel
            : Path.GetFileName(drive.Name.TrimEnd('/'));

    public bool PenDriveConfigurado => !string.IsNullOrWhiteSpace(CarregarConfig().PenDriveVolumeName);

    public bool GoogleDriveComErro =>
        CarregarStatus().GoogleDrive.Status == "erro";

    /// <summary>credentials.json presente em config/ (pré-requisito do OAuth).</summary>
    public bool GoogleDriveCredencialPresente => File.Exists(CredentialsPath);

    /// <summary>Token OAuth criptografado presente — conta já conectada.</summary>
    public bool GoogleDriveConfigurado =>
        Directory.Exists(_googleConfigDir) &&
        Directory.GetFiles(_googleConfigDir, EncryptedFileDataStore.FilePrefix + "*.enc").Length > 0;

    /// <summary>Exposto para a UI indicar onde salvar o credentials.json.</summary>
    public string CaminhoCredenciaisGoogle => CredentialsPath;

    private string CredentialsPath => Path.Combine(_googleConfigDir, "credentials.json");

    public DriveInfo? ObterPenDriveConfigurado()
    {
        var volumeName = CarregarConfig().PenDriveVolumeName;
        if (string.IsNullOrWhiteSpace(volumeName)) return null;
        return ObterUnidadesRemoviveis()
            .FirstOrDefault(d => NomeVolume(d).Equals(volumeName, StringComparison.OrdinalIgnoreCase));
    }

    public bool PenDriveConectado => ObterPenDriveConfigurado() is not null;

    // --- Status ---

    public BackupStatus CarregarStatus()
    {
        if (!File.Exists(_statusPath)) return new BackupStatus();
        try
        {
            return JsonSerializer.Deserialize<BackupStatus>(File.ReadAllText(_statusPath), _jsonOpts)
                ?? new BackupStatus();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao ler {Path}, retornando status vazio.", _statusPath);
            return new BackupStatus();
        }
    }

    private void SalvarStatus(BackupStatus status)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statusPath)!);
        File.WriteAllText(_statusPath, JsonSerializer.Serialize(status, _jsonOpts));
    }

    // --- Snapshot (VACUUM INTO) ---

    private string ResolverCaminhoDb()
    {
        var builder = new SqliteConnectionStringBuilder(_connString);
        var src = builder.DataSource;
        return Path.IsPathRooted(src) ? src : Path.Combine(_contentRoot, src);
    }

    public async Task<(bool ok, string mensagem)> FazerBackupPenDriveAsync()
    {
        var cfg = CarregarConfig();
        var drive = string.IsNullOrWhiteSpace(cfg.PenDriveVolumeName) ? null :
            ObterUnidadesRemoviveis()
                .FirstOrDefault(d => NomeVolume(d).Equals(cfg.PenDriveVolumeName, StringComparison.OrdinalIgnoreCase));

        if (drive is null)
        {
            var status = CarregarStatus();
            status.PenDrive.Status = "erro";
            status.PenDrive.MensagemErro = "Pen drive não encontrado.";
            SalvarStatus(status);
            return (false, "Pen drive não encontrado ou não conectado.");
        }

        var backupRaiz = Path.Combine(drive.RootDirectory.FullName, "TikaumBackup");
        var hoje = DateTime.Today.ToString("yyyy-MM-dd");
        var snapshotDir = Path.Combine(backupRaiz, hoje);
        Directory.CreateDirectory(snapshotDir);

        var snapshotPath = Path.Combine(snapshotDir, $"tikaum_{hoje}.db");

        try
        {
            await CriarSnapshotAsync(snapshotPath);

            // Retenção: remover pastas de dias mais antigos que RetencaoDias
            AplicarRetencao(backupRaiz, cfg.RetencaoDias);

            var status = CarregarStatus();
            status.PenDrive.UltimoOk = DateTime.Now;
            status.PenDrive.Status = "ok";
            status.PenDrive.MensagemErro = null;
            SalvarStatus(status);

            logger.LogInformation("Backup pen drive concluído: {Path}", snapshotPath);
            return (true, $"Backup salvo em {snapshotPath}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no backup pen drive");
            var status = CarregarStatus();
            status.PenDrive.Status = "erro";
            status.PenDrive.MensagemErro = ex.Message;
            SalvarStatus(status);
            return (false, $"Erro: {ex.Message}");
        }
    }

    private async Task CriarSnapshotAsync(string snapshotPath)
    {
        var sourceDb = ResolverCaminhoDb();
        if (!File.Exists(sourceDb))
            throw new FileNotFoundException($"Banco de dados não encontrado em: {sourceDb}");

        // VACUUM INTO não sobrescreve — remover se já existir (idempotente por dia)
        if (File.Exists(snapshotPath)) File.Delete(snapshotPath);

        await using var conn = new SqliteConnection($"Data Source={sourceDb}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"VACUUM INTO '{snapshotPath.Replace("'", "''")}'";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Cada backup vive na sua própria subpasta (nome = data, "AAAA-MM-DD"), para maior
    /// segurança/isolamento entre dias (decisão de 2026-07-06, substitui os arquivos soltos
    /// numa única pasta). Retenção apaga a subpasta inteira do dia expirado.
    /// </summary>
    public static void AplicarRetencao(string raizBackups, int dias)
    {
        if (!Directory.Exists(raizBackups)) return;
        var corte = DateTime.Today.AddDays(-dias);
        foreach (var pasta in Directory.GetDirectories(raizBackups))
        {
            if (DateTime.TryParseExact(Path.GetFileName(pasta), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var data) && data < corte)
            {
                Directory.Delete(pasta, recursive: true);
            }
        }
    }

    // --- Google Drive (TIKAUM_SPEC.md §8/§9) ---
    // A senha da conta Google NUNCA é armazenada nem usada: a API do Drive só aceita
    // OAuth2. O consentimento acontece uma única vez no navegador da máquina; o token
    // resultante fica criptografado em config/ (EncryptedFileDataStore).

    private async Task<DriveService> CriarDriveServiceAsync(CancellationToken ct)
    {
        await using var stream = File.OpenRead(CredentialsPath);
        var secrets = (await GoogleClientSecrets.FromStreamAsync(stream, ct)).Secrets;

        // Escopo mínimo: drive.file — o app só enxerga arquivos criados por ele
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets,
            [DriveService.Scope.DriveFile],
            "user",
            ct,
            new EncryptedFileDataStore(_googleConfigDir,
                dataProtection.CreateProtector("TikaumTech.GoogleDrive")));

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Tikaum-Tech",
        });
    }

    private static async Task<(string email, string uso)> ObterContaAsync(DriveService drive, CancellationToken ct)
    {
        var req = drive.About.Get();
        req.Fields = "user(emailAddress),storageQuota(usage,limit)";
        var about = await req.ExecuteAsync(ct);
        var email = about.User?.EmailAddress ?? "(desconhecido)";
        var uso = about.StorageQuota?.Usage is long u
            ? $"{u / 1_073_741_824.0:F1} GB usados" +
              (about.StorageQuota.Limit is long l ? $" de {l / 1_073_741_824.0:F0} GB" : "")
            : "";
        return (email, uso);
    }

    /// <summary>
    /// Dispara o consentimento OAuth (abre o navegador na primeira vez) e confere a
    /// conta conectada contra a esperada (BackupConfig.GoogleDriveAccountEmail).
    /// </summary>
    public async Task<(bool ok, string mensagem)> ConectarGoogleDriveAsync()
    {
        if (!GoogleDriveCredencialPresente)
            return (false, $"Arquivo de credenciais não encontrado: {CredentialsPath}. " +
                           "Siga as instruções da tela para baixá-lo do Google Cloud Console.");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var drive = await CriarDriveServiceAsync(cts.Token);
            var (email, _) = await ObterContaAsync(drive, cts.Token);

            SalvarRuntimeConfig("google_drive_email_conectado", email);

            var esperado = CarregarConfig().GoogleDriveAccountEmail;
            if (!string.IsNullOrWhiteSpace(esperado) &&
                !email.Equals(esperado, StringComparison.OrdinalIgnoreCase))
            {
                return (true, $"Conectado como {email} — atenção: a conta esperada era {esperado}. " +
                              "Desconecte e refaça o login com a conta correta se necessário.");
            }
            logger.LogInformation("Google Drive conectado: {Email}", email);
            return (true, $"Google Drive conectado com sucesso: {email}");
        }
        catch (OperationCanceledException)
        {
            return (false, "Tempo esgotado aguardando a autorização no navegador (3 minutos). Tente novamente.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao conectar Google Drive");
            return (false, $"Erro ao conectar: {ex.Message}");
        }
    }

    /// <summary>Prova de acesso: chama about.get e retorna conta + uso da cota.</summary>
    public async Task<(bool ok, string mensagem)> TestarConexaoGoogleDriveAsync()
    {
        if (!GoogleDriveConfigurado)
            return (false, "Google Drive ainda não conectado. Use \"Conectar Google Drive\" primeiro.");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var drive = await CriarDriveServiceAsync(cts.Token);
            var (email, uso) = await ObterContaAsync(drive, cts.Token);
            return (true, $"Acesso funcionando — conta {email}" +
                          (uso.Length > 0 ? $" ({uso})" : ""));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao testar conexão Google Drive");
            return (false, $"Falha no acesso: {ex.Message}");
        }
    }

    /// <summary>Apaga o token criptografado — exige novo consentimento para voltar a usar.</summary>
    public (bool ok, string mensagem) DesconectarGoogleDrive()
    {
        if (!GoogleDriveConfigurado) return (false, "Google Drive já está desconectado.");
        foreach (var file in Directory.GetFiles(_googleConfigDir, EncryptedFileDataStore.FilePrefix + "*.enc"))
            File.Delete(file);
        SalvarRuntimeConfig("google_drive_email_conectado", "");
        return (true, "Google Drive desconectado. O token de acesso foi removido.");
    }

    public async Task<(bool ok, string mensagem)> FazerBackupGoogleDriveAsync()
    {
        var cfg = CarregarConfig();
        var status = CarregarStatus();

        if (!GoogleDriveCredencialPresente || !GoogleDriveConfigurado)
        {
            status.GoogleDrive.Status = "nao_configurado";
            status.GoogleDrive.MensagemErro = null;
            SalvarStatus(status);
            return (false, "Google Drive não conectado. Configure na seção acima.");
        }

        var hoje = DateTime.Today.ToString("yyyy-MM-dd");
        var nomeArquivo = $"tikaum_{hoje}.db";
        var tempPath = Path.Combine(_contentRoot, "data", "backups", $"upload_{nomeArquivo}");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var drive = await CriarDriveServiceAsync(cts.Token);

            await CriarSnapshotAsync(tempPath);

            var pastaRaizId = await ObterOuCriarPastaAsync(drive, PastaDriveNome, null, cts.Token);
            var pastaDiaId = await ObterOuCriarPastaAsync(drive, hoje, pastaRaizId, cts.Token);
            await EnviarArquivoAsync(drive, pastaDiaId, nomeArquivo, tempPath, cts.Token);
            await AplicarRetencaoDriveAsync(drive, pastaRaizId, cfg.RetencaoDias, cts.Token);

            status = CarregarStatus();
            status.GoogleDrive.UltimoOk = DateTime.Now;
            status.GoogleDrive.Status = "ok";
            status.GoogleDrive.MensagemErro = null;
            SalvarStatus(status);

            logger.LogInformation("Backup Google Drive concluído: {Nome}", nomeArquivo);
            return (true, $"Backup enviado ao Google Drive: {PastaDriveNome}/{hoje}/{nomeArquivo}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no backup Google Drive");
            status = CarregarStatus();
            status.GoogleDrive.Status = "erro";
            status.GoogleDrive.MensagemErro = ex.Message;
            SalvarStatus(status);
            return (false, $"Erro no backup Google Drive: {ex.Message}");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Acha (ou cria) uma pasta pelo nome dentro do pai indicado (<paramref name="paiId"/> —
    /// null para a raiz do Drive). Usado tanto para a pasta raiz "TikaumBackup" quanto para a
    /// subpasta do dia dentro dela — cada backup fica isolado na sua própria subpasta
    /// (decisão de 2026-07-06).
    /// </summary>
    private static async Task<string> ObterOuCriarPastaAsync(
        DriveService drive, string nome, string? paiId, CancellationToken ct)
    {
        var paiQuery = paiId is null ? "'root' in parents" : $"'{paiId}' in parents";
        var busca = drive.Files.List();
        busca.Q = $"mimeType = 'application/vnd.google-apps.folder' and name = '{nome}' " +
                  $"and trashed = false and {paiQuery}";
        busca.Fields = "files(id)";
        var resultado = await busca.ExecuteAsync(ct);
        if (resultado.Files.Count > 0) return resultado.Files[0].Id;

        var pasta = new Google.Apis.Drive.v3.Data.File
        {
            Name = nome,
            MimeType = "application/vnd.google-apps.folder",
            Parents = paiId is null ? null : [paiId],
        };
        var criar = drive.Files.Create(pasta);
        criar.Fields = "id";
        return (await criar.ExecuteAsync(ct)).Id;
    }

    private static async Task EnviarArquivoAsync(
        DriveService drive, string pastaId, string nome, string caminhoLocal, CancellationToken ct)
    {
        // Idempotente por dia: se o arquivo de hoje já existe, atualiza o conteúdo
        var busca = drive.Files.List();
        busca.Q = $"'{pastaId}' in parents and name = '{nome}' and trashed = false";
        busca.Fields = "files(id)";
        var existentes = await busca.ExecuteAsync(ct);

        await using var stream = File.OpenRead(caminhoLocal);
        IUploadProgress progresso;
        if (existentes.Files.Count > 0)
        {
            var update = drive.Files.Update(new Google.Apis.Drive.v3.Data.File(),
                existentes.Files[0].Id, stream, "application/octet-stream");
            progresso = await update.UploadAsync(ct);
        }
        else
        {
            var meta = new Google.Apis.Drive.v3.Data.File { Name = nome, Parents = [pastaId] };
            var create = drive.Files.Create(meta, stream, "application/octet-stream");
            create.Fields = "id";
            progresso = await create.UploadAsync(ct);
        }

        if (progresso.Status != UploadStatus.Completed)
            throw progresso.Exception ?? new InvalidOperationException("Upload não concluído.");
    }

    /// <summary>
    /// Cada backup vive na sua própria subpasta do dia (nome "AAAA-MM-DD") dentro de
    /// "TikaumBackup" — a retenção apaga a subpasta inteira do dia expirado (cascata: o
    /// arquivo dentro dela também é removido, já que só existe ali).
    /// </summary>
    private static async Task AplicarRetencaoDriveAsync(
        DriveService drive, string pastaRaizId, int dias, CancellationToken ct)
    {
        var corte = DateTime.Today.AddDays(-dias);
        var busca = drive.Files.List();
        busca.Q = $"'{pastaRaizId}' in parents and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
        busca.Fields = "files(id,name)";
        var pastas = await busca.ExecuteAsync(ct);

        foreach (var pasta in pastas.Files)
        {
            if (DateTime.TryParseExact(pasta.Name, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var data) && data < corte)
            {
                await drive.Files.Delete(pasta.Id).ExecuteAsync(ct);
            }
        }
    }

    // --- Restore (TIKAUM_SPEC.md §9 — decisão de 2026-07-11) ---
    // O banco em uso nunca é substituído com o app rodando: a tela /backup só deixa o
    // snapshot escolhido em data/tikaum_restore_pending.db; a troca de verdade acontece
    // no próximo startup (AplicarRestorePendente, chamado pelo Program.cs ANTES das
    // migrations e de qualquer conexão com o banco).

    private string DataDir => Path.GetDirectoryName(ResolverCaminhoDb())!;

    /// <summary>Snapshot aguardando o próximo restart para virar o banco ativo.</summary>
    public string CaminhoRestorePendente => Path.Combine(DataDir, "tikaum_restore_pending.db");
    private string RestorePendenteInfoPath => Path.Combine(DataDir, "restore_pending.json");
    private string RestoreConcluidoInfoPath => Path.Combine(DataDir, "restore_done.json");

    public bool RestorePendente => File.Exists(CaminhoRestorePendente);

    public RestoreInfo? LerRestorePendenteInfo() => LerRestoreInfo(RestorePendenteInfoPath);
    public RestoreInfo? LerRestoreConcluidoInfo() => LerRestoreInfo(RestoreConcluidoInfoPath);

    /// <summary>Consumido pelo BackupBanner após exibir o toast pós-restauração.</summary>
    public void LimparRestoreConcluidoInfo()
    {
        if (File.Exists(RestoreConcluidoInfoPath)) File.Delete(RestoreConcluidoInfoPath);
    }

    private RestoreInfo? LerRestoreInfo(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<RestoreInfo>(File.ReadAllText(path), _jsonOpts);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao ler {Path}.", path);
            return null;
        }
    }

    public (bool ok, string mensagem) CancelarRestorePendente()
    {
        if (!RestorePendente) return (false, "Não há restauração pendente.");
        File.Delete(CaminhoRestorePendente);
        if (File.Exists(RestorePendenteInfoPath)) File.Delete(RestorePendenteInfoPath);
        return (true, "Restauração pendente cancelada. O banco atual permanece em uso.");
    }

    public async Task<(bool ok, string mensagem, List<BackupDisponivel> backups)> ListarBackupsGoogleDriveAsync()
    {
        if (!GoogleDriveCredencialPresente || !GoogleDriveConfigurado)
            return (false, "Google Drive não conectado. Configure na seção acima.", []);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var drive = await CriarDriveServiceAsync(cts.Token);

            // Uma busca só, sem descer pasta a pasta: o escopo drive.file limita a visão
            // aos arquivos criados pelo próprio app, então filtrar pelo padrão de nome dos
            // snapshots basta (cobre também backups antigos soltos direto em TikaumBackup/).
            var lista = new List<BackupDisponivel>();
            string? pageToken = null;
            do
            {
                var busca = drive.Files.List();
                busca.Q = "name contains 'tikaum_' and name contains '.db' and " +
                          "mimeType != 'application/vnd.google-apps.folder' and trashed = false";
                busca.Fields = "nextPageToken, files(id,name,size,modifiedTime)";
                busca.PageSize = 200;
                busca.PageToken = pageToken;
                var resultado = await busca.ExecuteAsync(cts.Token);
                lista.AddRange(resultado.Files.Select(f => new BackupDisponivel(
                    f.Id, f.Name, f.ModifiedTimeDateTimeOffset?.LocalDateTime, f.Size)));
                pageToken = resultado.NextPageToken;
            } while (pageToken is not null);

            // Nome desce = data desce (tikaum_AAAA-MM-DD.db) — mais recente primeiro
            return (true, "", [.. lista.OrderByDescending(b => b.Nome, StringComparer.Ordinal)
                                       .ThenByDescending(b => b.Modificado)]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao listar backups do Google Drive");
            return (false, $"Erro ao listar backups do Google Drive: {ex.Message}", []);
        }
    }

    public async Task<(bool ok, string mensagem)> BaixarBackupGoogleDriveAsync(string fileId)
    {
        var tempPath = CaminhoRestorePendente + ".part";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var drive = await CriarDriveServiceAsync(cts.Token);

            var metaReq = drive.Files.Get(fileId);
            metaReq.Fields = "name";
            var nome = (await metaReq.ExecuteAsync(cts.Token)).Name;

            Directory.CreateDirectory(DataDir);
            await using (var fs = File.Create(tempPath))
            {
                var download = await drive.Files.Get(fileId).DownloadAsync(fs, cts.Token);
                if (download.Status != DownloadStatus.Completed)
                    throw download.Exception ?? new InvalidOperationException("Download não concluído.");
            }

            ValidarArquivoSqlite(tempPath);
            File.Move(tempPath, CaminhoRestorePendente, overwrite: true);
            GravarRestorePendenteInfo("Google Drive", nome);

            logger.LogInformation("Restore agendado a partir do Google Drive: {Nome}", nome);
            return (true, $"Backup {nome} baixado. Reinicie o sistema para completar a restauração.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao baixar backup do Google Drive");
            return (false, $"Erro ao baixar backup: {ex.Message}");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public Task<(bool ok, string mensagem, List<BackupDisponivel> backups)> ListarBackupsPenDriveAsync() =>
        // Task.Run: enumerar um pen drive USB lento não deve travar o circuito Blazor
        Task.Run<(bool, string, List<BackupDisponivel>)>(() =>
        {
            var drive = ObterPenDriveConfigurado();
            if (drive is null)
                return (false, "Pen drive não encontrado ou não conectado.", []);

            var raiz = Path.Combine(drive.RootDirectory.FullName, "TikaumBackup");
            if (!Directory.Exists(raiz))
                return (false, $"Pasta TikaumBackup não encontrada no volume \"{NomeVolume(drive)}\".", []);

            try
            {
                // AllDirectories cobre o layout atual (subpasta por dia) e o legado (soltos)
                var backups = Directory.EnumerateFiles(raiz, "*.db", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f))
                    .Select(f => new BackupDisponivel(
                        f.FullName, f.Name, f.LastWriteTime, f.Length))
                    .OrderByDescending(b => b.Nome, StringComparer.Ordinal)
                    .ThenByDescending(b => b.Modificado)
                    .ToList();
                return (true, "", backups);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao listar backups do pen drive");
                return (false, $"Erro ao listar backups do pen drive: {ex.Message}", []);
            }
        });

    public Task<(bool ok, string mensagem)> CopiarBackupPenDriveAsync(string filePath) =>
        Task.Run<(bool, string)>(() =>
        {
            var tempPath = CaminhoRestorePendente + ".part";
            try
            {
                if (!File.Exists(filePath))
                    return (false, "Arquivo de backup não encontrado no pen drive.");

                ValidarArquivoSqlite(filePath);
                Directory.CreateDirectory(DataDir);
                File.Copy(filePath, tempPath, overwrite: true);
                File.Move(tempPath, CaminhoRestorePendente, overwrite: true);
                GravarRestorePendenteInfo("Pen drive", Path.GetFileName(filePath));

                logger.LogInformation("Restore agendado a partir do pen drive: {Path}", filePath);
                return (true, $"Backup {Path.GetFileName(filePath)} copiado. " +
                              "Reinicie o sistema para completar a restauração.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao copiar backup do pen drive");
                return (false, $"Erro ao copiar backup: {ex.Message}");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        });

    private void GravarRestorePendenteInfo(string origem, string backup)
    {
        File.WriteAllText(RestorePendenteInfoPath,
            JsonSerializer.Serialize(new RestoreInfo(origem, backup, DateTime.Now), _jsonOpts));
    }

    /// <summary>Barra cedo um arquivo que não é um banco SQLite (cabeçalho mágico).</summary>
    private static void ValidarArquivoSqlite(string path)
    {
        Span<byte> header = stackalloc byte[16];
        using var fs = File.OpenRead(path);
        if (fs.Read(header) != 16 ||
            !header.SequenceEqual("SQLite format 3\0"u8))
        {
            throw new InvalidOperationException(
                "O arquivo selecionado não é um banco de dados SQLite válido.");
        }
    }

    /// <summary>
    /// Chamado pelo Program.cs no startup, ANTES das migrations e de abrir qualquer conexão.
    /// Renomeia o banco atual para tikaum_pre_restore_TIMESTAMP.db (levando junto -wal/-shm,
    /// que pertencem a ele — deixá-los para trás faria o SQLite aplicar um WAL alheio sobre
    /// o banco restaurado) e promove tikaum_restore_pending.db a banco ativo. As migrations
    /// que rodam em seguida atualizam o snapshot restaurado para o schema corrente.
    /// </summary>
    public (bool aplicado, string mensagem) AplicarRestorePendente()
    {
        if (!RestorePendente) return (false, "");

        var dbPath = ResolverCaminhoDb();
        var info = LerRestorePendenteInfo();
        var preRestore = Path.Combine(DataDir,
            $"tikaum_pre_restore_{DateTime.Now:yyyyMMddHHmmss}.db");

        if (File.Exists(dbPath))
        {
            File.Move(dbPath, preRestore);
            foreach (var sufixo in new[] { "-wal", "-shm" })
                if (File.Exists(dbPath + sufixo))
                    File.Move(dbPath + sufixo, preRestore + sufixo);
        }
        File.Move(CaminhoRestorePendente, dbPath);

        if (File.Exists(RestorePendenteInfoPath)) File.Delete(RestorePendenteInfoPath);
        File.WriteAllText(RestoreConcluidoInfoPath, JsonSerializer.Serialize(
            new RestoreInfo(info?.Origem ?? "backup", info?.Backup ?? Path.GetFileName(dbPath),
                DateTime.Now), _jsonOpts));

        var mensagem = $"Banco restaurado de {info?.Origem ?? "backup"} ({info?.Backup ?? "?"}). " +
                       $"Banco anterior preservado como {Path.GetFileName(preRestore)}.";
        logger.LogWarning("RESTORE aplicado: {Mensagem}", mensagem);
        return (true, mensagem);
    }
}
