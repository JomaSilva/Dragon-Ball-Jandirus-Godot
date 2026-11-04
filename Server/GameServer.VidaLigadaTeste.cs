using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--vidaligadateste` (2026-11-01) -- o pedido 216 do dono: *"sempre q um kaioshin player morrer o player deus da destruição recebe
/// um aviso urgente q alguem ta matando um kaioshin ... com isso de todos os players kaioshins morrerem o deus da destruição tb morre. Se n
/// tiver kaioshin player nads acontece com o deus mesmo se matar os npcs kaioshins (eles n contam) e se n tiver player deus da destruição e
/// todos os players kaioshins morrerem o bills tb n morre por ser npc"*.
///
/// SERVIDOR PURO, NO BOOT, dentro do `PalcoDeApagamentos`: a pasta dos saves e trocada por uma temporaria, entao o Deus e o Kaioshin de FORA
/// do jogo sao saves de verdade, gravados e relidos de verdade -- noutro lugar. Os tronos dos dois cargos voltam ao que eram no fim. Os corpos
/// sao forjados sem `Peer`, com conta e slot proprios, e tudo passa pelas portas de producao: o `Morrer()` (que chama o funil da morte), o
/// `TickDaVidaLigada` e a entrada do Deus (`EntregarOQueOsKaioshinsDeixaram`).
///   L1 um Kaioshin de jogador cai: o funil SO ANOTA; o tique avisa o Deus (no tom urgente, so a ele) com o nome e "resta 1"; o Deus vive;
///   L2 o Kaioshin NPC (o Supremo Kaioh do Ceu: sem conta, sem vaga) cai: nada;
///   L3 o ultimo cai: o aviso de "o ultimo", e o Deus morre no tique, pela porta comum (o prazo do cadaver armado);
///   L4 sem Deus jogador, os Kaioshins todos caem e nada acontece com ninguem (o Bills e NPC);
///   L5 o Deus FORA DO JOGO: os nomes e a divida vao pro save dele (relido do disco); ao entrar ele le o aviso e morre;
///   L6 ...mas se um Kaioshin voltou a vida antes da entrada, a ligacao o poupa;
///   L7 o Kaioshin FORA DO JOGO conta pelo save: vivo no save, o Deus vive ("resta 1"); morto no save, o Deus morre com o ultimo do jogo;
///   L8 a MENTE: o Kaioshin que morre dentro dela nao e anotado; o Deus que esta nela so paga quando sai.
///
///     Godot --headless --path . --server --port 12850 --vidaligadateste
/// </summary>
public partial class GameServer
{
	private int _vlOk, _vlFalhou;
	private const int IdBaseDaVidaLigadaDeTeste = 0x7166_0000;

	private void AfirmarVl(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _vlOk++; GD.Print($"[vidaligada]   OK    {oque}   [{detalhe}]"); return; }
		_vlFalhou++;
		GD.PrintErr($"[vidaligada]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDaVidaLigada()
	{
		_vlOk = _vlFalhou = 0;
		GD.Print("[vidaligada] ================ A VIDA DOS KAIOSHINS LIGADA A DO DEUS DA DESTRUICAO (dono, 2026-11-01) ================");
		RankDef? kai = Cargos.Get(Jandirus.Core.Ranks.RitualDoKaioshin.Cargo), god = Cargos.Get(KitDoDeus.Cargo);
		if (kai == null || god == null)
		{
			AfirmarVl("(montagem) os dois cargos existem no catalogo", false, $"kaioshin {kai != null}; deus {god != null}");
			GD.Print($"[vidaligada] ================ {_vlOk} passaram, {_vlFalhou} falharam ================");
			return;
		}

		var tronosAntes = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		var personagensAntes = new Dictionary<string, (string Conta, int Slot, string Nome)>(_personagensDosTronos, StringComparer.OrdinalIgnoreCase);
		ZoneKey terra = ZoneKey.Premade("Earth");
		var corpos = new List<ServerPlayer>();
		ServerPlayer Forjar(string nome, string conta)
		{
			int i = corpos.Count;
			var p = new ServerPlayer
			{
				Id = IdBaseDaVidaLigadaDeTeste + i, Peer = null, Name = nome, Race = "Human", Genero = "Male", Idade = 25, Zone = terra,
				Pos = new Vec2(i * 3 * ZoneCollision.TileSize, 0), Conta = conta, Slot = conta.Length > 0 ? 0 : -1,
				Ficha = new Fighter { Race = "Human", BP = 10_000 }, Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			p.Ficha.Class = "Normal";
			PorNoMundo(p);
			corpos.Add(p);
			return p;
		}
		void Matar(ServerPlayer p) => p.Combate.Morrer(ignorarSeguro: true);
		string Ditos() => string.Join(" | ", (UrgentesDeTeste ?? []).Select(u => $"{u.Para}: {u.Texto}"));

		var palco = new PalcoDeApagamentos(this);
		UrgentesDeTeste = [];
		try
		{
			foreach (string v in VagasDe(kai).Concat(VagasDe(god)).ToList()) Desocupar(v);
			_kaioshinsQueCairam.Clear();
			_deusDevendoAMorte = null;

			ServerPlayer deus = Forjar("bancada: o Deus", "bancada_vl_deus");
			ServerPlayer k1 = Forjar("bancada: Kaioshin um", "bancada_vl_kai_um");
			ServerPlayer k2 = Forjar("bancada: Kaioshin dois", "bancada_vl_kai_dois");
			ServerPlayer npc = Forjar("bancada: Supremo Kaioh do Ceu", "");
			Ocupar(Vaga(god, 0), deus);
			Ocupar(Vaga(kai, 0), k1);
			Ocupar(Vaga(kai, 1), k2);

			// ---- L1) um Kaioshin cai
			Matar(k1);
			bool soAnotou = UrgentesDeTeste.Count == 0 && _kaioshinsQueCairam.Count == 1;
			TickDaVidaLigada();
			AfirmarVl("L1. UM KAIOSHIN DE JOGADOR CAI: o funil so anota, e o tique avisa o Deus -- no tom urgente, so a ele -- com o nome e \"resta 1\"; o Deus vive",
					  soAnotou && UrgentesDeTeste.Count == 1 && UrgentesDeTeste[0].Para == deus.Id && UrgentesDeTeste[0].Texto == FraseDaQueda(k1.Name, 1)
					  && !deus.Ficha.dead && _deusDevendoAMorte == null,
					  $"so anotou {soAnotou}; {Ditos()}; Deus morto {deus.Ficha.dead}");

			// ---- L2) o Kaioshin NPC nao conta
			UrgentesDeTeste.Clear();
			Matar(npc);
			bool npcAnotado = _kaioshinsQueCairam.Count > 0;
			TickDaVidaLigada();
			AfirmarVl("L2. O SUPREMO KAIOH NPC (sem conta, sem vaga) cai e nada acontece: nem anotado, nem aviso, e o Deus vive",
					  npc.Ficha.dead && !npcAnotado && UrgentesDeTeste.Count == 0 && !deus.Ficha.dead, $"o NPC morto {npc.Ficha.dead}; anotado {npcAnotado}; {Ditos()}");

			// ---- L3) o ultimo cai, e o Deus com ele
			UrgentesDeTeste.Clear();
			Matar(k2);
			TickDaVidaLigada();
			bool pelaPortaComum = deus.RelogioDaMorte != long.MaxValue && deus.RelogioDaMorte > NowMs() - 5_000;
			AfirmarVl("L3. O ULTIMO KAIOSHIN CAI: o aviso de \"o ultimo\", e o Deus da Destruicao morre no tique -- pela porta comum (o prazo do cadaver armado)",
					  deus.Ficha.dead && pelaPortaComum && UrgentesDeTeste.Any(u => u.Para == deus.Id && u.Texto == FraseDaQueda(k2.Name, 0))
					  && UrgentesDeTeste.Any(u => u.Para == deus.Id && u.Texto == FraseDaMorteDoDeus) && _deusDevendoAMorte == null,
					  $"Deus morto {deus.Ficha.dead}; prazo armado {pelaPortaComum}; {Ditos()}");

			// ---- L4) sem Deus jogador, nada
			Desocupar(Vaga(god, 0));
			deus.Combate.Reviver();
			k1.Combate.Reviver();
			k2.Combate.Reviver();
			UrgentesDeTeste.Clear();
			Matar(k1);
			Matar(k2);
			TickDaVidaLigada();
			AfirmarVl("L4. SEM DEUS DA DESTRUICAO JOGADOR, os Kaioshins todos caem e nada acontece com ninguem (o Bills e NPC)",
					  UrgentesDeTeste.Count == 0 && !deus.Ficha.dead && _deusDevendoAMorte == null && _kaioshinsQueCairam.Count == 0,
					  $"{Ditos()}; o de antes morto {deus.Ficha.dead}");

			// ---- L5) o Deus fora do jogo
			const string ContaDoDeusDeFora = "bancada_vl_deus_de_fora";
			var saveDoDeus = new CharacterSave { Nome = "bancada: o Deus de fora", Raca = "Human" };
			palco.Emprestar(ContaDoDeusDeFora, 0, saveDoDeus);
			_tronos[Vaga(god, 0)] = ContaDoDeusDeFora;
			_personagensDosTronos[Vaga(god, 0)] = (ContaDoDeusDeFora, 0, saveDoDeus.Nome);
			k1.Combate.Reviver();
			k2.Combate.Reviver();
			UrgentesDeTeste.Clear();
			Matar(k1);
			TickDaVidaLigada();
			bool soONome = saveDoDeus.KaioshinsQueCairam is [var primeiro] && primeiro == k1.Name && !saveDoDeus.DeveAMorteDosKaioshins;
			Matar(k2);
			TickDaVidaLigada();
			bool eADivida = saveDoDeus.KaioshinsQueCairam is { Count: 2 } && saveDoDeus.DeveAMorteDosKaioshins && UrgentesDeTeste.Count == 0;
			CharacterSave? relido = new AccountStore(palco.PastaDeTeste).Carregar(ContaDoDeusDeFora)?.Slots[0];
			bool noDisco = relido is { DeveAMorteDosKaioshins: true, KaioshinsQueCairam.Count: 2 };
			ServerPlayer volta = Forjar(saveDoDeus.Nome, ContaDoDeusDeFora);
			EntregarOQueOsKaioshinsDeixaram(volta, saveDoDeus);
			bool leuOAviso = UrgentesDeTeste.Any(u => u.Para == volta.Id && u.Texto.Contains("enquanto você estava fora", StringComparison.Ordinal)
														&& u.Texto.Contains(k1.Name, StringComparison.Ordinal) && u.Texto.Contains(k2.Name, StringComparison.Ordinal));
			AfirmarVl("L5. O DEUS FORA DO JOGO: os nomes e a divida vao pro save dele (e o disco os guarda); ao entrar ele le o aviso urgente e morre",
					  soONome && eADivida && noDisco && leuOAviso && volta.Ficha.dead && saveDoDeus.KaioshinsQueCairam == null && !saveDoDeus.DeveAMorteDosKaioshins,
					  $"so o nome {soONome}; a divida {eADivida}; no disco {noDisco}; leu {leuOAviso}; morreu {volta.Ficha.dead}; {Ditos()}");

			// ---- L6) o Kaioshin que voltou poupa o Deus
			volta.Combate.Reviver();
			saveDoDeus.DeveAMorteDosKaioshins = true;
			k1.Combate.Reviver();
			UrgentesDeTeste.Clear();
			EntregarOQueOsKaioshinsDeixaram(volta, saveDoDeus);
			AfirmarVl("L6. ...MAS SE UM KAIOSHIN VOLTOU A VIDA antes da entrada, a ligacao poupa o Deus (e ele e avisado disso)",
					  !volta.Ficha.dead && UrgentesDeTeste.Any(u => u.Para == volta.Id && u.Texto == FraseDoKaioshinQueVoltou),
					  $"morto {volta.Ficha.dead}; {Ditos()}");

			// ---- L7) o Kaioshin fora do jogo conta pelo save
			const string ContaDoKaiDeFora = "bancada_vl_kai_de_fora";
			var saveDoKai = new CharacterSave { Nome = "bancada: o Kaioshin de fora", Raca = "Human" };
			palco.Emprestar(ContaDoKaiDeFora, 0, saveDoKai);
			Desocupar(Vaga(kai, 0));
			_tronos[Vaga(kai, 0)] = ContaDoKaiDeFora;
			_personagensDosTronos[Vaga(kai, 0)] = (ContaDoKaiDeFora, 0, saveDoKai.Nome);
			k2.Combate.Reviver();
			UrgentesDeTeste.Clear();
			Matar(k2);
			TickDaVidaLigada();
			bool vivePeloSave = !volta.Ficha.dead && UrgentesDeTeste.Any(u => u.Para == volta.Id && u.Texto == FraseDaQueda(k2.Name, 1));
			saveDoKai.Ficha.dead = true;
			k2.Combate.Reviver();
			UrgentesDeTeste.Clear();
			Matar(k2);
			TickDaVidaLigada();
			bool morrePeloSave = volta.Ficha.dead && UrgentesDeTeste.Any(u => u.Para == volta.Id && u.Texto == FraseDaQueda(k2.Name, 0));
			AfirmarVl("L7. O KAIOSHIN FORA DO JOGO CONTA PELO SAVE: vivo no save, o Deus vive (\"resta 1\"); morto no save, o Deus morre com o ultimo do jogo",
					  vivePeloSave && morrePeloSave, $"vive pelo save {vivePeloSave}; morre pelo save {morrePeloSave}; {Ditos()}");

			// ---- L8) a mente
			volta.Combate.Reviver();
			k2.Combate.Reviver();
			k2.Zone = DimensaoMental.De(k2.Id);
			Matar(k2);
			bool naMenteNaoAnota = _kaioshinsQueCairam.Count == 0;
			k2.Zone = terra;
			k2.Combate.Reviver();
			volta.Zone = DimensaoMental.De(volta.Id);
			Matar(k2);
			TickDaVidaLigada();
			bool esperou = !volta.Ficha.dead && _deusDevendoAMorte != null;
			TickDaVidaLigada();
			esperou &= !volta.Ficha.dead;
			volta.Zone = terra;
			TickDaVidaLigada();
			AfirmarVl("L8. A MENTE: o Kaioshin que morre dentro dela nao e anotado; o Deus que esta nela so paga a morte quando sai",
					  naMenteNaoAnota && esperou && volta.Ficha.dead && _deusDevendoAMorte == null,
					  $"na mente nao anota {naMenteNaoAnota}; esperou {esperou}; pagou ao sair {volta.Ficha.dead}");
		}
		catch (Exception e)
		{
			AfirmarVl("a bancada correu ate o fim sem excecao", false, $"{e.GetType().Name}: {e.Message}\n{e.StackTrace}");
		}
		finally
		{
			UrgentesDeTeste = null;
			_kaioshinsQueCairam.Clear();
			_deusDevendoAMorte = null;
			foreach (ServerPlayer p in corpos)
			{
				_players.Remove(p.Id);
				ZoneList(p.Zone.Hash).Remove(p);
			}
			_tronos.Clear();
			foreach ((string v, string conta) in tronosAntes) _tronos[v] = conta;
			_personagensDosTronos.Clear();
			foreach ((string v, (string Conta, int Slot, string Nome) p) in personagensAntes) _personagensDosTronos[v] = p;
			palco.Dispose();
		}
		GD.Print($"[vidaligada] ================ {_vlOk} passaram, {_vlFalhou} falharam ================");
	}
}
