using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// OS GANCHOS DA FOTO DA ABSORCAO DO MAJIN (`--diagmajin`, `Client/RoboDaAbsorcaoMajin.cs`).
///
/// SO MONTAM A CENA E LEEM O ESTADO. Quem absorve, quem solta e quem luta e o codigo de producao: o verb
/// entra pelo `UsarHabilidade` (o `case` do `C2S.Habilidade`), a imagem anda pelo `TickDosCorposSemDono`, as
/// saidas sao colhidas pelo `TickDoMajin`. A regra que a bancada de boot (`--majinteste`) ja segue.
/// </summary>
public partial class GameServer
{
	/// <summary>Os Majins que esta bancada pos no mundo -- o <see cref="LimparAFotoDoMajin"/> os tira.</summary>
	private readonly List<int> _fotoDoMajinNascidos = [];

	/// <summary>
	/// O ROSA QUE A TELA DE CRIACAO OFERECE DE SAIDA (`CreationScreen`, "Cor do corpo", `ff9ab5`).
	///
	/// O Majin e raca de COR LIVRE: a folha do corpo e cinza-escura e a cor entra SOMADA
	/// (`VisualCatalog`, o `ICON_ADD` do jogo). Sem cor escolhida ele e um vulto quase preto -- e a
	/// primeira foto desta bancada saiu assim: a imagem dele, um vulto preto num chao de carne escura,
	/// nao se via. O sorteio de aparencia de NPC nao escolhe cor de corpo (a Terra nao produz habitante
	/// Majin), entao quem pinta e a cena.
	/// </summary>
	private static readonly Jandirus.Core.Appearance.Rgb RosaDaFotoDoMajin = new(0xff, 0x9a, 0xb5);

	/// <summary>
	/// UM MAJIN AO LADO DO DONO, com a skill que concede o verb (`/datum/skill/general/buuabsorb`) e a
	/// aparencia de verdade -- corpo, tom e ROUPA sorteados do catalogo pelo `AparenciaDeNpc`, como todo
	/// habitante. A roupa importa pra foto: a imagem dele nasce SEM ela (ver `ErguerOGuardiaoDoMajin`), e
	/// so da pra ver isso se o original estiver vestido.
	///
	/// SEM `Cerebro`: ele fica parado onde foi posto. Quem aperta o botao dele e a bancada, pela porta de
	/// producao (<see cref="HabilidadeNaFotoDoMajin"/>).
	/// </summary>
	/// <returns>o id do corpo, ou 0 se o dono nao esta em jogo.</returns>
	internal int MajinDaFoto(int idDono, Vec2 desloc, double bp)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? dono)) return 0;

		// ATAQUE E DEFESA EM 1, e nao os 5 dos outros corpos de foto: a imagem herda os SETE stats do Majin
		// (`MajinSaga.dm:369-375`), e com 5 em cada ela nocauteou um corpo de bancada seis vezes mais
		// forte que ela em sete segundos -- a luta e de verdade. A cena precisa de uma imagem vencivel
		// a soco dentro da paciencia; quanto uma imagem aguenta com numeros de jogo nao e desta bancada.
		// A VELOCIDADE FICA EM 5: e ela que da o passo (`MoveRules.SpeedStatFrom`), e a cena cobra que a
		// imagem chegue ate o preso em poucos segundos.
		const string nome = "Foto: o Majin";
		var f = new Fighter
		{
			Name = nome, Race = "Majin", BP = bp,
			physoff = 1, physdef = 1, technique = 1, kioff = 1, kidef = 1,
			kiskill = 1, speed = 5, magiskill = 1, Idade = 25,
			maxstamina = 100, stamina = 100,
		};
		f.Statify();
		f.PowerLevel();   // o `expressedBP` sai daqui, e e dele que sai a metade que a imagem recebe
		f.Ki = f.MaxKi;
		f.Class = "Normal";

		int id = _nextId++;
		var c = new ServerPlayer
		{
			Id = id,
			Peer = null,
			Name = nome,
			Race = "Majin",
			Genero = "Male",
			Idade = 25,
			Zone = dono.Zone,
			Pos = dono.Pos + desloc,
			Conta = "bancada_foto_majin",
			Slot = 0,
			LastInputMs = NowMs(),
			Ficha = f,
			Livro = new Jandirus.Core.Skills.SkillBook(),
			Cerebro = null,
			Visual = AparenciaDeNpc(
				new MoldeDeNpc { Id = "bancada_foto_majin", Racas = ["Majin"], Classe = "Normal" },
				"Majin", "Male", (ulong)id),
		};
		c.Visual.CorPele = RosaDaFotoDoMajin;
		PorNoMundo(c);
		c.Ficha.Tick(agoraMs: NowMs());
		c.Livro.Dar(AbsorcaoMajin.Skill);

		// A APARENCIA PRECISA SER APRESENTADA -- ver a mesma linha (e a foto do tufo de cabelo no chao) no
		// `ForjarCorpoDeFoto`.
		TrocarAparencias(c);

		_fotoDoMajinNascidos.Add(id);
		return id;
	}

	/// <summary>
	/// PINTA O CORPO DE UM MAJIN QUE JA ESTA EM JOGO com o mesmo rosa -- o corpo LOCAL do ato do Majin, que
	/// a criacao automatica da bancada (`--raca Majin`) faz sem cor. So cena: a regra nao le cor de corpo.
	/// </summary>
	internal bool PintarNaFotoDoMajin(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || !CorpoDeMajin(pl)) return false;
		pl.Visual.CorPele = RosaDaFotoDoMajin;
		TrocarAparencias(pl);
		return true;
	}

	/// <summary>
	/// DERRUBA pelo nocaute de producao, por tempo de sobra pra cena. Devolve se ele caiu.
	///
	/// O NOCAUTE DA CENA TEM AUTOR: <paramref name="deOnde"/> e o rumo de quem bateu, e o corpo cai virado
	/// pra ele (`M.dir = get_dir(M,src)`, o `ApontarRumoDoGolpe` que todo golpe chama). Sem isto ele cai
	/// "pro sul" -- deitado de cabeca pra cima, que e o desenho certo do jogo e na foto parece um corpo
	/// DE PE. A primeira foto desta bancada saiu assim.
	/// </summary>
	internal bool DerrubarNaFotoDoMajin(int id, Vec2 deOnde)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate == null) return false;
		pl.ApontarRumoDoGolpe(deOnde);
		pl.Combate.Nocautear(600);
		MandarFicha(pl);
		return pl.Ficha.KO;
	}

	/// <summary>
	/// APERTA UMA HABILIDADE DESTE CORPO pelo canal de producao -- `absorver`, `expelir`, `expelir:N`. A
	/// recarga de 2 s do verb e vencida antes: a bancada nao espera o relogio.
	/// </summary>
	internal void HabilidadeNaFotoDoMajin(int id, string qual)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		_prontoAbsorver.Remove(id);
		UsarHabilidade(pl, qual);
	}

	/// <summary>QUEM SEGURA ESTE PRESO: o Majin, a imagem que o guarda e o numero do registro. Zeros = ninguem.</summary>
	internal (int Majin, int Imagem, int Numero) PrisaoNaFotoDoMajin(int idDoPreso)
	{
		foreach ((int majin, List<AbsorvidoPeloMajin> lista) in _dentroDoMajin)
			foreach (AbsorvidoPeloMajin r in lista)
				if (r.Quem == idDoPreso) return (majin, r.Guardiao, r.Numero);
		return (0, 0, 0);
	}

	/// <summary>O estado de um corpo da cena, na conta do SERVIDOR.</summary>
	internal (bool Existe, bool Caido, double Vida, Vec2 Pos, ZoneKey Zona, double Poder, bool EmCombate) CorpoNaFotoDoMajin(int id)
		=> _players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate is { } c
			? (true, pl.Ficha.KO || pl.Ficha.dead, pl.Ficha.HP, pl.Pos, pl.Zone, pl.Ficha.expressedBP, c.EmCombate > 0)
			: (false, false, 0, default, default, 0, false);

	/// <summary>Este corpo SELA em vez de consumir? A pergunta do verb, pela mesma funcao (`CorpoDeMajin`).</summary>
	internal bool EhMajinNaFoto(int id) => _players.TryGetValue(id, out ServerPlayer? pl) && CorpoDeMajin(pl);

	/// <summary>O que este Majin esta usando de emprestado: o poder somado e quantos verbs.</summary>
	internal (double Poder, int Verbos, int Dentro) GanhoNaFotoDoMajin(int idDoMajin)
		=> _players.TryGetValue(idDoMajin, out ServerPlayer? pl)
			? (pl.Ficha.majin_absorb_bp, pl.VerbosAbsorvidos?.Count ?? 0, AbsorvidosPor(idDoMajin).Count)
			: (0, 0, 0);

	/// <summary>
	/// TIRA DO MUNDO OS MAJINS DA BANCADA -- depois de eles cuspirem quem tinham dentro, pela porta de
	/// producao: um corpo removido com gente dentro deixaria o prisioneiro esperando o tique notar.
	/// </summary>
	internal void LimparAFotoDoMajin()
	{
		foreach (int id in _fotoDoMajinNascidos)
		{
			CuspirTodosDoMajin(id, "");
			_prontoAbsorver.Remove(id);
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) continue;
			_players.Remove(id);
			ZoneList(pl.Zone.Hash).Remove(pl);
		}
		_fotoDoMajinNascidos.Clear();
	}
}
