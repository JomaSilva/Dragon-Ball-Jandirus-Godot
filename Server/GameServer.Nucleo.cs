using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// (2026-10-17) O NUCLEO DO BIO-ANDROIDE -- o adendo do dono: escolhido UMA vez entre braco esquerdo, braco direito, perna esquerda, perna
/// direita e cabeca; ele reergue o corpo com 5% de tudo quando a morte chega; nao funciona com o membro dele ferido; reergue ate DUAS vezes,
/// e na segunda detona e destroi o membro. A volta do Majin vem antes -- e vem antes de graca: o Majin se desfaz no `QuandoONucleoCede`, que
/// e ANTES do `Morrer`, e este e o ultimo elo do `NegarMorte` (depois do SSJ2 do bio, da aura e das raciais -- `PrepararCombate`).
///
/// LEITURAS (vao pro relatorio): "ferido" = quebrado (abaixo de 20%) ou decepado; o corpo volta com 5% em CADA membro, 5% de Ki e de folego,
/// e cai NOCAUTEADO por alguns segundos (o corpo se refazendo do nucleo); a detonacao decepa o braco ou a perna, e na cabeca a deixa em
/// pedacos (perder a cabeca e morrer); a escolha e permanente. So o bio de LABORATORIO tem nucleo -- o Cell da saga ja tem a volta dele (o
/// degrau de ressurgir do roteiro).
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// ESCOLHER O NUCLEO -- o verb `nucleo:<membro>` (aba Skills: um botao por lugar, so enquanto nao escolhido). Uma vez, pra sempre.
	/// </summary>
	private void EscolherNucleo(ServerPlayer pl, string lugar)
	{
		Fighter f = pl.Ficha;
		if (!f.bio_lab_born) { Avisar(pl, "seu corpo não tem núcleo."); return; }
		if (f.bio_nucleo.Length > 0) { Avisar(pl, $"o seu núcleo já tem lugar: {MembroNaFrase(f.bio_nucleo)} -- e é pra sempre."); return; }
		string? onde = Array.Find(BioAndroids.LugaresDoNucleo, l => string.Equals(l, lugar, StringComparison.OrdinalIgnoreCase));
		if (onde == null)
		{
			Avisar(pl, "escolha onde o seu núcleo mora: braço esquerdo, braço direito, perna esquerda, perna direita ou cabeça.");
			return;
		}
		f.bio_nucleo = onde;
		f.bio_nucleo_usos = 0;
		Persistir(pl);
		Avisar(pl, $"o seu NÚCLEO se assenta: {MembroNaFrase(onde)}. Enquanto esse membro estiver inteiro, ele te reergue da morte -- duas "
				   + "vezes, e na segunda ele detona.");
		GD.Print($"[server] {pl.Name}: o nucleo mora em '{onde}'");
	}

	/// <summary>
	/// (2026-10-17) UM MEMBRO NA FRASE, com o artigo: "o braço esquerdo", "a perna direita", "a cabeça", "o tronco" -- o nome do fio vem sem
	/// acento, e a tela o escreve com (`DebuffsDeMembro.NomeNaTela`). Com <paramref name="seu"/>, o possessivo: "o seu braço", "a sua cabeça".
	/// </summary>
	private static string MembroNaFrase(string nome, bool seu = false)
	{
		bool feminino = nome.StartsWith("Perna", StringComparison.Ordinal) || nome.StartsWith("Mao", StringComparison.Ordinal) || nome == "Cabeca";
		return (feminino ? (seu ? "a sua " : "a ") : (seu ? "o seu " : "o ")) + DebuffsDeMembro.NomeNaTela(nome).ToLowerInvariant();
	}

	/// <summary>
	/// O NUCLEO NEGA A MORTE -- o ultimo elo do `NegarMorte`. O corpo volta com 5% de tudo e cai nocauteado; na segunda vez o nucleo DETONA e
	/// destroi o membro em que morava. Com o membro ferido ele nao faz nada (e o corpo fica sabendo por que morreu).
	/// </summary>
	private bool NegarMortePeloNucleo(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		if (!f.bio_lab_born || f.bio_nucleo.Length == 0 || f.bio_nucleo_usos >= BioAndroids.RevividasDoNucleo) return false;
		if (pl.Combate is not { } c || EhCadaver(pl)) return false;
		if (!BioAndroids.NucleoInteiro(c.Corpo, f.bio_nucleo))
		{
			Avisar(pl, $"o seu núcleo estava ferido ({MembroNaFrase(f.bio_nucleo)}): nada te reergue desta vez.");
			return false;
		}

		f.bio_nucleo_usos++;
		double refil = BioAndroids.RefilDoNucleo;
		foreach (BodyPart p in c.Corpo.Partes)
			if (!p.Decepado) p.Vida = p.VidaMax * refil;
		c.SincronizarVida();
		f.Ki = f.MaxKi * refil;
		f.stamina = f.maxstamina * refil;
		c.Nocautear(BioAndroids.SegundosNoChaoPeloNucleo);

		bool detonou = f.bio_nucleo_usos >= BioAndroids.RevividasDoNucleo;
		if (detonou && c.Corpo.Achar(f.bio_nucleo) is { Decepado: false } membro)
		{
			// O BRACO E A PERNA SAEM (a peca cai no chao -- `Arrancar`, o `LopLimb` de sempre); A CABECA FICA EM PEDACOS: perde-la e morrer.
			if (membro.Papel == Vitalidade.Membro) c.Arrancar(membro);
			else membro.Vida = membro.VidaMax * 0.01;
			c.SincronizarVida();
		}
		CenaDoBio(pl, Jandirus.Core.Forms.Cinematicas.CenaBio.Rompimento);   // o clarao da carapaca: o corpo refeito do nucleo
		Persistir(pl);
		Avisar(pl, detonou
			? $"o seu NÚCLEO reergue o corpo pela última vez -- e DETONA: {MembroNaFrase(f.bio_nucleo)} se desfaz. Não há mais núcleo."
			: $"a morte chega... e o seu NÚCLEO pulsa: o corpo se refaz a partir d{MembroNaFrase(f.bio_nucleo)}, com 5% de tudo. "
			  + "Ele aguenta mais UMA vez.");
		AvisarPertoG3(pl, RaioDaVista, $"o corpo de {pl.Name} se refaz a partir de um núcleo pulsante!", excetoCentro: true);
		GD.Print($"[server] {pl.Name}: o NUCLEO negou a morte ({f.bio_nucleo_usos}/{BioAndroids.RevividasDoNucleo}{(detonou ? ", detonou" : "")})");
		return true;
	}
}
