using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TikaumTech.Data;
using TikaumTech.Models;
using TikaumTech.Services;
using Xunit;

namespace TikaumTech.Tests;

// Regressão do incidente de 2026-07-13: um banco de produção com CPFs duplicados (de antes
// da regra de unicidade existir) fazia CREATE UNIQUE INDEX falhar dentro de
// db.Database.Migrate() — sem try/catch em Program.cs, isso travava a migration inteira e o
// app não subia. A correção move a criação do índice para fora da migration versionada
// (Program.cs, pós-migration, idempotente) — estes testes cobrem que Migrate() não falha
// mais e que o problema real (duplicados) continua visível via AvisoSistemaService.
public class StartupResilienceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connString;

    public StartupResilienceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tikaum_startup_test_{Guid.NewGuid():N}.db");
        _connString = $"Data Source={_dbPath}";
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }

    private ApplicationDbContext CriarContexto() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connString).Options);

    [Fact]
    public async Task Migrate_NaoFalha_QuandoBancoJaTemCpfsDuplicados()
    {
        // Simula um banco antigo: dois clientes com o mesmo CPF inseridos direto (sem passar
        // pelo PessoaService, então nenhuma validação de unicidade roda aqui).
        await using (var db = CriarContexto())
        {
            await db.Database.MigrateAsync();
            db.Pessoas.AddRange(
                new Pessoa { Nome = "Legado 1", Cpf = "529.982.247-25" },
                new Pessoa { Nome = "Legado 2", Cpf = "529.982.247-25" });
            await db.SaveChangesAsync();
        }

        // Um restart do app roda Migrate() de novo — com a correção, isso não lança mais
        // mesmo com os duplicados presentes.
        await using var db2 = CriarContexto();
        await db2.Database.MigrateAsync();

        Assert.Equal(2, await db2.Pessoas.CountAsync());
    }

    [Fact]
    public async Task CreateUniqueIndex_Falha_QuandoHaDuplicados_EAvisoSistemaListaOsCpfs()
    {
        await using (var db = CriarContexto())
        {
            await db.Database.MigrateAsync();
            db.Pessoas.AddRange(
                new Pessoa { Nome = "Legado 1", Cpf = "529.982.247-25" },
                new Pessoa { Nome = "Legado 2", Cpf = "529.982.247-25" });
            await db.SaveChangesAsync();
        }

        await using var db2 = CriarContexto();
        await db2.Database.MigrateAsync();

        // O passo pós-migration em Program.cs tenta exatamente este SQL; aqui confirmamos
        // que ele de fato falha com dados duplicados (é o cenário que o try/catch cobre).
        await Assert.ThrowsAsync<SqliteException>(() => db2.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS ix_pessoas_cpf ON pessoas (cpf) WHERE cpf IS NOT NULL AND cpf != ''"));

        var duplicados = AvisoSistemaService.ListarCpfsDuplicados(_connString);
        var entrada = Assert.Single(duplicados);
        Assert.Contains("529.982.247-25", entrada);
        Assert.Contains("Legado 1", entrada);
        Assert.Contains("Legado 2", entrada);
    }

    [Fact]
    public async Task CreateUniqueIndex_Sucede_QuandoNaoHaDuplicados()
    {
        await using var db = CriarContexto();
        await db.Database.MigrateAsync();
        db.Pessoas.Add(new Pessoa { Nome = "Cliente Único", Cpf = "529.982.247-25" });
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS ix_pessoas_cpf ON pessoas (cpf) WHERE cpf IS NOT NULL AND cpf != ''");

        Assert.Empty(AvisoSistemaService.ListarCpfsDuplicados(_connString));
    }

    [Fact]
    public void ListarCpfsDuplicados_BancoSemTabelaPessoas_RetornaVazio()
    {
        // Banco novo (arquivo ainda não existe / sem migrations rodadas)
        Assert.Empty(AvisoSistemaService.ListarCpfsDuplicados(_connString));
    }
}
