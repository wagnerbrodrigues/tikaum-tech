using Microsoft.EntityFrameworkCore;
using TikaumTech.Data;
using TikaumTech.Models;

namespace TikaumTech.Services;

public record NovaVendaDto(
    int? PessoaId,
    DateTime DataHora,
    string? Observacao,
    List<NovoItemVendaDto> Itens,
    string? Usuario = null);

public record NovoItemVendaDto(
    int? ProdutoId,
    int? ServicoId,
    string? DescricaoLivre,
    decimal Quantidade,
    decimal ValorUnitario);

public class VendaService(ApplicationDbContext db)
{
    /// <summary>
    /// Lista vendas. Por padrão só registros ativos — disabled/deleted nunca aparecem
    /// em relatório/total/métrica (TIKAUM_SPEC.md §5); <paramref name="incluirHistorico"/>
    /// existe só para o toggle "Mostrar histórico de ajustes" da página de Vendas.
    /// </summary>
    public async Task<List<Venda>> ListarAsync(
        DateTime? inicio = null, DateTime? fim = null, int? pessoaId = null,
        bool incluirHistorico = false)
    {
        var query = db.Vendas
            .Include(v => v.Pessoa)
            .Include(v => v.Itens).ThenInclude(i => i.Produto)
            .Include(v => v.Itens).ThenInclude(i => i.Servico)
            .AsQueryable();

        query = incluirHistorico
            ? query.Include(v => v.Substitutas)
            : query.Where(v => v.Status == VendaStatus.Active);

        if (inicio.HasValue)
            query = query.Where(v => v.DataHora >= inicio.Value);
        if (fim.HasValue)
            query = query.Where(v => v.DataHora < fim.Value.AddDays(1));
        if (pessoaId.HasValue)
            query = query.Where(v => v.PessoaId == pessoaId.Value);

        // Venda só registra a data (hora 00:00) — Id desempata vendas do mesmo dia
        return await query
            .OrderByDescending(v => v.DataHora).ThenByDescending(v => v.Id)
            .ToListAsync();
    }

    public async Task<Venda?> ObterAsync(int id) =>
        await db.Vendas
            .Include(v => v.Pessoa)
            .Include(v => v.Itens).ThenInclude(i => i.Produto)
            .Include(v => v.Itens).ThenInclude(i => i.Servico)
            .Include(v => v.Substitutas)
            .FirstOrDefaultAsync(v => v.Id == id);

    public async Task<int> ContarAtivasAsync() =>
        await db.Vendas.CountAsync(v => v.Status == VendaStatus.Active);

    public async Task<List<Venda>> UltimasAsync(int quantidade) =>
        await db.Vendas
            .Include(v => v.Pessoa)
            .Include(v => v.Itens)
            .Where(v => v.Status == VendaStatus.Active)
            .OrderByDescending(v => v.DataHora).ThenByDescending(v => v.Id)
            .Take(quantidade)
            .ToListAsync();

    public async Task<Venda> CriarAsync(NovaVendaDto dto)
    {
        ValidarItens(dto);

        var venda = MontarVenda(dto);
        db.Vendas.Add(venda);
        await db.SaveChangesAsync();
        return venda;
    }

    /// <summary>
    /// Edição com rastreabilidade: o original vira disabled (adjusted_reason='edit') e um
    /// registro novo é criado com origin_id apontando para ele. Nada é alterado in-place.
    /// </summary>
    public async Task<Venda> EditarAsync(int vendaId, NovaVendaDto dto)
    {
        ValidarItens(dto);

        var original = await db.Vendas.FirstOrDefaultAsync(v => v.Id == vendaId)
            ?? throw new InvalidOperationException("Venda não encontrada.");
        if (original.Status != VendaStatus.Active)
            throw new InvalidOperationException("Apenas vendas ativas podem ser editadas.");

        original.Status = VendaStatus.Disabled;
        original.AdjustedAt = DateTime.Now;
        original.AdjustedReason = VendaAdjustedReason.Edit;

        var substituta = MontarVenda(dto);
        substituta.OriginId = original.Id;
        db.Vendas.Add(substituta);

        await db.SaveChangesAsync();
        return substituta;
    }

    /// <summary>Exclusão lógica: marca deleted e mantém o registro no banco para auditoria.</summary>
    public async Task ExcluirAsync(int vendaId)
    {
        var venda = await db.Vendas.FirstOrDefaultAsync(v => v.Id == vendaId)
            ?? throw new InvalidOperationException("Venda não encontrada.");
        if (venda.Status != VendaStatus.Active)
            throw new InvalidOperationException("Apenas vendas ativas podem ser excluídas.");

        venda.Status = VendaStatus.Deleted;
        venda.AdjustedAt = DateTime.Now;
        venda.AdjustedReason = VendaAdjustedReason.Delete;
        await db.SaveChangesAsync();
    }

    private static void ValidarItens(NovaVendaDto dto)
    {
        if (dto.Itens.Count == 0)
            throw new InvalidOperationException("A venda precisa ter pelo menos um item.");

        foreach (var item in dto.Itens)
        {
            if (item.ProdutoId is null && item.ServicoId is null &&
                string.IsNullOrWhiteSpace(item.DescricaoLivre))
                throw new InvalidOperationException("Item livre requer uma descrição.");
            if (item.Quantidade <= 0)
                throw new InvalidOperationException("Quantidade deve ser maior que zero.");
            if (item.ValorUnitario < 0)
                throw new InvalidOperationException("Valor unitário não pode ser negativo.");
        }
    }

    private static Venda MontarVenda(NovaVendaDto dto)
    {
        var venda = new Venda
        {
            PessoaId = dto.PessoaId,
            DataHora = dto.DataHora,
            Observacao = dto.Observacao,
            Usuario = dto.Usuario,
            Itens = dto.Itens.Select(i => new ItemVenda
            {
                ProdutoId = i.ProdutoId,
                ServicoId = i.ServicoId,
                DescricaoLivre = i.DescricaoLivre,
                Quantidade = i.Quantidade,
                ValorUnitario = i.ValorUnitario,
                ValorTotal = Math.Round(i.Quantidade * i.ValorUnitario, 2),
            }).ToList()
        };
        venda.ValorTotal = venda.Itens.Sum(i => i.ValorTotal);
        return venda;
    }
}
