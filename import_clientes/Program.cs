using ClosedXML.Excel;
using Microsoft.Data.Sqlite;

var baseDir = AppContext.BaseDirectory;

// Locate spreadsheet
var xlsxPath = Path.Combine(baseDir, "clientes.xlsx");
if (!File.Exists(xlsxPath))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"ERRO: planilha não encontrada em:\n  {xlsxPath}");
    Console.ResetColor();
    Console.WriteLine("\nPressione qualquer tecla para sair...");
    Console.ReadKey();
    return;
}

// Locate database — ../data/tikaum.db relative to the import_clientes folder
var importDir = Path.GetFullPath(Path.Combine(baseDir, ".."));
// When running via `dotnet run`, baseDir points to the project folder itself
// so we walk up until we find the expected structure or fall back to adjacent data/
var dbPath = Path.GetFullPath(Path.Combine(importDir, "data", "tikaum.db"));
if (!File.Exists(dbPath))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"ERRO: banco de dados não encontrado em:\n  {dbPath}");
    Console.ResetColor();
    Console.WriteLine("\nPressione qualquer tecla para sair...");
    Console.ReadKey();
    return;
}

// ── Helpers ─────────────────────────────────────────────────────────────────

static string? ExtrairDigitos(string? valor) =>
    string.IsNullOrWhiteSpace(valor)
        ? null
        : new string(valor.Where(char.IsDigit).ToArray());

static string? FormatarTelefone(string? raw)
{
    var digits = ExtrairDigitos(raw);
    if (digits == null) return null;
    return digits.Length switch
    {
        11 => $"({digits[..2]}) {digits[2..7]}-{digits[7..]}",
        10 => $"({digits[..2]}) {digits[2..6]}-{digits[6..]}",
        0  => null,
        _  => digits,
    };
}

static string? FormatarCpf(string? raw)
{
    var digits = ExtrairDigitos(raw);
    if (digits == null || digits.Length == 0) return null;
    return digits.Length == 11
        ? $"{digits[..3]}.{digits[3..6]}.{digits[6..9]}-{digits[9..]}"
        : digits;
}

static DateTime? ParseData(object? rawCell, out string? aviso)
{
    aviso = null;
    if (rawCell == null) return null;

    var str = rawCell.ToString()?.Trim();
    if (string.IsNullOrEmpty(str)) return null;

    // DD/MM/YYYY
    if (DateTime.TryParseExact(str, "dd/MM/yyyy",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dt))
        return dt;

    // OLE Automation serial (numeric string stored as text, or numeric cell)
    if (double.TryParse(str, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var serial))
    {
        try { return DateTime.FromOADate(serial); }
        catch { /* fall through */ }
    }

    aviso = $"data '{str}' não reconhecida";
    return null;
}

// ── Read spreadsheet ─────────────────────────────────────────────────────────

var clientes = new List<LinhaCliente>();

using (var wb = new XLWorkbook(xlsxPath))
{
    var ws = wb.Worksheets.First();

    // Detect header: skip row 1 if A1 is not a positive integer
    int startRow = 1;
    var a1 = ws.Cell(1, 1).GetValue<string>()?.Trim();
    if (!int.TryParse(a1, out var headerTest) || headerTest <= 0)
        startRow = 2;

    for (int row = startRow; ; row++)
    {
        // Stop at first completely empty row
        bool allEmpty = Enumerable.Range(1, 5)
            .All(col => string.IsNullOrWhiteSpace(ws.Cell(row, col).GetValue<string>()));
        if (allEmpty) break;

        // B: Nome
        var nome = ws.Cell(row, 2).GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(nome)) continue;

        var avisos = new List<string>();

        // C: DataNascimento
        var rawData = ws.Cell(row, 3).GetValue<string>();
        // ClosedXML may give us a formatted date string or a serial; try numeric from cell value too
        DateTime? dataNasc = null;
        var cellC = ws.Cell(row, 3);
        if (cellC.DataType == XLDataType.DateTime)
        {
            dataNasc = cellC.GetDateTime();
        }
        else
        {
            dataNasc = ParseData(rawData, out var avisoData);
            if (avisoData != null) avisos.Add(avisoData);
        }

        // D: Telefone
        var tel = FormatarTelefone(ws.Cell(row, 4).GetValue<string>());

        // E: CPF
        var cpf = FormatarCpf(ws.Cell(row, 5).GetValue<string>());

        clientes.Add(new LinhaCliente(nome, dataNasc, tel, cpf, avisos));
    }
}

// ── Insert into database ──────────────────────────────────────────────────────

var importados = new List<string>();
var comAviso = new List<(string Nome, List<string> Avisos)>();
var duplicados = new List<string>();

var connStr = $"Data Source={dbPath}";
using var conn = new SqliteConnection(connStr);
conn.Open();

foreach (var c in clientes)
{
    // Duplicate check by normalised name
    using var checkCmd = conn.CreateCommand();
    checkCmd.CommandText = "SELECT 1 FROM pessoas WHERE TRIM(LOWER(Nome)) = TRIM(LOWER(@nome))";
    checkCmd.Parameters.AddWithValue("@nome", c.Nome);
    var exists = checkCmd.ExecuteScalar() != null;

    if (exists)
    {
        duplicados.Add(c.Nome);
        continue;
    }

    using var insCmd = conn.CreateCommand();
    insCmd.CommandText = """
        INSERT INTO pessoas (Nome, Telefone, DataNascimento, Cpf, CriadoEm)
        VALUES (@nome, @tel, @data, @cpf, @agora)
        """;
    insCmd.Parameters.AddWithValue("@nome", c.Nome);
    insCmd.Parameters.AddWithValue("@tel",  (object?)c.Telefone  ?? DBNull.Value);
    insCmd.Parameters.AddWithValue("@data",
        c.DataNasc.HasValue
            ? (object)c.DataNasc.Value.ToString("yyyy-MM-dd")
            : DBNull.Value);
    insCmd.Parameters.AddWithValue("@cpf",  (object?)c.Cpf  ?? DBNull.Value);
    insCmd.Parameters.AddWithValue("@agora", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
    insCmd.ExecuteNonQuery();

    if (c.Avisos.Count > 0)
        comAviso.Add((c.Nome, c.Avisos));
    else
        importados.Add(c.Nome);
}

// ── Summary ───────────────────────────────────────────────────────────────────

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"✓ {importados.Count} cliente(s) importado(s) com sucesso.");
Console.ResetColor();

if (comAviso.Count > 0)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"⚠  {comAviso.Count} importado(s) com aviso:");
    foreach (var (nome, avisos) in comAviso)
        Console.WriteLine($"   • {nome}: {string.Join("; ", avisos)}");
    Console.ResetColor();
}

if (duplicados.Count > 0)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"✗ {duplicados.Count} duplicado(s) ignorado(s):");
    foreach (var nome in duplicados)
        Console.WriteLine($"   • {nome}");
    Console.ResetColor();
}

Console.WriteLine();
Console.WriteLine("Pressione qualquer tecla para fechar...");
Console.ReadKey();

// Type declarations must follow all top-level statements in a top-level program
record LinhaCliente(string Nome, DateTime? DataNasc, string? Telefone, string? Cpf, List<string> Avisos);
