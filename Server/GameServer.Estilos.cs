using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// ESTILOS DE LUTA, LADO DO SERVIDOR.
///
/// O SOQUETE JA ESTAVA PRONTO: os dez campos `*Style` do lutador ja multiplicavam a cadeia inteira
/// de stats, e nenhum deles era escrito por ninguem. Aqui e onde eles passam a ser escritos -- e o
/// `CheckStyle()` do original, que la roda num laco de 1,5 s e aqui roda quando o estilo MUDA,
/// porque nada mais mexe naqueles campos.
///
/// TRES COISAS DEPENDEM DISTO E NENHUMA E OPCIONAL:
///   1. os multiplicadores (o motivo de existir);
///   2. a MAESTRIA, que sobe com o tempo e enferruja nos estilos parados;
///   3. o dano PLANO da disputa de estilos, que entra em cada soco.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// Quanto tempo de maestria cada um ja acumulou desde o ultimo ganho. E o `mastery_buffer`
	/// do original: la sao 100 tiques de 1,5 s, aqui sao os mesmos 150 segundos contados direto.
	/// </summary>
	private readonly Dictionary<int, double> _bufferDeEstilo = [];

	private void CarregarEstilos()
	{
		const string cj = "res://Assets/Data/estilos.json";
		if (!Godot.FileAccess.FileExists(cj))
		{
			GD.PushWarning("[server] sem estilos.json -- rode o AssetPipeline (comando 'estilos')");
			return;
		}
		Estilos.Carregar(Godot.FileAccess.GetFileAsString(cj));
		GD.Print($"[server] estilos: {Estilos.Total} no catalogo");
	}

	/// <summary>
	/// OS ESTILOS QUE ESTA PESSOA APRENDEU. Vem das skills, e so nove skills no jogo inteiro
	/// concedem um -- as sete de rank (ensinadas, nunca compradas) e a Martial Arts, que e a
	/// porta de entrada de todo mundo.
	/// </summary>
	private List<Estilo> EstilosDe(ServerPlayer pl)
	{
		var l = new List<Estilo>();
		if (_skills == null) return l;
		foreach (string path in pl.Livro.Aprendidas)
		{
			Skill? s = _skills.Get(path);
			if (s == null || s.Estilo.Length == 0) continue;
			Estilo? e = Estilos.Get(s.Estilo);
			if (e != null && !l.Contains(e)) l.Add(e);
		}
		return l;
	}

	/// <summary>
	/// ESCREVE OS DEZ MULTIPLICADORES no lutador. Sem estilo, todos voltam a 1.
	///
	/// ZERAR ANTES DE ESCREVER e o ponto: como o valor e SUBSTITUIDO e nao somado, trocar de
	/// estilo nunca acumula. O original tem exatamente este bug ao contrario -- o `UpdateStyle()`
	/// dele so faz `max()`, entao um multiplicador que subiu nunca desce, e um `styleReset()`
	/// deixa o jogador com 4x de velocidade pra sempre (Style.dm:117-126). Aqui a fonte da verdade
	/// e o catalogo, e o catalogo e recalculado inteiro a cada troca.
	/// </summary>
	private void AplicarEstilo(ServerPlayer pl)
	{
		Estilo? e = Estilos.Get(pl.Ficha.EstiloAtual);

		// A POSTURA QUE ELE NAO SABE MAIS CAI SOZINHA -- e este e o unico funil por onde os dez
		// multiplicadores sao escritos, entao e aqui que a pergunta tem que ser feita.
		//
		// SEIS DOS SETE ESTILOS DE LUTA DO JOGO VEM DE UM CARGO (`DadivaDeCargo`) e voltam com ele.
		// O `TrocarEstilo` ja perguntava "voce aprendeu?" na hora de VESTIR, mas ninguem perguntava
		// depois -- entao perder o cargo (ou o login, que reaplica o estilo lido do save) deixava a
		// ficha com os numeros de um estilo que o livro nao tem mais.
		//
		// A GUARDA DO `_skills` NAO E ZELO: sem catalogo o `EstilosDe` devolve lista vazia, e sem
		// ela um servidor que subiu sem `skills.json` despiria todo mundo em silencio.
		if (e != null && _skills != null && !EstilosDe(pl).Contains(e))
		{
			pl.Ficha.EstiloAtual = "";
			e = null;
		}

		var f = pl.Ficha;

		f.physoffStyle = f.physdefStyle = f.techniqueStyle = 1;
		f.kioffStyle = f.kidefStyle = f.kiskillStyle = f.speedStyle = 1;
		f.kiregenStyle = f.staminadrainStyle = 1;
		// magiStyle NAO entra: nenhum estilo do original o declara e nada o move. Ver Estilos.cs.

		if (e != null)
			foreach ((string campo, double v) in Estilos.Multiplicadores(e))
				switch (campo)
				{
					case "physoffStyle": f.physoffStyle = v; break;
					case "physdefStyle": f.physdefStyle = v; break;
					case "techniqueStyle": f.techniqueStyle = v; break;
					case "kioffStyle": f.kioffStyle = v; break;
					case "kidefStyle": f.kidefStyle = v; break;
					case "kiskillStyle": f.kiskillStyle = v; break;
					case "speedStyle": f.speedStyle = v; break;
					case "kiregenStyle": f.kiregenStyle = v; break;
					case "staminadrainStyle": f.staminadrainStyle = v; break;
				}

		f.Statify();
		pl.SigAtributos = "";
	}

	/// <summary>
	/// TROCA DE ESTILO. Sem custo, sem recarga, sem gate -- e assim no original (`PickStyle`,
	/// Style.dm:160-176) e faz sentido: o preco do estilo foi pago ao aprende-lo, e a escolha de
	/// postura no meio da luta e justamente o que a mecanica quer que aconteca.
	/// </summary>
	private void TrocarEstilo(ServerPlayer pl, string id)
	{
		if (id.Length == 0 || id == "-")
		{
			pl.Ficha.EstiloAtual = "";
			AplicarEstilo(pl);
			Avisar(pl, "voce solta a postura.");
			MandarEstilos(pl);
			return;
		}

		Estilo? e = Estilos.Get(id);
		if (e == null) { Avisar(pl, "esse estilo nao existe."); return; }
		if (!EstilosDe(pl).Contains(e)) { Avisar(pl, $"voce nao aprendeu {e.Nome}."); return; }

		pl.Ficha.EstiloAtual = e.Id;
		AplicarEstilo(pl);
		Avisar(pl, $"voce assume a postura de {e.Nome}.");
		MandarEstilos(pl);
	}

	/// <summary>
	/// A MAESTRIA ANDANDO. Roda a cada segundo e so rende de 150 em 150 -- o mesmo passo do
	/// original, contado em tempo em vez de em tiques.
	///
	/// E AQUI QUE OS ESTILOS PARADOS ENFERRUJAM: 10% de chance de o estilo NAO usado perder 4% da
	/// maestria a cada ganho. Sem isso da pra maximizar todos e nunca escolher, e a escolha e a
	/// mecanica inteira.
	/// </summary>
	private void TickDosEstilos()
	{
		foreach (ServerPlayer pl in _players.Values)
		{
			Estilo? atual = Estilos.Get(pl.Ficha.EstiloAtual);
			if (atual == null || pl.Ficha.dead) continue;

			double buffer = _bufferDeEstilo.GetValueOrDefault(pl.Id) + 1;
			if (buffer < Estilos.SegundosPorGanho) { _bufferDeEstilo[pl.Id] = buffer; continue; }
			_bufferDeEstilo[pl.Id] = 0;

			double teto = Estilos.TetoDe(pl.Ficha.TetoDeEstilo.GetValueOrDefault(atual.Id, 5), atual.Pontos);
			pl.Ficha.TetoDeEstilo[atual.Id] = teto;

			double m = pl.Ficha.MaestriaDeEstilo.GetValueOrDefault(atual.Id);
			m += Estilos.GanhoDeMaestria(atual.Pontos, teto, pl.Ficha.train, pl.Ficha.IsInFight);
			pl.Ficha.MaestriaDeEstilo[atual.Id] = Math.Min(teto, m);

			foreach (Estilo outro in EstilosDe(pl))
			{
				if (outro == atual) continue;
				if (_rng.NextDouble() >= Estilos.ChanceDeEnferrujar) continue;
				double v = pl.Ficha.MaestriaDeEstilo.GetValueOrDefault(outro.Id);
				pl.Ficha.MaestriaDeEstilo[outro.Id] = v - v * Estilos.PerdaAoEnferrujar;
			}

			MandarEstilos(pl);
		}
	}

	/// <summary>
	/// O BONUS DE DANO da disputa de estilos, somado plano no golpe (`calcs.dm:87`).
	/// Teto de 10 -- e pouco de proposito: estilo desempata, nao decide.
	/// </summary>
	private double DanoDeEstilo(ServerPlayer a, ServerPlayer d)
	{
		string meu = a.Ficha.EstiloAtual, dele = d.Ficha.EstiloAtual;
		if (meu.Length == 0) return 0;
		return Estilos.DanoDeEstilo(
			meu, a.Ficha.MaestriaDeEstilo.GetValueOrDefault(meu),
			dele, d.Ficha.MaestriaDeEstilo.GetValueOrDefault(dele),
			a.Ficha.Etechnique, _rng.NextDouble);
	}

	/// <summary>Manda o estilo atual, os aprendidos e a maestria de cada um.</summary>
	private void MandarEstilos(ServerPlayer pl)
	{
		List<Estilo> meus = EstilosDe(pl);
		var w = Protocol.Begin(Protocol.S2C.Estilos);
		w.Put(pl.Ficha.EstiloAtual);
		w.Put((byte)meus.Count);
		foreach (Estilo e in meus)
		{
			w.Put(e.Id);
			w.Put(e.Nome);
			w.Put(pl.Ficha.MaestriaDeEstilo.GetValueOrDefault(e.Id));
			w.Put(pl.Ficha.TetoDeEstilo.GetValueOrDefault(e.Id, 5));
		}
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
