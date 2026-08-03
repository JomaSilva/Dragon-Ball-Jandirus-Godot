using Godot;
using Jandirus.Core.Forms;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// TRANSFORMACOES, LADO DO SERVIDOR.
///
/// Aqui e onde o multiplicador da forma vira poder de verdade: o <c>Fighter.ssjBuff</c> ja
/// existia no port da Etapa 3b (ele alimenta `formBuff` e o `tempBP *= BPBoost * formBuff` do
/// `powerlevel()`), e o que faltava era alguem escrever nele. Quem escreve e este arquivo, e so
/// ele -- o cliente nunca manda multiplicador nenhum, so pede "subir" ou "descer".
///
/// O CICLO DE UMA FORMA, por tick:
///   1. cobra o Ki (fracao do MaxKi por segundo, ver <see cref="EscadaSaiyajin.DrenoPorSegundo"/>);
///   2. sobe a maestria (so se ganha DENTRO da forma -- e o unico eixo que nao se compra);
///   3. recalcula o multiplicador (maestria muda ele em degraus, ao vivo);
///   4. se o Ki acabou, DERRUBA pra base.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// Quem usa a escada Saiyajin. O `ssj_ladder_user()` do original, na parte que ja da pra
	/// responder aqui: Saiyajin puro e mestico.
	/// </summary>
	private static bool UsaEscadaSaiyajin(ServerPlayer pl) =>
		pl.Race is "Saiyan" or "Halfbreed";

	/// <summary>Sangue diluido puxa a base do SSJ1 de 2 pra 1,35 (`ssj1base` nerfado).</summary>
	private static bool SangueDiluido(ServerPlayer pl) => pl.Race == "Halfbreed";

	/// <summary>
	/// "Quero subir" (ou descer). O cliente NAO escolhe a forma -- ele pede a direcao e o
	/// servidor decide qual degrau cabe. E o comportamento da tecla "C" do original, que sobe a
	/// escada sozinha, e evita o cliente pedir SSJ3 direto.
	/// </summary>
	private void Transformar(ServerPlayer pl, bool subir)
	{
		if (!UsaEscadaSaiyajin(pl))
		{
			Avisar(pl, "sua raca nao tem essa escada de transformacao.");
			return;
		}

		EstadoDeForma est = pl.Forma;
		bool diluido = SangueDiluido(pl);

		if (!subir)
		{
			if (est.Atual == Forma.Base) return;
			Forma antes = est.Atual;
			est.Entrar(Forma.Base);
			AplicarForma(pl);
			Avisar(pl, "voce volta ao normal.");
			AnunciarForma(pl, antes, Forma.Base, primeira: false);
			return;
		}

		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "nao da, caido."); return; }

		FormaDef? alvo = est.Proxima(pl.Ficha.BP, diluido);
		if (alvo == null)
		{
			Avisar(pl, PorQueNao(est, pl, diluido));
			return;
		}

		Forma anterior = est.Atual;
		bool primeira = est.Entrar(alvo.Id);
		AplicarForma(pl);

		// KI CHEIO AO DESPERTAR. E o que o original faz nas primeiras vezes e o que transforma a
		// cena: a forma nova nao pode nascer sem folego pra ser usada.
		if (primeira) pl.Ficha.Ki = pl.Ficha.MaxKi;

		GD.Print($"[server] {pl.Name}: {anterior} -> {alvo.Id} "
				 + $"(x{est.Multiplicador(diluido):0.##}, BP {pl.Ficha.BP:N0} -> {pl.Ficha.expressedBP:N0})"
				 + (primeira ? "  <- PRIMEIRA VEZ" : ""));

		Avisar(pl, primeira
			? $"VOCE DESPERTA: {alvo.Nome}!"
			: $"{alvo.Nome} (x{est.Multiplicador(diluido):0.##}).");

		AnunciarForma(pl, anterior, alvo.Id, primeira);
	}

	/// <summary>A mensagem que explica o que falta. Ver o comentario de `Avaliar`.</summary>
	private static string PorQueNao(EstadoDeForma est, ServerPlayer pl, bool diluido)
	{
		// procura o degrau mais barato a partir daqui e conta o que falta NELE
		FormaDef? candidato = null;
		RecusaForma pior = RecusaForma.JaEsta;
		foreach (FormaDef d in EscadaSaiyajin.Degraus)
		{
			if (d.Id == est.Atual) continue;
			RecusaForma r = est.Avaliar(d.Id, pl.Ficha.BP, pl.Ficha.MaxKi > 0 ? pl.Ficha.Ki / pl.Ficha.MaxKi : 1,
										pl.Ficha.KO || pl.Ficha.dead, diluido);
			if (r is RecusaForma.ForaDeOrdem or RecusaForma.JaEsta) continue;
			candidato = d; pior = r; break;
		}
		if (candidato == null) return "nao ha mais degrau acima deste.";

		return pior switch
		{
			// SEM NUMERO. Esta frase desfazia sozinha a reforma da aba Forms: o limiar saia da tela e
			// voltava pela mensagem de erro. E o limiar e SORTEADO por personagem -- dize-lo entrega
			// de graca o que o jogo quer que se descubra tentando.
			RecusaForma.SemPoder => $"{candidato.Nome} ainda esta alem do seu alcance.",
			RecusaForma.SemMaestria => $"{candidato.Nome} pede {candidato.PedeMaestria:0}% de maestria no degrau anterior "
									 + $"(voce tem {est.Maestria.De(candidato.Vem):0.#}%).",
			RecusaForma.SemKi => "Ki baixo demais pra sustentar a forma.",
			RecusaForma.Caido => "nao da, caido.",
			_ => "ainda nao.",
		};
	}

	/// <summary>Escreve o multiplicador na ficha. E o unico lugar que mexe no `ssjBuff`.</summary>
	private static void AplicarForma(ServerPlayer pl)
	{
		pl.Ficha.ssjBuff = pl.Forma.Multiplicador(SangueDiluido(pl));
		pl.SigAtributos = "";   // a aba Forms mostra maestria: forca o proximo pacote a sair
	}

	/// <summary>
	/// O TICK DA FORMA: cobra Ki, sobe maestria, derruba quem ficou sem folego.
	/// </summary>
	private void TickDaForma(ServerPlayer pl, double dt)
	{
		EstadoDeForma est = pl.Forma;
		if (est.Atual == Forma.Base) return;

		if (pl.Ficha.KO || pl.Ficha.dead) { Reverter(pl, "voce cai, e a forma se desfaz."); return; }

		double dreno = est.DrenoPorSegundo() * pl.Ficha.MaxKi * dt;
		pl.Ficha.Ki -= dreno;

		if (pl.Ficha.Ki <= 0)
		{
			pl.Ficha.Ki = 0;
			Reverter(pl, "o Ki acaba e a forma se desfaz.");
			return;
		}

		// MAESTRIA SO CRESCE DENTRO DA FORMA. Sustentar a transformacao E o treino dela.
		if (est.Maestria.Subir(est.Atual, EscadaSaiyajin.MaestriaPorSegundo * dt, out string marco))
		{
			Avisar(pl, $"{EscadaSaiyajin.Def(est.Atual)?.Nome}: {marco}.");
			GD.Print($"[server] {pl.Name}: {est.Atual} -> {marco}");
		}

		// o multiplicador muda EM DEGRAUS conforme a maestria sobe, e o degrau pode cair no meio
		// da luta -- por isso recalcula por tick em vez de so na transformacao
		AplicarForma(pl);
	}

	private void Reverter(ServerPlayer pl, string motivo)
	{
		Forma antes = pl.Forma.Atual;
		pl.Forma.Entrar(Forma.Base);
		AplicarForma(pl);
		Avisar(pl, motivo);
		AnunciarForma(pl, antes, Forma.Base, primeira: false);
	}

	/// <summary>
	/// Conta pra ZONA que alguem mudou de forma. E o que acende a aura e pinta o cabelo nos
	/// outros clientes -- e o que dispara a cinematica no dono, quando e a primeira vez.
	/// </summary>
	private void AnunciarForma(ServerPlayer pl, Forma de, Forma para, bool primeira)
	{
		var w = Protocol.Begin(Protocol.S2C.Forma);
		w.Put(pl.Id);
		w.Put((ushort)de);
		w.Put((ushort)para);
		w.Put(primeira);
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
