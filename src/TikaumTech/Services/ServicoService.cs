using Microsoft.EntityFrameworkCore;
using TikaumTech.Data;
using TikaumTech.Models;

namespace TikaumTech.Services;

public class ServicoService(ApplicationDbContext db)
{
    // Leituras para exibição são AsNoTracking — mesma razão do PessoaService.ListarAsync
    // (a escrita rehidrata via FindAsync + SetValues, ver AtualizarAsync).
    public async Task<List<Servico>> ListarAsync(string? busca = null, bool apenasAtivos = true)
    {
        var query = db.Servicos.AsNoTracking();
        if (apenasAtivos) query = query.Where(s => s.Ativo);
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var b = busca.ToLower();
            query = query.Where(s => s.Nome.ToLower().Contains(b));
        }
        return await query.OrderBy(s => s.Nome).ToListAsync();
    }

    public async Task<Servico?> ObterAsync(int id) =>
        await db.Servicos.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);

    public async Task<int> ContarAtivosAsync() =>
        await db.Servicos.CountAsync(s => s.Ativo);

    public async Task CriarAsync(Servico servico)
    {
        if (servico.PrecoPadrao < 0)
            throw new InvalidOperationException("Preço padrão não pode ser negativo.");
        db.Servicos.Add(servico);
        await db.SaveChangesAsync();
    }

    public async Task AtualizarAsync(Servico servico)
    {
        if (servico.PrecoPadrao < 0)
            throw new InvalidOperationException("Preço padrão não pode ser negativo.");
        // Mesma razão do PessoaService.AtualizarAsync: o diálogo edita uma cópia e a
        // instância da listagem segue rastreada no DbContext do circuito — Update(cópia)
        // lançava exceção de tracking; SetValues na instância rastreada não.
        var existente = await db.Servicos.FindAsync(servico.Id)
            ?? throw new InvalidOperationException("Serviço não encontrado.");
        db.Entry(existente).CurrentValues.SetValues(servico);
        await db.SaveChangesAsync();
    }

    public async Task AlternarAtivoAsync(int id)
    {
        var servico = await db.Servicos.FindAsync(id);
        if (servico is not null)
        {
            servico.Ativo = !servico.Ativo;
            await db.SaveChangesAsync();
        }
    }
}
