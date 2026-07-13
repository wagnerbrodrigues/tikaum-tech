using Microsoft.Data.Sqlite;

namespace TikaumTech.Services;

/// <summary>
/// Avisos de sistema definidos no startup e exibidos pelo BackupBanner em todas as telas.
/// Caso coberto hoje: CPFs duplicados que impedem a migration do índice único
/// ix_pessoas_cpf (TIKAUM_SPEC.md §5) — o app sobe mesmo assim, com o índice adiado
/// (ver Program.cs), e este aviso diz ao usuário exatamente o que corrigir.
/// </summary>
public class AvisoSistemaService
{
    /// <summary>"CPF (Nome1, Nome2)" por CPF duplicado; vazio = nenhum pendente.</summary>
    public IReadOnlyList<string> CpfsDuplicados { get; set; } = [];

    /// <summary>
    /// CPFs duplicados (comparação exata, espelhando o índice único parcial) em `pessoas`,
    /// direto via SQLite — roda ANTES das migrations, então não usa o modelo EF.
    /// Banco novo (ainda sem a tabela) retorna vazio.
    /// </summary>
    public static List<string> ListarCpfsDuplicados(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        using (var existe = conn.CreateCommand())
        {
            existe.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'pessoas'";
            if (Convert.ToInt64(existe.ExecuteScalar()) == 0) return [];
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT cpf, GROUP_CONCAT(nome, ', ')
            FROM pessoas
            WHERE cpf IS NOT NULL AND cpf != ''
            GROUP BY cpf
            HAVING COUNT(*) > 1
            ORDER BY cpf
            """;
        var duplicados = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            duplicados.Add($"{reader.GetString(0)} ({reader.GetString(1)})");
        return duplicados;
    }
}
