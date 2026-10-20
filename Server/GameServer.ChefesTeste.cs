using System.Text.Json;
using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--chefesteste` -- O LOTE DOS CHEFES E DOS DOIS NERFS (dono, 2026-10-18): *"acido do saibaman ta meio op, da uma nerfada, spirit
/// gun tb ta absurdo, e o freeza n ta usando death beam, outra coisa se o freeza boss spawnar mas fechar o server ele volta? se n ele deveria
/// voltar na forma q ele estava (isso pra todos os chefes n so o freeza), e caso eu feche o server agora sem esse update, faça o server
/// checar se o freeza ou outro boss teve sua saga concluida pra caso n tenha sido concluida mas tenha sido iniciada ou tenham cumprido o
/// requisito dela ela inicie novamente de onde parou ou do começo caso o jogo atualmente n salve muitas informaçoes da saga"*.
///
/// Um cliente cru entra pela porta (o marco da saga e de JOGADOR), e cada familia mede pela porta de PRODUCAO, com o contra-exemplo:
///   A. O CUSPE ACIDO -- a bola sai com metade do dano-base do DM (20) e a recarga dobra (2 s). Contra: `CuspeComoNoDmDeTeste`.
///   S. O SPIRIT GUN -- a bola carrega o BP de quem atira, e o multiplicador fica so no dano-base. Contra: `SpiritGunComoNoDmDeTeste`.
///   D. O DEATH BEAM DO FREEZA -- o Freeza de saga, numa luta pelo tique de producao, solta o raio dele.
///   F. A FORMA NO DISCO -- o Freeza de Namek que se transforma por dano vai pro `sagas.json` NA HORA, e volta do reinicio na forma em que
///      estava; o "Save All" grava as sagas, e o fechamento tambem. Contra: `DegrauSoNoProximoEventoDeTeste`.
///   C. O CELL QUE VOLTA DO ALEM -- o tempo que faltava atravessa o reinicio (o relogio do mundo recomeca do zero). Contra:
///      `RessurgeNoRelogioVelhoDeTeste`.
///   I. O VILAO DO INFERNO -- volta do reinicio igual (molde, forma, BP, premio), com o tempo que lhe faltava. Contra: `VilaoSoNaMemoriaDeTeste`.
///   M. O MARCO BATIDO -- quem bateu o marco com a cadeia esperando fica registrado, e a saga dispara na vez dela com ele fora do jogo; a
///      auditoria do boot acha no DISCO quem ja tinha batido, e desfaz o "disparada sem comecar". Contra: `MarcoSoDeQuemEstaAquiDeTeste`,
///      `SemAuditoriaDeTeste`.
///   H. A HORA DO CHEFE (2026-10-18) -- o Freeza de Namek nocauteado na forma 3 nao morre: o golpe letal vira a forma 4, com o anuncio, e a vida
///      inteira; na forma final ele morre; o Cell morre em qualquer forma. Contra: `ChefeMorreAntesDaHoraDeTeste`.
///   R. RESETAR UMA SAGA (2026-10-18) -- o verb de admin lista as sagas pelas escolhas e reseta a do Freeza de Namek (o Freeza morto na forma
///      3): os chefes saem do mundo e ela recomeca do marco, com a contagem.
///   P. PULAR OS DIAS (2026-10-18) -- o verb de admin poe no mundo, na hora, o chefe que estava a caminho. Contra: `AgoraEsperaAViradaDeTeste`.
///   N. NAMEK SALVA (2026-10-19) -- o Freeza caido quando o prazo vence nao ergue a Death Ball; morto nessa janela, a saga fecha vencida e o
///      planeta fica. As familias estao em `GameServer.NamekSalvaTeste.cs`. Contra: `EsferaSobeNoChaoDeTeste`.
///   T. A TROPA DO FREEZA EM NAMEK (2026-10-18) -- o desembarque, a esfera do chao, o povo, quem carrega, o derrotado e a partida com o
///      chefe. As familias estao em `GameServer.TropaTeste.cs`, com os contra-exemplos de cada uma.
///
/// O MUNDO VOLTA COMO ESTAVA: o estado das sagas e fotografado e devolvido (e regravado) no fim, os chefes e corpos saem, e a conta
/// plantada no disco e apagada.
/// </summary>
public sealed partial class GameServer
{
	private const string ContaDaBancadaDosChefes = "bancada_chefes_ag";
	private const string ContaPlantadaDosChefes = "bancada_chefes_disco";

	private bool _chefesRodando, _chefesJaRodou;
	private int _chOk, _chFalhou;

	private void AfirmarCh(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _chOk++; GD.Print($"[chefes]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _chFalhou++; GD.PrintErr($"[chefes]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDosChefes(int porta)
	{
		if (_chefesRodando || _chefesJaRodou) return;
		_chefesRodando = _chefesJaRodou = true;
		_chOk = _chFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[chefes] ================ OS CHEFES E OS DOIS NERFS (dono, 2026-10-18) ================");

		EstadoDasSagas guardado = _sagas;
		double relogioGuardado = _relogioDoMundo;
		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		ServerPlayer? pl = null;
		double bpGuardado = 0;
		try
		{
			pl = mesa.EntraNoMundo(cli, ContaDaBancadaDosChefes, "Testemunha");
			AfirmarCh("(montagem) o cliente entra pela porta e e jogador; a Terra tem mapa; a cadeia de sagas e o catalogo de moldes carregaram",
					  pl != null && EhJogador(pl) && _pjMapa != null && _cadeia.Length == 4 && _moldes != null);
			if (pl == null || _pjMapa == null || _moldes == null || _cadeia.Length != 4) return;
			bpGuardado = pl.Ficha.BP;

			OCuspeAcido();
			OSpiritGun();
			ODeathBeamDoFreeza();
			AFormaNoDisco();
			OCellQueVoltaDoAlem();
			OVilaoDoInfernoVolta();
			OMarcoBatido(pl);
			AHoraDoChefe();
			OChefeDeSagaMata();
			NamekSalvaDoFreezaCaido();   // (2026-10-19) ver `GameServer.NamekSalvaTeste.cs`
			ResetarUmaSaga(pl);
			PularOsDiasDaSaga(pl);
			ATropaDoFreeza(pl);
		}
		catch (Exception e)
		{
			AfirmarCh("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			CuspeComoNoDmDeTeste = false;
			SpiritGunComoNoDmDeTeste = false;
			DegrauSoNoProximoEventoDeTeste = false;
			RessurgeNoRelogioVelhoDeTeste = false;
			VilaoSoNaMemoriaDeTeste = false;
			MarcoSoDeQuemEstaAquiDeTeste = false;
			SemAuditoriaDeTeste = false;
			ChefeMorreAntesDaHoraDeTeste = false;
			AgoraEsperaAViradaDeTeste = TropaNaoVemDeTeste = TropaFicaSemOChefeDeTeste = SoldadoComoHostilComumDeTeste = false;
			SoldadoDerrotadoVoltaDeTeste = SoldadoIgnoraQuemCarregaDeTeste = SoldadoNaoSeguraEsferaDeTeste = false;
			SoldadoProvocaOPovoDeTeste = EsferaSomeComOSoldadoDeTeste = false;
			EscutaDoRoteiro = null;
			EscutaDeEscolhas = null;
			DiscoComAsBancadasDeTeste = false;
			Fighter.ChefeSemPisoDeTanqueDeTeste = false;

			foreach (ServerPlayer c in ChefesNoMundo()) RemoverNpc(c);
			TirarATropaDaBancada();
			if (_vilaoDoInferno is { } v && _players.TryGetValue(v.Npc, out ServerPlayer? corpoDoVilao)) RemoverNpc(corpoDoVilao);
			_vilaoDoInferno = null;
			_vilaoPorRetomar = null;
			if (pl != null) pl.Ficha.BP = bpGuardado;
			_relogioDoMundo = relogioGuardado;
			_sagas = guardado;
			_sagasPorRetomar = false;
			SalvarSagas();

			LimparTudoDaBancada();
			mesa.Fecha();
			if (_store != null)
				foreach (string conta in new[] { ContaDaBancadaDosChefes, ContaPlantadaDosChefes })
					try
					{
						string caminho = System.IO.Path.Combine(_store.Pasta, conta + ".json");
						if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
					}
					catch (Exception e) { GD.PushWarning($"[chefes] nao apaguei '{conta}': {e.Message}"); }
			_chefesRodando = false;
			GD.Print($"[chefes] ================ {_chOk} OK, {_chFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// A) O CUSPE ACIDO
	// =====================================================================
	private void OCuspeAcido()
	{
		GD.Print("[chefes] -- A) o cuspe acido: metade do dano-base do DM e o dobro da recarga --");
		ServerPlayer saiba = ForjarV2("Saibaman da bancada", TerraFirmeG12(12), 50_000, "Saibaman");
		saiba.Livro.Dar("/datum/skill/saiba/Acid_Spit");
		saiba.Facing = Facing.East;

		// um cuspe pela porta de producao: o dano-base da bola que saiu, a recarga que ficou armada, e se o segundo aperto (meio tempo depois)
		// foi recusado e o de depois do prazo saiu
		(double Dano, long Recarga, bool MeioTempoRecusa, bool DepoisSai) Cuspir()
		{
			_cuspeProntoG14.Remove(saiba.Id);
			saiba.Ficha.Ki = saiba.Ficha.MaxKi;
			long agora = NowMs();
			UsarHabilidade(saiba, "Acid_Spit");
			Projetil? bola = ProjeteisDaZona(saiba.Zone.Hash).LastOrDefault(p => p.Dono == saiba.Id && p.Nome == "Cuspe Acido");
			long recarga = _cuspeProntoG14.GetValueOrDefault(saiba.Id) - agora;
			int antes = ProjeteisDaZona(saiba.Zone.Hash).Count(p => p.Dono == saiba.Id);
			// "meio tempo depois": o prazo armado puxado pra perto, pela metade -- a bancada nao espera relogio de parede
			_cuspeProntoG14[saiba.Id] = NowMs() + recarga / 2;
			saiba.Ficha.Ki = saiba.Ficha.MaxKi;
			UsarHabilidade(saiba, "Acid_Spit");
			bool recusou = ProjeteisDaZona(saiba.Zone.Hash).Count(p => p.Dono == saiba.Id) == antes;
			_cuspeProntoG14[saiba.Id] = NowMs() - 1;
			saiba.Ficha.Ki = saiba.Ficha.MaxKi;
			UsarHabilidade(saiba, "Acid_Spit");
			bool saiu = ProjeteisDaZona(saiba.Zone.Hash).Count(p => p.Dono == saiba.Id) > antes;
			foreach (Projetil p in ProjeteisDaZona(saiba.Zone.Hash).Where(p => p.Dono == saiba.Id).ToList()) p.Vivo = false;
			return (bola?.BaseDano ?? -1, recarga, recusou, saiu);
		}

		var agora2 = Cuspir();
		AfirmarCh("**O CUSPE ACIDO NERFADO** (*\"acido do saibaman ta meio op, da uma nerfada\"*): pela porta de producao, a bola sai com dano-base "
				  + "20 (o DM: 40, o do Kikoho) e a recarga e de 2 s (o DM: 1) -- um quarto do dano por segundo; meio prazo depois o segundo cuspe e "
				  + "recusado, e depois do prazo ele sai",
				  Math.Abs(agora2.Dano - 20) < 1e-9 && agora2.Recarga is >= 1990 and <= 2010 && agora2.MeioTempoRecusa && agora2.DepoisSai,
				  $"dano-base {agora2.Dano}; recarga {agora2.Recarga} ms; meio prazo recusa {agora2.MeioTempoRecusa}; depois sai {agora2.DepoisSai}");

		CuspeComoNoDmDeTeste = true;
		(double Dano, long Recarga, bool MeioTempoRecusa, bool DepoisSai) doDm;
		try { doDm = Cuspir(); }
		finally { CuspeComoNoDmDeTeste = false; }
		AfirmarCh("(defeito injetado: o cuspe do DM) a bola volta a sair com 40 e a recarga com 1 s -- a regua de cima REPROVA",
				  Math.Abs(doDm.Dano - 40) < 1e-9 && doDm.Recarga is >= 990 and <= 1010, $"dano-base {doDm.Dano}; recarga {doDm.Recarga} ms");
		RemoverNpc(saiba);
	}

	// =====================================================================
	// S) O SPIRIT GUN
	// =====================================================================
	private void OSpiritGun()
	{
		GD.Print("[chefes] -- S) o Spirit Gun: a bola carrega o BP de quem atira; o multiplicador so no dano-base --");
		Vec2 chao = TerraFirmeG12(12);
		ServerPlayer atirador = ForjarV2("Yusuke da bancada", chao, 50_000);
		ServerPlayer alvo = ForjarV2("Alvo do Spirit Gun", chao + new Vec2(6 * ZoneCollision.TileSize, 0), 50_000);
		atirador.Livro.Dar(PathDoSpiritGun);
		atirador.Facing = Facing.East;
		// O MAXIMO QUE O JOGO DA: o nivel 2 do Spirit Gun (x2 e x1,25) e o nivel 1 do Spirit Fist (x2, `Spirit.dm:419`) -- x5.
		atirador.Ficha.SpiritBallDamage = 5;

		(double RazaoDoBp, double Base, double Dano) Atirar()
		{
			_prontoG3.Remove(atirador.Id);
			atirador.Ficha.maxstamina = atirador.Ficha.stamina = 1e7;
			UsarHabilidade(atirador, "Spirit_Gun");
			Projetil? bola = ProjeteisDaZona(atirador.Zone.Hash).LastOrDefault(p => p.Dono == atirador.Id && p.Nome == "Spirit Gun");
			if (bola == null) return (-1, -1, -1);
			double r = bola.Bp / Math.Max(atirador.Ficha.expressedBP, 1e-9);
			double dano = DanoFinalDe(bola, alvo);
			bola.Vivo = false;
			return (r, bola.BaseDano, dano);
		}

		var agora = Atirar();
		SpiritGunComoNoDmDeTeste = true;
		(double RazaoDoBp, double Base, double Dano) doDm;
		try { doDm = Atirar(); }
		finally { SpiritGunComoNoDmDeTeste = false; }

		AfirmarCh("**O SPIRIT GUN NERFADO** (*\"spirit gun tb ta absurdo\"*): com o multiplicador no maximo do jogo (x5), a bola carrega o BP de "
				  + "QUEM ATIRA (x1, e nao x5) e o dano-base x5 -- o dano contra um igual cai a um quinto: linear no nivel, e nao ao quadrado",
				  Math.Abs(agora.RazaoDoBp - 1) < 1e-6 && Math.Abs(agora.Base - 5) < 1e-9 && doDm.Dano > 0 && Math.Abs(agora.Dano / doDm.Dano - 0.2) < 0.02,
				  $"BP da bola x{agora.RazaoDoBp:0.##}; dano-base {agora.Base}; dano contra um igual {agora.Dano:0.##} (o do DM {doDm.Dano:0.##})");
		AfirmarCh("(defeito injetado: o Spirit Gun do DM) a bola volta a carregar x5 o BP de quem atira -- a regua de cima REPROVA",
				  Math.Abs(doDm.RazaoDoBp - 5) < 1e-6, $"BP da bola x{doDm.RazaoDoBp:0.##}");
		RemoverNpc(atirador);
		RemoverNpc(alvo);
	}

	// =====================================================================
	// D) O DEATH BEAM DO FREEZA
	// =====================================================================
	/// <summary>
	/// UMA LUTA DE VERDADE: o Freeza de Namek nasce pela porta dos NPCs (`NascerNpc`) e e armado como o nascimento de saga o arma (o BP pinado,
	/// o degrau, a caca do planeta, o Ki de chefe); o alvo e um saco do mesmo poder, de pe, imposto como presa (`PresaDoRoteiro`, a porta do
	/// torneio e do Cell). O tique e o de producao, na ordem do `_Process` (as grades, o combate, os relogios do corpo -- o VOO mora la --, os
	/// corpos, os canais, os projeteis, os embates, os dois prazos da IA e o roteiro a 1 Hz). O que se mede e o que SAI da mao dele.
	///
	/// A PRIMEIRA RODADA DESTA FAMILIA (job92) ACHOU A CAUSA: o tanque de NPC do Freeza era 259 de Ki, e o Death Beam cobra 975 por ciclo
	/// (`kireq*25`, `DeathBeam.dm:46`) -- a IA nunca o achava pagavel. Daí o piso do tanque do chefe (`Fighter.PisoDoTanqueDeChefe`).
	/// </summary>
	private void ODeathBeamDoFreeza()
	{
		GD.Print("[chefes] -- D) o Death Beam do Freeza: numa luta, ele solta o raio dele --");
		(int DeathBeams, string Tiros) agora = LutaDoFreeza(90);
		AfirmarCh("**O FREEZA SOLTA O DEATH BEAM** (*\"o freeza n ta usando death beam\"*): numa luta de 90 s pelo tique de producao, o raio dele "
					  + "sai da mao dele, no rodizio do arsenal -- e o tanque de chefe (com o piso) paga o aluguel de 975+ por ciclo",
					  agora.DeathBeams >= 1, $"{agora.DeathBeams} Death Beam(s); tiros {agora.Tiros}");

		Fighter.ChefeSemPisoDeTanqueDeTeste = true;
		(int DeathBeams, string Tiros) comODefeito;
		try { comODefeito = LutaDoFreeza(40); }
		finally { Fighter.ChefeSemPisoDeTanqueDeTeste = false; }
		AfirmarCh("(defeito injetado: o tanque de NPC, o jogo de antes) o Freeza luta sem soltar um Death Beam -- so Ki Wave e o que cabe no tanque "
					  + "-- e a regua de cima REPROVA", comODefeito.DeathBeams == 0, $"tiros {comODefeito.Tiros}");
	}

	/// <summary>A LUTA DO FREEZA CONTRA O SACO, por <paramref name="segundos"/> de tique de producao. Devolve quantos Death Beams sairam e o resumo dos tiros.</summary>
	private (int DeathBeams, string Tiros) LutaDoFreeza(int segundos)
	{
		const float T = ZoneCollision.TileSize;
		Vec2 chao = TerraFirmeG12(16);
		MoldeDeNpc molde = _moldes!.Get("freeza_namek")!;
		ServerPlayer? freeza = NascerNpc("freeza_namek", ZonaDaBancadaDeProjetil, chao, 9_700);
		if (freeza?.Papel == null) { AfirmarCh("(montagem) o Freeza de Namek nasce pela porta dos NPCs", false); return (-1, ""); }
		ArmarComoChefeDeSaga(freeza, molde);
		Vec2 doSaco = new(chao.X + 6 * T, chao.Y);
		ServerPlayer saco = Forjar("Saco do Freeza", doSaco, freeza.Ficha.BP);
		freeza.Papel.PresaDoRoteiro = saco.Id;
		if (freeza.Cerebro != null) freeza.Cerebro.Explicando = true;

		Arsenal a = ArsenalDeLonge(freeza);
		GD.Print($"[chefes]      Freeza: BP {freeza.Ficha.BP:N0}, Ki {freeza.Ficha.MaxKi:N0} (BaseDrain {freeza.Ficha.BaseDrain():0.##}); arsenal: "
						+ string.Join("; ", Enumerable.Range(0, a.Quantas).Select(i =>
							$"{a[i].Id} [{a[i].AlcanceMin / T:0.#}-{a[i].AlcanceMax / T:0.#} tiles, {a[i].CustoDeKi:N0} Ki]")));

		var decisoes = new Dictionary<string, int>();
		var canais = new List<string>();
		var tiros = new Dictionary<int, string>();
		int noAr = 0, tiques = segundos * 30;
		for (int i = 0; i < tiques; i++)
		{
			// o saco nao cai, nao foge e nao morre: quem esta sendo medido e o Freeza
			if (saco.Ficha.KO) saco.Combate.Levantar();
			saco.Combate.Corpo.Restaurar();
			saco.Combate.SincronizarVida();
			saco.Pos = doSaco;
			TiqueDaLutaDeChefe(i);

			string porque = freeza.Cerebro?.Porque ?? "";
			int dois = porque.IndexOf(':');
			string chave = dois > 0 ? porque[..dois] : porque;
			if (chave.Length > 0) decisoes[chave] = decisoes.GetValueOrDefault(chave) + 1;
			if (_canais.TryGetValue(freeza.Id, out CanalDeKi? c) && (canais.Count == 0 || canais[^1] != c.Verbo)) canais.Add(c.Verbo);
			else if (!_canais.ContainsKey(freeza.Id) && canais.Count > 0 && canais[^1] != "-") canais.Add("-");
			foreach (Projetil p in ProjeteisDaZona(freeza.Zone.Hash))
				if (p.Dono == freeza.Id && !tiros.ContainsKey(p.Id)) tiros[p.Id] = p.Nome;
			if (freeza.Voando) noAr++;
		}

		string resumo = string.Join(", ", tiros.Values.GroupBy(n => n).Select(g => $"{g.Key} x{g.Count()}"));
		GD.Print($"[chefes]      em {segundos} s: decisoes {string.Join("; ", decisoes.OrderByDescending(d => d.Value).Select(d => $"{d.Key} x{d.Value}"))}");
		GD.Print($"[chefes]      canais abertos: {string.Join(" ", canais.Where(x => x != "-"))}");
		GD.Print($"[chefes]      tiros: {resumo}; no ar {noAr * 100 / tiques}% do tempo");

		RemoverNpc(freeza);
		RemoverNpc(saco);
		LimparEmbatesDaBancada();
		return (tiros.Values.Count(n => n == "Death Beam"), resumo);
	}

	// =====================================================================
	// H) A HORA DO CHEFE
	// =====================================================================
	/// <summary>Um chefe de saga de bancada no degrau pedido, NOCAUTEADO -- o instante em que o dono o matou.</summary>
	private ServerPlayer? ChefeCaidoNoDegrau(string moldeId, int degrau, ulong semente)
	{
		MoldeDeNpc? molde = _moldes!.Get(moldeId);
		ServerPlayer? npc = molde == null ? null : NascerNpc(moldeId, ZonaDaBancadaDeProjetil, TerraFirmeG12(16), semente);
		if (npc?.Papel == null) return null;
		ArmarComoChefeDeSaga(npc, molde!);
		npc.Papel.Estagio = degrau;
		EntrarNoDegrau(npc, cura: 0, anunciar: false);
		npc.Combate.Nocautear(30);
		return npc;
	}

	private void AHoraDoChefe()
	{
		GD.Print("[chefes] -- H) a hora do chefe: o Freeza nao morre antes da forma final (dono, 2026-10-18) --");
		ServerPlayer? freeza = ChefeCaidoNoDegrau("freeza_namek", 2, 9_720);
		if (freeza?.Papel == null) { AfirmarCh("(montagem) o Freeza de Namek nasce como chefe de saga, na forma 3, nocauteado", false); return; }
		int formas = freeza.Papel.Molde.Estagios.Length;
		AfirmarCh("(montagem) o Freeza de Namek nasce como chefe de saga, na forma 3 de " + formas + ", nocauteado",
				  freeza.Papel.Estagio == 2 && freeza.Ficha.KO, $"forma {freeza.Papel.Estagio + 1}; caido {freeza.Ficha.KO}");

		EscutaDoRoteiro = [];
		bool morreu = freeza.Combate.Morrer();
		List<(int Quem, string Texto)> anunciou = EscutaDoRoteiro;
		EscutaDoRoteiro = null;
		bool inteiro = freeza.Combate.Corpo.Partes.All(p => !p.Decepado && p.Vida >= p.VidaMax - 1e-6);
		AfirmarCh("**O FREEZA NAO MORRE ANTES DA HORA** (*\"ao o jogo detectar q o vilao morreu ele deveria iniciar a cinematica de transformaçao e "
				  + "recuperar toda a vida\"*): o golpe letal na forma 3, caido, vira a FORMA 4 -- com o anuncio dela --, de pe e com a vida inteira",
				  !morreu && !freeza.Ficha.dead && !freeza.Ficha.KO && freeza.Papel.Estagio == 3 && inteiro && anunciou.Any(a => a.Texto.Contains("Forma 4")),
				  $"morreu {morreu}; forma {freeza.Papel.Estagio + 1}; caido {freeza.Ficha.KO}; inteiro {inteiro}; anuncios: {string.Join(" | ", anunciou.Select(a => a.Texto))}");

		// NA FORMA FINAL ELE MORRE -- o fim que a saga espera.
		freeza.Papel.Estagio = formas - 1;
		EntrarNoDegrau(freeza, cura: 0, anunciar: false);
		freeza.Combate.Nocautear(30);
		bool morreuNoFim = freeza.Combate.Morrer();
		AfirmarCh("...e na FORMA FINAL ele morre (o fim que a saga espera)", morreuNoFim && freeza.Ficha.dead, $"morreu {morreuNoFim}");
		RemoverNpc(freeza);

		// O CELL MORRE EM QUALQUER FORMA -- o dono: *"menos o cell"*.
		ServerPlayer? cell = ChefeCaidoNoDegrau("cell", 0, 9_721);
		bool cellMorreu = cell != null && cell.Combate.Morrer();
		AfirmarCh("...e o CELL morre na forma imperfeita, que tem forma por vir (*\"menos o cell\"*)",
				  cell?.Papel is { } pc && pc.GatilhoAtual >= 0 && cellMorreu, $"morreu {cellMorreu}");
		if (cell != null) RemoverNpc(cell);

		ServerPlayer? deAntes = ChefeCaidoNoDegrau("freeza_namek", 2, 9_722);
		ChefeMorreAntesDaHoraDeTeste = true;
		bool morreuAntes;
		try { morreuAntes = deAntes != null && deAntes.Combate.Morrer(); }
		finally { ChefeMorreAntesDaHoraDeTeste = false; }
		AfirmarCh("(defeito injetado: o chefe morre em qualquer forma, o jogo de antes) o Freeza morre na forma 3 -- a regua de cima REPROVA",
				  morreuAntes && deAntes!.Papel!.Estagio == 2, $"morreu {morreuAntes}");
		if (deAntes != null) RemoverNpc(deAntes);
	}

	// =====================================================================
	// L) O CHEFE DE SAGA MATA (dono, 2026-10-18)
	// =====================================================================
	/// <summary>
	/// *"faça os chefes das sagas sempre estarem no modo letal, pq eles so estavam nocauteando e nao matando"*. O Freeza de Namek pelo nascimento
	/// de PRODUCAO da saga (`NascerChefeDaSaga`), uma vitima ao lado dele, nocauteada, e o tique de producao da luta: ele vai ate ela e a mata.
	/// Contra-exemplos: o chefe nao-letal (o jogo de antes) e o letal que larga o caido (a IA de antes) -- nos dois a vitima fica viva.
	/// </summary>
	private void OChefeDeSagaMata()
	{
		GD.Print("[chefes] -- L) o chefe de saga luta pra matar (dono, 2026-10-18) --");
		EloDaSaga? elo = Array.Find(_cadeia, e => e.Chefes.Any(c => c.Molde == "freeza_namek"));
		if (elo == null) { AfirmarCh("(montagem) a cadeia tem a saga do Freeza de Namek", false); return; }

		// UMA LUTA: o chefe pelo nascimento de producao, a vitima a dois tiles dele, nocauteada, e ate 20 s de tique de producao. Devolve se ela morreu.
		(bool Letal, bool Morreu, string Porque) Luta(ulong semente)
		{
			SagasLimpas();
			(_, _, EstadoDoChefe ec) = SagaComOChefeNoMundo(elo.Id, "freeza_namek");
			if (!_players.TryGetValue(ec.Corpo, out ServerPlayer? freeza) || freeza.Papel == null) return (false, false, "o chefe nao nasceu");
			ServerPlayer vitima = Forjar("Vitima do Freeza", freeza.Pos, 1_000);
			ZoneList(vitima.Zone.Hash).Remove(vitima);
			vitima.Zone = freeza.Zone;
			vitima.Pos = freeza.Pos + new Vec2(2 * ZoneCollision.TileSize, 0);
			ZoneList(vitima.Zone.Hash).Add(vitima);
			freeza.Papel.PresaDoRoteiro = vitima.Id;
			vitima.Combate.Nocautear(60);
			string porque = "";
			for (int i = 0; i < 20 * 30 && !vitima.Ficha.dead; i++)
			{
				TiqueDaLutaDeChefe(i);
				if (freeza.Cerebro?.Porque is { Length: > 0 } p) porque = p;
			}
			(bool, bool, string) r = (freeza.Combate.Letal, vitima.Ficha.dead, porque);
			RemoverNpc(freeza);
			RemoverNpc(vitima);
			LimparEmbatesDaBancada();
			SagasLimpas();
			return r;
		}

		(bool letal, bool morreu, string porque) = Luta(9_740);
		AfirmarCh("**O CHEFE DE SAGA NASCE NO MODO LETAL** (*\"faça os chefes das sagas sempre estarem no modo letal\"*): o Freeza de Namek, pelo "
				  + "nascimento de producao da saga", letal, $"letal {letal}");
		AfirmarCh("**...E MATA** (*\"eles so estavam nocauteando e nao matando\"*): com a vitima nocauteada ao lado, ele nao a larga -- vai ate ela "
				  + "e o golpe letal a mata", morreu, $"morreu {morreu}; ultima decisao \"{porque}\"");

		ChefeNaoLetalDeTeste = true;
		(bool Letal, bool Morreu, string Porque) semLetal;
		try { semLetal = Luta(9_741); }
		finally { ChefeNaoLetalDeTeste = false; }
		AfirmarCh("(defeito injetado: o chefe nao-letal, o jogo de antes) a vitima nocauteada sobrevive aos 20 s -- a regua de cima REPROVA",
				  !semLetal.Morreu, $"morreu {semLetal.Morreu}");

		Jandirus.Core.Ai.Cerebro.LetalLargaOCaidoDeTeste = true;
		(bool Letal, bool Morreu, string Porque) largando;
		try { largando = Luta(9_742); }
		finally { Jandirus.Core.Ai.Cerebro.LetalLargaOCaidoDeTeste = false; }
		AfirmarCh("(defeito injetado: o letal que larga o caido, a IA de antes) mesmo letal, ele para em cima da vitima e ela sobrevive -- a regua REPROVA",
				  !largando.Morreu, $"morreu {largando.Morreu}; ultima decisao \"{largando.Porque}\"");
	}

	// =====================================================================
	// R) RESETAR UMA SAGA
	// =====================================================================
	private void ResetarUmaSaga(ServerPlayer pl)
	{
		GD.Print("[chefes] -- R) resetar uma saga pelo verb de admin (dono, 2026-10-18) --");
		EloDaSaga? elo = Array.Find(_cadeia, e => e.Chefes.Any(c => c.Molde == "freeza_namek"));
		if (elo == null) { AfirmarCh("(montagem) a cadeia tem a saga do Freeza de Namek", false); return; }

		// O ESTADO "BUGADO" DO DONO: a saga em curso com o Freeza derrotado na forma 3, e ele ainda de pe num canto do mundo.
		SagasLimpas();
		EstadoDoElo est = _sagas.Do(elo.Id) ?? new EstadoDoElo { Id = elo.Id };
		if (!_sagas.Elos.Contains(est)) _sagas.Elos.Add(est);
		ServerPlayer? sobra = ChefeCaidoNoDegrau("freeza_namek", 2, 9_723);
		est.Disparou = true;
		est.Fase = FaseDaSaga.EmCurso;
		est.Chefes.Clear();
		est.Chefes.Add(new EstadoDoChefe { Molde = "freeza_namek", Fase = FaseDoChefe.NoMundo, Degrau = 2, Corpo = sobra?.Id ?? 0 });

		EscutaDeEscolhas = [];
		List<string> listou = Ouvir(() => VerboDeAdmin(pl, "admin_sagas", ""));
		List<string> itens = EscutaDeEscolhas.Find(e => e.Verbo == "admin_sagas").Itens ?? [];
		EscutaDeEscolhas = null;
		AfirmarCh("**O ADMIN VE AS SAGAS** (*\"adicione um verb pra adm q permite ele resetar uma saga\"*): a lista vai pelas escolhas, uma linha por "
				  + "saga com o id e o estado -- a do Freeza de Namek diz \"em curso\", com ele na forma 3",
				  itens.Count == _cadeia.Length && itens.Any(i => i.StartsWith(elo.Id + "|", StringComparison.Ordinal) && i.Contains("em curso") && i.Contains("forma 3")),
				  string.Join(" || ", itens));

		List<string> resetou = Ouvir(() => VerboDeAdmin(pl, "admin_saga_resetar", elo.Id));
		EstadoDoElo? depois = _sagas.Do(elo.Id);
		bool sobraSaiu = sobra == null || !_players.ContainsKey(sobra.Id);
		AfirmarCh("**RESETAR A SAGA DO FREEZA DE NAMEK** (*\"pra eu poder resetar a do freeza de namek ja q ele morreu antes da hora\"*): o chefe que "
				  + "estava no mundo sai, e ela recomeca do marco -- contando os dias, com o Freeza a caminho na forma 1",
				  depois is { Disparou: true, Fase: FaseDaSaga.Contando } && depois.Chefes.Count == elo.Chefes.Length
				  && depois.Chefes.All(c => c.Fase == FaseDoChefe.ACaminho && c.Degrau == 0 && c.DiasRestantes > 0) && sobraSaiu
				  && resetou.Any(f => f.Contains("resetada", StringComparison.Ordinal)),
				  $"fase {depois?.Fase}; chefes {string.Join(", ", depois?.Chefes.Select(c => $"{c.Molde} {c.Fase} {c.DiasRestantes}d") ?? [])}; o de antes saiu {sobraSaiu}; "
				  + string.Join(" | ", resetou));
		List<string> errou = Ouvir(() => VerboDeAdmin(pl, "admin_saga_resetar", "nao_existe"));
		AfirmarCh("...e um id que nao existe e recusado com a lista das sagas", errou.Any(f => f.Contains(elo.Id, StringComparison.Ordinal)), string.Join(" | ", errou));
		SagasLimpas();
	}

	private long _tiqueDaLutaDeChefe;

	/// <summary>Um tique de producao, na ordem do `_Process` -- o que uma luta de chefe atravessa.</summary>
	private void TiqueDaLutaDeChefe(int i)
	{
		double dt = Protocol.TickSeconds;
		MontarAsGrades();
		TickCombate(dt);
		TickDosRelogiosDoCorpo(dt);
		TickDosCorposSemDono(dt);
		TickDosCanaisDeKi(dt);
		TickDosProjeteis(dt);
		TickDosEmbatesDeKi(dt);
		TickDoPrazoDeRaioDaIa(dt);
		TickDoSegundoApertoDaIa();
		if (i % 30 == 0) TickDoRoteiro();
		_tiqueDaLutaDeChefe++;
	}

	// =====================================================================
	// AS SAGAS DA BANCADA
	// =====================================================================
	/// <summary>Um estado de sagas limpo, todos os elos dormindo, no dia de hoje -- o comeco de cada familia das sagas.</summary>
	private void SagasLimpas()
	{
		foreach (ServerPlayer c in ChefesNoMundo()) RemoverNpc(c);
		_sagas = new EstadoDasSagas { Elos = [.. _cadeia.Select(e => new EstadoDoElo { Id = e.Id })], UltimoDiaVisto = DiaDoMundo };
		_sagasPorRetomar = false;
		_vilaoPorRetomar = null;
	}

	/// <summary>A saga ja disparada, com o chefe pinado na media de agora e chegando HOJE (pelo nascimento de producao).</summary>
	private (EloDaSaga Elo, EstadoDoElo Est, EstadoDoChefe Ec) SagaComOChefeNoMundo(string id, string molde)
	{
		EloDaSaga elo = Array.Find(_cadeia, e => e.Id == id)!;
		EstadoDoElo est = _sagas.Do(id)!;
		est.Disparou = true;
		est.Fase = FaseDaSaga.Contando;
		var ec = new EstadoDoChefe { Molde = molde, DiasRestantes = 0, Bps = Sagas.Pinar(_moldes!.Get(molde)!, MediaDoServidor()) };
		est.Chefes.Add(ec);
		NascerChefeDaSaga(elo, est, ec);
		return (elo, est, ec);
	}

	/// <summary>O `sagas.json` como ESTA NO DISCO -- e nao a memoria: um estado que so vive em memoria some no reinicio.</summary>
	private EstadoDasSagas? SagasDoDisco()
	{
		try
		{
			return JsonSerializer.Deserialize<EstadoDasSagas>(System.IO.File.ReadAllText(CaminhoDasSagas),
				new JsonSerializerOptions { IncludeFields = true });
		}
		catch { return null; }
	}

	/// <summary>
	/// O REINICIO DA BANCADA: os corpos que nao atravessam o processo somem (chefes e o vilao), a memoria das sagas e jogada fora, o relogio
	/// do mundo recomeca do zero, o estado volta do DISCO (`CarregarSagas`) e o primeiro tique da cadeia roda (o `RetomarSagas`).
	/// </summary>
	private void ReiniciarAsSagas()
	{
		foreach (ServerPlayer c in ChefesNoMundo()) RemoverNpc(c);
		if (_vilaoDoInferno is { } v && _players.TryGetValue(v.Npc, out ServerPlayer? corpo)) RemoverNpc(corpo);
		_vilaoDoInferno = null;
		_vilaoPorRetomar = null;
		_relogioDoMundo = 0;
		_sagas = new EstadoDasSagas();
		CarregarSagas();
		TickDasSagas();
	}

	// =====================================================================
	// F) A FORMA NO DISCO
	// =====================================================================
	private void AFormaNoDisco()
	{
		GD.Print("[chefes] -- F) a forma no disco: o Freeza que se transforma volta do reinicio na forma em que estava --");

		// a forma 3 por dano, pelo caminho de producao (o roteiro le o pior membro); o monitor da cadeia (1 Hz) copia o degrau -- e mais nada
		int Transformar(ServerPlayer f)
		{
			for (int passo = 0; passo < 2; passo++)
			{
				PorPiorMembroEm(f, f.Papel!.GatilhoAtual - 0.01);
				TickDoRoteiro();
			}
			MonitorarElos();
			return f.Papel!.Estagio;
		}

		SagasLimpas();
		_sagas.Do("freeza_vegeta")!.Fase = FaseDaSaga.Vencida;
		(EloDaSaga elo, EstadoDoElo est, EstadoDoChefe ec) = SagaComOChefeNoMundo("freeza_namek", "freeza_namek");
		SalvarSagas();   // o disco com o Freeza na forma 1
		ServerPlayer? freeza = _players.GetValueOrDefault(ec.Corpo);
		AfirmarCh("(montagem) o Freeza de Namek chegou pelo nascimento de saga, na forma 1", freeza?.Papel?.Estagio == 0);
		if (freeza == null) return;
		int forma = Transformar(freeza);
		int noDisco = SagasDoDisco()?.Do("freeza_namek")?.Chefes.FirstOrDefault()?.Degrau ?? -1;
		AfirmarCh("**A FORMA VAI PRO DISCO NA HORA**: o Freeza se transforma por dano (forma 3) e o `sagas.json` ja diz forma 3 -- sem virada de "
				  + "dia, sem morte, sem gravacao a mao", forma == 2 && noDisco == 2, $"forma {forma + 1}; no disco {noDisco + 1}");

		ReiniciarAsSagas();
		ServerPlayer? voltou = ChefesNoMundo().FirstOrDefault(c => c.Papel?.Molde.Id == "freeza_namek");
		string corpoDaForma3 = _moldes!.Get("freeza_namek")!.Estagios[2].Corpo;
		AfirmarCh("**O FREEZA VOLTA DO REINICIO NA FORMA EM QUE ESTAVA** (*\"ele deveria voltar na forma q ele estava\"*): o processo cai, o estado "
				  + "volta do disco e o primeiro tique o poe de volta no mundo -- na forma 3, com o corpo dela",
				  voltou?.Papel?.Estagio == 2 && corpoDaForma3.Length > 0 && voltou.Visual.FormasDeFrost.FirstOrDefault() == corpoDaForma3,
				  $"forma {(voltou?.Papel?.Estagio ?? -1) + 1}; corpo {voltou?.Visual.FormasDeFrost.FirstOrDefault()} (o da forma 3: {corpoDaForma3})");

		// O "SAVE ALL" (o verb do admin, o `salvar` do terminal) grava as sagas -- e o FECHAMENTO tambem
		if (voltou != null && _sagas.Do("freeza_namek") is { Chefes.Count: > 0 } est2)
		{
			est2.Chefes[0].DiasRestantes = 4242;   // um numero que so a memoria tem
			GravarTudoAgora();
			AfirmarCh("...e o \"Save All\" grava as sagas junto (o personagem, o mundo, as bases, os cargos -- e agora elas)",
					  SagasDoDisco()?.Do("freeza_namek")?.Chefes.FirstOrDefault()?.DiasRestantes == 4242);
			est2.Chefes[0].DiasRestantes = 0;
		}
		string fechamento = CorpoDoMetodo(LerFonteDaBancada("Server/GameServer.cs"), "public void SalvarEParar(");
		AfirmarCh("...e o FECHAMENTO do servidor tambem (`SalvarEParar` chama o `SalvarSagas`) -- antes ele gravava tudo menos elas",
				  fechamento.Length > 100 && fechamento.Contains("SalvarSagas();"), $"{fechamento.Length} caracteres lidos");

		// o contra-exemplo: a forma nova so vai pro disco no proximo evento
		SagasLimpas();
		_sagas.Do("freeza_vegeta")!.Fase = FaseDaSaga.Vencida;
		(_, _, EstadoDoChefe ec3) = SagaComOChefeNoMundo("freeza_namek", "freeza_namek");
		SalvarSagas();
		DegrauSoNoProximoEventoDeTeste = true;
		int formaComODefeito, voltouComODefeito;
		try
		{
			formaComODefeito = _players.TryGetValue(ec3.Corpo, out ServerPlayer? f3) ? Transformar(f3) : -1;
			ReiniciarAsSagas();
			voltouComODefeito = ChefesNoMundo().FirstOrDefault(c => c.Papel?.Molde.Id == "freeza_namek")?.Papel?.Estagio ?? -1;
		}
		finally { DegrauSoNoProximoEventoDeTeste = false; }
		AfirmarCh("(defeito injetado: a forma so vai pro disco no proximo evento, o jogo de antes) o Freeza que se transformou volta do reinicio "
				  + "na FORMA 1 -- a regua de cima REPROVA", formaComODefeito == 2 && voltouComODefeito == 0,
				  $"forma antes do reinicio {formaComODefeito + 1}; depois {voltouComODefeito + 1}");
		SagasLimpas();
	}

	// =====================================================================
	// C) O CELL QUE VOLTA DO ALEM
	// =====================================================================
	private void OCellQueVoltaDoAlem()
	{
		GD.Print("[chefes] -- C) o Cell que volta do alem: o tempo que faltava atravessa o reinicio --");
		ChefeDaSaga fichaDoCell = Array.Find(Array.Find(_cadeia, e => e.Id == "cell")!.Chefes, c => c.Molde == "cell")!;

		double FaltavaDepoisDoReinicio()
		{
			SagasLimpas();
			foreach (string id in new[] { "freeza_vegeta", "freeza_namek" }) _sagas.Do(id)!.Fase = FaseDaSaga.Vencida;
			EstadoDoElo est = _sagas.Do("cell")!;
			est.Disparou = true;
			est.Fase = FaseDaSaga.EmCurso;
			// UMA HORA DE JOGO no relogio do mundo, e o Cell morto na forma perfeita voltando em 40 s
			_relogioDoMundo = 3600;
			est.Chefes.Add(new EstadoDoChefe
			{
				Molde = "cell", Fase = FaseDoChefe.Derrotado, Degrau = 3, RessurgeEm = _relogioDoMundo + 40,
				Bps = Sagas.Pinar(_moldes!.Get("cell")!, MediaDoServidor()),
			});
			SalvarSagas();
			ReiniciarAsSagas();   // o relogio do mundo recomeca do zero
			EstadoDoChefe? ec = _sagas.Do("cell")?.Chefes.FirstOrDefault();
			return ec == null ? -1 : ec.RessurgeEm - _relogioDoMundo;
		}

		double falta = FaltavaDepoisDoReinicio();
		AfirmarCh("**O CELL QUE VOLTAVA EM 40 S VOLTA EM 40 S DEPOIS DO REINICIO**: o tempo que faltava vai pro disco, e o relogio do mundo, que "
				  + $"recomeca do zero, e re-ancorado (o prazo inteiro do DM e {fichaDoCell.RessurgeEmSegundos:0} s)",
				  falta is > 38 and < 42, $"falta {falta:0.#} s");

		RessurgeNoRelogioVelhoDeTeste = true;
		double comODefeito;
		try { comODefeito = FaltavaDepoisDoReinicio(); }
		finally { RessurgeNoRelogioVelhoDeTeste = false; }
		AfirmarCh("(defeito injetado: o `RessurgeEm` do relogio velho, o jogo de antes) o Cell so voltaria uma hora depois do boot -- a regua de "
				  + "cima REPROVA", comODefeito > 3000, $"falta {comODefeito:0} s");
		SagasLimpas();
	}

	// =====================================================================
	// I) O VILAO DO INFERNO
	// =====================================================================
	private void OVilaoDoInfernoVolta()
	{
		GD.Print("[chefes] -- I) o vilao do Inferno: volta do reinicio igual, com o tempo que lhe faltava --");

		(string Molde, int Forma, double Bp, double Zeni, long Falta)? Retrato()
		{
			if (_vilaoDoInferno is not { } v || !_players.TryGetValue(v.Npc, out ServerPlayer? npc) || npc.Papel == null) return null;
			return (v.Molde, npc.Papel.Estagio, npc.Ficha.BP, npc.Ficha.Zeni, v.Ate - NowMs());
		}

		(string Molde, int Forma, double Bp, double Zeni, long Falta)? antes, depois, comODefeito;
		SagasLimpas();
		NascerVilaoDoInferno(NowMs(), qualquerDaCadeia: true);
		antes = Retrato();
		SalvarSagas();
		ReiniciarAsSagas();
		depois = Retrato();
		AfirmarCh("**O VILAO DO INFERNO VOLTA DO REINICIO** (*\"isso pra todos os chefes n so o freeza\"*): o mesmo molde, a mesma forma, o mesmo "
				  + "BP e o mesmo premio, com o tempo que lhe faltava (e nao os 15 min de novo)",
				  antes != null && depois != null && depois.Value.Molde == antes.Value.Molde && depois.Value.Forma == antes.Value.Forma
				  && Math.Abs(depois.Value.Bp - antes.Value.Bp) < 1 && Math.Abs(depois.Value.Zeni - antes.Value.Zeni) < 1e-6
				  && depois.Value.Falta > 0 && depois.Value.Falta <= antes.Value.Falta,
				  $"antes {antes}; depois {depois}");

		VilaoSoNaMemoriaDeTeste = true;
		try
		{
			SagasLimpas();
			NascerVilaoDoInferno(NowMs(), qualquerDaCadeia: true);
			SalvarSagas();
			ReiniciarAsSagas();
			comODefeito = Retrato();
		}
		finally { VilaoSoNaMemoriaDeTeste = false; }
		AfirmarCh("(defeito injetado: o vilao so na memoria, o jogo de antes) depois do reinicio o Inferno esta vazio -- a regua de cima REPROVA",
				  comODefeito == null, $"{comODefeito}");
		if (_vilaoDoInferno is { } v2 && _players.TryGetValue(v2.Npc, out ServerPlayer? resto)) RemoverNpc(resto);
		_vilaoDoInferno = null;
		SagasLimpas();
	}

	// =====================================================================
	// M) O MARCO BATIDO
	// =====================================================================
	private void OMarcoBatido(ServerPlayer pl)
	{
		GD.Print("[chefes] -- M) o marco batido: a saga comeca na vez dela, com quem bateu o marco fora do jogo; e a auditoria do boot --");
		EloDaSaga namek = Array.Find(_cadeia, e => e.Id == "freeza_namek")!;
		EloDaSaga vegeta = Array.Find(_cadeia, e => e.Id == "freeza_vegeta")!;
		double bpDeVolta = pl.Ficha.BP;

		// 1. a cadeia esperando: a saga 1 em curso (o Freeza ainda a caminho), e o jogador passa do marco da 2
		(bool Registrou, bool EsperouAVez, bool Disparou) NaVezDela()
		{
			SagasLimpas();
			EstadoDoElo e1 = _sagas.Do("freeza_vegeta")!;
			e1.Disparou = true;
			e1.Fase = FaseDaSaga.Contando;
			e1.Chefes.Add(new EstadoDoChefe { Molde = "freeza_vegeta", DiasRestantes = 99, Bps = [1] });
			pl.Ficha.BP = namek.GatilhoBp * 2;
			ConferirMarcos();
			EstadoDoElo e2 = _sagas.Do("freeza_namek")!;
			bool registrou = e2.MarcoBatidoPor == pl.Name;
			bool esperou = !e2.Disparou;
			// quem bateu o marco SAI (nenhum online passa dele agora), e a saga 1 acaba
			pl.Ficha.BP = 1;
			e1.Fase = FaseDaSaga.Vencida;
			e1.Chefes.Clear();
			ConferirMarcos();
			return (registrou, esperou, e2 is { Disparou: true, Fase: FaseDaSaga.Contando });
		}

		var naVez = NaVezDela();
		AfirmarCh("**O MARCO BATIDO FICA REGISTRADO, E A SAGA COMECA NA VEZ DELA** (*\"caso ... tenham cumprido o requisito dela ela inicie\"*): "
				  + "com a cadeia esperando, quem passa do marco da saga 2 fica no registro; a saga 1 acaba com ele fora -- e a 2 comeca do mesmo jeito",
				  naVez.Registrou && naVez.EsperouAVez && naVez.Disparou, $"{naVez}");
		MarcoSoDeQuemEstaAquiDeTeste = true;
		(bool Registrou, bool EsperouAVez, bool Disparou) soOnline;
		try { soOnline = NaVezDela(); }
		finally { MarcoSoDeQuemEstaAquiDeTeste = false; }
		AfirmarCh("(defeito injetado: so dispara com quem esta online, o jogo de antes) a saga 2 fica esperando alguem do marco aparecer -- a regua "
				  + "de cima REPROVA", soOnline.Registrou && !soOnline.Disparou, $"{soOnline}");

		// 2. a auditoria do boot: a saga 1 dormindo, ninguem online no marco -- e no DISCO um Saiyajin que ja passou dele
		if (_store != null)
		{
			var plantada = new AccountSave { Conta = ContaPlantadaDosChefes };
			plantada.Slots[0] = new CharacterSave { Nome = "Kakaroto do Disco", Raca = "Saiyan", Ficha = new Fighter { Race = "Saiyan", BP = vegeta.GatilhoBp * 3 } };
			_store.Gravar(plantada);
		}
		(string Marco, bool Disparou) Boot()
		{
			SagasLimpas();
			pl.Ficha.BP = 1;
			DiscoComAsBancadasDeTeste = true;
			try { AuditarSagasNoBoot(); }
			finally { DiscoComAsBancadasDeTeste = false; }
			ConferirMarcos();
			EstadoDoElo e1 = _sagas.Do("freeza_vegeta")!;
			return (e1.MarcoBatidoPor, e1 is { Disparou: true, Fase: FaseDaSaga.Contando });
		}

		var boot = Boot();
		AfirmarCh("**A AUDITORIA DO BOOT ACHA O MARCO NO DISCO** (*\"faça o server checar ... ou tenham cumprido o requisito dela\"*): o jogo de "
				  + "antes so conferia os online; no boot, um Saiyajin gravado acima do marco do Planeta Vegeta e registrado -- e a saga 1 comeca",
				  boot.Marco == "Kakaroto do Disco" && boot.Disparou, $"{boot}");
		SemAuditoriaDeTeste = true;
		(string Marco, bool Disparou) semAuditoria;
		try { semAuditoria = Boot(); }
		finally { SemAuditoriaDeTeste = false; }
		AfirmarCh("(defeito injetado: sem a auditoria, o jogo de antes) a saga 1 continua dormindo -- a regua de cima REPROVA",
				  semAuditoria.Marco.Length == 0 && !semAuditoria.Disparou, $"{semAuditoria}");

		// 3. o "disparada sem comecar" (o save cortado no meio): volta a esperar o marco, do comeco
		SagasLimpas();
		EstadoDoElo cortada = _sagas.Do("freeza_vegeta")!;
		cortada.Disparou = true;
		cortada.Chefes.Add(new EstadoDoChefe { Molde = "freeza_vegeta" });
		AuditarSagasNoBoot();
		AfirmarCh("...e a saga MARCADA E NUNCA COMECADA (o `Disparou` sem fase de um save cortado) volta a esperar o marco, do comeco",
				  cortada is { Disparou: false, Fase: FaseDaSaga.Adormecida, Chefes.Count: 0 }, $"{cortada.Disparou} {cortada.Fase} {cortada.Chefes.Count}");

		pl.Ficha.BP = bpDeVolta;
		SagasLimpas();
	}
}
