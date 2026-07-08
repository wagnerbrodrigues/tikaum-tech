using MudBlazor;
using TikaumTech.Services;
using Xunit;

namespace TikaumTech.Tests;

public class TelefoneMascaraTests
{
    private static string Digitar(string digitos, BaseMask? mascara = null)
    {
        mascara ??= TelefoneMascara.Criar();
        foreach (var c in digitos)
            mascara.Insert(c.ToString());
        return mascara.Text ?? "";
    }

    [Theory]
    [InlineData("19997018817", "(19) 99701-8817")] // regressão: 9º dígito era descartado
    [InlineData("11987654321", "(11) 98765-4321")]
    public void Digitar_Celular_AceitaNoveDigitosAposDdd(string digitos, string esperado) =>
        Assert.Equal(esperado, Digitar(digitos));

    [Theory]
    [InlineData("1932291122", "(19) 3229-1122")]
    [InlineData("1140044004", "(11) 4004-4004")]
    public void Digitar_Fixo_MantemOitoDigitosAposDdd(string digitos, string esperado) =>
        Assert.Equal(esperado, Digitar(digitos));

    [Fact]
    public void ApagarCelular_RedigitarFixo_TrocaDeMascara()
    {
        var mascara = TelefoneMascara.Criar();
        Digitar("19997018817", mascara);
        for (var i = 0; i < 15; i++)
            mascara.Backspace();

        Assert.Equal("(19) 3229-1122", Digitar("1932291122", mascara));
    }
}
