using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TikaumTech.Data;
using TikaumTech.Services;
using Xunit;

namespace TikaumTech.Tests;

/// <summary>
/// Cobre o UsuarioService com um UserManager REAL (UserStore sobre SQLite in-memory) —
/// sem mock: hash de senha, normalização de nome e validação são os mesmos da produção.
/// As opções de senha espelham as do Program.cs (RequiredLength=4, sem exigências extras).
/// </summary>
public class UsuarioServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UsuarioService _svc;

    public UsuarioServiceTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_conn).Options;
        _db = new ApplicationDbContext(opts);
        _db.Database.EnsureCreated();

        var identityOptions = Options.Create(new IdentityOptions());
        identityOptions.Value.Password.RequireDigit = false;
        identityOptions.Value.Password.RequireUppercase = false;
        identityOptions.Value.Password.RequireLowercase = false;
        identityOptions.Value.Password.RequireNonAlphanumeric = false;
        identityOptions.Value.Password.RequiredLength = 4;

        _userManager = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(_db),
            identityOptions,
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            [new PasswordValidator<ApplicationUser>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        // ResetPasswordAsync (usado por AtualizarAsync) exige um token provider registrado
        _userManager.RegisterTokenProvider(TokenOptions.DefaultProvider,
            new DataProtectorTokenProvider<ApplicationUser>(
                new EphemeralDataProtectionProvider(),
                Options.Create(new DataProtectionTokenProviderOptions()),
                NullLogger<DataProtectorTokenProvider<ApplicationUser>>.Instance));

        _svc = new UsuarioService(_userManager);
    }

    public void Dispose()
    {
        _userManager.Dispose();
        _db.Dispose();
        _conn.Dispose();
    }

    [Fact]
    public async Task CriarAsync_CriaUsuarioComSenhaFuncional()
    {
        await _svc.CriarAsync("tikaum", null, "admin");

        var user = await _userManager.FindByNameAsync("tikaum");
        Assert.NotNull(user);
        Assert.True(await _userManager.CheckPasswordAsync(user!, "admin"));
    }

    [Fact]
    public async Task CriarAsync_NomeDuplicado_Lanca()
    {
        await _svc.CriarAsync("tikaum", null, "admin");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.CriarAsync("tikaum", null, "outra"));
    }

    [Fact]
    public async Task ListarAsync_OrdenaPorNome_PrimeiroNaoAdminEhODefaultDasVendas()
    {
        await _svc.CriarAsync("tikaum", null, "admin");
        await _svc.CriarAsync("admin", null, "admin");
        await _svc.CriarAsync("zeca", null, "admin");

        var usuarios = await _svc.ListarAsync();
        var nomes = usuarios.Select(u => u.UserName).ToList();

        Assert.Equal(["admin", "tikaum", "zeca"], nomes);
        // Regra da tela de vendas (spec §6): primeiro usuário que não é admin
        Assert.Equal("tikaum", nomes.First(n => n != "admin"));
    }

    [Fact]
    public async Task AtualizarAsync_RenomeiaUsuario()
    {
        await _svc.CriarAsync("tikaum", null, "admin");
        var user = await _userManager.FindByNameAsync("tikaum");

        await _svc.AtualizarAsync(user!.Id, "tikaum2", null, null);

        Assert.Null(await _userManager.FindByNameAsync("tikaum"));
        Assert.NotNull(await _userManager.FindByNameAsync("tikaum2"));
    }

    [Fact]
    public async Task AtualizarAsync_RedefineSenha_SemExigirSenhaAntiga()
    {
        await _svc.CriarAsync("tikaum", null, "admin");
        var user = await _userManager.FindByNameAsync("tikaum");

        await _svc.AtualizarAsync(user!.Id, "tikaum", null, "novaSenha");

        var recarregado = await _userManager.FindByNameAsync("tikaum");
        Assert.False(await _userManager.CheckPasswordAsync(recarregado!, "admin"));
        Assert.True(await _userManager.CheckPasswordAsync(recarregado!, "novaSenha"));
    }

    [Fact]
    public async Task DeletarAsync_BloqueiaUltimoUsuario()
    {
        await _svc.CriarAsync("tikaum", null, "admin");
        var user = await _userManager.FindByNameAsync("tikaum");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.DeletarAsync(user!.Id));
        Assert.Contains("único usuário", ex.Message);
    }

    [Fact]
    public async Task DeletarAsync_RemoveQuandoHaMaisDeUmUsuario()
    {
        await _svc.CriarAsync("admin", null, "admin");
        await _svc.CriarAsync("tikaum", null, "admin");
        var tikaum = await _userManager.FindByNameAsync("tikaum");

        await _svc.DeletarAsync(tikaum!.Id);

        Assert.Null(await _userManager.FindByNameAsync("tikaum"));
        Assert.NotNull(await _userManager.FindByNameAsync("admin"));
    }
}
