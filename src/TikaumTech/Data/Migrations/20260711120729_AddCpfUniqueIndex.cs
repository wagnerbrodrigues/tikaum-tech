using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TikaumTech.Migrations
{
    /// <inheritdoc />
    public partial class AddCpfUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Não-op de propósito (revisão de 2026-07-13). A criação original aqui era
            // "CREATE UNIQUE INDEX IF NOT EXISTS ix_pessoas_cpf ...": se o banco já tivesse
            // dois clientes com o MESMO CPF (dado antigo, de antes desta regra), o SQLite
            // recusava o índice — e como db.Database.Migrate() não tinha try/catch em
            // Program.cs, isso travava a migration inteira e o app não subia. A criação do
            // índice foi movida para um passo idempotente pós-migration em Program.cs
            // (try/catch, tentado em todo start) para nunca impedir o app de subir — ver
            // TIKAUM_SPEC.md §5. Esta migration continua existindo só para o histórico/
            // model snapshot ficarem consistentes com o índice declarado em
            // ApplicationDbContext.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_pessoas_cpf");
        }
    }
}
