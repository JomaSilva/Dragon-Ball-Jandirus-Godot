using System.Text.Json;
using Godot;

namespace Jandirus.Client;

/// <summary>Um servidor onde ja se jogou, com a conta usada la.</summary>
public sealed class Perfil
{
    public string Servidor = "127.0.0.1";
    public string Conta = "";

    /// <summary>
    /// A senha, quando o jogador pediu pra lembrar. Fica em texto no arquivo do perfil, na
    /// pasta de dados do jogo -- e o mesmo compromisso que o "lembrar senha" de qualquer
    /// jogo faz. Por isso e uma escolha SEPARADA de lembrar o servidor e a conta: da pra
    /// guardar o endereco sem guardar a senha.
    /// </summary>
    public string Senha = "";

    /// <summary>
    /// Este servidor era HOSPEDADO por mim. Voltar a este perfil sobe o servidor de novo
    /// antes de conectar -- senao o jogador clica no proprio servidor e nao ha ninguem
    /// escutando do outro lado.
    /// </summary>
    public bool Hospedado;

    public long UltimoAcesso;

    public string Rotulo => (Conta.Length > 0 ? $"{Conta} @ {Servidor}" : Servidor) + (Hospedado ? "  (meu)" : "");
}

/// <summary>
/// PERFIS SALVOS NESTA MAQUINA. Servem pra voltar a um servidor sem redigitar IP, conta e
/// senha -- como o Project Zomboid faz.
///
/// Sao do CLIENTE e so dele: o servidor nunca ve este arquivo. Fica em `user://`, que e a
/// pasta de dados do jogador (o `res://` e somente leitura numa build exportada).
/// </summary>
public static class Profiles
{
    private const string Arquivo = "user://perfis.json";

    private static readonly JsonSerializerOptions Opcoes = new()
    {
        IncludeFields = true,
        WriteIndented = true,
    };

    public static List<Perfil> Carregar()
    {
        if (!Godot.FileAccess.FileExists(Arquivo)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<Perfil>>(
                Godot.FileAccess.GetFileAsString(Arquivo), Opcoes) ?? [];
        }
        catch (Exception e)
        {
            GD.PushWarning($"[perfis] arquivo ilegivel: {e.Message}");
            return [];
        }
    }

    public static void Gravar(List<Perfil> perfis)
    {
        using Godot.FileAccess? f = Godot.FileAccess.Open(Arquivo, Godot.FileAccess.ModeFlags.Write);
        if (f == null) { GD.PushWarning("[perfis] nao consegui gravar"); return; }
        f.StoreString(JsonSerializer.Serialize(perfis, Opcoes));
    }

    /// <summary>
    /// Registra (ou atualiza) um perfil e o poe no topo. A chave e servidor+conta: a mesma
    /// conta em dois servidores sao dois perfis, que e o ponto de nao haver conta global.
    /// </summary>
    public static void Lembrar(string servidor, string conta, string? senha, bool hospedado = false)
    {
        List<Perfil> perfis = Carregar();
        perfis.RemoveAll(p => p.Servidor == servidor && string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase));
        perfis.Insert(0, new Perfil
        {
            Servidor = servidor,
            Conta = conta,
            Senha = senha ?? "",
            Hospedado = hospedado,
            UltimoAcesso = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
        if (perfis.Count > 12) perfis.RemoveRange(12, perfis.Count - 12);
        Gravar(perfis);
    }

    public static void Esquecer(Perfil p)
    {
        List<Perfil> perfis = Carregar();
        perfis.RemoveAll(x => x.Servidor == p.Servidor && x.Conta == p.Conta);
        Gravar(perfis);
    }
}
