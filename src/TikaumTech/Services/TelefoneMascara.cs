using MudBlazor;

namespace TikaumTech.Services;

/// <summary>
/// Máscara de telefone do cadastro de cliente (TIKAUM_SPEC.md §5): 8 dígitos após o DDD
/// por padrão, 9 quando o número após o DDD começa com 9.
/// </summary>
public static class TelefoneMascara
{
    // O MultiMask do MudBlazor casa o regex das opções contra o texto JÁ FORMATADO
    // (ex.: "(19) 9..."), não contra os dígitos crus — os padrões precisam incluir os
    // parênteses e o espaço, senão a opção nunca ativa e o 9º dígito é descartado.
    // Cada componente precisa da sua própria instância (a máscara guarda estado de edição).
    public static MultiMask Criar() => new(
        "(00) 0000-0000",
        new MaskOption("fixo", "(00) 0000-0000", @"^\(\d{2}\) [0-8]"),
        new MaskOption("celular", "(00) 00000-0000", @"^\(\d{2}\) 9"));
}
