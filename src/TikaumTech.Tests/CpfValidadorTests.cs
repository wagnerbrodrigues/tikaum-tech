using TikaumTech.Services;
using Xunit;

namespace TikaumTech.Tests;

public class CpfValidadorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EhValido_VazioOuNulo_RetornaTrue(string? cpf) =>
        Assert.True(CpfValidador.EhValido(cpf));

    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    public void EhValido_CpfComDigitosCorretos_RetornaTrue(string cpf) =>
        Assert.True(CpfValidador.EhValido(cpf));

    [Theory]
    [InlineData("529.982.247-24")] // dígito verificador errado
    [InlineData("123.456.789-00")]
    [InlineData("111.111.111-11")] // sequência repetida
    [InlineData("000.000.000-00")]
    [InlineData("123")] // tamanho errado
    public void EhValido_CpfInvalido_RetornaFalse(string cpf) =>
        Assert.False(CpfValidador.EhValido(cpf));
}
