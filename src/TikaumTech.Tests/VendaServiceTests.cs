using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TikaumTech.Data;
using TikaumTech.Models;
using TikaumTech.Services;
using Xunit;

namespace TikaumTech.Tests;

public class VendaServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ApplicationDbContext _db;
    private readonly VendaService _svc;

    public VendaServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_conn).Options;
        _db = new ApplicationDbContext(opts);
        _db.Database.EnsureCreated();
        _svc = new VendaService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    [Fact]
    public async Task CriarAsync_LancaExcecao_QuandoSemItens()
    {
        var dto = new NovaVendaDto(null, DateTime.Now, null, []);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CriarAsync(dto));
    }

    [Fact]
    public async Task CriarAsync_LancaExcecao_QuandoItemLivreSemDescricao()
    {
        var dto = new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, null, 1, 50m)
        ]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CriarAsync(dto));
    }

    [Fact]
    public async Task CriarAsync_LancaExcecao_QuandoQuantidadeZero()
    {
        var dto = new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Piercing avulso", 0, 80m)
        ]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CriarAsync(dto));
    }

    [Fact]
    public async Task CriarAsync_LancaExcecao_QuandoQuantidadeNegativa()
    {
        var dto = new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Retoque", -1, 100m)
        ]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.CriarAsync(dto));
    }

    [Fact]
    public async Task CriarAsync_CalculaValorTotalCorretamente()
    {
        var dto = new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Tatuagem custom", 1, 350m),
            new NovoItemVendaDto(null, null, "Piercing", 2, 80m),
        ]);

        var venda = await _svc.CriarAsync(dto);

        Assert.Equal(510m, venda.ValorTotal);
    }

    [Fact]
    public async Task CriarAsync_CalculaValorTotalDeItem_Corretamente()
    {
        var dto = new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Sessao", 3, 150m),
        ]);

        var venda = await _svc.CriarAsync(dto);

        Assert.Equal(450m, venda.Itens.First().ValorTotal);
    }

    [Fact]
    public async Task CriarAsync_PersistidaNoDb()
    {
        var dto = new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Consulta gratuita", 1, 0m)
        ]);

        var venda = await _svc.CriarAsync(dto);

        Assert.True(venda.Id > 0);
        Assert.Equal(1, await _db.Vendas.CountAsync());
    }

    [Fact]
    public async Task CriarAsync_VendaAvulsa_PessoaIdNulo()
    {
        var dto = new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Servico avulso", 1, 100m)
        ]);

        var venda = await _svc.CriarAsync(dto);

        Assert.Null(venda.PessoaId);
    }

    [Fact]
    public async Task CriarAsync_VendaComPessoa_AssociaCorretamente()
    {
        var pessoa = new Pessoa { Nome = "Carlos Silva" };
        _db.Pessoas.Add(pessoa);
        await _db.SaveChangesAsync();

        var dto = new NovaVendaDto(pessoa.Id, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Tatuagem", 1, 400m)
        ]);

        var venda = await _svc.CriarAsync(dto);

        Assert.Equal(pessoa.Id, venda.PessoaId);
    }

    [Fact]
    public async Task ListarAsync_FiltrarPorPeriodo_RetornaSoVendasNaFaixa()
    {
        var ontem = new NovaVendaDto(null, DateTime.Today.AddDays(-1), null, [
            new NovoItemVendaDto(null, null, "Venda ontem", 1, 50m)
        ]);
        var hoje = new NovaVendaDto(null, DateTime.Today, null, [
            new NovoItemVendaDto(null, null, "Venda hoje", 1, 100m)
        ]);
        await _svc.CriarAsync(ontem);
        await _svc.CriarAsync(hoje);

        var resultado = await _svc.ListarAsync(inicio: DateTime.Today);

        Assert.Single(resultado);
        Assert.Equal(100m, resultado[0].ValorTotal);
    }

    [Fact]
    public async Task ListarAsync_FiltrarPorPessoa_RetornaSoVendasDaPessoa()
    {
        var p1 = new Pessoa { Nome = "Ana" };
        var p2 = new Pessoa { Nome = "Bruno" };
        _db.Pessoas.AddRange(p1, p2);
        await _db.SaveChangesAsync();

        await _svc.CriarAsync(new NovaVendaDto(p1.Id, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "A", 1, 100m)
        ]));
        await _svc.CriarAsync(new NovaVendaDto(p2.Id, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "B", 1, 200m)
        ]));

        var resultado = await _svc.ListarAsync(pessoaId: p1.Id);

        Assert.Single(resultado);
        Assert.Equal(p1.Id, resultado[0].PessoaId);
    }

    [Fact]
    public async Task ListarAsync_RetornaOrdenadoPorDataDecrescente()
    {
        var base_ = new DateTime(2026, 1, 1);
        await _svc.CriarAsync(new NovaVendaDto(null, base_, null, [
            new NovoItemVendaDto(null, null, "Primeiro", 1, 10m)
        ]));
        await _svc.CriarAsync(new NovaVendaDto(null, base_.AddDays(1), null, [
            new NovoItemVendaDto(null, null, "Segundo", 1, 20m)
        ]));

        var resultado = await _svc.ListarAsync();

        Assert.Equal(base_.AddDays(1), resultado[0].DataHora);
        Assert.Equal(base_, resultado[1].DataHora);
    }

    // ------------------------------------------------------------------
    // Rastreabilidade (TIKAUM_SPEC.md §5): editar/excluir nunca apagam nem
    // alteram in-place; consultas padrão só enxergam status = active
    // ------------------------------------------------------------------

    [Fact]
    public async Task CriarAsync_NovaVenda_NasceActive()
    {
        var venda = await _svc.CriarAsync(new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Tattoo", 1, 100m)
        ]));

        Assert.Equal(VendaStatus.Active, venda.Status);
        Assert.Null(venda.OriginId);
        Assert.Null(venda.AdjustedAt);
        Assert.Null(venda.AdjustedReason);
    }

    [Fact]
    public async Task EditarAsync_DesabilitaOriginal_ECriaSubstitutaComOriginId()
    {
        var original = await _svc.CriarAsync(new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Tattoo braço", 1, 300m)
        ]));

        var substituta = await _svc.EditarAsync(original.Id, new NovaVendaDto(
            null, DateTime.Now, "valor corrigido", [
                new NovoItemVendaDto(null, null, "Tattoo braço", 1, 350m)
            ]));

        var originalNoDb = await _db.Vendas.AsNoTracking().FirstAsync(v => v.Id == original.Id);
        Assert.Equal(VendaStatus.Disabled, originalNoDb.Status);
        Assert.Equal(VendaAdjustedReason.Edit, originalNoDb.AdjustedReason);
        Assert.NotNull(originalNoDb.AdjustedAt);

        Assert.Equal(VendaStatus.Active, substituta.Status);
        Assert.Equal(original.Id, substituta.OriginId);
        Assert.Equal(350m, substituta.ValorTotal);
        Assert.Equal(2, await _db.Vendas.CountAsync()); // nada foi apagado
    }

    [Fact]
    public async Task EditarAsync_PermiteTrocarCliente()
    {
        var p1 = new Pessoa { Nome = "Ana" };
        var p2 = new Pessoa { Nome = "Bruno" };
        _db.Pessoas.AddRange(p1, p2);
        await _db.SaveChangesAsync();

        var original = await _svc.CriarAsync(new NovaVendaDto(p1.Id, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Piercing", 1, 80m)
        ]));

        var substituta = await _svc.EditarAsync(original.Id, new NovaVendaDto(
            p2.Id, original.DataHora, null, [
                new NovoItemVendaDto(null, null, "Piercing", 1, 80m)
            ]));

        Assert.Equal(p2.Id, substituta.PessoaId);
        Assert.Equal(original.Id, substituta.OriginId);
    }

    [Fact]
    public async Task EditarAsync_LancaExcecao_QuandoVendaNaoEstaActive()
    {
        var venda = await _svc.CriarAsync(new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Tattoo", 1, 100m)
        ]));
        await _svc.ExcluirAsync(venda.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.EditarAsync(venda.Id, new NovaVendaDto(null, DateTime.Now, null, [
                new NovoItemVendaDto(null, null, "Tattoo", 1, 100m)
            ])));
    }

    [Fact]
    public async Task EditarAsync_ItensInvalidos_NaoDesabilitaOriginal()
    {
        var venda = await _svc.CriarAsync(new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Tattoo", 1, 100m)
        ]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.EditarAsync(venda.Id, new NovaVendaDto(null, DateTime.Now, null, [])));

        var noDb = await _db.Vendas.AsNoTracking().FirstAsync(v => v.Id == venda.Id);
        Assert.Equal(VendaStatus.Active, noDb.Status);
    }

    [Fact]
    public async Task ExcluirAsync_MarcaDeleted_SemRemoverDoBanco()
    {
        var venda = await _svc.CriarAsync(new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Tattoo", 1, 100m)
        ]));

        await _svc.ExcluirAsync(venda.Id);

        var noDb = await _db.Vendas.AsNoTracking().FirstAsync(v => v.Id == venda.Id);
        Assert.Equal(VendaStatus.Deleted, noDb.Status);
        Assert.Equal(VendaAdjustedReason.Delete, noDb.AdjustedReason);
        Assert.NotNull(noDb.AdjustedAt);
        Assert.Equal(1, await _db.Vendas.CountAsync());
    }

    [Fact]
    public async Task ExcluirAsync_LancaExcecao_QuandoJaExcluida()
    {
        var venda = await _svc.CriarAsync(new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Tattoo", 1, 100m)
        ]));
        await _svc.ExcluirAsync(venda.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.ExcluirAsync(venda.Id));
    }

    [Fact]
    public async Task ListarAsync_PorPadrao_OcultaDisabledEDeleted()
    {
        var editada = await _svc.CriarAsync(new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Editada", 1, 100m)
        ]));
        await _svc.EditarAsync(editada.Id, new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Editada v2", 1, 120m)
        ]));
        var excluida = await _svc.CriarAsync(new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Excluída", 1, 50m)
        ]));
        await _svc.ExcluirAsync(excluida.Id);

        var resultado = await _svc.ListarAsync();

        // Só a substituta ativa aparece; original disabled e a deleted, não
        Assert.Single(resultado);
        Assert.Equal(VendaStatus.Active, resultado[0].Status);
        Assert.Equal(120m, resultado[0].ValorTotal);
    }

    [Fact]
    public async Task ListarAsync_ComHistorico_RetornaTudo_ECarregaSubstituta()
    {
        var original = await _svc.CriarAsync(new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Editada", 1, 100m)
        ]));
        var substituta = await _svc.EditarAsync(original.Id, new NovaVendaDto(
            null, DateTime.Now, null, [
                new NovoItemVendaDto(null, null, "Editada v2", 1, 120m)
            ]));

        var resultado = await _svc.ListarAsync(incluirHistorico: true);

        Assert.Equal(2, resultado.Count);
        var disabled = resultado.First(v => v.Id == original.Id);
        Assert.Equal(substituta.Id, disabled.SubstituidaPor?.Id);
    }

    [Fact]
    public async Task ContarAtivasAsync_ContaApenasActive()
    {
        var v1 = await _svc.CriarAsync(new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "A", 1, 10m)
        ]));
        await _svc.EditarAsync(v1.Id, new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "A2", 1, 15m)
        ]));
        var v2 = await _svc.CriarAsync(new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "B", 1, 20m)
        ]));
        await _svc.ExcluirAsync(v2.Id);

        // No banco: disabled (v1), active (substituta), deleted (v2) → conta 1
        Assert.Equal(1, await _svc.ContarAtivasAsync());
    }

    [Fact]
    public async Task UltimasAsync_RetornaApenasActive_LimitadoAQuantidade()
    {
        for (int i = 0; i < 4; i++)
        {
            await _svc.CriarAsync(new NovaVendaDto(null, DateTime.Today.AddDays(-i), null, [
                new NovoItemVendaDto(null, null, $"Venda {i}", 1, 10m + i)
            ]));
        }
        var excluida = await _svc.CriarAsync(new NovaVendaDto(null, DateTime.Today.AddDays(1), null, [
            new NovoItemVendaDto(null, null, "Excluída", 1, 99m)
        ]));
        await _svc.ExcluirAsync(excluida.Id);

        var resultado = await _svc.UltimasAsync(3);

        Assert.Equal(3, resultado.Count);
        Assert.All(resultado, v => Assert.Equal(VendaStatus.Active, v.Status));
        Assert.DoesNotContain(resultado, v => v.Id == excluida.Id);
    }

    [Fact]
    public async Task CriarAsync_GravaUsuarioDaVenda()
    {
        var dto = new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Tatuagem", 1, 300m)
        ], Usuario: "tikaum");

        var venda = await _svc.CriarAsync(dto);

        var salva = await _db.Vendas.FindAsync(venda.Id);
        Assert.Equal("tikaum", salva!.Usuario);
    }

    [Fact]
    public async Task EditarAsync_SubstitutaRecebeUsuarioNovo_OriginalPreservaOAntigo()
    {
        var original = await _svc.CriarAsync(new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Piercing", 1, 80m)
        ], Usuario: "tikaum"));

        var substituta = await _svc.EditarAsync(original.Id, new NovaVendaDto(null, DateTime.Now, null, [
            new NovoItemVendaDto(null, null, "Piercing", 1, 90m)
        ], Usuario: "admin"));

        var originalNoBanco = await _db.Vendas.FindAsync(original.Id);
        Assert.Equal("tikaum", originalNoBanco!.Usuario); // snapshot de auditoria intacto
        Assert.Equal("admin", substituta.Usuario);
    }
}
