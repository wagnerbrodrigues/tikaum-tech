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
            // SQL cru em vez de CreateIndex pelo IF NOT EXISTS. Se o banco já tiver dois
            // clientes com o MESMO CPF (dado antigo, de antes desta regra), o SQLite recusa
            // o índice e a migration falha no start — resolver os duplicados pela tela de
            // Clientes e abrir o app de novo.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS ix_pessoas_cpf " +
                "ON pessoas (cpf) WHERE cpf IS NOT NULL AND cpf != ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_pessoas_cpf");
        }
    }
}
