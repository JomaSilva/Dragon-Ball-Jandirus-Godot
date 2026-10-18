using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// OS GANCHOS DA FOTO DA SAGA DO CORRUPTED MAJIN (`--diagsagamajin`, `Client/RoboDaSagaMajin.cs`, 2026-10-16).
///
/// SO MONTAM A CENA E LEEM O ESTADO. Quem sobe a saga e o codigo de producao, pelos MESMOS gatilhos do jogo: o verb de absorver
/// (`HabilidadeNaFotoDoMajin`, o `UsarHabilidade`), o luto (`AoPerderALuta`), a outra metade vencida e absorvida, os tres jogadores
/// selados. A sanidade, o Ki e o fim do sono sao escritos a mao -- esperar quinze minutos de Pura sa, o Ki da insana acabar ou dois
/// minutos de sono nao cabe numa foto --, e quem REAGE a eles e o tique de producao (`TickDaLoucuraDoMajin`). As regras sao da
/// `--sagamajinteste`; aqui o que se ve e a tela.
/// </summary>
public partial class GameServer
{
	/// <summary>Os corpos que esta foto pos no mundo -- o <see cref="LimparAFotoDaSaga"/> os tira.</summary>
	private readonly List<int> _fotoDaSagaNascidos = [];

	/// <summary>O prefixo do nome dos corpos da foto -- e por ele que a limpeza acha o cadaver do amigo.</summary>
	private const string NomeDaFotoDaSaga = "Foto: ";

	/// <summary>
	/// O CORPO LOCAL VIRA UM CORRUPTED MAJIN -- a classe rara que anda na saga (`is_corrupted_majin()`), no rosa da foto do Majin (a
	/// criacao automatica da bancada nasce sem cor, e o Majin sem cor e um vulto quase preto), sabendo absorver.
	/// </summary>
	internal bool CorromperNaFotoDaSaga(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || !CorpoDeMajin(pl)) return false;
		pl.Class = SagaDoMajin.Classe;
		pl.Ficha.Class = SagaDoMajin.Classe;
		pl.Visual.CorPele = RosaDaFotoDoMajin;
		// A SKILL QUE CONCEDE O VERB DE ABSORVER -- a criacao automatica nao a da (a `--diagmajin` a pede pelo `--skillteste`).
		pl.Livro?.Dar(AbsorcaoMajin.Skill);
		TrocarAparencias(pl);
		MandarFicha(pl);
		MandarSkills(pl, forcar: true);
		Persistir(pl);
		return CorrompidoDaSaga(pl);
	}

	/// <summary>O ESTADO DA SAGA deste corpo, na conta do servidor. `Dirigido` = a IA tem as redeas.</summary>
	internal (int Forma, bool Pura, double Sanidade, string FormaAtual, bool Dormindo, bool Dirigido, int Dentro, int Metade, double Ki)
		SagaNaFoto(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return default;
		bool dorme = _majinsPuros.TryGetValue(id, out MajinPuroVivo? v) && v.DormeAte != 0;
		return (pl.Ficha.majin_saga_form, pl.Ficha.majin_pure_unlocked, pl.Ficha.majin_sanidade, pl.Forma.Atual, dorme,
				pl.Cerebro != null, AbsorvidosPor(id).Count, OutraMetadeDe(id)?.Id ?? 0, pl.Ficha.Ki / Math.Max(1, pl.Ficha.MaxKi));
	}

	/// <summary>
	/// UM CORPO DA FOTO, a <paramref name="desloc"/> do dono: a aparencia sorteada do catalogo (como todo habitante) e parado (sem
	/// `Cerebro`). `pessoa` da conta e vaga -- a assinatura que separa o jogador SELADO do NPC devorado (`SelarNoMajin`).
	/// </summary>
	private ServerPlayer CorpoDaFotoDaSaga(string nome, ServerPlayer dono, Vec2 desloc, double bp, string raca, bool pessoa)
	{
		var f = new Fighter
		{
			Name = nome, Race = raca, BP = bp,
			physoff = 1, physdef = 1, technique = 1, kioff = 1, kidef = 1,
			kiskill = 1, speed = 5, magiskill = 1, Idade = 25,
			maxstamina = 100, stamina = 100,
		};
		f.Statify();
		f.PowerLevel();
		f.Ki = f.MaxKi;
		f.Class = "Normal";

		int id = _nextId++;
		var c = new ServerPlayer
		{
			Id = id,
			Peer = null,
			Name = nome,
			Race = raca,
			Genero = "Male",
			Idade = 25,
			Zone = dono.Zone,
			Pos = dono.Pos + desloc,
			Conta = pessoa ? $"bancada_foto_saga_{id}" : "",
			Slot = pessoa ? 0 : -1,
			LastInputMs = NowMs(),
			Ficha = f,
			Livro = new Jandirus.Core.Skills.SkillBook(),
			Cerebro = null,
			Visual = AparenciaDeNpc(
				new MoldeDeNpc { Id = "bancada_foto_saga", Racas = [raca], Classe = "Normal" }, raca, "Male", (ulong)id),
		};
		PorNoMundo(c);
		c.Ficha.Tick(agoraMs: NowMs());
		TrocarAparencias(c);
		_fotoDaSagaNascidos.Add(id);
		return c;
	}

	/// <summary>UMA VITIMA NOCAUTEADA a um tile do dono, caida de frente pra ele -- um Kai (a forma 1) ou um jogador (os tres da forma 3).</summary>
	internal int VitimaNaFotoDaSaga(int idDono, string raca, bool pessoa)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? dono)) return 0;
		string nome = raca == "Kai" ? NomeDaFotoDaSaga + "o Kai" : $"{NomeDaFotoDaSaga}a vítima {_fotoDaSagaNascidos.Count + 1}";
		ServerPlayer v = CorpoDaFotoDaSaga(nome, dono, new Vec2(ZoneCollision.TileSize, 0), 400_000, raca, pessoa);
		v.ApontarRumoDoGolpe(new Vec2(-1, 0));
		v.Combate.Nocautear(600);
		MandarFicha(v);
		return v.Id;
	}

	/// <summary>
	/// O LUTO DA FORMA 2: um amigo DECLARADO a dois tiles, morto por um algoz a tres -- o `AoPerderALuta` de producao, que acende a
	/// furia na vizinhanca e parte o Majin da forma 1 (`Death.dm:82`).
	/// </summary>
	internal bool AmigoMortoNaFotoDaSaga(int idDono)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? dono)) return false;
		const int T = ZoneCollision.TileSize;
		ServerPlayer amigo = CorpoDaFotoDaSaga(NomeDaFotoDaSaga + "o amigo", dono, new Vec2(2 * T, 0), 500_000, "Human", pessoa: true);
		ServerPlayer algoz = CorpoDaFotoDaSaga(NomeDaFotoDaSaga + "o algoz", dono, new Vec2(3 * T, 0), 900_000, "Human", pessoa: true);
		DeclararDeTeste(dono, amigo, Jandirus.Core.Social.Relacao.Bom);
		AoPerderALuta(amigo, algoz, morreu: true);
		return dono.Ficha.majin_saga_form == 2;
	}

	/// <summary>A OUTRA METADE VENCIDA, a um tile do mestre e de frente pra ele: nocauteada (o `M.KO` da recusa do DM, `:125`).</summary>
	internal int VencerAOutraMetadeNaFoto(int idDono)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? dono) || OutraMetadeDe(idDono) is not { } metade) return 0;
		metade.Pos = dono.Pos + new Vec2(ZoneCollision.TileSize, 0);
		metade.ApontarRumoDoGolpe(new Vec2(-1, 0));
		metade.Combate.Nocautear(600);
		MandarFicha(metade);
		return metade.Id;
	}

	/// <summary>O BP BASE do corpo -- a porta da Forma Pura e de 2 bilhoes, e o corpo da bancada nasce com 3 milhoes.</summary>
	internal void BpBaseNaFotoDaSaga(int id, double bp)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.BP = bp;
		pl.Ficha.Statify();
		RepercutirPoder(pl);
		MandarFicha(pl);
	}

	/// <summary>
	/// A SANIDADE escrita a mao; quem reage e o tique de producao (a tela da loucura acompanha pelo `S2C.Loucura`). A LOUCURA SO ENTRA NA
	/// DESCIDA -- o tique compara antes e depois do passo --, entao a foto dela escreve um fio acima de zero e deixa a Pura sa gastar o resto.
	/// </summary>
	internal void SanidadeNaFotoDaSaga(int id, double s)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Ficha.majin_sanidade = Math.Clamp(s, 0, SagaDoMajin.SanidadeCheia);
	}

	/// <summary>O Ki do corpo, em fracao do maximo. A insana adormece quando ele acaba (`TickDaInsana`).</summary>
	internal void KiNaFotoDaSaga(int id, double fracao)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.Ki = pl.Ficha.MaxKi * Math.Clamp(fracao, 0, 1);
		MandarFicha(pl);
	}

	/// <summary>O SONO VENCE AGORA (os dois minutos acabam), e quem acorda o corpo e o tique (`Acordar`). Falso = ninguem dormia.</summary>
	internal bool AcordarNaFotoDaSaga(int id)
	{
		if (!_majinsPuros.TryGetValue(id, out MajinPuroVivo? v) || v.DormeAte == 0) return false;
		v.DormeAte = NowMs();
		return true;
	}

	/// <summary>O SOCO QUE MATA: o Majin se DESFAZ em pedacos pela porta de producao (`CombatState.Desfazer`) -- a regeneracao de morte.</summary>
	internal bool DesfazerNaFotoDaSaga(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate == null) return false;
		pl.Combate.Desfazer();
		MandarFicha(pl);
		return pl.Combate.Desfeito;
	}

	/// <summary>OS 30 S DO DESFEITO VENCEM AGORA: o corpo se levanta inteiro (`CombatState.Levantar`), e a tela recompoe os pedacos.</summary>
	internal void LevantarNaFotoDaSaga(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate == null) return;
		pl.Combate.Levantar();
		MandarFicha(pl);
	}

	/// <summary>
	/// O CORPO LOCAL COMO UM MAJIN COMUM -- o passo 0b da foto, antes da saga: a classe de fabrica e o mesmo rosa da foto (a criacao
	/// automatica nasce sem cor). O dono: *"o conserto na regeneraçao do majin é pra todos, nao so o corrupted"* -- a regeneracao de
	/// morte e fotografada primeiro nele. Verdadeiro = e Majin, e a saga nao o toca.
	/// </summary>
	internal bool MajinComumNaFotoDaSaga(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || !CorpoDeMajin(pl)) return false;
		pl.Class = "Normal";
		pl.Ficha.Class = "Normal";
		pl.Visual.CorPele = RosaDaFotoDoMajin;
		// A ROUPA DE UM HABITANTE SORTEADO (o mesmo gerador das vitimas), se o corpo nao veste nada -- a criacao automatica nasce sem
		// roupa, e a foto tem de mostrar o que some e volta: os olhos E a roupa.
		if (pl.Visual.Roupa.Count == 0)
			pl.Visual.Roupa.AddRange(AparenciaDeNpc(new MoldeDeNpc { Id = "bancada_foto_saga", Racas = ["Human"], Classe = "Normal" },
																				"Human", "Male", (ulong)id).Roupa);
		TrocarAparencias(pl);
		MandarFicha(pl);
		return !CorrompidoDaSaga(pl);
	}

	/// <summary>
	/// A VITIMA AINDA ESTA NO MUNDO? E quantos CADAVERES ha na zona do dono. O dono (2026-10-16): *"lembrando q majin absorver npc faz
	/// ele sumir sem deixar um corpo"* -- as fotos de antes mostravam a vitima forjada caida, com o "[E] revistar" em cima.
	/// </summary>
	internal (bool NoMundo, int Cadaveres) VitimaNoMundoNaFotoDaSaga(int idVitima, int idDono) =>
		(_players.ContainsKey(idVitima),
		 _players.TryGetValue(idDono, out ServerPlayer? dono) ? ZoneList(dono.Zone.Hash).Count(o => o.ECadaver) : 0);

	/// <summary>O RAIO NA MAO: o verb `Ki_Wave` de producao -- o primeiro aperto carrega, o segundo solta. Devolve se esta carregando.</summary>
	internal bool RaioNaFotoDaSaga(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		KiWave(pl);
		return _canais.ContainsKey(id);
	}

	/// <summary>A SYCOPHANTIC RAGE pelo canal de producao (`UsarHabilidade`). Devolve por quanto o tiro armado sai (1 = nada armado).</summary>
	internal double FuriaNaFotoDaSaga(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		UsarHabilidade(pl, SagaDoMajin.VerboDaFuria);
		return _majinsPuros.TryGetValue(id, out MajinPuroVivo? v) ? v.FuriaNoTiro : 1;
	}

	/// <summary>
	/// TIRA DO MUNDO OS CORPOS DA FOTO -- depois de o Majin cuspir quem tinha dentro, pela porta de producao (um corpo removido com
	/// gente dentro deixaria o prisioneiro esperando o tique notar), e a outra metade, se sobrou. O cadaver do amigo sai pelo nome.
	/// </summary>
	internal void LimparAFotoDaSaga(int idDono)
	{
		if (_players.TryGetValue(idDono, out ServerPlayer? dono) && AlguemDentro(dono)) CuspirTodosDoMajin(idDono, "");
		if (OutraMetadeDe(idDono) is { } metade)
		{
			_outrasMetades.Remove(metade.Id);
			RemoverNpc(metade);
		}
		foreach (int id in _fotoDaSagaNascidos)
		{
			_prontoAbsorver.Remove(id);
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) continue;
			_players.Remove(id);
			ZoneList(pl.Zone.Hash).Remove(pl);
		}
		_fotoDaSagaNascidos.Clear();
		if (dono == null) return;
		// O QUE A ABSORCAO DEVOLVEU COM ID NOVO (o NPC cuspido) e o cadaver do amigo saem pelo nome.
		foreach (ServerPlayer pl in ZoneList(dono.Zone.Hash).ToList())
		{
			if (!pl.Name.StartsWith(NomeDaFotoDaSaga, StringComparison.Ordinal)
				&& !pl.NomeDeQuemMorreu.StartsWith(NomeDaFotoDaSaga, StringComparison.Ordinal)) continue;
			ZoneList(dono.Zone.Hash).Remove(pl);
			if (_players.TryGetValue(pl.Id, out ServerPlayer? mesmo) && ReferenceEquals(mesmo, pl)) _players.Remove(pl.Id);
		}
	}
}
