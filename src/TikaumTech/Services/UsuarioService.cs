using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TikaumTech.Data;

namespace TikaumTech.Services;

public class UsuarioService(UserManager<ApplicationUser> userManager)
{
    public Task<List<ApplicationUser>> ListarAsync() =>
        userManager.Users.OrderBy(u => u.UserName).ToListAsync();

    public async Task CriarAsync(string username, string? email, string senha)
    {
        var user = new ApplicationUser { UserName = username, Email = email, EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, senha);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));
    }

    public async Task AtualizarAsync(string id, string username, string? email, string? novaSenha)
    {
        var user = await userManager.FindByIdAsync(id)
            ?? throw new InvalidOperationException("Usuário não encontrado.");

        if (user.UserName != username)
        {
            var renomeio = await userManager.SetUserNameAsync(user, username);
            if (!renomeio.Succeeded)
                throw new InvalidOperationException(string.Join(", ", renomeio.Errors.Select(e => e.Description)));
        }

        user.Email = email;
        await userManager.UpdateAsync(user);

        if (!string.IsNullOrWhiteSpace(novaSenha))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var reset = await userManager.ResetPasswordAsync(user, token, novaSenha);
            if (!reset.Succeeded)
                throw new InvalidOperationException(string.Join(", ", reset.Errors.Select(e => e.Description)));
        }
    }

    public async Task DeletarAsync(string id)
    {
        if (await userManager.Users.CountAsync() <= 1)
            throw new InvalidOperationException("Não é possível excluir o único usuário do sistema.");

        var user = await userManager.FindByIdAsync(id)
            ?? throw new InvalidOperationException("Usuário não encontrado.");

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));
    }
}
