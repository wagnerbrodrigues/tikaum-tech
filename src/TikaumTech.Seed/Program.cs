// Massa de testes para homologação (TIKAUM_SPEC.md §11) — popula um banco VAZIO com
// dados realistas de estúdio usando os services reais do app (mesmas validações e
// regras de domínio da UI). Nunca toca um banco que já tenha dados de domínio.
//
// Uso:  dotnet run --project src/TikaumTech.Seed              → banco dev padrão
//       dotnet run --project src/TikaumTech.Seed -- --db x.db → outro arquivo

using Microsoft.EntityFrameworkCore;
using TikaumTech.Data;
using TikaumTech.Models;
using TikaumTech.Services;

if (args.Contains("--ajuda") || args.Contains("--help"))
{
    Console.WriteLine("""
        TikaumTech.Seed — massa de testes para homologação

        Popula um banco VAZIO com clientes, produtos, serviços e vendas realistas
        (incluindo vendas avulsas, itens livres e a trilha de auditoria de vendas
        editadas/excluídas). Se o banco já tiver qualquer dado de domínio, aborta.

        Opções:
          --db <caminho>   arquivo SQLite alvo (padrão: banco de desenvolvimento em
                           src/TikaumTech/bin/Debug/net9.0/data/tikaum-dev.db)
          --ajuda          esta mensagem

        Nunca aponte para o banco de produção do estúdio.
        """);
    return 0;
}

string? dbPath = null;
for (var i = 0; i < args.Length - 1; i++)
    if (args[i] == "--db")
        dbPath = Path.GetFullPath(args[i + 1]);

if (dbPath is null)
{
    // Sem --db: alvo é o banco de desenvolvimento (o que `dotnet run --project
    // src/TikaumTech` usa, já que o app ancora o CWD na pasta do binário).
    var raiz = new DirectoryInfo(AppContext.BaseDirectory);
    while (raiz is not null && !File.Exists(Path.Combine(raiz.FullName, "TikaumTech.sln")))
        raiz = raiz.Parent;
    if (raiz is null)
    {
        Console.Error.WriteLine("Raiz do repositório não encontrada — informe o alvo com --db <caminho>.");
        return 1;
    }
    dbPath = Path.Combine(raiz.FullName,
        "src", "TikaumTech", "bin", "Debug", "net9.0", "data", "tikaum-dev.db");
}

Console.WriteLine($"Banco alvo: {dbPath}");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;
await using var db = new ApplicationDbContext(options);
await db.Database.MigrateAsync();

if (await db.Pessoas.AnyAsync() || await db.Produtos.AnyAsync() ||
    await db.Servicos.AnyAsync() || await db.Vendas.AnyAsync())
{
    Console.Error.WriteLine(
        "Este banco já tem dados de domínio — nada foi alterado. A massa de testes só " +
        "é criada em banco vazio; para re-semear, apague o arquivo .db e rode de novo.");
    return 1;
}

var pessoaService = new PessoaService(db);
var produtoService = new ProdutoService(db);
var servicoService = new ServicoService(db);
var vendaService = new VendaService(db);

// Semente fixa: rodar de novo num banco limpo gera exatamente a mesma massa.
var rng = new Random(20260705);

// ---------- Produtos (10, um inativo) ----------
var produtos = new List<Produto>
{
    new() { Nome = "Pomada cicatrizante 30g", PrecoPadrao = 35m },
    new() { Nome = "Protetor segunda pele (metro)", PrecoPadrao = 25m },
    new() { Nome = "Sabonete neutro pós-tattoo", PrecoPadrao = 18m },
    new() { Nome = "Protetor solar FPS 50 para tatuagem", PrecoPadrao = 55m },
    new() { Nome = "Joia de titânio — labret 8mm", PrecoPadrao = 90m },
    new() { Nome = "Joia de titânio — barbell 14mm", PrecoPadrao = 110m },
    new() { Nome = "Argola de aço cirúrgico 10mm", PrecoPadrao = 60m },
    new() { Nome = "Expansor de acrílico 6mm", PrecoPadrao = 40m },
    new() { Nome = "Garrafa Tikaum (brinde oficial)", PrecoPadrao = 45m },
    new() { Nome = "Tinta importada 30ml (linha antiga)", PrecoPadrao = 120m, Ativo = false },
};
foreach (var p in produtos)
    await produtoService.CriarAsync(p);

// ---------- Serviços (12 nos três tipos, um inativo) ----------
var servicos = new List<Servico>
{
    new() { Nome = "Tatuagem pequena (até 10cm)", PrecoPadrao = 250m },
    new() { Nome = "Tatuagem média (10–20cm)", PrecoPadrao = 450m },
    new() { Nome = "Tatuagem grande (sessão)", PrecoPadrao = 700m },
    new() { Nome = "Fine line / traço fino", PrecoPadrao = 300m },
    new() { Nome = "Lettering", PrecoPadrao = 280m },
    new() { Nome = "Cover-up (sessão)", PrecoPadrao = 600m },
    new() { Nome = "Piercing lóbulo", PrecoPadrao = 80m },
    new() { Nome = "Piercing hélix", PrecoPadrao = 120m },
    new() { Nome = "Piercing septo", PrecoPadrao = 130m },
    new() { Nome = "Piercing umbigo", PrecoPadrao = 150m },
    new() { Nome = "Retoque (após 30 dias)", PrecoPadrao = 100m },
    new() { Nome = "Remoção a laser (sessão — parceria)", PrecoPadrao = 200m, Ativo = false },
};
foreach (var s in servicos)
    await servicoService.CriarAsync(s);

// ---------- Clientes (20; par de homônimos com CPFs distintos; cadastros incompletos;
// a última da lista fica sem nenhuma compra, para testar a ficha vazia) ----------
var pessoas = new List<Pessoa>
{
    new() { Nome = "Ana Beatriz Camargo", DataNascimento = new DateTime(1996, 3, 14), Telefone = "(11) 98765-1001", Cpf = "412.556.318-72", Observacoes = "Alérgica a látex — usar luva nitrílica." },
    new() { Nome = "Bruno Ferraz", DataNascimento = new DateTime(1989, 11, 2), Telefone = "(11) 97654-1002", Cpf = "308.441.276-63" },
    new() { Nome = "Carla Mendes Vieira", DataNascimento = new DateTime(2001, 7, 25), Telefone = "(11) 96543-1003", Cpf = "455.902.113-95", Observacoes = "Fechando a manga esquerda — projeto em 3 sessões." },
    new() { Nome = "Diego Nakamura", DataNascimento = new DateTime(1993, 1, 30), Telefone = "(11) 95432-1004", Cpf = "377.618.540-68" },
    new() { Nome = "Elaine Souza Prado", DataNascimento = new DateTime(1985, 9, 8), Telefone = "(11) 94321-1005", Cpf = "290.735.481-79", Observacoes = "Prefere atendimento aos sábados." },
    new() { Nome = "Fábio Antunes", DataNascimento = new DateTime(1998, 5, 17), Telefone = "(11) 93210-1006", Cpf = "401.226.879-14" },
    new() { Nome = "Gabriela Luz Martins", DataNascimento = new DateTime(2003, 12, 4), Telefone = "(11) 92109-1007", Cpf = "468.310.752-07", Observacoes = "Menor de idade na 1ª visita — autorização arquivada." },
    new() { Nome = "Henrique Dias Barbosa", DataNascimento = new DateTime(1979, 4, 21), Telefone = "(11) 91098-1008", Cpf = "215.887.634-47" },
    new() { Nome = "Isabela Fontes", DataNascimento = new DateTime(1994, 8, 12), Telefone = "(11) 99876-1009", Cpf = "389.554.207-57" },
    // Homônimos — distinguíveis pelo CPF/celular nos autocompletes (Pessoa.Identificacao):
    new() { Nome = "João da Silva", DataNascimento = new DateTime(1990, 2, 19), Telefone = "(11) 98700-1010", Cpf = "351.442.968-58" },
    new() { Nome = "João da Silva", DataNascimento = new DateTime(1972, 10, 6), Telefone = "(11) 97600-1011", Cpf = "104.336.825-66", Observacoes = "Pai do João mais novo — indicação de família." },
    new() { Nome = "Karina Almeida Rocha", DataNascimento = new DateTime(1997, 6, 28), Telefone = "(11) 96500-1012", Cpf = "422.719.083-53" },
    new() { Nome = "Leonardo Pacheco", DataNascimento = new DateTime(1986, 3, 3), Telefone = "(11) 95400-1013", Cpf = "267.905.441-51", Observacoes = "Cicatrização lenta — reforçar orientação pós." },
    // Cadastros incompletos (sem CPF, sem nascimento, sem celular):
    new() { Nome = "Marina Queiroz", Telefone = "(11) 94300-1014", Observacoes = "Cadastro incompleto — pegar CPF na próxima visita." },
    new() { Nome = "Nelson Tavares", DataNascimento = new DateTime(1968, 1, 15), Cpf = "158.223.907-06" },
    new() { Nome = "Otávio Ramos Lima", Telefone = "(11) 92100-1016" },
    new() { Nome = "Patrícia Cardoso Nunes", DataNascimento = new DateTime(1992, 11, 23), Telefone = "(11) 91000-1017", Cpf = "336.480.529-69" },
    new() { Nome = "Rafael Siqueira", DataNascimento = new DateTime(2000, 9, 9), Telefone = "(11) 98901-1018", Cpf = "477.128.365-66", Observacoes = "Diabético — avaliar antes de sessões longas." },
    new() { Nome = "Sofia Weber Andrade", DataNascimento = new DateTime(1995, 12, 31), Telefone = "(11) 97801-1019", Cpf = "395.667.201-14" },
    new() { Nome = "Zuleide Fagundes", DataNascimento = new DateTime(1975, 7, 7), Telefone = "(11) 96701-1020", Cpf = "203.514.786-71", Observacoes = "Só orçamento até agora — nenhum atendimento realizado." },
};
foreach (var p in pessoas)
    await pessoaService.CriarAsync(p);

// ---------- Vendas ----------
// ~320 no período dos últimos 2 anos (730 dias) + reforço no mês corrente + 3 hoje (para os
// painéis "Vendas do Dia" e "Vendas do Mês" do dashboard terem dados).
var clientesComCompra = pessoas.Take(pessoas.Count - 1).ToList(); // Zuleide fica sem compras
var hoje = DateTime.Today;
string[] observacoes =
[
    "Pagamento em 2x no cartão.",
    "Cliente veio por indicação.",
    "Sessão 2 de 3 do projeto.",
    "Aplicado desconto de cliente frequente.",
];
string[] itensLivres =
[
    "Colocação de joia trazida pelo cliente",
    "Sinal para orçamento de fechamento de braço",
    "Taxa de retoque fora do prazo",
    "Ajuste de projeto (arte enviada pelo cliente)",
];

var datas = new List<DateTime>();
for (var i = 0; i < 320; i++)
    datas.Add(hoje.AddDays(-rng.Next(4, 731)));
for (var i = 0; i < 5; i++)
    datas.Add(hoje.AddDays(-rng.Next(1, Math.Max(2, hoje.Day)))); // reforço no mês corrente
datas.Add(hoje); datas.Add(hoje); datas.Add(hoje);

var vendasCriadas = new List<Venda>();
foreach (var dia in datas.OrderBy(d => d))
{
    var dataHora = dia.AddHours(rng.Next(10, 20)).AddMinutes(15 * rng.Next(0, 4));
    var avulsa = rng.Next(100) < 15;
    var pessoa = avulsa ? null : clientesComCompra[rng.Next(clientesComCompra.Count)];
    var usuario = rng.Next(100) < 80 ? "tikaum" : "admin";

    var itens = new List<NovoItemVendaDto>();
    var qtdItens = 1 + (rng.Next(100) < 40 ? rng.Next(1, 3) : 0);
    for (var i = 0; i < qtdItens; i++)
    {
        var sorteio = rng.Next(100);
        if (sorteio < 55 || (avulsa && i == 0))
        {
            // Serviço é o carro-chefe (e toda venda avulsa começa com um).
            var servico = servicos[rng.Next(servicos.Count)];
            var preco = rng.Next(100) < 20
                ? Math.Round(servico.PrecoPadrao * (0.9m + 0.2m * rng.Next(0, 2)), 0)
                : servico.PrecoPadrao; // ~20% com preço ajustado na venda (campo editável)
            itens.Add(new NovoItemVendaDto(null, servico.Id, null, 1, preco));
        }
        else if (sorteio < 90)
        {
            var produto = produtos[rng.Next(produtos.Count)];
            var quantidade = rng.Next(100) < 25 ? rng.Next(2, 4) : 1;
            itens.Add(new NovoItemVendaDto(produto.Id, null, null, quantidade, produto.PrecoPadrao));
        }
        else
        {
            itens.Add(new NovoItemVendaDto(null, null,
                itensLivres[rng.Next(itensLivres.Length)], 1, 50m + 10m * rng.Next(0, 6)));
        }
    }

    var venda = await vendaService.CriarAsync(new NovaVendaDto(
        pessoa?.Id, dataHora,
        rng.Next(100) < 25 ? observacoes[rng.Next(observacoes.Length)] : null,
        itens, usuario));
    vendasCriadas.Add(venda);
}

// ---------- Trilha de auditoria: 2 vendas editadas + 2 excluídas ----------
// (pela mesma via da UI — original vira disabled + substituta com origin_id / deleted)
var paraEditar = vendasCriadas.Where(v => v.PessoaId != null).Take(2).ToList();
foreach (var v in paraEditar)
{
    var itensNovos = v.Itens
        .Select(i => new NovoItemVendaDto(i.ProdutoId, i.ServicoId, i.DescricaoLivre,
            i.Quantidade, Math.Round(i.ValorUnitario * 1.1m, 0)))
        .ToList();
    await vendaService.EditarAsync(v.Id, new NovaVendaDto(
        v.PessoaId, v.DataHora, "Valor corrigido após conferência.", itensNovos, v.Usuario));
}
foreach (var v in vendasCriadas.Skip(2).Where(v => v.Status == VendaStatus.Active).Take(2))
    await vendaService.ExcluirAsync(v.Id);

// ---------- Resumo ----------
var ativas = await db.Vendas.CountAsync(v => v.Status == VendaStatus.Active);
var editadas = await db.Vendas.CountAsync(v => v.Status == VendaStatus.Disabled);
var excluidas = await db.Vendas.CountAsync(v => v.Status == VendaStatus.Deleted);
var totalAtivo = await db.Vendas.Where(v => v.Status == VendaStatus.Active).SumAsync(v => v.ValorTotal);
var hojeQtd = await db.Vendas.CountAsync(v => v.Status == VendaStatus.Active && v.DataHora >= hoje);

Console.WriteLine($"""

    Massa de testes criada com sucesso:
      Clientes:  {pessoas.Count} (1 par de homônimos "João da Silva"; "Zuleide Fagundes" sem compras)
      Produtos:  {produtos.Count} (1 inativo)
      Serviços:  {servicos.Count} (1 inativo; tipos Tatuagem/Piercing/Outros)
      Vendas:    {ativas} ativas (R$ {totalAtivo:0.00}), {editadas} editadas (histórico),
                 {excluidas} excluídas (lógicas), {hojeQtd} hoje
    Logins padrão do app: admin/admin e tikaum/admin (criados pelo próprio app ao subir).
    """);
return 0;
