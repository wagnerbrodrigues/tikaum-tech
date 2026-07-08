using Microsoft.EntityFrameworkCore;
using TikaumTech.Data;
using TikaumTech.Models;

namespace TikaumTech.Services;

public class ProdutoService(ApplicationDbContext db)
{
    // Leituras para exibição são AsNoTracking — mesma razão do PessoaService.ListarAsync
    // (a escrita rehidrata via FindAsync + SetValues, ver AtualizarAsync).
    public async Task<List<Produto>> ListarAsync(string? busca = null, bool apenasAtivos = true)
    {
        var query = db.Produtos.AsNoTracking();
        if (apenasAtivos) query = query.Where(p => p.Ativo);
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var b = busca.ToLower();
            query = query.Where(p => p.Nome.ToLower().Contains(b));
        }
        return await query.OrderBy(p => p.Nome).ToListAsync();
    }

    public async Task<Produto?> ObterAsync(int id) =>
        await db.Produtos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);

    public async Task<int> ContarAtivosAsync() =>
        await db.Produtos.CountAsync(p => p.Ativo);

    public async Task CriarAsync(Produto produto)
    {
        if (produto.PrecoPadrao < 0)
            throw new InvalidOperationException("Preço padrão não pode ser negativo.");
        db.Produtos.Add(produto);
        await db.SaveChangesAsync();
    }

    public async Task AtualizarAsync(Produto produto)
    {
        if (produto.PrecoPadrao < 0)
            throw new InvalidOperationException("Preço padrão não pode ser negativo.");
        // Mesma razão do PessoaService.AtualizarAsync: o diálogo edita uma cópia e a
        // instância da listagem segue rastreada no DbContext do circuito — Update(cópia)
        // lançava exceção de tracking; SetValues na instância rastreada não.
        var existente = await db.Produtos.FindAsync(produto.Id)
            ?? throw new InvalidOperationException("Produto não encontrado.");
        db.Entry(existente).CurrentValues.SetValues(produto);
        await db.SaveChangesAsync();
    }

    public async Task AlternarAtivoAsync(int id)
    {
        var produto = await db.Produtos.FindAsync(id);
        if (produto is not null)
        {
            produto.Ativo = !produto.Ativo;
            await db.SaveChangesAsync();
        }
    }
}
