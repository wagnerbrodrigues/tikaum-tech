using Microsoft.Extensions.Localization;
using MudBlazor;

namespace TikaumTech.Services;

/// <summary>
/// Traduz para pt-BR as poucas strings internas do MudBlazor que aparecem visíveis ao
/// usuário nas telas de data (MudBlazor 9 não traz tradução pt-BR embutida — cai no inglês
/// por padrão). Hoje só cobre a mensagem de data inválida (dígitos verificadores do dia/mês
/// que não existem, ex. 32/13/2026); as demais strings do MudBlazor seguem em inglês.
/// A chave "Converter_InvalidDateTime" é interna ao MudBlazor (MudBlazor.Resources.
/// LanguageResource não é pública) — só dá pra referenciar pelo nome literal.
/// </summary>
public class TikaumMudLocalizer : MudLocalizer
{
    public override LocalizedString this[string key] =>
        key == "Converter_InvalidDateTime"
            ? new LocalizedString(key, "Data inválida")
            : base[key];
}
