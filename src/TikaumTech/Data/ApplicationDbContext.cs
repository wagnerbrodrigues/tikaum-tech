using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TikaumTech.Models;

namespace TikaumTech.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Pessoa> Pessoas => Set<Pessoa>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<Servico> Servicos => Set<Servico>();
    public DbSet<Venda> Vendas => Set<Venda>();
    public DbSet<ItemVenda> ItensVenda => Set<ItemVenda>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Pessoa>(e =>
        {
            e.ToTable("pessoas");
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Nome).HasColumnName("nome");
            e.Property(p => p.DataNascimento).HasColumnName("data_nascimento");
            e.Property(p => p.Telefone).HasColumnName("telefone");
            e.Property(p => p.Cpf).HasColumnName("cpf");
            e.Property(p => p.Observacoes).HasColumnName("observacoes");
            e.Property(p => p.CriadoEm).HasColumnName("criado_em");
            e.Ignore(p => p.Identificacao);
        });

        builder.Entity<Produto>(e =>
        {
            e.ToTable("produtos");
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Nome).HasColumnName("nome");
            e.Property(p => p.PrecoPadrao).HasColumnName("preco_padrao").HasColumnType("NUMERIC(10,2)");
            e.Property(p => p.Ativo).HasColumnName("ativo");
        });

        builder.Entity<Servico>(e =>
        {
            e.ToTable("servicos");
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Nome).HasColumnName("nome");
            e.Property(p => p.PrecoPadrao).HasColumnName("preco_padrao").HasColumnType("NUMERIC(10,2)");
            e.Property(p => p.Ativo).HasColumnName("ativo");
        });

        builder.Entity<Venda>(e =>
        {
            e.ToTable("vendas");
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.PessoaId).HasColumnName("pessoa_id");
            e.Property(p => p.DataHora).HasColumnName("data_hora");
            e.Property(p => p.Observacao).HasColumnName("observacao");
            e.Property(p => p.ValorTotal).HasColumnName("valor_total").HasColumnType("NUMERIC(10,2)");
            e.Property(p => p.Usuario).HasColumnName("usuario");
            e.Property(p => p.Status).HasColumnName("status").IsRequired().HasDefaultValue(VendaStatus.Active);
            e.Property(p => p.OriginId).HasColumnName("origin_id");
            e.Property(p => p.AdjustedAt).HasColumnName("adjusted_at");
            e.Property(p => p.AdjustedReason).HasColumnName("adjusted_reason");
            e.HasIndex(p => p.PessoaId).HasDatabaseName("ix_vendas_pessoa_id");
            e.HasIndex(p => p.DataHora).HasDatabaseName("ix_vendas_data_hora");
            e.HasIndex(p => p.Status).HasDatabaseName("ix_vendas_status");
            e.HasIndex(p => p.OriginId).HasDatabaseName("ix_vendas_origin_id");
            e.HasOne(p => p.Origin).WithMany(p => p.Substitutas)
                .HasForeignKey(p => p.OriginId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(p => p.SubstituidaPor);
        });

        builder.Entity<ItemVenda>(e =>
        {
            e.ToTable("itens_venda");
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.VendaId).HasColumnName("venda_id");
            e.Property(p => p.ProdutoId).HasColumnName("produto_id");
            e.Property(p => p.ServicoId).HasColumnName("servico_id");
            e.Property(p => p.DescricaoLivre).HasColumnName("descricao_livre");
            e.Property(p => p.Quantidade).HasColumnName("quantidade").HasColumnType("NUMERIC(10,3)");
            e.Property(p => p.ValorUnitario).HasColumnName("valor_unitario").HasColumnType("NUMERIC(10,2)");
            e.Property(p => p.ValorTotal).HasColumnName("valor_total").HasColumnType("NUMERIC(10,2)");
            e.HasIndex(p => p.ProdutoId).HasDatabaseName("ix_itens_venda_produto_id");
            e.HasIndex(p => p.ServicoId).HasDatabaseName("ix_itens_venda_servico_id");
        });
    }
}
