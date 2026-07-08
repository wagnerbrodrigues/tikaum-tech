using System.Diagnostics;

namespace TikaumTech;

/// <summary>
/// Data/hora de modificação do executável em disco — usado só para o usuário conseguir
/// confirmar visualmente (tela de login / rodapé) se está rodando a versão recém-instalada
/// ou uma cópia antiga que o instalador não conseguiu substituir.
/// </summary>
public static class BuildInfo
{
    public static readonly DateTime Timestamp = LerDataDoExecutavel();

    private static DateTime LerDataDoExecutavel()
    {
        var caminho = Process.GetCurrentProcess().MainModule?.FileName;
        return !string.IsNullOrEmpty(caminho) && File.Exists(caminho)
            ? File.GetLastWriteTime(caminho)
            : DateTime.MinValue;
    }
}
