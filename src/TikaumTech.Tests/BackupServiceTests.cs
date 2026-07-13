using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using TikaumTech.Services;
using Xunit;

namespace TikaumTech.Tests;

public class BackupServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BackupService _svc;
    private readonly IDataProtectionProvider _dp;

    public BackupServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "tikaum_backup_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = $"Data Source={Path.Combine(_tempDir, "tikaum.db")}",
            ["BackupConfig:GoogleDriveAccountEmail"] = "tikaumtech@gmail.com",
            ["BackupConfig:RetencaoDias"] = "120",
        }).Build();

        _dp = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_tempDir, "dpkeys")));
        _svc = new BackupService(config, new FakeEnv(_tempDir), NullLogger<BackupService>.Instance, _dp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* melhor esforço */ }
    }

    // --- Retenção por pasta-do-dia (TIKAUM_SPEC.md §9, 2026-07-06) ---

    [Fact]
    public void AplicarRetencao_ApagaPastaDeDiaMaisAntigoQueRetencao()
    {
        var raiz = Path.Combine(_tempDir, "TikaumBackup");
        var pastaAntiga = Path.Combine(raiz, DateTime.Today.AddDays(-121).ToString("yyyy-MM-dd"));
        var pastaRecente = Path.Combine(raiz, DateTime.Today.AddDays(-10).ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(pastaAntiga);
        Directory.CreateDirectory(pastaRecente);
        File.WriteAllText(Path.Combine(pastaAntiga, "tikaum_x.db"), "x");
        File.WriteAllText(Path.Combine(pastaRecente, "tikaum_y.db"), "y");

        BackupService.AplicarRetencao(raiz, 120);

        Assert.False(Directory.Exists(pastaAntiga));
        Assert.True(Directory.Exists(pastaRecente));
    }

    [Fact]
    public void AplicarRetencao_IgnoraPastasComNomeQueNaoEDataEPastaRaizInexistente()
    {
        var raiz = Path.Combine(_tempDir, "TikaumBackup");
        Directory.CreateDirectory(raiz);
        var pastaNaoData = Path.Combine(raiz, "config-antiga");
        Directory.CreateDirectory(pastaNaoData);

        BackupService.AplicarRetencao(raiz, 120);
        Assert.True(Directory.Exists(pastaNaoData));

        // Não lança quando a pasta raiz ainda não existe (primeiro backup do estúdio)
        BackupService.AplicarRetencao(Path.Combine(_tempDir, "nunca-existiu"), 120);
    }

    [Fact]
    public void SalvarVolumePenDrive_PreservaOutrasChavesDoRuntimeConfig()
    {
        var runtimePath = Path.Combine(_tempDir, "data", "backups", "runtime_config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(runtimePath)!);
        File.WriteAllText(runtimePath,
            """{"google_drive_email_conectado":"tikaumtech@gmail.com"}""");

        _svc.SalvarVolumePenDrive("MEU_PENDRIVE");

        var cfg = _svc.CarregarConfig();
        Assert.Equal("MEU_PENDRIVE", cfg.PenDriveVolumeName);
        Assert.Equal("tikaumtech@gmail.com", cfg.GoogleDriveEmailConectado);
    }

    [Fact]
    public void GoogleDriveConfigurado_ReflitaPresencaDoTokenCriptografado()
    {
        Assert.False(_svc.GoogleDriveConfigurado);

        var configDir = Path.Combine(_tempDir, "config");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, EncryptedFileDataStore.FilePrefix + "teste.enc"), "x");

        Assert.True(_svc.GoogleDriveConfigurado);
    }

    [Fact]
    public void DesconectarGoogleDrive_RemoveTokenELimpaEmail()
    {
        var configDir = Path.Combine(_tempDir, "config");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, EncryptedFileDataStore.FilePrefix + "teste.enc"), "x");

        var (ok, _) = _svc.DesconectarGoogleDrive();

        Assert.True(ok);
        Assert.False(_svc.GoogleDriveConfigurado);
        Assert.Empty(_svc.CarregarConfig().GoogleDriveEmailConectado);
    }

    [Fact]
    public async Task FazerBackupGoogleDrive_SemConexao_FalhaComStatusNaoConfigurado()
    {
        var (ok, mensagem) = await _svc.FazerBackupGoogleDriveAsync();

        Assert.False(ok);
        Assert.Contains("não conectado", mensagem);
        Assert.Equal("nao_configurado", _svc.CarregarStatus().GoogleDrive.Status);
    }

    [Fact]
    public async Task TestarConexao_SemToken_OrientaConectarPrimeiro()
    {
        var (ok, mensagem) = await _svc.TestarConexaoGoogleDriveAsync();

        Assert.False(ok);
        Assert.Contains("Conectar", mensagem);
    }

    // --- Restore (TIKAUM_SPEC.md §9, 2026-07-11) ---

    private static readonly byte[] _sqliteHeader = "SQLite format 3\0"u8.ToArray();

    private string CriarArquivoSqliteFake(string caminho, string marcador)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllBytes(caminho, [.. _sqliteHeader, .. System.Text.Encoding.ASCII.GetBytes(marcador)]);
        return caminho;
    }

    [Fact]
    public async Task CopiarBackupPenDrive_AgendaRestoreComInfoECancelamentoLimpa()
    {
        var origem = CriarArquivoSqliteFake(Path.Combine(_tempDir, "pen", "tikaum_2026-07-01.db"), "backup");

        var (ok, mensagem) = await _svc.CopiarBackupPenDriveAsync(origem);

        Assert.True(ok);
        Assert.Contains("Reinicie o sistema", mensagem);
        Assert.True(_svc.RestorePendente);
        var info = _svc.LerRestorePendenteInfo();
        Assert.NotNull(info);
        Assert.Equal("Pen drive", info!.Origem);
        Assert.Equal("tikaum_2026-07-01.db", info.Backup);

        var (cancelou, _) = _svc.CancelarRestorePendente();
        Assert.True(cancelou);
        Assert.False(_svc.RestorePendente);
        Assert.Null(_svc.LerRestorePendenteInfo());
    }

    [Fact]
    public async Task CopiarBackupPenDrive_RejeitaArquivoQueNaoESqlite()
    {
        var origem = Path.Combine(_tempDir, "pen", "nao_e_banco.db");
        Directory.CreateDirectory(Path.GetDirectoryName(origem)!);
        File.WriteAllText(origem, "isto é um txt disfarçado");

        var (ok, mensagem) = await _svc.CopiarBackupPenDriveAsync(origem);

        Assert.False(ok);
        Assert.Contains("não é um banco de dados SQLite válido", mensagem);
        Assert.False(_svc.RestorePendente);
    }

    [Fact]
    public async Task AplicarRestorePendente_PromovePendingEPreservaBancoAnteriorComWalShm()
    {
        var dbPath = Path.Combine(_tempDir, "tikaum.db");
        CriarArquivoSqliteFake(dbPath, "banco-atual");
        File.WriteAllText(dbPath + "-wal", "wal-do-banco-atual");
        File.WriteAllText(dbPath + "-shm", "shm-do-banco-atual");
        var origem = CriarArquivoSqliteFake(Path.Combine(_tempDir, "pen", "tikaum_2026-06-15.db"), "banco-restaurado");
        await _svc.CopiarBackupPenDriveAsync(origem);

        var (aplicado, mensagem) = _svc.AplicarRestorePendente();

        Assert.True(aplicado);
        Assert.Contains("tikaum_pre_restore_", mensagem);
        // O snapshot virou o banco ativo…
        Assert.Contains("banco-restaurado", File.ReadAllText(dbPath));
        Assert.False(_svc.RestorePendente);
        // …o banco anterior sobreviveu como pre_restore, levando -wal/-shm junto
        // (um WAL órfão seria aplicado pelo SQLite sobre o banco restaurado — corrupção)
        var preRestore = Assert.Single(Directory.GetFiles(_tempDir, "tikaum_pre_restore_*.db"));
        Assert.Contains("banco-atual", File.ReadAllText(preRestore));
        Assert.True(File.Exists(preRestore + "-wal"));
        Assert.True(File.Exists(preRestore + "-shm"));
        Assert.False(File.Exists(dbPath + "-wal"));
        // Info pendente virou "concluído" (consumida pelo toast do BackupBanner)
        Assert.Null(_svc.LerRestorePendenteInfo());
        var concluido = _svc.LerRestoreConcluidoInfo();
        Assert.NotNull(concluido);
        Assert.Equal("Pen drive", concluido!.Origem);
        _svc.LimparRestoreConcluidoInfo();
        Assert.Null(_svc.LerRestoreConcluidoInfo());
    }

    [Fact]
    public void AplicarRestorePendente_SemPendencia_NaoFazNada()
    {
        var (aplicado, _) = _svc.AplicarRestorePendente();

        Assert.False(aplicado);
        Assert.Empty(Directory.GetFiles(_tempDir, "tikaum_pre_restore_*.db"));
    }

    [Fact]
    public async Task ListarBackupsPenDrive_SemPenDriveConfigurado_FalhaComMensagem()
    {
        var (ok, mensagem, backups) = await _svc.ListarBackupsPenDriveAsync();

        Assert.False(ok);
        Assert.Contains("Pen drive", mensagem);
        Assert.Empty(backups);
    }

    // --- EncryptedFileDataStore ---

    [Fact]
    public async Task EncryptedFileDataStore_RoundTrip_ENuncaGravaTokenEmTextoPlano()
    {
        var folder = Path.Combine(_tempDir, "config");
        var store = new EncryptedFileDataStore(folder, _dp.CreateProtector("teste"));
        var token = new TokenResponse
        {
            AccessToken = "segredo-access-token-123",
            RefreshToken = "segredo-refresh-token-456",
        };

        await store.StoreAsync("user", token);

        var arquivos = Directory.GetFiles(folder, EncryptedFileDataStore.FilePrefix + "*.enc");
        var arquivo = Assert.Single(arquivos);
        var conteudo = File.ReadAllText(arquivo);
        Assert.DoesNotContain("segredo-access-token-123", conteudo);
        Assert.DoesNotContain("segredo-refresh-token-456", conteudo);

        var lido = await store.GetAsync<TokenResponse>("user");
        Assert.Equal(token.AccessToken, lido.AccessToken);
        Assert.Equal(token.RefreshToken, lido.RefreshToken);
    }

    [Fact]
    public async Task EncryptedFileDataStore_Get_TokenIlegivel_RetornaDefault()
    {
        var folder = Path.Combine(_tempDir, "config");
        var store = new EncryptedFileDataStore(folder, _dp.CreateProtector("teste"));
        await store.StoreAsync("user", new TokenResponse { AccessToken = "abc" });

        // Corrompe o arquivo (simula chave trocada) — deve virar "não conectado", não exceção
        var arquivo = Directory.GetFiles(folder, EncryptedFileDataStore.FilePrefix + "*.enc").Single();
        File.WriteAllText(arquivo, "conteudo-invalido");

        var lido = await store.GetAsync<TokenResponse>("user");
        Assert.Null(lido);
    }

    [Fact]
    public async Task EncryptedFileDataStore_DeleteEClear_RemovemArquivos()
    {
        var folder = Path.Combine(_tempDir, "config");
        var store = new EncryptedFileDataStore(folder, _dp.CreateProtector("teste"));
        await store.StoreAsync("a", new TokenResponse { AccessToken = "1" });
        await store.StoreAsync("b", new TokenResponse { AccessToken = "2" });

        await store.DeleteAsync<TokenResponse>("a");
        Assert.Single(Directory.GetFiles(folder, EncryptedFileDataStore.FilePrefix + "*.enc"));

        await store.ClearAsync();
        Assert.Empty(Directory.GetFiles(folder, EncryptedFileDataStore.FilePrefix + "*.enc"));
    }

    private sealed class FakeEnv(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TikaumTech.Tests";
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Development";
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
