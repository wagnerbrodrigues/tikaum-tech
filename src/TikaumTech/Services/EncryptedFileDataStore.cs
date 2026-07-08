using Google.Apis.Json;
using Google.Apis.Util.Store;
using Microsoft.AspNetCore.DataProtection;

namespace TikaumTech.Services;

/// <summary>
/// IDataStore do Google OAuth com criptografia em repouso (TIKAUM_SPEC.md §8):
/// o token de acesso/refresh NUNCA é gravado em texto plano — cada valor é
/// protegido com ASP.NET Data Protection e salvo como config/google_token_*.enc.
/// Usa o serializador Newtonsoft do próprio Google.Apis (o TokenResponse é
/// anotado com atributos Newtonsoft; System.Text.Json quebraria o roundtrip).
/// </summary>
public class EncryptedFileDataStore(string folder, IDataProtector protector) : IDataStore
{
    public const string FilePrefix = "google_token_";

    public Task StoreAsync<T>(string key, T value)
    {
        Directory.CreateDirectory(folder);
        var json = NewtonsoftJsonSerializer.Instance.Serialize(value);
        File.WriteAllText(CaminhoDoArquivo<T>(key), protector.Protect(json));
        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        var path = CaminhoDoArquivo<T>(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key)
    {
        var path = CaminhoDoArquivo<T>(key);
        if (!File.Exists(path)) return Task.FromResult(default(T)!);
        try
        {
            var json = protector.Unprotect(File.ReadAllText(path));
            return Task.FromResult(NewtonsoftJsonSerializer.Instance.Deserialize<T>(json));
        }
        catch (Exception)
        {
            // Token ilegível (chave de proteção trocada/arquivo corrompido):
            // trata como "não conectado" — força um novo consentimento
            return Task.FromResult(default(T)!);
        }
    }

    public Task ClearAsync()
    {
        if (Directory.Exists(folder))
            foreach (var file in Directory.GetFiles(folder, FilePrefix + "*.enc"))
                File.Delete(file);
        return Task.CompletedTask;
    }

    private string CaminhoDoArquivo<T>(string key)
    {
        var bruto = $"{typeof(T).FullName}-{key}";
        var seguro = string.Join("_", bruto.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(folder, $"{FilePrefix}{seguro}.enc");
    }
}
