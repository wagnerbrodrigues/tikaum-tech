namespace TikaumTech.Models;

public class BackupStatus
{
    public BackupDestinoStatus PenDrive { get; set; } = new();
    public BackupDestinoStatus GoogleDrive { get; set; } = new();
}

public class BackupDestinoStatus
{
    public DateTime? UltimoOk { get; set; }
    public string Status { get; set; } = "nunca"; // ok | erro | nao_configurado | nunca
    public string? MensagemErro { get; set; }
}
