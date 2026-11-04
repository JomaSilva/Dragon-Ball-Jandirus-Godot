using System.Text.Json;
using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// ============================ OS CUSTOS DO OUTRO MUNDO NA CONFIGURACAO DO SERVIDOR (2026-11-01, o pedido 213) ============================
/// O dono: *"E faca custo de reviver, custo de visitar mundo dos vivos morto etc ser uma config de adm do server, assim n precisando
/// recompilar o jogo sempre q for alterar algo"*. Os numeros moram em `Core/World/CustosDoAlem.cs`; aqui ficam as tres portas deles:
///   * o ARQUIVO: o `servidor.json` (o mesmo do modo das racas, `GameServer.ModoDeRacas.cs`) guarda os quatro, e o boot os le
///     (<see cref="CarregarCustosDoAlem"/>) -- quem administra a VPS tambem pode edita-lo a mao, com o servidor desligado;
///   * o VERB da aba Admin (<see cref="VerboDosCustosDoAlem"/>): sem argumento responde os de agora; com `chave valor`, muda, grava o
///     arquivo e manda os numeros novos pra todo mundo;
///   * o FIO (<see cref="MandarCustosDoAlem"/>): no login e a cada mudanca, pros botoes da mesa do Enma e o cartao da aba Admin.
/// A volta paga (`PrecoDoEnmaPara`) e o passe (`EnmaPasse`) cobram pelos numeros vivos.
/// ===========================================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>O verb da aba Admin: `admin_custos_alem` (os de agora) ou `admin_custos_alem chave valor` (muda).</summary>
	internal const string VerboDosCustosDoAlem = "admin_custos_alem";

	/// <summary>Os custos deste processo ja foram lidos do arquivo? Antes disso, gravar o modo das racas nao pode escrever o padrao por cima deles.</summary>
	private static bool _custosDoAlemLidos;

	/// <summary>Os custos gravados neste `servidor.json` (os padroes, sem o arquivo ou sem os campos -- o arquivo de antes do pedido 213).</summary>
	private static void CarregarCustosDoAlem(string caminho)
	{
		ConfiguracaoDoServidor c = new();
		try
		{
			if (File.Exists(caminho)) c = JsonSerializer.Deserialize<ConfiguracaoDoServidor>(File.ReadAllText(caminho)) ?? new();
		}
		catch (Exception e) { GD.PushWarning($"[server] {NomeDoArquivoDoServidor} ilegivel ({e.Message}) -- os custos do Outro Mundo ficam no padrao"); }
		CustosDoAlem.Por(c.PrecoDaVoltaNoEnma, c.MultiplicadorDoHakaiNoEnma, c.PrecoDoPasseDoEnma, c.DiasDoPasseDoEnma);
		_custosDoAlemLidos = true;
		GD.Print($"[server] custos do Outro Mundo: {CustosDoAlem.Resumo()}");
	}

	/// <summary>O `admin_custos_alem` -- ver o cabecalho deste arquivo.</summary>
	private void AdminCustosDoAlem(ServerPlayer adm, string arg)
	{
		string[] p = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (p.Length == 0)
		{
			Avisar(adm, $"[admin] custos do Outro Mundo: {CustosDoAlem.Resumo()}.");
			MandarCustosDoAlem(adm);
			return;
		}
		if (p.Length < 2) { Avisar(adm, $"use: {VerboDosCustosDoAlem} <revive|hakai|passe|dias> <valor>"); return; }
		if (CustosDoAlem.Mudar(p[0], p[1]) is { } recusa) { Avisar(adm, $"[admin] {recusa}"); return; }

		GravarConfiguracaoDoServidor();
		GD.Print($"[server] custos do Outro Mundo: {CustosDoAlem.Resumo()} (por {adm.Name}, {adm.Conta})");
		foreach (ServerPlayer o in Jogadores)
		{
			MandarCustosDoAlem(o);
			if (EhAdmin(o)) Avisar(o, $"[admin] {adm.Name} mudou os custos do Outro Mundo: {CustosDoAlem.Resumo()}.");
		}
		if (!Jogadores.Contains(adm)) Avisar(adm, $"[admin] {adm.Name} mudou os custos do Outro Mundo: {CustosDoAlem.Resumo()}.");
	}

	/// <summary>
	/// OS NUMEROS PRO CLIENTE: `double volta` + `double multiplicador do Hakai` + `double passe` + `int dias`. No login e a cada mudanca,
	/// pra todo jogador -- os botoes da mesa do Enma os escrevem. Ver `Protocol.S2C.CustosDoAlem`.
	/// </summary>
	private void MandarCustosDoAlem(ServerPlayer pl)
	{
		if (pl.Peer == null) return;
		var w = Protocol.Begin(Protocol.S2C.CustosDoAlem);
		w.Put(CustosDoAlem.PrecoDoRevive);
		w.Put(CustosDoAlem.MultiplicadorDoHakai);
		w.Put(CustosDoAlem.PrecoDoPasse);
		w.Put(CustosDoAlem.DiasDoPasse);
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
