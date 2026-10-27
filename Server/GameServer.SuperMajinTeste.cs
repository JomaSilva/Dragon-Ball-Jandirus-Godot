using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Npc;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--supermajinteste` -- O SUPER MAJIN (dono, 2026-10-23): *"dar a forma super majin melhorada pros majins q nao sao corrupted"*,
/// com a maestria *"bem lenta"* que *"trava com base no bp atual do majin"*: 6x no nivel do SSJ1, 10x no do SSJ2, 100% = 16x perto do SSJ3.
///
/// Corpos forjados (sem `Peer`, como as outras de boot), e cada familia passa pela porta de PRODUCAO -- a tecla C (`Transformar`), o tique
/// da forma (`TickDaForma`, que e quem chama o funil da maestria) e a biografia do NPC (`SorteioDeNpc.AbrirFormas`):
///   A. A LINHA -- o Majin tem a escada, o Corrupted e o meio-Majin nao; o C vira Super Majin no nivel do SSJ1, e abaixo dele diz que esta
///      alem do alcance; o Corrupted continua ouvindo a frase de sempre. Contra-exemplo: `SuperMajin.CorruptedTambemDeTeste`.
///   B. A TRAVA -- pelo tique da forma, a barra para CRAVADA no teto da faixa do BP base (30% = 6x, 60% = 10x, 100% = 16x), o aviso sai uma
///      vez, e com o BP no nivel de cima ela volta a subir. Contra-exemplo: `SuperMajin.SemTravaDeTeste`.
///   C. A LENTIDAO -- o mesmo tique da 1/10 da maestria que o SSJ1 da. Contra-exemplo: `SuperMajin.NaTaxaComumDeTeste`.
///   D. NADA DESCE -- o BP de volta ao nivel de baixo nao tira maestria nem multiplicador (escolha minha, ver `SuperMajin.TetoDaMaestria`).
///   E. O NPC -- a biografia do Majin no nivel do SSJ1 abre o Super Majin com a maestria do molde; a do Corrupted, nao.
///
/// OS NUMEROS DO DONO SAO ESCRITOS AQUI POR EXTENSO (6x, 10x, 16x, 30%, 60%, 1/10) e nao lidos das constantes do Core: uma regua que lesse a
/// constante mutada aprovaria o mutante.
///
///     Godot --headless --server --port 10870 --supermajinteste
/// </summary>
public partial class GameServer
{
	private const int IdBaseDoSuperMajinDeTeste = 986_870;
	private static readonly ZoneKey ZonaDaBancadaDoSuperMajin = new(ZoneKey.KindPremade, "Earth");

	/// <summary>BP base de cada faixa, com folga das portas de fabrica (SSJ1 1,5 M; SSJ2 583,3 M; SSJ3 2 B) -- o Majin nao sorteia limiar.</summary>
	private const double BpAbaixoDoSsj1Sm = 1_000_000, BpNoSsj1Sm = 2_000_000, BpNoSsj2Sm = 600_000_000, BpNoSsj3Sm = 2_500_000_000;

	private int _smOk, _smFalhou, _smCorpos;
	private readonly List<ServerPlayer> _smForjados = [];

	private void AfirmarSm(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _smOk++; GD.Print($"[supermajin]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _smFalhou++; GD.PrintErr($"[supermajin]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDoSuperMajin()
	{
		_smOk = _smFalhou = 0;
		GD.Print("[supermajin] ================ O SUPER MAJIN (dono, 2026-10-23) ================");
		try
		{
			AfirmarSm("(montagem) a forma esta no catalogo, na linha dela, com a porta do SSJ1",
					  Catalogo.Def("super_majin") is { Linha: LinhaDeForma.SuperMajin, ChaveDoLimiar: "ssjat" } d && d.PortaBp == Catalogo.PortaSsj1);
			ALinhaSm();
			ATravaSm();
			ALentidaoSm();
			NadaDesceSm();
			ONpcSm();
		}
		catch (Exception e)
		{
			AfirmarSm("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			SuperMajin.CorruptedTambemDeTeste = SuperMajin.SemTravaDeTeste = SuperMajin.NaTaxaComumDeTeste = false;
			EscutaDeAvisos = null;
			foreach (ServerPlayer pl in _smForjados)
			{
				_players.Remove(pl.Id);
				ZoneList(pl.Zone.Hash).Remove(pl);
			}
			_smForjados.Clear();
			GD.Print($"[supermajin] ================ {_smOk} OK, {_smFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// OS CORPOS
	// =====================================================================
	/// <summary>Um corpo de bancada da raca e da classe pedidas, no BP base pedido, com o tanque cheio e na base.</summary>
	private ServerPlayer ForjarSm(string nome, string raca, string classe, double bp, string paiDe = "")
	{
		int n = _smCorpos++;
		var novo = new ServerPlayer
		{
			Id = IdBaseDoSuperMajinDeTeste + n,
			Peer = null,
			Name = "bancada: " + nome,
			Race = raca,
			Genero = "Male",
			Idade = 25,
			Zone = ZonaDaBancadaDoSuperMajin,
			Pos = new Vec2(400 + 40 * n, 400),
			Conta = $"bancada_supermajin_{n}",
			Slot = 0,
			Ficha = new Fighter { Race = raca, BP = bp, ParentRace = paiDe },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		novo.Class = novo.Ficha.Class = classe;
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.Ficha.Tick(agoraMs: NowMs());
		_smForjados.Add(novo);
		return novo;
	}

	/// <summary>O corpo NA FORMA, sem cena (a estreia congelaria o tique da forma) e com o tanque cheio.</summary>
	private void NaFormaSm(ServerPlayer pl, string forma, double maestria)
	{
		pl.Forma.Entrar(forma);
		pl.Forma.Maestria.Por(forma, maestria);
		pl.CenaSegundos = 0;
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		AplicarForma(pl);
	}

	/// <summary>UM TIQUE DA FORMA de <paramref name="segundos"/> -- o de producao -- e o que o servidor disse. O tanque enche antes (o dreno tem bancada propria).</summary>
	private List<string> TiqueSm(ServerPlayer pl, double segundos)
	{
		// (2026-10-26) E O TANQUE AGUENTA O TIQUE INTEIRO: o dreno agora e em PONTOS (o pedido 93), e um minuto do Super Majin custa mais que
		// o tanque de fabrica de uma ficha de bancada -- a forma cairia no meio do tique, e o assunto daqui nao e o dreno.
		AguentarOTiqueDeTeste(pl, segundos);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.CenaSegundos = 0;
		return Ouvir(() => TickDaForma(pl, segundos));
	}

	private static bool DisseSm(List<string> falas, string trecho) =>
		falas.Any(f => f.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	// =====================================================================
	// A) A LINHA
	// =====================================================================
	private void ALinhaSm()
	{
		GD.Print("[supermajin] -- A) a linha: o Majin tem, o Corrupted e o meio-Majin nao --");
		HashSet<LinhaDeForma> majin = Catalogo.LinhasAbertas(new PerfilDeFormas(Raca: "Majin", Classe: "Majin"));
		AfirmarSm("A1. o Majin (classe Majin) tem a linha do Super Majin -- e nao a escada Saiyajin",
				  majin.Contains(LinhaDeForma.SuperMajin) && !majin.Contains(LinhaDeForma.Saiyajin), string.Join(", ", majin));
		AfirmarSm("A2. o Majin sem classe (save de antes das classes) tambem",
				  Catalogo.LinhasAbertas(new PerfilDeFormas(Raca: "Majin")).Contains(LinhaDeForma.SuperMajin));

		ServerPlayer meio = ForjarSm("o meio-Majin", "Human", "", BpNoSsj1Sm, paiDe: "Majin");
		AfirmarSm("A4. o meio-Majin (Humano filho de Majin) NAO tem a linha -- o dono disse \"majins\"",
				  !Catalogo.LinhasAbertas(Perfil(meio)).Contains(LinhaDeForma.SuperMajin), string.Join(", ", Catalogo.LinhasAbertas(Perfil(meio))));

		// A5: pela tecla C, no nivel do SSJ1
		ServerPlayer comum = ForjarSm("o Majin", "Majin", "Majin", BpNoSsj1Sm);
		Transformar(comum, subir: true);
		AfirmarSm("A5. **O C VIRA SUPER MAJIN** no nivel do SSJ1 (2 M de BP base), a 2x com a maestria zerada",
				  comum.Forma.Atual == "super_majin" && Math.Abs(comum.Ficha.ssjBuff - 2) < 1e-9, $"forma {comum.Forma.Atual}, x{comum.Ficha.ssjBuff:0.##}");

		// A7: abaixo da porta
		ServerPlayer fraco = ForjarSm("o Majin fraco", "Majin", "Majin", BpAbaixoDoSsj1Sm);
		List<string> falas = Ouvir(() => Transformar(fraco, subir: true));
		AfirmarSm("A7. abaixo do nivel do SSJ1 (1 M) o C nao vira -- e diz que o Super Majin esta alem do alcance, sem numero",
				  fraco.Forma.NaBase && DisseSm(falas, "Super Majin ainda esta alem do seu alcance"), string.Join(" | ", falas));

		// A3/A6: o Corrupted, pela mesma regua com e sem o defeito
		(bool temALinha, bool naBase, bool fraseDeSempre, string detalhe) Corrupted()
		{
			ServerPlayer c = ForjarSm("o Corrupted", "Majin", SagaDoMajin.Classe, BpNoSsj1Sm * 2);
			List<string> f = Ouvir(() => Transformar(c, subir: true));
			return (Catalogo.LinhasAbertas(Perfil(c)).Contains(LinhaDeForma.SuperMajin), c.Forma.NaBase,
					DisseSm(f, "sua raca nao tem essa escada"), $"forma {c.Forma.Atual} | {string.Join(" | ", f)}");
		}
		var r = Corrupted();
		AfirmarSm("A3. o Corrupted Majin NAO tem a linha (a escada dele e a saga)", !r.temALinha, r.detalhe);
		AfirmarSm("A6. ...e aperta C e continua na base, ouvindo a frase de sempre (\"sua raca nao tem essa escada\")",
				  r.naBase && r.fraseDeSempre, r.detalhe);

		SuperMajin.CorruptedTambemDeTeste = true;
		try
		{
			var errado = Corrupted();
			AfirmarSm("(defeito injetado: o Corrupted tambem ganha a linha) ele vira Super Majin pelo C -- as reguas A3 e A6 REPROVAM",
					  errado.temALinha && !errado.naBase, errado.detalhe);
		}
		finally { SuperMajin.CorruptedTambemDeTeste = false; }
	}

	// =====================================================================
	// B) A TRAVA PELO BP BASE
	// =====================================================================
	private void ATravaSm()
	{
		GD.Print("[supermajin] -- B) a trava: a barra para no teto da faixa do BP base, pelo tique da forma --");
		const string id = "super_majin";

		// a regua: no nivel do SSJ1, de 29,98% um tique de 60 s (que passaria do teto) para a barra CRAVADA em 30% = 6x
		(double maestria, double mult, List<string> falas) DoSsj1()
		{
			ServerPlayer m = ForjarSm("o Majin no SSJ1", "Majin", "Majin", BpNoSsj1Sm);
			NaFormaSm(m, id, 29.98);
			List<string> f = TiqueSm(m, 60);
			return (m.Forma.Maestria.De(id), m.Ficha.ssjBuff, f);
		}
		var s1 = DoSsj1();
		AfirmarSm("B1. **NO NIVEL DO SSJ1 A MAESTRIA TRAVA EM 30% (6x)**: de 29,98%, um tique de 60 s para a barra cravada em 30%",
				  Math.Abs(s1.maestria - 30) < 1e-12 && Math.Abs(s1.mult - 6) < 1e-9, $"{s1.maestria:0.############}%, x{s1.mult:0.##}");
		AfirmarSm("B2. ...e o jogador ouve uma vez que a maestria parou no teto do poder",
				  DisseSm(s1.falas, "Super Majin: a maestria parou em 30%, no teto que o seu poder segura agora"), string.Join(" | ", s1.falas));

		SuperMajin.SemTravaDeTeste = true;
		try
		{
			var solto = DoSsj1();
			AfirmarSm("(defeito injetado: a maestria sem o teto do poder) o mesmo tique passa dos 30% -- a regua B1 REPROVA",
					  solto.maestria > 30 + 1e-6, $"{solto.maestria:0.####}%");
		}
		finally { SuperMajin.SemTravaDeTeste = false; }

		ServerPlayer pl = ForjarSm("o Majin que cresce", "Majin", "Majin", BpNoSsj1Sm);
		NaFormaSm(pl, id, 30);
		List<string> parada = TiqueSm(pl, 60);
		AfirmarSm("B2b. encostada no teto, o tique seguinte nao sobe nem repete o aviso",
				  Math.Abs(pl.Forma.Maestria.De(id) - 30) < 1e-12 && !DisseSm(parada, "parou em"), $"{pl.Forma.Maestria.De(id)}% | {string.Join(" | ", parada)}");

		pl.Ficha.BP = BpNoSsj2Sm;
		TiqueSm(pl, 60);
		AfirmarSm("B3. com o BP no nivel do SSJ2 (600 M) a barra VOLTA A SUBIR dos 30%", pl.Forma.Maestria.De(id) > 30, $"{pl.Forma.Maestria.De(id):0.#####}%");
		pl.Forma.Maestria.Por(id, 59.98);
		List<string> f2 = TiqueSm(pl, 60);
		AfirmarSm("B3b. **NO NIVEL DO SSJ2 ELA TRAVA EM 60% (10x)**, com o aviso",
				  Math.Abs(pl.Forma.Maestria.De(id) - 60) < 1e-12 && Math.Abs(pl.Ficha.ssjBuff - 10) < 1e-9 && DisseSm(f2, "parou em 60%"),
				  $"{pl.Forma.Maestria.De(id):0.############}%, x{pl.Ficha.ssjBuff:0.##} | {string.Join(" | ", f2)}");

		pl.Ficha.BP = BpNoSsj3Sm;
		pl.Forma.Maestria.Por(id, 99.98);
		List<string> f3 = TiqueSm(pl, 60);
		AfirmarSm("B4. **NO NIVEL DO SSJ3 (2,5 B) ELA CHEGA A 100% = 16x**, com o aviso de sempre (forma DOMINADA)",
				  pl.Forma.Maestria.De(id) >= 100 && Math.Abs(pl.Ficha.ssjBuff - 16) < 1e-9 && DisseSm(f3, "forma DOMINADA"),
				  $"{pl.Forma.Maestria.De(id):0.####}%, x{pl.Ficha.ssjBuff:0.##} | {string.Join(" | ", f3)}");
	}

	// =====================================================================
	// C) A LENTIDAO
	// =====================================================================
	private void ALentidaoSm()
	{
		GD.Print("[supermajin] -- C) a lentidao: 1/10 da maestria que o SSJ1 da no mesmo tique --");

		double Razao()
		{
			ServerPlayer saiyajin = ForjarSm("o Saiyajin de controle", "Saiyan", "Normal", BpNoSsj3Sm);
			NaFormaSm(saiyajin, "ssj1", 10);
			TiqueSm(saiyajin, 60);
			double doSsj1 = saiyajin.Forma.Maestria.De("ssj1") - 10;

			// no nivel do SSJ3 o teto e 100: aqui so a lentidao corta
			ServerPlayer majin = ForjarSm("o Majin lento", "Majin", "Majin", BpNoSsj3Sm);
			NaFormaSm(majin, "super_majin", 10);
			TiqueSm(majin, 60);
			double doSm = majin.Forma.Maestria.De("super_majin") - 10;
			AfirmarSm("(montagem) o SSJ1 ganhou maestria no tique de 60 s", doSsj1 > 0, $"{doSsj1}");
			return doSsj1 > 0 ? doSm / doSsj1 : double.NaN;
		}

		double r = Razao();
		AfirmarSm("C1. **A MAESTRIA DO SUPER MAJIN SOBE 1/10 DA DO SSJ1** no mesmo tique (30 h de forma de 0 a 100%, contra as 3 h do SSJ1)",
				  Math.Abs(r - 0.1) < 1e-9, $"razao {r:0.######}");

		SuperMajin.NaTaxaComumDeTeste = true;
		try
		{
			double comum = Razao();
			AfirmarSm("(defeito injetado: a maestria no ritmo de uma forma comum) a razao vira 1 -- a regua C1 REPROVA", Math.Abs(comum - 1) < 1e-9,
					  $"razao {comum:0.######}");
		}
		finally { SuperMajin.NaTaxaComumDeTeste = false; }
	}

	// =====================================================================
	// D) NADA DESCE
	// =====================================================================
	private void NadaDesceSm()
	{
		GD.Print("[supermajin] -- D) e se o BP base descer: nada desce --");
		ServerPlayer pl = ForjarSm("o Majin que perdeu poder", "Majin", "Majin", BpNoSsj2Sm);
		NaFormaSm(pl, "super_majin", 60);
		pl.Ficha.BP = BpNoSsj1Sm;
		List<string> falas = TiqueSm(pl, 60);
		AfirmarSm("D1. com 60% (10x) e o BP de volta ao nivel do SSJ1, a barra FICA em 60% e o multiplicador em 10x -- e nao sobe",
				  Math.Abs(pl.Forma.Maestria.De("super_majin") - 60) < 1e-12 && Math.Abs(pl.Ficha.ssjBuff - 10) < 1e-9 && !DisseSm(falas, "parou em"),
				  $"{pl.Forma.Maestria.De("super_majin")}%, x{pl.Ficha.ssjBuff:0.##} | {string.Join(" | ", falas)}");
	}

	// =====================================================================
	// E) O NPC
	// =====================================================================
	private void ONpcSm()
	{
		GD.Print("[supermajin] -- E) o NPC: a biografia abre o Super Majin do Majin, e nao a do Corrupted --");
		var molde = new MoldeDeNpc { Id = "bancada_supermajin", Maestria = 25 };

		var est = new EstadoDeForma();
		SorteioDeNpc.AbrirFormas(est, molde, BpNoSsj1Sm, new PerfilDeFormas(Raca: "Majin", Classe: "Majin"));
		AfirmarSm("E1. o NPC Majin no nivel do SSJ1 sai da biografia com o Super Majin despertado, a maestria do molde (25% = 4x) e na base",
				  est.Despertou("super_majin") && Math.Abs(est.Maestria.De("super_majin") - 25) < 1e-9 && est.NaBase,
				  $"despertou {est.Despertou("super_majin")}, {est.Maestria.De("super_majin")}%, em {est.Atual}");

		var corrompido = new EstadoDeForma();
		SorteioDeNpc.AbrirFormas(corrompido, molde, BpNoSsj1Sm * 2, new PerfilDeFormas(Raca: "Majin", Classe: SagaDoMajin.Classe));
		AfirmarSm("E2. ...e o NPC Corrupted Majin, nao", !corrompido.Despertou("super_majin"));
	}
}
