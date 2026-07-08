namespace TikaumTech.Services;

/// <summary>Validação de CPF por dígitos verificadores (mod 11) — TIKAUM_SPEC.md §5.</summary>
public static class CpfValidador
{
    public static bool EhValido(string? cpf)
    {
        if (string.IsNullOrWhiteSpace(cpf)) return true; // campo opcional

        var digitos = new string(cpf.Where(char.IsDigit).ToArray());
        if (digitos.Length != 11) return false;
        if (digitos.Distinct().Count() == 1) return false; // 000.000.000-00 etc.

        var numeros = digitos.Select(c => c - '0').ToArray();

        var soma1 = 0;
        for (var i = 0; i < 9; i++) soma1 += numeros[i] * (10 - i);
        var resto1 = soma1 % 11;
        var d1 = resto1 < 2 ? 0 : 11 - resto1;
        if (d1 != numeros[9]) return false;

        var soma2 = 0;
        for (var i = 0; i < 10; i++) soma2 += numeros[i] * (11 - i);
        var resto2 = soma2 % 11;
        var d2 = resto2 < 2 ? 0 : 11 - resto2;
        if (d2 != numeros[10]) return false;

        return true;
    }
}
