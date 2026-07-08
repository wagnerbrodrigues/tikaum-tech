namespace TikaumTech.Services;

/// <summary>
/// Disparo automático de backup (TIKAUM_SPEC.md §9): tenta ~20s após a inicialização
/// (deixa o app subir e abrir o navegador primeiro) e a cada 24h a partir daí.
/// Só tenta destino disponível — pen drive conectado / token do Drive presente; destino
/// ausente é pulado em silêncio (o BackupBanner já avisa o usuário na tela). Como o
/// snapshot é idempotente por dia (um arquivo por data), repetir nunca duplica nada.
/// </summary>
public class BackupAutomaticoService(BackupService backupService, ILogger<BackupAutomaticoService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await TentarBackupsAsync();
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal do app
        }
    }

    private async Task TentarBackupsAsync()
    {
        try
        {
            if (backupService.PenDriveConectado)
            {
                var (ok, mensagem) = await backupService.FazerBackupPenDriveAsync();
                logger.LogInformation("Backup automático (pen drive): {Resultado} — {Mensagem}",
                    ok ? "ok" : "falhou", mensagem);
            }

            if (backupService.GoogleDriveConfigurado)
            {
                var (ok, mensagem) = await backupService.FazerBackupGoogleDriveAsync();
                logger.LogInformation("Backup automático (Google Drive): {Resultado} — {Mensagem}",
                    ok ? "ok" : "falhou", mensagem);
            }
        }
        catch (Exception ex)
        {
            // Nunca derruba o app por causa de backup — o status/banner já reportam o erro
            logger.LogError(ex, "Erro inesperado no backup automático.");
        }
    }
}
