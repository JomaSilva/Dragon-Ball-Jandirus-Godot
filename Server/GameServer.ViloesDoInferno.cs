using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// OS VILOES DE SAGA NO INFERNO -- o encanamento do <see cref="ViloesDoInferno"/> (o pedido do dono, as escolhas dele e os numeros
/// estao la). Feito no molde do vilao avulso (`GameServer.Viloes.cs`), que ja e um evento com relogio, aviso, premio e prazo.
///
/// ============================ O CICLO ============================
///   1. O RELOGIO SO ANDA COM GENTE NO OUTRO MUNDO -- em qualquer das tres zonas (o Outro Mundo, o Ceu, o Inferno): o morto que anda
///      ate la pela passagem, ou o Demonio que nasceu la. Sem ninguem, ele para; o primeiro que chega o recomeca.
///   2. NASCE UM CHEFE DERROTADO DA CADEIA (sorteado entre os que caíram), no Inferno, longe de quem esta la -- pela MESMA receita do
///      chefe convocado na mente (`ChamarChefe`): `NascerNpc` + o BP pinado + o degrau. O BP e o da media de AGORA
///      (`ViloesDoInferno.Bps`), o degrau e o em que ele caiu. Caca o Inferno inteiro (`PapelDeNpc.DaSaga`), tem o Ki de chefe, e e um
///      MORTO DE PE de aureola (<see cref="FazerMortoDoAlem"/>), como todo mundo la.
///   3. O OUTRO MUNDO E AVISADO, com o premio e o prazo, e quem esta nas tres zonas ve a cena de chegada de chefe.
///   4. ACABA de tres jeitos: alguem o DERROTA (nocaute, ou a "segunda morte" do morto -- o premio vai pra esse alguem, uma vez), o PRAZO
///      vence (ele afunda de volta, sem premio), ou o corpo some por outra porta.
/// ================================================================
///
/// A "SEGUNDA MORTE" E A DERROTA. Ele ja esta morto: um golpe letal nele chama o `AoMorrerDeNovo`, que no jogador e a volta a mesa do
/// Enma (`MorrerDeNovoNoAlem`). Aqui o gancho e trocado pelo da derrota (<see cref="VilaoDoInfernoFoiDerrotado"/>) -- senao o Freeza
/// derrotado iria parar de pe na frente do Enma.
/// </summary>
public sealed partial class GameServer
{
	private sealed class VilaoNoInferno
	{
		public int Npc;
		public string Molde = "", Elo = "", Nome = "";
		public long Ate;

		/// <summary>Ela (a Androide 18) ou ele -- o aviso fala no genero de quem voltou.</summary>
		public bool Ela;

		/// <summary>Alguem ja o derrotou: o premio foi pago, e o tique so falta tirar o corpo. Ver <see cref="VilaoDoInfernoFoiDerrotado"/>.</summary>
		public bool Derrotado;
	}

	/// <summary>O vilao de pe no Inferno -- um por vez no servidor inteiro. Nulo = nenhum.</summary>
	private VilaoNoInferno? _vilaoDoInferno;

	/// <summary>Quando o proximo pode aparecer (ms do relogio de parede). Zero = o relogio esta parado (ninguem no Outro Mundo).</summary>
	private long _proximoVilaoDoInferno;

	/// <summary>A faixa propria de "lugar" (a semente do sorteio do NPC), longe das do povoamento, das sagas e dos viloes avulsos.</summary>
	private ulong _lugarDoInferno = 0x7120_0000UL;

	/// <summary>A MOEDA DA BANCADA: um sorteio fixo no lugar do `_rng`. Nulo em jogo.</summary>
	internal double? SorteioDoInfernoDeTeste;

	private double SorteioDoInferno() => SorteioDoInfernoDeTeste ?? _rng.NextDouble();

	private long IntervaloDoInferno() => (long)(ViloesDoInferno.ProximoIntervaloS(SorteioDoInferno()) * 1000);

	/// <summary>Alguem que pode lutar esta no Outro Mundo (em qualquer das tres zonas)? So jogador, e nao o cadaver.</summary>
	private bool GenteNoOutroMundo()
	{
		foreach (ServerPlayer p in _players.Values)
			if (EhJogador(p) && Alem.EhOAlem(p.Zone) && !EhCadaver(p)) return true;
		return false;
	}

	/// <summary>O tique, a 1 Hz: fecha o evento que acabou e abre o proximo quando e a hora.</summary>
	private void TickDosViloesDoInferno()
	{
		long agora = NowMs();

		if (_vilaoDoInferno is { } v)
		{
			// O VILAO ESTA SEMPRE "MORTO" (de pe, no Inferno): "de pe" aqui e so existir. O `!dead` do vilao avulso nao serve.
			bool noMundo = _players.TryGetValue(v.Npc, out ServerPlayer? npc);
			if (noMundo && !v.Derrotado && agora < v.Ate) return;
			if (noMundo)
			{
				// O PRAZO VENCEU: ele afunda de volta, sem premio pra ninguem. (O DERROTADO sai calado: a derrota ja foi anunciada.)
				if (!v.Derrotado)
					AnunciarNoOutroMundo($"{v.Nome} cansou de esperar por um desafiante e afundou de volta nas profundezas do Inferno.");
				RemoverNpc(npc!);
			}
			_vilaoDoInferno = null;
			// O INTERVALO CONTA DO FIM do evento -- derrotado no primeiro minuto ou ido embora no ultimo, o mesmo sossego.
			_proximoVilaoDoInferno = GenteNoOutroMundo() ? agora + IntervaloDoInferno() : 0;
			return;
		}

		if (!ViloesDoInferno.Ligado) return;
		if (!GenteNoOutroMundo()) { _proximoVilaoDoInferno = 0; return; }   // ninguem la: o relogio para

		// O PRIMEIRO A CHEGAR LIGA O RELOGIO -- ninguem e recebido por um vilao no instante em que morre.
		if (_proximoVilaoDoInferno == 0) { _proximoVilaoDoInferno = agora + IntervaloDoInferno(); return; }
		if (agora < _proximoVilaoDoInferno) return;

		// NASCEU OU NAO, o relogio anda: sem vilao derrotado na cadeia (nenhuma saga vencida ainda) o Inferno so espera de novo.
		_proximoVilaoDoInferno = agora + IntervaloDoInferno();
		NascerVilaoDoInferno(agora);
	}

	/// <summary>
	/// POE UM VILAO DERROTADO NO INFERNO e avisa o Outro Mundo. <paramref name="qualquerDaCadeia"/> e do ADMIN: sem vilao derrotado
	/// ainda, o verb sorteia entre todos os chefes da cadeia, pra quem testa nao depender de vencer uma saga antes. Devolve o corpo.
	/// </summary>
	private ServerPlayer? NascerVilaoDoInferno(long agora, bool qualquerDaCadeia = false, VilaoGuardado? retomado = null)
	{
		var candidatos = ViloesDoInferno.Candidatos(_cadeia, _sagas);
		// (2026-10-18) O VILAO QUE VOLTA DO REINICIO E O MESMO: o elo, o molde e o degrau gravados, e nao um sorteio novo.
		if (retomado != null)
		{
			candidatos.Clear();
			foreach (EloDaSaga elo in _cadeia)
				if (string.Equals(elo.Id, retomado.Elo, StringComparison.OrdinalIgnoreCase))
					foreach (ChefeDaSaga ficha in elo.Chefes)
						if (ficha.Molde == retomado.Molde)
							candidatos.Add((elo, ficha, new EstadoDoChefe { Molde = ficha.Molde, Fase = FaseDoChefe.Derrotado, Degrau = retomado.Degrau }));
		}
		if (candidatos.Count == 0 && qualquerDaCadeia)
			foreach (EloDaSaga elo in _cadeia)
				foreach (ChefeDaSaga ficha in elo.Chefes)
					candidatos.Add((elo, ficha, new EstadoDoChefe { Molde = ficha.Molde, Fase = FaseDoChefe.Derrotado }));
		if (candidatos.Count == 0) return null;

		var zona = ZoneKey.Premade(Alem.ZonaDoInferno);
		if (_catalogo?.Get(zona) == null)
		{
			GD.PushError("[inferno] o Inferno nao esta no manifesto de mapas -- nenhum vilao volta");
			return null;
		}

		int i = Math.Clamp((int)(SorteioDoInferno() * candidatos.Count), 0, candidatos.Count - 1);
		(EloDaSaga eloSorteado, ChefeDaSaga ficha2, EstadoDoChefe ec) = candidatos[i];
		if (_moldes?.Get(ficha2.Molde) is not { EhChefe: true } molde) return null;

		Vec2 onde = LugarNoInferno(zona);
		ulong lugar = ++_lugarDoInferno;
		ServerPlayer? npc = NascerNpc(ficha2.Molde, zona, onde, lugar);
		if (npc?.Papel == null) return null;

		// O BP DE AGORA E O DEGRAU EM QUE ELE CAIU, os dois antes de qualquer outra coisa tocar a ficha -- a ordem do nascimento de
		// saga (`NascerChefeDaSaga`) e da lembranca na mente (`ChamarChefe`).
		npc.Papel.BpsPinados = retomado is { Bps.Length: > 0 } ? retomado.Bps
			: InfernoComOBpDaSagaDeTeste
			? Sagas.Pinar(molde, MediaDoServidor())
			: ViloesDoInferno.Bps(molde, MediaDoServidor());
		npc.Papel.Estagio = Math.Clamp(ec.Degrau, 0, Math.Max(molde.Estagios.Length - 1, 0));
		EntrarNoDegrau(npc, cura: 0, anunciar: false);

		// UMA LUTA DE CHEFE: caca o Inferno inteiro (`CacaOPlaneta`) e nao fica sem Ki (`Nutricao.KiSempreCheio`).
		npc.Papel.DaSaga = true;
		npc.Ficha.KiDeChefe = true;

		// MORTO, DE AUREOLA, DE PE -- e a segunda morte dele e a DERROTA (ver o cabecalho).
		FazerMortoDoAlem(npc);
		npc.Combate.AoMorrerDeNovo = _ =>
		{
			VilaoDoInfernoFoiDerrotado(npc, null);
			npc.Combate.Nocautear(Alem.SegundosDeComaDoMortoDeNovo);   // caido ate o tique o tirar, e nao lutando mais um quadro
		};

		// O PREMIO VIAJA NA BOLSA DELE, como o do vilao avulso -- quem o derrota fica com ela.
		npc.Ficha.Zeni = retomado?.Zeni ?? ViloesDoInferno.Premio(ficha2);

		_vilaoDoInferno = new VilaoNoInferno
		{
			Npc = npc.Id, Molde = ficha2.Molde, Elo = eloSorteado.Id, Nome = npc.Name,
			Ela = string.Equals(npc.Genero, "Female", StringComparison.OrdinalIgnoreCase),
			Ate = agora + (retomado?.RestanteMs ?? (long)(ViloesDoInferno.DuracaoS * 1000)),
		};

		string frase = retomado != null
			? $"{npc.Name.ToUpperInvariant()} CONTINUA NO INFERNO -- a ameaça não passou."
			: $"{npc.Name.ToUpperInvariant()} ESCAPOU DAS PROFUNDEZAS DO INFERNO!";
		foreach (ServerPlayer p in _players.Values)
		{
			if (p.Peer == null || !Alem.EhOAlem(p.Zone)) continue;
			string onde2 = p.Zone.Equals(zona) ? RumoDe(p.Pos, onde) : "no Inferno";
			bool ela = _vilaoDoInferno.Ela;
			Avisar(p, $"{frase} {(ela ? "Ela" : "Ele")} está {onde2}, à procura de briga. Quem {(ela ? "a" : "o")} derrotar ganha "
						+ $"{npc.Ficha.Zeni:N0} zeni e karma (+{Jandirus.Core.Social.Karma.PorDerrotarChefe}). Em {Math.Max(1, Math.Round((_vilaoDoInferno.Ate - agora) / 60_000.0)):0} minutos "
						+ $"{(ela ? "ela" : "ele")} afunda de volta.");
		}
		// A CENA DE CHEGADA DE CHEFE, so pra quem esta no Outro Mundo: e la que a luta e.
		// Quem RETOMA nao chegou: ele ja estava la antes do reinicio (a mesma regra do chefe de saga).
		if (retomado == null) AnunciarChegadaDoChefe(npc, Alem.ZonaDoInferno, frase, p => Alem.EhOAlem(p.Zone));

		GD.Print($"[inferno] {npc.Name} ('{ficha2.Molde}', da saga '{eloSorteado.Id}') voltou no Inferno em ({onde.X:0}, {onde.Y:0}) | "
			   + $"degrau {npc.Papel.Estagio + 1}/{molde.Estagios.Length} | BP {npc.Ficha.BP:N0} (media {MediaDoServidor():N0}) | "
			   + $"premio {npc.Ficha.Zeni:N0} zeni");
		return npc;
	}

	/// <summary>
	/// (2026-10-18) O VILAO DE AGORA, pro `sagas.json` -- nulo sem vilao, ou com ele ja derrotado (o premio foi pago; so falta tirar o corpo),
	/// ou com o prazo vencido. Chamado pelo `SalvarSagas`.
	/// </summary>
	private VilaoGuardado? GuardarOVilaoDoInferno()
	{
		if (_vilaoDoInferno is not { Derrotado: false } v || !_players.TryGetValue(v.Npc, out ServerPlayer? npc) || npc.Papel == null) return null;
		long falta = v.Ate - NowMs();
		if (falta <= 0) return null;
		return new VilaoGuardado
		{
			Molde = v.Molde, Elo = v.Elo, Degrau = npc.Papel.Estagio, Bps = npc.Papel.BpsPinados, Zeni = npc.Ficha.Zeni, RestanteMs = falta,
		};
	}

	/// <summary>
	/// (2026-10-18) O VILAO GRAVADO VOLTA AO INFERNO, IGUAL -- o mesmo molde, a mesma forma, o mesmo BP e o mesmo premio -- com o tempo que lhe
	/// faltava. Chamado pelo `RetomarSagas`, no primeiro tique.
	/// </summary>
	private ServerPlayer? RetomarOVilaoDoInferno(VilaoGuardado g) =>
		g.RestanteMs > 0 && !VilaoSoNaMemoriaDeTeste ? NascerVilaoDoInferno(NowMs(), retomado: g) : null;

	/// <summary>DEFEITO INJETADO (`--chefesteste`): o vilao do Inferno nao volta do reinicio -- o jogo de antes. Falso em jogo, sempre.</summary>
	internal static bool VilaoSoNaMemoriaDeTeste;

	/// <summary>
	/// ONDE ELE APARECE: na FAIXA JOGAVEL do vilao avulso (de 10 a 30 tiles, pela mesma `LugarDeVilao`) de quem esta no Inferno --
	/// ou do PORTAO, por onde se chega, se ninguem esta la agora (o aviso vai pro Outro Mundo inteiro). Nunca em cima de ninguem, e
	/// nunca do outro lado do mapa: a primeira versao sorteava qualquer vaga a dez tiles ou mais, e a bancada o viu nascer a 274 e a 406
	/// passos -- num Inferno de 500x500 com ilhas, onde a pe nao se chega a todo lugar. Sem habitat, o berco da zona.
	/// </summary>
	private Vec2 LugarNoInferno(ZoneKey zona)
	{
		List<Vec2> perto = [];
		foreach (ServerPlayer p in ZoneList(zona.Hash))
			if (EhJogador(p) && !EhCadaver(p)) perto.Add(p.Pos);
		if (perto.Count == 0) perto.Add(PortaoDoInferno(zona));
		return LugarDeVilao(zona, perto) ?? PontoDeNascimento(zona);
	}

	/// <summary>Avisa todo jogador que esta numa das tres zonas do Outro Mundo -- e la que a luta e.</summary>
	private void AnunciarNoOutroMundo(string texto)
	{
		foreach (ServerPlayer p in _players.Values)
			if (p.Peer != null && Alem.EhOAlem(p.Zone)) Avisar(p, texto);
	}

	/// <summary>
	/// O FUNIL DA DERROTA PASSOU PELO VILAO DO INFERNO (o nocaute, de `AoPerderALuta`) -- ou a segunda morte dele (o gancho do
	/// nascimento, com <paramref name="algoz"/> nulo: ai quem derrotou e o ultimo agressor, o funil de agressao). O premio vai pra quem o
	/// derrotou -- o zeni da bolsa e a "honra" que o dono pediu, que e o karma de derrubar um chefe de saga (`Karma.PorDerrotarChefe`) --,
	/// uma vez, e o evento acaba ali.
	///
	/// QUEM TIRA O CORPO E O TIQUE, no segundo seguinte: esta funcao roda dentro da resolucao de um golpe, com alguem percorrendo a lista
	/// da zona (o mesmo argumento do <see cref="VilaoFoiDerrotado"/>).
	/// </summary>
	private void VilaoDoInfernoFoiDerrotado(ServerPlayer vitima, ServerPlayer? algoz)
	{
		if (_vilaoDoInferno is not { } v || v.Npc != vitima.Id) return;
		if (v.Derrotado && !InfernoPagaDeNovoDeTeste) return;
		v.Derrotado = true;

		// QUEM DERROTOU. O algoz pode ser o clone ou a imagem de alguem: o merito e de quem manda nele.
		ServerPlayer? quem = algoz ?? (_players.TryGetValue(vitima.UltimoAgressor, out ServerPlayer? ultimo) ? ultimo : null);
		ServerPlayer? heroi = quem == null ? null
			: EhJogador(quem) ? quem
			: quem.DonoDoClone != 0 && _players.TryGetValue(quem.DonoDoClone, out ServerPlayer? dono) && EhJogador(dono) ? dono
			: null;

		if (heroi == null)
		{
			AnunciarNoOutroMundo($"{v.Nome} caiu no Inferno e afundou de volta nas profundezas.");
			return;
		}

		// A HONRA: o karma de quem livra o mundo de um chefe de saga -- o mesmo +30, pela mesma porta.
		KarmaPorDerrotarChefe(heroi);

		// O ZENI DA BOLSA dele, direto pra carteira -- como o do vilao avulso, e pelo mesmo motivo (cai em nocaute, e o corpo sai no
		// segundo seguinte).
		double premio = Math.Floor(vitima.Ficha.Zeni);
		if (premio >= 1 && !InfernoNaoPagaZeniDeTeste)
		{
			vitima.Ficha.Zeni = 0;
			heroi.Ficha.Zeni += premio;
			MandarCarteira(heroi);
			Avisar(heroi, $"o prêmio é seu: {premio:N0} zeni.");
		}
		AnunciarNoOutroMundo($"{NomeVisivel(heroi)} derrotou {v.Nome} no Inferno! {(v.Ela ? "Ela" : "Ele")} afunda de volta nas profundezas.");
		GD.Print($"[inferno] {heroi.Name} derrotou {v.Nome} ('{v.Molde}') e levou {premio:N0} zeni + karma");
	}

	// =====================================================================
	// O ADMIN E A BANCADA
	// =====================================================================
	/// <summary>
	/// O VERB DO ADMIN: chama um vilao no Inferno AGORA (sorteado entre os derrotados; sem nenhum derrotado ainda, entre todos os chefes
	/// da cadeia -- e uma ferramenta de teste). Recusa se ja ha um de pe.
	/// </summary>
	private void AdminVilaoDoInferno(ServerPlayer adm)
	{
		if (_vilaoDoInferno != null && _players.ContainsKey(_vilaoDoInferno.Npc))
		{
			Avisar(adm, $"{_vilaoDoInferno.Nome} já está no Inferno -- um vilão por vez.");
			return;
		}
		_vilaoDoInferno = null;
		bool algumDerrotado = ViloesDoInferno.Candidatos(_cadeia, _sagas).Count > 0;
		ServerPlayer? npc = NascerVilaoDoInferno(NowMs(), qualquerDaCadeia: true);
		if (npc == null) { Avisar(adm, "nenhum vilão de saga pôde voltar (a cadeia de sagas está vazia ou o Inferno não tem mapa)."); return; }
		Avisar(adm, $"{npc.Name} voltou no Inferno" + (algumDerrotado ? "." : " (nenhum vilão foi derrotado ainda: veio um da cadeia, pro teste)."));
	}

	/// <summary>DEFEITO INJETADO (bancada `--infernoteste`): o vilao volta com o BP da saga, e nao o da media de agora. Falso em jogo, sempre.</summary>
	internal static bool InfernoComOBpDaSagaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--infernoteste`): o premio e pago a cada queda do MESMO vilao. Falso em jogo, sempre.</summary>
	internal static bool InfernoPagaDeNovoDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--infernoteste`): derrotar o vilao nao rende o zeni da bolsa. Falso em jogo, sempre.</summary>
	internal static bool InfernoNaoPagaZeniDeTeste;

	/// <summary>O vilao de pe no Inferno (o id do corpo, o molde, derrotado?). SO PRA BANCADA E PRO ROBO.</summary>
	internal (int Npc, string Molde, bool Derrotado)? VilaoDoInfernoDeTeste =>
		_vilaoDoInferno is { } v ? (v.Npc, v.Molde, v.Derrotado) : null;
}
