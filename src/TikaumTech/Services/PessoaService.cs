using Microsoft.EntityFrameworkCore;
using TikaumTech.Data;
using TikaumTech.Models;

namespace TikaumTech.Services;

public class PessoaService(ApplicationDbContext db)
{
    // Leituras para exibição são AsNoTracking: o DbContext do circuito Blazor vive
    // enquanto a aba estiver aberta, e cada listagem/autocomplete rastreada acumularia
    // entidades no ChangeTracker — além de ser a origem do conflito de tracking na edição
    // (a escrita rehidrata via FindAsync + SetValues, ver AtualizarAsync).
    public async Task<List<Pessoa>> ListarAsync(string? busca = null)
    {
        var query = db.Pessoas.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var b = busca.ToLower();
            query = query.Where(p =>
                p.Nome.ToLower().Contains(b) ||
                (p.Telefone != null && p.Telefone.Contains(b)) ||
                (p.Cpf != null && p.Cpf.Contains(b)));
        }
        return await query.OrderBy(p => p.Nome).ToListAsync();
    }

    public async Task<Pessoa?> ObterAsync(int id) =>
        await db.Pessoas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);

    public async Task<int> ContarAsync() =>
        await db.Pessoas.CountAsync();

    public async Task<List<Venda>> HistoricoAsync(int pessoaId) =>
        await db.Vendas.AsNoTracking()
            .Where(v => v.PessoaId == pessoaId && v.Status == VendaStatus.Active)
            .Include(v => v.Itens).ThenInclude(i => i.Produto)
            .Include(v => v.Itens).ThenInclude(i => i.Servico)
            .OrderByDescending(v => v.DataHora).ThenByDescending(v => v.Id)
            .ToListAsync();

    public async Task CriarAsync(Pessoa pessoa)
    {
        await ValidarCpfAsync(pessoa);
        pessoa.CriadoEm = DateTime.Now;
        db.Pessoas.Add(pessoa);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// CPF: dígitos verificadores quando preenchido + unicidade entre clientes (nomes iguais
    /// são permitidos; CPF igual não — TIKAUM_SPEC.md §5). A comparação exclui o próprio
    /// registro (edição mantém o próprio CPF) e ignora nulo/vazio, espelhando o índice único
    /// parcial ix_pessoas_cpf do banco — a checagem aqui existe para a mensagem amigável
    /// (com o nome do outro cliente); o índice é a defesa final contra corrida.
    /// </summary>
    private async Task ValidarCpfAsync(Pessoa pessoa)
    {
        if (!CpfValidador.EhValido(pessoa.Cpf))
            throw new InvalidOperationException("CPF inválido. Verifique o número digitado.");
        if (string.IsNullOrWhiteSpace(pessoa.Cpf)) return;

        var duplicado = await db.Pessoas.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id != pessoa.Id && p.Cpf == pessoa.Cpf);
        if (duplicado is not null)
            throw new InvalidOperationException(
                $"Este CPF já está cadastrado para o cliente {duplicado.Nome}.");
    }

    public async Task AtualizarAsync(Pessoa pessoa)
    {
        await ValidarCpfAsync(pessoa);
        // O diálogo de edição trabalha numa CÓPIA da entidade (para Cancelar não sujar a
        // listagem), e no circuito interativo o DbContext é compartilhado — a instância
        // original da listagem continua rastreada. Update(cópia) lançava "another instance
        // with the same key value is already being tracked"; FindAsync devolve a instância
        // rastreada (ou carrega) e SetValues copia os escalares da cópia para ela.
        var existente = await db.Pessoas.FindAsync(pessoa.Id)
            ?? throw new InvalidOperationException("Cliente não encontrado.");
        db.Entry(existente).CurrentValues.SetValues(pessoa);
        await db.SaveChangesAsync();
    }

    public async Task<bool> TemVendasAsync(int id) =>
        await db.Vendas.AnyAsync(v => v.PessoaId == id);

    public async Task DeletarAsync(int id)
    {
        if (await TemVendasAsync(id))
            throw new InvalidOperationException(
                "Não é possível excluir este cliente pois ele possui vendas registradas.");

        var pessoa = await db.Pessoas.FindAsync(id);
        if (pessoa is not null)
        {
            db.Pessoas.Remove(pessoa);
            await db.SaveChangesAsync();
        }
    }
}
