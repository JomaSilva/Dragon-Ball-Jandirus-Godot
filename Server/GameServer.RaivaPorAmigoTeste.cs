using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Social;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DOS DOIS DEGRAUS DA RAIVA POR AMIGO (`--raivateste`, no 1o login) -- o pedido do dono de 2026-10-11:
/// amigo NOCAUTEADO = raiva comum e so; amigo MORTO ou ABSORVIDO = o degrau mais alto, com a cinematica, o tema e
/// (pra quem tem controle de ki) o Ki saltando pro teto de carga.
///
/// ============================ O QUE SO DAQUI SE VE ============================
/// As bancadas que ja existiam medem pedacos: a `--formasteste` chama o gancho A MAO (`ADuplaRaivaAoVivo`), a
/// `--conhecidosteste` mede a corrente convivio -> declaracao -> DEGRAU -> tecla C. Nenhuma das duas le o que cada
/// degrau VALE (o numero que vira BP), nem a ordem em que as coisas acontecem numa luta de verdade (o amigo CAI,
/// e so depois morre), nem o Ki. Aqui tudo entra pelo funil de producao (`AoPerderALuta`, `LutoPelaAbsorcao`) e
/// o enlutado e o HOST: so um corpo com dono na tela recebe cinematica (`Murder.dm:138`, `if(!client) return`).
///
/// E AS QUATRO DECISOES DO DONO DO MESMO DIA, depois de ver a primeira rodada (secoes 4, 7 e 8):
///   * a fusao Namekuseijin NAO enfurece ninguem -- medida com a fusao de verdade, na frente de quem declarou o
///     absorvido amigo;
///   * com a forma JA despertada e nada novo a estrear, a cinematica que toca e a da RAIVA;
///   * com os requisitos de uma forma NOVA que fica um degrau acima, o corpo SOBE sozinho pelas que ja sao dele
///     (sem cena) e estreia a nova com a cinematica dela;
///   * MEDITAR acalma os dois degraus na hora -- e so a meditacao que o servidor aceitou.
///
/// OS OUTROS CORPOS SAO FORJADOS (pessoas com conta, como na bancada do convivio), postos em volta do host e
/// tirados no `finally`; o host volta como entrou.
///
///   --headless --host --rede 8320 --raivateste --raca Saiyan --conta bancada_raiva_ag --nome Enlutado
/// ==============================================================================
/// </summary>
public partial class GameServer
{
	private bool? _raivaDeTeste;

	/// <summary>Faixa de ids desta bancada (longe do `_nextId` e das outras faixas de bancada).</summary>
	private const int IdBaseDaRaivaDeTeste = 93_200;

	/// <summary>A unica linha desta bancada no `Entrar`: a flag e lida aqui, e ela roda uma vez so.</summary>
	private void RodarBancadaDaRaivaSePedida(ServerPlayer pl)
	{
		_raivaDeTeste ??= Array.IndexOf(OS.GetCmdlineArgs(), "--raivateste") >= 0;
		if (_raivaDeTeste != true) return;
		_raivaDeTeste = false;
		RodarBancadaDaRaivaPorAmigo(pl);
	}

	/// <summary>
	/// Uma pessoa forjada (com conta: e o que da assinatura e a poe na vida social) a `tiles` do host.
	/// <paramref name="cara"/> e so pra bancada de JANELA: sem aparencia o corpo existe e nao se desenha.
	/// </summary>
	private ServerPlayer ForjarPessoaDaRaiva(ServerPlayer host, int i, string nome, float tiles, string raca = "Human",
											 Jandirus.Core.Appearance.Appearance? cara = null)
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDaRaivaDeTeste + i,
			Peer = null,
			Name = nome,
			Race = raca,
			Genero = "Male",
			Idade = 25,
			Zone = host.Zone,
			Pos = host.Pos + new Vec2(tiles * ZoneCollision.TileSize, 0),
			Conta = $"bancada_raiva_{i}",
			Slot = 0,
			Ficha = new Fighter { Race = raca, BP = 5_000 },
		};
		if (cara != null) novo.Visual = cara;
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		// A CARA VAI NUM PACOTE PROPRIO, uma vez (nao no snapshot): sem ele o cliente desenha o rabo e mais nada.
		if (cara != null) TrocarAparencias(novo);
		return novo;
	}

	private void RodarBancadaDaRaivaPorAmigo(ServerPlayer pl)
	{
		GD.Print("\n[raiva] ================ A RAIVA POR AMIGO EM DOIS DEGRAUS (dono, 2026-10-11) ================");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[raiva]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
			else { falhou++; GD.PrintErr($"[raiva]   FALHA {nome}   [{detalhe}]"); }
		}

		// ---- a foto do host: ele volta como entrou ----
		Fighter f = pl.Ficha;
		double kiAntes = f.Ki, bpAntes = f.BP, capAntes = f.powerupcap;
		string classeAntes = f.Class, formaAntes = pl.Forma.Atual;
		var liberadasAntes = new HashSet<int>(pl.Forma.Liberadas);
		var estreiasAntes = new HashSet<int>(pl.Forma.EstreiaVista);
		double cenaSegundosAntes = pl.CenaSegundos;
		bool caidoAntes = f.KO;
		double unlockedAntes = f.MeditateGivesKiRegen, controleAntes = f.canPower;
		bool travaAntes = f.fd_ki_locked, overAntes = f.overcharge;
		long extremaAntes = pl.FuriaExtremaAte, comumAntes = pl.RaivaComumAte, cenaAntes = pl.FuriaCenaAte;
		bool treinavaAntes = f.train, meditavaAntes = f.med, voavaAntes = pl.Voando;
		double maestriaDoSsj1Antes = pl.Forma.Maestria.De("ssj1");
		// (os marcos de ganho: a secao 7 confere o do Super Saiyajin 2 pelo funil de producao, que escreve na ficha)
		var marcosAntes = new HashSet<string>(f.bp_milestones_done, StringComparer.Ordinal);
		double ganhoDosMarcosAntes = f.bp_milestone_mult;

		// O HOST AINDA NAO ESTA NA LISTA DA ZONA (bancada de 1o login roda antes da linha que o poe no mundo), e o
		// luto varre a zona: sem ele la, ninguem "ve" nada e a bancada daria verde por ausencia.
		List<ServerPlayer> daZona = ZoneList(pl.Zone.Hash);
		bool entrouPelaBancada = !daZona.Contains(pl);
		if (entrouPelaBancada) daZona.Add(pl);

		var forjados = new List<ServerPlayer>();
		List<int>? furiasDeFora = EscutaDeFurias;
		List<(int Quem, string De, string Para, DegrauDeCena Degrau)>? anunciosDeFora = EscutaDeAnuncios;
		var furias = new List<int>();
		var vistos = new List<(int Enfurecido, int[] Para)>();
		EscutaDeFurias = furias;
		EscutaDeQuemViuAFuria = vistos;

		// ---- os gestos da bancada ----
		int Cenas() => furias.Count(id => id == pl.Id);
		NivelDeRaiva Nivel() => NivelDaRaivaDe(pl);
		double Folga() => f.MaxAnger - 100;
		double Razao() => f.Ki / Math.Max(f.MaxKi, 1e-9);
		void KiEm(double razao) { f.Ki = f.MaxKi * razao; f.overcharge = false; }
		void EmPaz()
		{
			pl.FuriaExtremaAte = 0;
			pl.RaivaComumAte = 0;
			pl.FuriaCenaAte = 0;
			ProjetarRaiva(pl);
			furias.Clear();
			vistos.Clear();
		}
		// O QUE A ZONA FICOU SABENDO das formas do host durante um gesto: o funil `AnunciarForma`, na ordem (de onde,
		// pra onde, e com que degrau de cinematica). E por ele que a tela de todo mundo veste o corpo.
		List<(string De, string Para, DegrauDeCena Degrau)> AnunciosDe(Action gesto)
		{
			var ouvidos = new List<(int Quem, string De, string Para, DegrauDeCena Degrau)>();
			EscutaDeAnuncios = ouvidos;
			try { gesto(); }
			finally { EscutaDeAnuncios = anunciosDeFora; }
			return [.. ouvidos.Where(a => a.Quem == pl.Id).Select(a => (a.De, a.Para, a.Degrau))];
		}
		static string Lista(List<(string De, string Para, DegrauDeCena Degrau)> anuncios) =>
			anuncios.Count == 0 ? "nenhum anuncio" : string.Join(" | ", anuncios.Select(a => $"{a.De}->{a.Para}/{a.Degrau}"));

		try
		{
			// ============================ 0. A MONTAGEM ============================
			GD.Print("[raiva] -- 0) a montagem --");
			ServerPlayer amigo = ForjarPessoaDaRaiva(pl, 1, "bancada: o amigo", 2); forjados.Add(amigo);
			ServerPlayer algoz = ForjarPessoaDaRaiva(pl, 2, "bancada: o algoz", 3); forjados.Add(algoz);
			ServerPlayer estranho = ForjarPessoaDaRaiva(pl, 3, "bancada: o estranho", -2); forjados.Add(estranho);
			ServerPlayer vizinho = ForjarPessoaDaRaiva(pl, 4, "bancada: o vizinho", -10); forjados.Add(vizinho);
			ServerPlayer distante = ForjarPessoaDaRaiva(pl, 5, "bancada: o distante", 40); forjados.Add(distante);
			ServerPlayer majin = ForjarPessoaDaRaiva(pl, 6, "bancada: o majin", 4, "Majin"); forjados.Add(majin);
			ServerPlayer namek = ForjarPessoaDaRaiva(pl, 7, "bancada: o namekuseijin", 5, "Namekian"); forjados.Add(namek);

			DeclararDeTeste(pl, amigo, Relacao.Bom);
			Checa("(montagem) o host e pessoa, DECLAROU o amigo -- e do estranho nao disse nada",
				  EhPessoa(pl) && pl.Social.EhAmigo(amigo.Assinatura) && !pl.Social.Conhece(estranho.Assinatura),
				  $"assinatura '{pl.Assinatura}'");
			Checa("(montagem) pra vitima o algoz e inimigo (ela nao o declarou amigo)",
				  amigo.Social.AlgozEhInimigo(algoz.Assinatura) && estranho.Social.AlgozEhInimigo(algoz.Assinatura));

			// O CONTROLE DE KI E O TETO DE FABRICA (140%). `powerupcap` e reescrito pelo `Statify`, e nada daqui
			// ate o `finally` roda um: os tetos desta bancada sao postos a mao de proposito, pra medir a REGRA do
			// salto com numeros conhecidos.
			// E A CLASSE E A DO TRONCO COMUM: a classe do personagem e SORTEADA na criacao, e um Legendary sorteado
			// teria outra escada (e outra porta de raiva) -- a bancada reprovaria por sorteio.
			f.Class = "Normal";
			f.MeditateGivesKiRegen = 1;
			f.canPower = 1;
			f.fd_ki_locked = false;
			f.powerupcap = 1.4;
			f.BP = 1;   // sem poder pra forma nenhuma: a furia nao vira transformacao, e a cena e dela
			EmPaz();
			Checa("(montagem) o host tem folga de raiva pra medir (MaxAnger acima de 100)", Folga() > 1, $"MaxAnger {f.MaxAnger:0.##}");
			Checa("(montagem) o host tem o controle de ki exigido, e o teto de carga dele e 140% do tanque",
				  RaivaEmDegraus.TemOControleDeKiExigido(f) && Math.Abs(CargaDeKi.TetoDeCarga(f) / f.MaxKi - 1.4) < 1e-6,
				  $"teto {CargaDeKi.TetoDeCarga(f) / Math.Max(f.MaxKi, 1e-9):0.##}x");
			double comum = 100 + Folga() / RaivaEmDegraus.DivisorDaRaivaComum;

			// ============================ 1. AMIGO NOCAUTEADO: RAIVA COMUM, E SO ============================
			GD.Print("[raiva] -- 1) amigo NOCAUTEADO: raiva comum, e so --");
			KiEm(0.05);
			List<string> falas = Ouvir(() => AoPerderALuta(amigo, algoz, morreu: false));
			Checa("amigo NOCAUTEADO por um inimigo: o degrau e o COMUM, e nao o mais alto",
				  Nivel() == NivelDeRaiva.Comum, Nivel().ToString());
			Checa("**...e a raiva dele vale o 'Angry' do DM (100 + folga/1,30), e NAO o topo (MaxAnger)**",
				  Math.Abs(f.Anger - comum) < 0.05 && f.Anger < f.MaxAnger - 0.5,
				  $"Anger {f.Anger:0.##}; comum {comum:0.##}; topo {f.MaxAnger:0.##}");
			RepercutirPoder(pl);
			Checa("...o buff de BP existe e e MENOR que o da furia extrema",
				  f.angerBuff > 1.0001 && f.angerBuff < Math.Min(f.MaxAnger / 100, 2) - 0.001,
				  $"angerBuff {f.angerBuff:0.###}x contra {Math.Min(f.MaxAnger / 100, 2):0.###}x no topo");
			Checa("...SEM cinematica (e portanto sem o tema)", Cenas() == 0, $"{Cenas()} cena(s)");
			Checa("**...e SEM salto de Ki, mesmo com o controle na mao** (raiva comum nao faz isso)",
				  Math.Abs(Razao() - 0.05) < 0.001, $"Ki em {Razao() * 100:0.#}%");
			Checa("...ele le que o amigo CAIU, e quem esta perto le que ele ficou COM RAIVA (e nao extremamente furioso)",
				  Disse(falas, "cai na sua frente") && Disse(falas, "ficou com raiva") && !Disse(falas, "EXTREMAMENTE"),
				  Ultimos(falas));

			// O PRECO DA RAIVA COMUM (dono, 2026-10-11; o `if("Angry")` do `supersaiyan.dm:112`): com ela acesa o Super
			// Saiyajin pede a porta VEZES o agio -- o poder que basta a furia extrema nao basta aqui. (Ate ali esta
			// checagem dizia "a raiva comum NAO abre o Super Saiyajin", com poder nenhum. A secao 9 mede o jogo inteiro.)
			double portaDoSsj1 = PortaDeTeste(pl, "ssj1"), precoComumDoSsj1 = portaDoSsj1 * Catalogo.AgioComumDoSaiyajin;
			f.BP = (portaDoSsj1 + precoComumDoSsj1) / 2;
			RecusaForma comumEntreOsPrecos = pl.Forma.Avaliar("ssj1", f.BP, 1, false, Perfil(pl));
			f.BP = precoComumDoSsj1;
			RecusaForma comumNoPreco = pl.Forma.Avaliar("ssj1", f.BP, 1, false, Perfil(pl));
			Checa("**...e a raiva comum ABRE o Super Saiyajin, mas so com o BP MAIOR**: entre a porta e 1,2x ela e recusada (falta poder pra ESTA raiva), em 1,2x abre",
				  comumEntreOsPrecos == RecusaForma.SemPoderPraRaivaComum && comumNoPreco == RecusaForma.Pode,
				  $"entre os precos {comumEntreOsPrecos}; no preco {comumNoPreco}");
			f.BP = 1;

			// a raiva comum goteja como a outra, e nao encosta na calma antes do prazo
			pl.RaivaComumAte = NowMs() + (long)(0.01 * SegundosDeRaiva * 1000);
			ProjetarRaiva(pl);
			double noFim = comum - Folga() * (0.99 * SegundosDeRaiva) / 1600.0;
			Checa("...ela goteja (Stats.dm:443) e no fim dos 120 s ainda e raiva: quem a derruba e o PRAZO",
				  Math.Abs(f.Anger - noFim) < 0.05 && f.Anger > 100 + Folga() * 0.5, $"Anger {f.Anger:0.##}, esperado {noFim:0.##}");
			pl.RaivaComumAte = NowMs() - 1;
			ProjetarRaiva(pl);
			Checa("...e vencido o prazo ela volta a 100 sozinha", Math.Abs(f.Anger - 100) < 0.001, $"{f.Anger:0.##}");

			// ---- contra-exemplo: o nocaute valendo o topo ----
			EmPaz();
			RaivaEmDegraus.NocauteNoTopoDeTeste = true;
			try
			{
				AoPerderALuta(amigo, algoz, morreu: false);
				Checa("(defeito injetado: o nocaute volta a valer o topo) a checagem do numero REPROVA",
					  !(Math.Abs(f.Anger - comum) < 0.05 && f.Anger < f.MaxAnger - 0.5), $"Anger {f.Anger:0.##}");
			}
			finally { RaivaEmDegraus.NocauteNoTopoDeTeste = false; }

			// ============================ 2. AMIGO MORTO: O DEGRAU MAIS ALTO ============================
			GD.Print("[raiva] -- 2) amigo MORTO: furia extrema, cinematica e o Ki no teto --");
			EmPaz();
			KiEm(0.05);
			falas = Ouvir(() => AoPerderALuta(amigo, algoz, morreu: true));
			Checa("amigo MORTO por um inimigo: o degrau e o MAIS ALTO (furia extrema)",
				  Nivel() == NivelDeRaiva.Extrema, Nivel().ToString());
			Checa("...e a raiva dele vale o topo (MaxAnger, Murder.dm:112)",
				  Math.Abs(f.Anger - f.MaxAnger) < 0.05, $"Anger {f.Anger:0.##} de {f.MaxAnger:0.##}");
			Checa("...a CINEMATICA dispara, uma vez, e e a dele", Cenas() == 1, $"{Cenas()} cena(s)");
			Checa("**...e o Ki SALTA de 5% pro teto de carga dele (140% do tanque), na hora**",
				  Math.Abs(Razao() - 1.4) < 0.001, $"Ki em {Razao() * 100:0.#}%");
			Checa("...com o excesso protegido (o Ki que a furia da nao machuca o enfurecido)", f.overcharge);
			Checa("...ele le que o amigo SE FOI e que a energia explodiu; quem esta perto le EXTREMAMENTE furioso",
				  Disse(falas, "se foi") && Disse(falas, "Ki salta") && Disse(falas, "EXTREMAMENTE"), Ultimos(falas));

			int[] para = vistos.Where(v => v.Enfurecido == pl.Id).SelectMany(v => v.Para).ToArray();
			Checa("**a cena e o tema vao pro enfurecido e pra quem esta PERTO dele (a 10 tiles), e nao pra quem esta a 40**",
				  para.Contains(pl.Id) && para.Contains(vizinho.Id) && !para.Contains(distante.Id),
				  para.Length == 0 ? "nenhum destinatario anotado" : string.Join(",", para));

			// O QUE OS VIZINHOS VEEM: o estado que ja se replica (a aura de excesso, no snapshot)
			TickDaCarga(pl, 1 / 30.0);
			Checa("...e no tique seguinte o snapshot ja conta aos vizinhos que ele esta SOBRECARREGADO (a aura de excesso)",
				  EstadoDe(pl, NowMs()).Sobrecarregado && Razao() > 1.39, $"Ki em {Razao() * 100:0.#}%");

			// ---- contra-exemplos do salto ----
			EmPaz();
			KiEm(0.05);
			RaivaEmDegraus.SemSaltoDeKiDeTeste = true;
			try
			{
				AoPerderALuta(amigo, algoz, morreu: true);
				Checa("(defeito injetado: a furia extrema nao mexe no Ki) a checagem do salto REPROVA",
					  !(Math.Abs(Razao() - 1.4) < 0.001), $"Ki em {Razao() * 100:0.#}%");
			}
			finally { RaivaEmDegraus.SemSaltoDeKiDeTeste = false; }

			EmPaz();
			KiEm(0.05);
			RaivaEmDegraus.SaltoNaRaivaComumDeTeste = true;
			try
			{
				AoPerderALuta(amigo, algoz, morreu: false);
				Checa("(defeito injetado: a raiva comum tambem salta) a checagem 'sem salto no nocaute' REPROVA",
					  !(Math.Abs(Razao() - 0.05) < 0.001), $"Ki em {Razao() * 100:0.#}%");
			}
			finally { RaivaEmDegraus.SaltoNaRaivaComumDeTeste = false; }

			EmPaz();
			KiEm(0.05);
			FuriaProPlanetaInteiroDeTeste = true;
			try
			{
				AoPerderALuta(amigo, algoz, morreu: true);
				int[] todos = vistos.Where(v => v.Enfurecido == pl.Id).SelectMany(v => v.Para).ToArray();
				Checa("(defeito injetado: a cena vai pra zona inteira) a checagem de quem recebe REPROVA",
					  todos.Contains(distante.Id), todos.Length == 0 ? "nenhum destinatario anotado" : string.Join(",", todos));
			}
			finally { FuriaProPlanetaInteiroDeTeste = false; }

			// ============================ 3. A LUTA DE VERDADE: ELE CAI, E DEPOIS MORRE ============================
			GD.Print("[raiva] -- 3) a luta de verdade: o amigo CAI, e so depois morre --");
			EmPaz();
			KiEm(0.30);
			AoPerderALuta(amigo, algoz, morreu: false);
			Checa("o amigo cai: raiva comum, sem cena, Ki onde estava",
				  Nivel() == NivelDeRaiva.Comum && Cenas() == 0 && Math.Abs(Razao() - 0.30) < 0.001,
				  $"{Nivel()}, {Cenas()} cena(s), Ki {Razao() * 100:0.#}%");
			AoPerderALuta(amigo, algoz, morreu: true);
			Checa("...e ele MORRE logo depois: a raiva SOBE de degrau e de numero",
				  Nivel() == NivelDeRaiva.Extrema && Math.Abs(f.Anger - f.MaxAnger) < 0.05, $"{Nivel()}, Anger {f.Anger:0.##}");
			Checa("**...e a cinematica TOCA: a morte e degrau novo, mesmo pra quem ja estava com raiva do nocaute**",
				  Cenas() == 1, $"{Cenas()} cena(s)");
			Checa("...e o Ki salta de 30% pro teto", Math.Abs(Razao() - 1.4) < 0.001, $"Ki em {Razao() * 100:0.#}%");

			// PROLONGAMENTO: outro amigo morre com a furia extrema ja acesa (a recarga zerada, pra isolar a pergunta)
			pl.FuriaCenaAte = 0;
			furias.Clear();
			KiEm(0.50);
			AoPerderALuta(amigo, algoz, morreu: true);
			Checa("outra morte NO MEIO da furia extrema: a cena nao repete (ja era o degrau dela)",
				  Cenas() == 0, $"{Cenas()} cena(s)");
			Checa("...mas o Ki volta ao teto (o salto e de todo evento do degrau extremo, como o numero da raiva no DM, Murder.dm:112)",
				  Math.Abs(Razao() - 1.4) < 0.001, $"Ki em {Razao() * 100:0.#}%");

			// A RECARGA DE 60 s: furia nova (janelas fechadas) dentro da recarga da cena anterior
			EmPaz();
			AoPerderALuta(amigo, algoz, morreu: true);
			pl.FuriaExtremaAte = 0;
			pl.RaivaComumAte = 0;
			ProjetarRaiva(pl);
			furias.Clear();
			AoPerderALuta(amigo, algoz, morreu: true);
			Checa("uma furia NOVA dentro dos 60 s da cena anterior nao repete a cena (rageCinematicCD, Murder.dm:139)",
				  Cenas() == 0 && Nivel() == NivelDeRaiva.Extrema, $"{Cenas()} cena(s)");

			// ---- contra-exemplo: o `wasRaging` literal do DM ----
			EmPaz();
			KiEm(0.30);
			CenaSoSaiDaCalmaDeTeste = true;
			try
			{
				AoPerderALuta(amigo, algoz, morreu: false);
				AoPerderALuta(amigo, algoz, morreu: true);
				Checa("(defeito injetado: a cena so sai da calma) quem viu o amigo cair e morrer fica SEM cinematica -- a checagem REPROVA",
					  Cenas() == 0, $"{Cenas()} cena(s)");
			}
			finally { CenaSoSaiDaCalmaDeTeste = false; }

			// ============================ 4. AMIGO ABSORVIDO POR UM INIMIGO: IDEM -- E A FUSAO NAMEKUSEIJIN, NAO ============================
			GD.Print("[raiva] -- 4) amigo ABSORVIDO por um Majin: furia extrema; FUNDIDO a um Namekuseijin: nada --");
			EmPaz();
			KiEm(0.05);
			LutoPelaAbsorcao(amigo, majin);
			Checa("amigo ABSORVIDO por um Majin: furia extrema, numero no topo, cinematica e o Ki no teto",
				  Nivel() == NivelDeRaiva.Extrema && Math.Abs(f.Anger - f.MaxAnger) < 0.05 && Cenas() == 1 && Math.Abs(Razao() - 1.4) < 0.001,
				  $"{Nivel()}, Anger {f.Anger:0.##}, {Cenas()} cena(s), Ki {Razao() * 100:0.#}%");
			Checa("...e quem ABSORVEU nao se enfurece com o proprio ato", NivelDaRaivaDe(majin) == NivelDeRaiva.Nenhuma);

			// A FUSAO NAMEKUSEIJIN NAO ENFURECE NINGUEM (dono, 2026-10-11: *"fusao namek n deveria dar o trigger na
			// raiva"*). Medida com a fusao DE VERDADE -- o `AbsorverNamekuseijin` de producao, que apaga o personagem do
			// absorvido --, e por isso dentro do palco de apagamentos: a conta do amigo e emprestada, e nada do que se
			// grava aqui encosta na pasta de saves de ninguem.
			using (PalcoDeApagamentos palcoDaFusao = PalcoDeApagamentosDeBancada())
			{
				ServerPlayer nail = ForjarPessoaDaRaiva(pl, 8, "bancada: o amigo namekuseijin", 2.5f, "Namekian"); forjados.Add(nail);
				// (o livro vazio: a absorcao herda skills e reaplica os poderes de quem absorveu, e os dois leem o livro)
				nail.Livro = new Jandirus.Core.Skills.SkillBook();
				namek.Livro = new Jandirus.Core.Skills.SkillBook();
				DeclararDeTeste(pl, nail, Relacao.Bom);
				AccountSave contaDoNail = palcoDaFusao.Emprestar(nail.Conta, nail.Slot, AccountStore.DeJogador(nail, NowMs()));

				// O CONTROLE VEM PRIMEIRO: este amigo, deste lugar, ENFURECE quando e um inimigo que o leva. Sem isto o
				// "nada acende" de baixo ficaria verde por o host simplesmente nao estar vendo (ou nao o ter por amigo).
				EmPaz();
				KiEm(0.05);
				LutoPelaAbsorcao(nail, majin);
				Checa("(controle) o amigo Namekuseijin, de onde esta, ENFURECE o host quando e um MAJIN que o absorve",
					  pl.Social.EhAmigo(nail.Assinatura) && Nivel() == NivelDeRaiva.Extrema && Cenas() == 1,
					  $"{Nivel()}, {Cenas()} cena(s)");

				EmPaz();
				KiEm(0.05);
				double bpDoNamek = namek.Ficha.BP;
				falas = Ouvir(() => AbsorverNamekuseijin(namek, nail));
				Checa("(montagem) a fusao Namekuseijin ACONTECEU pelo gesto de producao: o poder entrou em quem absorveu e o personagem do amigo se perdeu",
					  namek.Ficha.BP > bpDoNamek + 1 && nail.PersonagemConsumido && contaDoNail.Slots[nail.Slot] == null,
					  $"BP {bpDoNamek:0} -> {namek.Ficha.BP:0}, consumido {nail.PersonagemConsumido} | {Ultimos(falas)}");
				Checa("**o amigo declarado e absorvido numa FUSAO NAMEKUSEIJIN na frente dele: NENHUMA raiva, nenhuma cena, o Ki onde estava** (dono, 2026-10-11; Fusion.dm:301-310)",
					  Nivel() == NivelDeRaiva.Nenhuma && Math.Abs(f.Anger - 100) < 0.001 && Cenas() == 0 && Math.Abs(Razao() - 0.05) < 0.001,
					  $"{Nivel()}, Anger {f.Anger:0.##}, {Cenas()} cena(s), Ki {Razao() * 100:0.#}%");
				// (a frase do luto e "...dentro de voce se PARTE"; "se foi" sozinho nao serve, porque quem e absorvido
				// le "o personagem se foi PARA SEMPRE")
				Checa("...e ele VIU a fusao (leu o anuncio dela) -- ninguem leu a frase do luto, nem a de quem ve alguem se enfurecer",
					  Disse(falas, "absorve") && !Disse(falas, "se PARTE") && !Disse(falas, "furioso") && !Disse(falas, "com raiva"),
					  Ultimos(falas));
			}

			// ============================ 5. QUEM NAO E AMIGO DECLARADO NAO MOVE NADA ============================
			GD.Print("[raiva] -- 5) quem nao foi declarado amigo nao move nada --");
			EmPaz();
			KiEm(0.05);
			AoPerderALuta(estranho, algoz, morreu: false);
			AoPerderALuta(estranho, algoz, morreu: true);
			LutoPelaAbsorcao(estranho, majin);
			Checa("um ESTRANHO cai, morre e e absorvido na frente dele: nenhuma raiva, nenhuma cena, Ki onde estava",
				  Nivel() == NivelDeRaiva.Nenhuma && Math.Abs(f.Anger - 100) < 0.001 && Cenas() == 0 && Math.Abs(Razao() - 0.05) < 0.001,
				  $"{Nivel()}, Anger {f.Anger:0.##}, {Cenas()} cena(s), Ki {Razao() * 100:0.#}%");

			DeclararDeTeste(pl, estranho, Relacao.Neutro);
			AoPerderALuta(estranho, algoz, morreu: true);
			Checa("...e conhecer sem chamar de amigo ('neutro') tambem nao", Nivel() == NivelDeRaiva.Nenhuma && Cenas() == 0, Nivel().ToString());

			// o duelo entre amigos: a VITIMA declarou o algoz amigo
			DeclararDeTeste(amigo, algoz, Relacao.Bom);
			AoPerderALuta(amigo, algoz, morreu: true);
			Checa("o amigo morto por um AMIGO DELE (treino que acabou mal) nao enfurece quem assiste (Death.dm:75)",
				  Nivel() == NivelDeRaiva.Nenhuma && Cenas() == 0, Nivel().ToString());
			DeclararDeTeste(amigo, algoz, Relacao.Neutro);

			// longe da vista
			Vec2 lugarDoAmigo = amigo.Pos;
			amigo.Pos = pl.Pos + new Vec2(40 * ZoneCollision.TileSize, 0);
			AoPerderALuta(amigo, algoz, morreu: true);
			Checa("...e o amigo morrendo a 40 tiles (fora da vista) tambem nao", Nivel() == NivelDeRaiva.Nenhuma && Cenas() == 0, Nivel().ToString());
			amigo.Pos = lugarDoAmigo;

			// ============================ 6. O SALTO, TETO A TETO ============================
			GD.Print("[raiva] -- 6) o salto de Ki: o teto de cada um, e quem nao tem controle --");
			foreach (double teto in new[] { 1.4, 2.0, 3.8 })
			{
				EmPaz();
				f.powerupcap = teto;
				KiEm(0.05);
				AoPerderALuta(amigo, algoz, morreu: true);
				Checa($"com teto de carga de {teto * 100:0}%: o Ki vai de 5% a {teto * 100:0}%",
					  Math.Abs(Razao() - teto) < 0.001, $"Ki em {Razao() * 100:0.#}%");
			}
			f.powerupcap = 1.4;

			EmPaz();
			KiEm(1.0);
			AoPerderALuta(amigo, algoz, morreu: true);
			Checa("de 100% (tanque cheio) ele tambem vai ao teto", Math.Abs(Razao() - 1.4) < 0.001, $"Ki em {Razao() * 100:0.#}%");

			EmPaz();
			KiEm(3.0);
			AoPerderALuta(amigo, algoz, morreu: true);
			Checa("quem ja esta ACIMA do teto de carga (300%) fica onde esta: o salto nunca desce o Ki",
				  Math.Abs(Razao() - 3.0) < 0.001, $"Ki em {Razao() * 100:0.#}%");

			EmPaz();
			KiEm(0.05);
			f.canPower = 0;
			AoPerderALuta(amigo, algoz, morreu: true);
			Checa("**SEM o controle de ki (so Ki Unlocked): furia extrema e cinematica, mas o Ki fica onde estava**",
				  Nivel() == NivelDeRaiva.Extrema && Cenas() == 1 && Math.Abs(Razao() - 0.05) < 0.001,
				  $"{Nivel()}, {Cenas()} cena(s), Ki {Razao() * 100:0.#}%");
			f.canPower = 1;

			EmPaz();
			KiEm(0.05);
			f.MeditateGivesKiRegen = 0;
			AoPerderALuta(amigo, algoz, morreu: true);
			Checa("...sem saber reunir energia (sem Ki Unlocked) tambem nao salta", Math.Abs(Razao() - 0.05) < 0.001, $"Ki em {Razao() * 100:0.#}%");
			f.MeditateGivesKiRegen = 1;

			EmPaz();
			KiEm(0.05);
			f.fd_ki_locked = true;
			AoPerderALuta(amigo, algoz, morreu: true);
			Checa("...nem com o ki TRAVADO (o Frost mutante descontrolado nao reune nada)", Math.Abs(Razao() - 0.05) < 0.001, $"Ki em {Razao() * 100:0.#}%");
			f.fd_ki_locked = false;

			// ============================ 7. A RAIVA E AS FORMAS: A NOVA ESTREIA SOZINHA; SEM FORMA NOVA, A CENA E A DA RAIVA ============================
			// O dono, em tres mensagens de 2026-10-11:
			//   * *"a preferencia de cinematica e de transformacao (...) ao bater reqs de transformacao q precisam de
			//     raiva, o personagem se transforma AUTOMATICAMENTE, o jogador n tem q apertar nada"*;
			//   * *"se o personagem ja despertou a transformacao toca a cinematica da raiva normal"*;
			//   * *"mas se ele tiver os reqs por exemplo do ssj2 e ele estiver na base e ficar com raiva, ele vai pro
			//     ssj1 e comeca a cinematica do ssj2"*.
			// Tudo aqui entra pelo funil da derrota e anda pelo tique da forma (`TickDaForma`), que e por onde o jogo de
			// verdade passa; ninguem chama `Transformar`.
			GD.Print("[raiva] -- 7) a raiva e as formas: a NOVA estreia sozinha (subindo pelas que ja sao dele); sem forma nova, a cena e a da raiva --");
			double portaSsj1 = PortaDeTeste(pl, "ssj1"), portaSsj2 = PortaDeTeste(pl, "ssj2");
			// (paga o Super Saiyajin e NAO o 2: a porta e sorteada por personagem, entao o poder sai dela)
			double soOSsj1 = portaSsj2 > portaSsj1 ? Math.Sqrt(portaSsj1 * portaSsj2) : portaSsj1 * 1.01;
			double praODois = portaSsj2 * 1.2;
			string marcoDoSsj2 = Catalogo.Def("ssj2")!.Marco;
			void UmTique() => TickDaForma(pl, 1 / 30.0);
			void ParaABase(bool esquecerAsFormas)
			{
				pl.CenaSegundos = 0;
				pl.Forma.Entrar(Catalogo.IdBase);
				if (esquecerAsFormas) { pl.Forma.Liberadas.Clear(); pl.Forma.EstreiaVista.Clear(); }
				AplicarForma(pl);
			}
			// (palco) NA BASE, com o Super Saiyajin ja ESTREADO um dia -- e nada alem dele
			void NaBaseComOSsj1JaDespertado()
			{
				ParaABase(esquecerAsFormas: true);
				pl.Forma.Entrar("ssj1");
				pl.Forma.Entrar(Catalogo.IdBase);
				AplicarForma(pl);
			}

			// (a) SEM requisito de forma: a cena e a da RAIVA, e ninguem se transforma
			ParaABase(esquecerAsFormas: true);
			EmPaz();
			KiEm(0.05);
			f.BP = 1;
			AoPerderALuta(amigo, algoz, morreu: true);
			UmTique();
			Checa("(a) furia extrema SEM poder pra forma nenhuma: toca a cinematica da RAIVA, e ele continua na base",
				  Cenas() == 1 && pl.Forma.NaBase, $"{Cenas()} cena(s), forma {pl.Forma.Atual}");

			// (b) COM o requisito de uma forma NOVA cumprido: transforma sozinho, cena da TRANSFORMACAO, e a da raiva nao toca
			ParaABase(esquecerAsFormas: true);
			EmPaz();
			KiEm(0.05);
			f.BP = soOSsj1;
			RecusaForma emPazComPoder = pl.Forma.Avaliar("ssj1", f.BP, 1, false, Perfil(pl));
			UmTique();
			Checa("(montagem) com o poder do Super Saiyajin e EM PAZ so falta a furia -- e o tique nao transforma ninguem",
				  emPazComPoder == RecusaForma.SemFuria && pl.Forma.NaBase, $"{emPazComPoder}, forma {pl.Forma.Atual}");

			falas = Ouvir(() => { AoPerderALuta(amigo, algoz, morreu: true); UmTique(); });
			Checa("**(b) o amigo morre e ele VIRA SUPER SAIYAJIN SOZINHO, no tique seguinte, sem ninguem apertar nada**",
				  pl.Forma.Atual == "ssj1" && pl.Forma.Despertou("ssj1"), $"forma {pl.Forma.Atual}");
			Checa("...pela ESTREIA de sempre: ele le 'VOCE DESPERTA', a cinematica da transformacao prende o corpo e a forma multiplica",
				  Disse(falas, "VOCE DESPERTA") && EmCena(pl) && f.ssjBuff > 1.5,
				  $"cena {pl.CenaSegundos:0.#}s, ssjBuff {f.ssjBuff:0.##} | {Ultimos(falas)}");
			Checa("**...e a cinematica da RAIVA nao toca: nunca as duas**", Cenas() == 0, $"{Cenas()} cena(s) de furia");
			Checa("...o Ki saltou ANTES e atravessa a transformacao inteiro: 140% do tanque da forma",
				  Math.Abs(Razao() - 1.4) < 0.01, $"Ki em {Razao() * 100:0.#}%");
			PassarACena(pl);
			Checa("...e acabada a cena ele continua no Super Saiyajin (com este poder a raiva nao abre o 2)",
				  pl.Forma.Atual == "ssj1", $"forma {pl.Forma.Atual}");

			// (c) A FORMA JA E DELE, e nao ha forma NOVA que a raiva pague: a cena e a da RAIVA, e ele fica como esta
			ParaABase(esquecerAsFormas: false);   // (o Super Saiyajin do caso (b) continua despertado)
			EmPaz();
			KiEm(0.05);
			RecusaForma doisSemPoder = pl.Forma.AvaliarEstandoEm("ssj1", "ssj2", f.BP, 1, false,
																  Perfil(pl) with { Raiva = NivelDeRaiva.Extrema });
			Checa("(montagem c) o Super Saiyajin ja e dele (estreado) e o poder NAO paga o 2: nao ha forma nova pra raiva estrear",
				  pl.Forma.NaBase && pl.Forma.JaViuAEstreia("ssj1") && doisSemPoder == RecusaForma.SemPoder,
				  $"o 2, a partir do 1 e com furia: {doisSemPoder}");
			List<(string De, string Para, DegrauDeCena Degrau)> semNovidade =
				AnunciosDe(() => { AoPerderALuta(amigo, algoz, morreu: true); UmTique(); });
			Checa("**(c) forma JA despertada e nenhuma nova a estrear: a cinematica da RAIVA TOCA** (dono, 2026-10-11) -- e o Ki salta",
				  Cenas() == 1 && Math.Abs(Razao() - 1.4) < 0.001, $"{Cenas()} cena(s), Ki {Razao() * 100:0.#}%");
			Checa("...e ele FICA na forma em que estava (a base): a transformacao forcada e so a da ESTREIA",
				  pl.Forma.NaBase && !EmCena(pl) && semNovidade.Count == 0, $"forma {pl.Forma.Atual}; {Lista(semNovidade)}");

			// ...e vale igual pra quem JA ESTA na forma que e dele
			pl.Forma.Entrar("ssj1");
			AplicarForma(pl);
			pl.CenaSegundos = 0;
			EmPaz();
			KiEm(0.05);
			AoPerderALuta(amigo, algoz, morreu: true);
			UmTique();
			Checa("...e quem JA ESTA no Super Saiyajin (dele) ve a cinematica da raiva e continua no Super Saiyajin",
				  Cenas() == 1 && pl.Forma.Atual == "ssj1" && !EmCena(pl), $"{Cenas()} cena(s), forma {pl.Forma.Atual}");

			// ---- contra-exemplo: a previsao do DM (`hasssj ||`, Murder.dm:129) ----
			ParaABase(esquecerAsFormas: false);
			EmPaz();
			KiEm(0.05);
			FuriaCedeAoGestoDeTeste = true;
			try
			{
				AoPerderALuta(amigo, algoz, morreu: true);
				UmTique();
				Checa("(defeito injetado: a cena cede o momento ao proximo C, como no DM) ele fica SEM cinematica nenhuma -- a checagem (c) REPROVA",
					  Cenas() == 0 && pl.Forma.NaBase, $"{Cenas()} cena(s), forma {pl.Forma.Atual}");
			}
			finally { FuriaCedeAoGestoDeTeste = false; }

			// (d) o Super Saiyajin 2 tambem: DENTRO do Super Saiyajin, com o poder do 2
			pl.Forma.Entrar("ssj1");
			AplicarForma(pl);
			pl.CenaSegundos = 0;
			EmPaz();
			KiEm(0.50);
			f.BP = praODois;
			RecusaForma doisEmPaz = pl.Forma.Avaliar("ssj2", f.BP, 1, false, Perfil(pl));
			AoPerderALuta(amigo, algoz, morreu: true);
			UmTique();
			Checa("(d) vale pro Super Saiyajin 2: dentro do 1, com o poder do 2 (so faltava a furia), a morte do amigo o leva ao 2 sozinho, sem cena de raiva",
				  doisEmPaz == RecusaForma.SemFuria && pl.Forma.Atual == "ssj2" && Cenas() == 0,
				  $"em paz {doisEmPaz}; forma {pl.Forma.Atual}; {Cenas()} cena(s)");

			// (h) O EXEMPLO DO DONO: na BASE, com o Super Saiyajin ja despertado e os requisitos do 2
			NaBaseComOSsj1JaDespertado();
			EmPaz();
			KiEm(0.05);
			f.BP = praODois;
			f.bp_milestones_done.Remove(marcoDoSsj2);
			RecusaForma doisDaBase = pl.Forma.Avaliar("ssj2", f.BP, 1, false, Perfil(pl) with { Raiva = NivelDeRaiva.Extrema });
			Checa("(montagem h) na BASE, com o Super Saiyajin ja estreado e o poder do 2 pago, o 2 nao se alcanca daqui nem com furia: falta o DEGRAU",
				  pl.Forma.NaBase && pl.Forma.JaViuAEstreia("ssj1") && !pl.Forma.Despertou("ssj2") && doisDaBase == RecusaForma.ForaDeOrdem,
				  $"o 2, da base e com furia: {doisDaBase}");

			List<(string De, string Para, DegrauDeCena Degrau)> subida =
				AnunciosDe(() => falas = Ouvir(() => { AoPerderALuta(amigo, algoz, morreu: true); UmTique(); }));
			Checa("**(h) NA BASE, COM OS REQUISITOS DO SUPER SAIYAJIN 2: ele termina em SUPER SAIYAJIN 2, sozinho** (dono: 'ele vai pro ssj1 e comeca a cinematica do ssj2')",
				  pl.Forma.Atual == "ssj2" && pl.Forma.Despertou("ssj2"), $"forma {pl.Forma.Atual}");
			Checa("**...PASSANDO pelo Super Saiyajin: a zona ouve base -> ssj1 SEM cena e ssj1 -> ssj2 com a cena de ESTREIA, nessa ordem**",
				  subida.Count == 2
				  && subida[0] is { De: "base", Para: "ssj1", Degrau: DegrauDeCena.Nenhuma }
				  && subida[1] is { De: "ssj1", Para: "ssj2", Degrau: DegrauDeCena.Estreia },
				  Lista(subida));
			double presoPeloSsj2 = Cinematicas.NoDegrau(Catalogo.Def("ssj2")!, DegrauDeCena.Estreia)?.SegundosPreso ?? -1;
			Checa("...a cena que prende o corpo e a do SUPER SAIYAJIN 2 (o prazo dela), e o que ele le e 'VOCE DESPERTA: Super Saiyajin 2'",
				  presoPeloSsj2 > 0 && Math.Abs(pl.CenaSegundos - presoPeloSsj2) < 0.001
				  && Disse(falas, "VOCE DESPERTA: Super Saiyajin 2") && !Disse(falas, "VOCE DESPERTA: Super Saiyajin!"),
				  $"preso {pl.CenaSegundos:0.#}s de {presoPeloSsj2:0.#}s | {Ultimos(falas)}");
			Checa("**...e a cinematica da RAIVA nao toca**", Cenas() == 0, $"{Cenas()} cena(s) de furia");
			Checa("...o Ki saltou pro teto e atravessou os DOIS degraus: 140% do tanque do Super Saiyajin 2",
				  Math.Abs(Razao() - 1.4) < 0.01, $"Ki em {Razao() * 100:0.#}%");
			ConferirOsMarcosDeForma(pl);   // o que o laco de fichas faz cinco vezes por segundo
			Checa("**...e o MARCO do Super Saiyajin 2 entrou na ficha** (a estreia foi a de sempre, e o laco de fichas a viu)",
				  f.bp_milestones_done.Contains(marcoDoSsj2), $"marcos: {string.Join(",", f.bp_milestones_done)}");
			PassarACena(pl);
			Checa("...e acabada a cena ele continua no Super Saiyajin 2", pl.Forma.Atual == "ssj2", $"forma {pl.Forma.Atual}");

			// ---- contra-exemplo: a raiva so alcanca o que esta a um passo (o DM, supersaiyan.dm:122) ----
			NaBaseComOSsj1JaDespertado();
			EmPaz();
			KiEm(0.05);
			SubidaPelaRaiva.NaoSobeDegrausDeTeste = true;
			try
			{
				AoPerderALuta(amigo, algoz, morreu: true);
				UmTique();
				Checa("(defeito injetado: a raiva so desperta a forma que se alcanca de onde o corpo esta) ele FICA NA BASE, sem o Super Saiyajin 2 -- a checagem (h) REPROVA",
					  pl.Forma.NaBase && !pl.Forma.Despertou("ssj2"), $"forma {pl.Forma.Atual}");
			}
			finally { SubidaPelaRaiva.NaoSobeDegrausDeTeste = false; }

			// (i) NUM GRADE ele conta como no Super Saiyajin ou acima: estreia o 2 DE ONDE ESTA, sem degrau no caminho
			NaBaseComOSsj1JaDespertado();
			pl.Forma.Maestria.Por("ssj1", Catalogo.Grade2Pct);
			pl.Forma.Entrar("ssj1");
			pl.Forma.Entrar("grade2");
			AplicarForma(pl);
			pl.CenaSegundos = 0;
			EmPaz();
			KiEm(0.50);
			List<(string De, string Para, DegrauDeCena Degrau)> doGrade =
				AnunciosDe(() => { AoPerderALuta(amigo, algoz, morreu: true); UmTique(); });
			Checa("(i) NUM GRADE (o Super Saiyajin inchado) com os requisitos do 2: ele estreia o Super Saiyajin 2 DE ONDE ESTA -- os grades nao sao degrau do caminho",
				  pl.Forma.Atual == "ssj2" && doGrade.Count == 1 && doGrade[0] is { De: "grade2", Para: "ssj2", Degrau: DegrauDeCena.Estreia },
				  $"forma {pl.Forma.Atual}; {Lista(doGrade)}");
			pl.Forma.Maestria.Por("ssj1", maestriaDoSsj1Antes);

			// (j) NEM O 1 NEM O 2 DESPERTADOS, e o poder paga os dois: sao DUAS estreias, uma depois da outra
			ParaABase(esquecerAsFormas: true);
			EmPaz();
			KiEm(0.05);
			List<(string De, string Para, DegrauDeCena Degrau)> primeiraEstreia =
				AnunciosDe(() => { AoPerderALuta(amigo, algoz, morreu: true); UmTique(); });
			bool estreouSoOUm = pl.Forma.Atual == "ssj1" && !pl.Forma.Despertou("ssj2")
								&& primeiraEstreia.Count == 1 && primeiraEstreia[0].Degrau == DegrauDeCena.Estreia;
			// (a primeira cena passa, com a raiva ainda acesa: o tique que solta o corpo e o que desperta o seguinte)
			List<(string De, string Para, DegrauDeCena Degrau)> segundaEstreia = AnunciosDe(() => PassarACena(pl));
			Checa("(j) nem o 1 nem o 2 despertados, poder pros dois: a raiva estreia o SUPER SAIYAJIN primeiro (a subida nao atravessa calada um degrau que ele nunca vestiu)",
				  estreouSoOUm, Lista(primeiraEstreia));
			Checa("...e quando a cena dele solta o corpo, ainda enfurecido, vem a ESTREIA do 2: duas cinematicas de transformacao em fila, e nenhuma de raiva",
				  pl.Forma.Atual == "ssj2" && segundaEstreia.Count == 1
				  && segundaEstreia[0] is { De: "ssj1", Para: "ssj2", Degrau: DegrauDeCena.Estreia } && Cenas() == 0,
				  $"forma {pl.Forma.Atual}; {Lista(segundaEstreia)}; {Cenas()} cena(s) de furia");

			// (e) os DOIS degraus de raiva na linha Legendary: a COMUM tambem a desperta, pelo preco DELA -- 1,5x a porta
			// (`supersaiyan.dm:68`). Ate 2026-10-11 o Legendary abria a linha com a raiva comum PELA PORTA: era o
			// desconto dele, e a regra nova (os numeros do DM) o desfez.
			ParaABase(esquecerAsFormas: true);
			f.Class = "Legendary";
			f.Statify();
			f.powerupcap = 1.4;
			EmPaz();
			KiEm(0.50);
			double portaW = PortaDeTeste(pl, Catalogo.IdWrathful), portaC = PortaDeTeste(pl, "c_type"), portaL = PortaDeTeste(pl, "legendary");
			double precoComumW = portaW * Catalogo.AgioComumDoLegendary;
			f.BP = (portaW + precoComumW) / 2;
			AoPerderALuta(amigo, algoz, morreu: false);
			UmTique();
			RecusaForma wrathfulSemOPreco = pl.Forma.Avaliar(Catalogo.IdWrathful, f.BP, 1, false, Perfil(pl));
			Checa("(e) o Legendary NAO tem mais desconto: o amigo so CAI, e com o poder entre a porta e 1,5x ela o Wrathful NAO vem",
				  pl.Forma.NaBase && wrathfulSemOPreco == RecusaForma.SemPoderPraRaivaComum, $"forma {pl.Forma.Atual}, {wrathfulSemOPreco}");
			EmPaz();
			f.BP = precoComumW * 1.02;
			RecusaForma wrathfulEmPaz = pl.Forma.Avaliar(Catalogo.IdWrathful, f.BP, 1, false, Perfil(pl));
			AoPerderALuta(amigo, algoz, morreu: false);
			UmTique();
			Checa("...e com 1,5x a porta o MESMO nocaute desperta o Wrathful sozinho (a raiva comum paga a linha dele, mais caro que o Saiyajin comum)",
				  f.BP < portaC && wrathfulEmPaz == RecusaForma.SemFuria && pl.Forma.Atual == Catalogo.IdWrathful && Cenas() == 0,
				  $"em paz {wrathfulEmPaz}; forma {pl.Forma.Atual}");
			Checa("...e o Ki dele vai a 100% e para: e o tanque cheio de toda ESTREIA (`EntrarNaForma`), e nao o salto da furia extrema",
				  Math.Abs(Razao() - 1.0) < 0.01, $"Ki em {Razao() * 100:0.#}%");

			// (e2) e a SUBIDA vale pra linha dele tambem, com a raiva COMUM: base -> Wrathful (ja dele) -> estreia do C-Type
			pl.CenaSegundos = 0;
			pl.Forma.Entrar(Catalogo.IdBase);
			AplicarForma(pl);
			EmPaz();
			KiEm(0.50);
			f.BP = portaL > portaC ? Math.Sqrt(portaC * portaL) : portaC * 1.01;
			List<(string De, string Para, DegrauDeCena Degrau)> subidaLendaria =
				AnunciosDe(() => { AoPerderALuta(amigo, algoz, morreu: false); UmTique(); });
			Checa("(e2) a subida e do CATALOGO, nao do Super Saiyajin: Legendary na base, com o Wrathful ja dele e o poder do C-Type, ve o amigo CAIR -- sobe pelo Wrathful (sem cena) e estreia o C-Type",
				  pl.Forma.Atual == "c_type" && subidaLendaria.Count == 2
				  && subidaLendaria[0] is { De: "base", Degrau: DegrauDeCena.Nenhuma } && subidaLendaria[0].Para == Catalogo.IdWrathful
				  && subidaLendaria[1] is { Para: "c_type", Degrau: DegrauDeCena.Estreia } && Cenas() == 0,
				  $"forma {pl.Forma.Atual}; {Lista(subidaLendaria)}");
			ParaABase(esquecerAsFormas: true);
			f.Class = "Normal";
			f.Statify();
			f.powerupcap = 1.4;

			// (f) o corpo ocupado no instante da furia: caido, ele vira quando se levanta
			EmPaz();
			KiEm(0.05);
			f.BP = soOSsj1;
			f.KO = true;
			AoPerderALuta(amigo, algoz, morreu: true);
			UmTique();
			bool caidoNaoVirou = pl.Forma.NaBase;
			f.KO = false;
			UmTique();
			Checa("(f) NOCAUTEADO no instante da furia ele nao se transforma; quando se levanta, ainda enfurecido, a forma vem sozinha (e um estado, nao um instante)",
				  caidoNaoVirou && pl.Forma.Atual == "ssj1", $"caido: {(caidoNaoVirou ? "na base" : "virou")}; de pe: {pl.Forma.Atual}");

			// (g) A RAIVA SO SOBE: numa forma mais forte que a que ela abriria, ela nao puxa o corpo pra baixo.
			// O Mistico (16x) e dado aqui a mao, como o ritual do Kaioshin o daria; o Super Saiyajin (2x) continua
			// nunca despertado, com o poder dele pago.
			ParaABase(esquecerAsFormas: true);
			pl.Forma.Liberar(Catalogo.IdMistico);
			pl.Forma.Entrar(Catalogo.IdMistico);
			AplicarForma(pl);
			pl.CenaSegundos = 0;
			EmPaz();
			KiEm(0.50);
			f.BP = soOSsj1;
			double noMistico = pl.Forma.Multiplicador(Perfil(pl));
			RecusaForma ssj1DoMistico = pl.Forma.Avaliar("ssj1", f.BP, 1, false, Perfil(pl));
			AoPerderALuta(amigo, algoz, morreu: true);
			RecusaForma ssj1ComFuria = pl.Forma.Avaliar("ssj1", f.BP, 1, false, Perfil(pl));
			UmTique();
			Checa("(g) A RAIVA SO SOBE: no Mistico (16x), com o Super Saiyajin (2x) aberto pela furia, ele NAO e puxado pra baixo",
				  noMistico > 2.5 && ssj1DoMistico == RecusaForma.SemFuria && ssj1ComFuria == RecusaForma.Pode
				  && pl.Forma.Atual == Catalogo.IdMistico,
				  $"Mistico {noMistico:0.##}x; ssj1 em paz {ssj1DoMistico}, com furia {ssj1ComFuria}; forma {pl.Forma.Atual}");
			Checa("...e sem transformacao pra ceder o momento, a cena que toca e a da RAIVA", Cenas() == 1, $"{Cenas()} cena(s)");

			EmPaz();
			SubidaPelaRaiva.PraBaixoDeTeste = true;
			try
			{
				AoPerderALuta(amigo, algoz, morreu: true);
				UmTique();
				Checa("(defeito injetado: a raiva desperta a forma mesmo sendo mais fraca) ele CAI do Mistico pro Super Saiyajin -- a checagem REPROVA",
					  pl.Forma.Atual == "ssj1", $"forma {pl.Forma.Atual}");
			}
			finally { SubidaPelaRaiva.PraBaixoDeTeste = false; }

			// ---- contra-exemplos do despertar ----
			ParaABase(esquecerAsFormas: true);
			EmPaz();
			KiEm(0.05);
			RaivaNaoTransformaDeTeste = true;
			try
			{
				AoPerderALuta(amigo, algoz, morreu: true);
				UmTique();
				Checa("(defeito injetado: a raiva so LIBERA a forma e espera a tecla) a checagem do automatico REPROVA",
					  pl.Forma.NaBase, $"forma {pl.Forma.Atual}");
			}
			finally { RaivaNaoTransformaDeTeste = false; }

			ParaABase(esquecerAsFormas: true);
			EmPaz();
			KiEm(0.05);
			FuriaNaoCedeACenaDeTeste = true;
			try
			{
				AoPerderALuta(amigo, algoz, morreu: true);
				UmTique();
				Checa("(defeito injetado: a cena da furia nao cede o momento) as DUAS tocam -- a checagem 'nunca as duas' REPROVA",
					  Cenas() == 1 && pl.Forma.Atual == "ssj1", $"{Cenas()} cena(s) de furia, forma {pl.Forma.Atual}");
			}
			finally { FuriaNaoCedeACenaDeTeste = false; }

			// O FIM DA JANELA NAO DEVOLVE O KI: a raiva passa, a energia fica (e vaza devagar, como todo excesso)
			ParaABase(esquecerAsFormas: true);
			EmPaz();
			KiEm(0.05);
			f.BP = 1;
			AoPerderALuta(amigo, algoz, morreu: true);
			pl.FuriaExtremaAte = NowMs() - 1;
			pl.RaivaComumAte = 0;
			ProjetarRaiva(pl);
			Checa("vencidos os 120 s a raiva volta a 100 e o Ki NAO e tirado de volta",
				  Math.Abs(f.Anger - 100) < 0.001 && Razao() > 1.39, $"Anger {f.Anger:0.##}, Ki {Razao() * 100:0.#}%");

			// ============================ 8. MEDITAR ACALMA -- `Meditate.dm:27-31` ============================
			// O dono (2026-10-11): *"sim meditar deveria acalmar aqui tb"*. Tudo entra pelo `DefinirAtividade`, que e
			// o que o pacote da tecla M chama: a conferencia do servidor (`AceitarTreinoOuMeditacao`) roda de verdade.
			GD.Print("[raiva] -- 8) meditar acalma: os dois degraus acabam na hora, e so quando a meditacao e ACEITA --");
			void Sentar() => DefinirAtividade(pl, Jandirus.Net.Protocol.Activity.Meditando);
			void Levantar() => DefinirAtividade(pl, Jandirus.Net.Protocol.Activity.Parado);
			ParaABase(esquecerAsFormas: true);
			f.BP = 1;
			Levantar();

			// (a) a furia extrema, com o salto de Ki e a cena
			EmPaz();
			KiEm(0.05);
			AoPerderALuta(amigo, algoz, morreu: true);
			RepercutirPoder(pl);
			double buffEnfurecido = f.angerBuff;
			falas = Ouvir(Sentar);
			Checa("**a furia EXTREMA acaba no instante em que ele senta pra meditar** (Meditate.dm:27-31)",
				  f.med && Nivel() == NivelDeRaiva.Nenhuma && Math.Abs(f.Anger - 100) < 0.001
				  && pl.FuriaExtremaAte == 0 && pl.RaivaComumAte == 0,
				  $"medita {f.med}, {Nivel()}, Anger {f.Anger:0.##}");
			Checa("...ele le a frase da MEDITACAO (e nao a do prazo vencido), e o buff de poder da raiva sai na hora",
				  Disse(falas, "se centra") && !Disse(falas, "se apaga") && buffEnfurecido > 1.0001 && Math.Abs(f.angerBuff - 1) < 0.0001,
				  $"angerBuff {buffEnfurecido:0.###}x -> {f.angerBuff:0.###}x | {Ultimos(falas)}");
			Checa("...o Ki que a furia deu NAO e devolvido (os 140% do tanque continuam la)", Razao() > 1.39, $"Ki em {Razao() * 100:0.#}%");
			Checa("...e a recarga da cinematica continua contando (rageCinematicCD, Murder.dm:139: acalmar nao a zera)",
				  pl.FuriaCenaAte > NowMs(), $"faltam {(pl.FuriaCenaAte - NowMs()) / 1000.0:0.#}s");
			Levantar();

			// (b) a raiva comum
			EmPaz();
			KiEm(0.05);
			AoPerderALuta(amigo, algoz, morreu: false);
			bool estavaComRaivaComum = Nivel() == NivelDeRaiva.Comum;
			Sentar();
			Checa("a raiva COMUM tambem acaba (sao os dois degraus)",
				  estavaComRaivaComum && Nivel() == NivelDeRaiva.Nenhuma && Math.Abs(f.Anger - 100) < 0.001, $"{Nivel()}, Anger {f.Anger:0.##}");
			Levantar();

			// (c) as DUAS janelas abertas: o amigo caiu e morreu
			EmPaz();
			AoPerderALuta(amigo, algoz, morreu: false);
			AoPerderALuta(amigo, algoz, morreu: true);
			bool asDuasAbertas = pl.FuriaExtremaAte > NowMs() && pl.RaivaComumAte > NowMs();
			Sentar();
			Checa("com as DUAS janelas abertas (ele caiu, e morreu) meditar fecha as duas: nao sobra a raiva comum por baixo",
				  asDuasAbertas && Nivel() == NivelDeRaiva.Nenhuma && pl.FuriaExtremaAte == 0 && pl.RaivaComumAte == 0, Nivel().ToString());
			Levantar();

			// (d) a forma que a raiva despertou fica
			ParaABase(esquecerAsFormas: true);
			EmPaz();
			KiEm(0.05);
			f.BP = soOSsj1;
			AoPerderALuta(amigo, algoz, morreu: true);
			UmTique();
			PassarACena(pl);
			Sentar();
			UmTique();
			Checa("a FORMA que a raiva despertou fica: ele medita em Super Saiyajin, calmo, e continua nele",
				  pl.Forma.Atual == "ssj1" && pl.Forma.Despertou("ssj1") && f.med && Nivel() == NivelDeRaiva.Nenhuma,
				  $"forma {pl.Forma.Atual}, {Nivel()}");
			Levantar();
			ParaABase(esquecerAsFormas: true);
			f.BP = 1;

			// (e) meditacao RECUSADA nao acalma
			EmPaz();
			KiEm(0.05);
			AoPerderALuta(amigo, algoz, morreu: true);
			pl.Voando = true;
			falas = Ouvir(Sentar);
			pl.Voando = voavaAntes;
			Checa("**meditacao RECUSADA pelo servidor (ele esta voando) NAO acalma: a furia continua**",
				  !f.med && Nivel() == NivelDeRaiva.Extrema && f.Anger > 100 && !Disse(falas, "se centra"),
				  $"medita {f.med}, {Nivel()} | {Ultimos(falas)}");

			// (f) so o COMECO da meditacao acalma (o `if(!med)` do DM)
			EmPaz();
			Sentar();
			AoPerderALuta(amigo, algoz, morreu: true);
			Sentar();   // o pacote repetido do que ja esta ligado
			bool continuouEnfurecido = f.med && Nivel() == NivelDeRaiva.Extrema;
			Levantar();
			Sentar();
			Checa("quem JA meditava quando a furia chegou continua enfurecido (so o COMECO acalma, o `if(!med)` de Meditate.dm:26); levantar e sentar de novo acalma",
				  continuouEnfurecido && Nivel() == NivelDeRaiva.Nenhuma, $"antes: {(continuouEnfurecido ? "enfurecido" : "calmo")}; depois: {Nivel()}");
			Levantar();

			// (g) treinar nao e meditar
			EmPaz();
			AoPerderALuta(amigo, algoz, morreu: true);
			DefinirAtividade(pl, Jandirus.Net.Protocol.Activity.Treinando);
			Checa("TREINAR nao acalma (e so a meditacao)", f.train && Nivel() == NivelDeRaiva.Extrema, $"treina {f.train}, {Nivel()}");
			Levantar();

			// ---- contra-exemplo ----
			EmPaz();
			AoPerderALuta(amigo, algoz, morreu: true);
			MeditarNaoAcalmaDeTeste = true;
			try
			{
				Sentar();
				Checa("(defeito injetado: meditar nao acalma -- o port ate 2026-10-11) ele medita EXTREMAMENTE furioso -- a checagem REPROVA",
					  f.med && Nivel() == NivelDeRaiva.Extrema && f.Anger > 100, $"medita {f.med}, {Nivel()}, Anger {f.Anger:0.##}");
			}
			finally { MeditarNaoAcalmaDeTeste = false; Levantar(); }

			// ============================ 9. O PRECO DA RAIVA COMUM, NO CORPO VIVO (dono, 2026-10-11) ============================
			// *"o rage normal (ao ver um amigo ser nocauteado mas n morto) tb faz vc se transformar da mesma forma q o
			// extreme, porem o requisito de bp pro rage normal liberar sua proxima transformacao e maior"*. O preco e a
			// porta de BP vezes o agio da forma (`FormaDef.AgioNaRaivaComum`: o `if("Angry")` do `supersaiyan.dm:112`), e
			// a tabela inteira -- forma por forma, com o Heran, o Futuro, as duas linhas Legendary e o Beast -- esta na
			// bancada pura `raiva` [13]. Aqui e o JOGO: o amigo cai pelo funil da derrota e o tique da forma faz o resto.
			GD.Print("[raiva] -- 9) o preco da raiva comum: o amigo so CAI, e a forma vem sozinha -- com o BP maior --");
			double precoComum1 = portaSsj1 * Catalogo.AgioComumDoSaiyajin, entreOsPrecos1 = (portaSsj1 + precoComum1) / 2;
			double presoPeloSsj1 = Cinematicas.NoDegrau(Catalogo.Def("ssj1")!, DegrauDeCena.Estreia)?.SegundosPreso ?? -1;

			// (a) CALMO, com o poder que a raiva comum cobra: nada
			ParaABase(esquecerAsFormas: true);
			EmPaz();
			KiEm(0.30);
			f.BP = precoComum1 * 1.01;
			UmTique();
			Checa("(a) CALMO, com o poder que a raiva comum cobra: ninguem se transforma",
				  pl.Forma.NaBase && !pl.Forma.Despertou("ssj1"), $"forma {pl.Forma.Atual}");

			// (b) O AMIGO CAI, e o poder esta ENTRE os dois precos: a raiva acende, a forma NAO vem
			f.BP = entreOsPrecos1;
			AoPerderALuta(amigo, algoz, morreu: false);
			UmTique();
			RecusaForma entreOsDois = pl.Forma.Avaliar("ssj1", f.BP, 1, false, Perfil(pl));
			Checa("**(b) o amigo so CAI e o poder esta entre a porta e 1,2x ela: com a raiva comum acesa, ele NAO se transforma**",
				  Nivel() == NivelDeRaiva.Comum && pl.Forma.NaBase && entreOsDois == RecusaForma.SemPoderPraRaivaComum,
				  $"{Nivel()}, forma {pl.Forma.Atual}, {entreOsDois}");
			List<string> aoApertarC = Ouvir(() => Transformar(pl, subir: true));
			Checa("...e a tecla C diz o porque sem numero nenhum: a raiva dele pede mais poder -- ou uma dor maior",
				  pl.Forma.NaBase && Disse(aoApertarC, "pede mais poder") && Disse(aoApertarC, "dor maior")
				  && !aoApertarC.Any(a => a.Any(char.IsDigit)), Ultimos(aoApertarC));

			// (c) o MESMO corpo, o MESMO poder, e o amigo MORRE: a furia extrema entra com o BP menor
			AoPerderALuta(amigo, algoz, morreu: true);
			UmTique();
			Checa("**(c) o MESMO poder, e o amigo MORRE: a furia extrema o transforma -- ela entra com o BP MENOR**",
				  pl.Forma.Atual == "ssj1" && Cenas() == 0, $"forma {pl.Forma.Atual}, {Cenas()} cena(s) de furia");

			// (d) O AMIGO CAI, com o poder que a raiva comum cobra: ele VIRA SOZINHO, pela estreia -- e nada do que e da furia extrema acontece
			ParaABase(esquecerAsFormas: true);
			EmPaz();
			KiEm(0.30);
			f.BP = precoComum1 * 1.01;
			falas = Ouvir(() => { AoPerderALuta(amigo, algoz, morreu: false); UmTique(); });
			Checa("**(d) o amigo so CAI e o poder paga 1,2x a porta: ele VIRA SUPER SAIYAJIN SOZINHO, com a raiva COMUM**",
				  pl.Forma.Atual == "ssj1" && pl.Forma.Despertou("ssj1") && Nivel() == NivelDeRaiva.Comum,
				  $"forma {pl.Forma.Atual}, {Nivel()}");
			Checa("...pela ESTREIA de sempre: ele le 'VOCE DESPERTA', e a cinematica que prende o corpo e a DA TRANSFORMACAO",
				  Disse(falas, "VOCE DESPERTA") && presoPeloSsj1 > 0 && Math.Abs(pl.CenaSegundos - presoPeloSsj1) < 0.2,
				  $"preso {pl.CenaSegundos:0.#}s de {presoPeloSsj1:0.#}s | {Ultimos(falas)}");
			Checa("**...SEM cinematica de raiva (e sem o tema dela) e SEM salto de Ki**: o tanque esta em 100% -- o cheio de toda estreia --, e nao no teto de 140%",
				  Cenas() == 0 && Math.Abs(Razao() - 1.0) < 0.01, $"{Cenas()} cena(s) de furia, Ki {Razao() * 100:0.#}%");
			double comumAgora = 100 + Folga() / RaivaEmDegraus.DivisorDaRaivaComum;
			Checa("...e a raiva dele continua valendo o numero da COMUM (100 + folga/1,30), e nao o topo",
				  Math.Abs(f.Anger - comumAgora) < 0.5 && f.Anger < f.MaxAnger - 0.5,
				  $"Anger {f.Anger:0.##}; comum {comumAgora:0.##}; topo {f.MaxAnger:0.##}");

			// (e) a SUBIDA de hoje vale pra raiva comum: na BASE, com o Super Saiyajin ja despertado, o 2 vem pelo preco DELE
			NaBaseComOSsj1JaDespertado();
			EmPaz();
			KiEm(0.30);
			double precoComum2 = portaSsj2 * Catalogo.AgioComumDoSaiyajin;
			f.BP = (portaSsj2 + precoComum2) / 2;
			List<(string De, string Para, DegrauDeCena Degrau)> semOPrecoDoDois =
				AnunciosDe(() => { AoPerderALuta(amigo, algoz, morreu: false); UmTique(); });
			bool ficouNaBase = pl.Forma.NaBase && semOPrecoDoDois.Count == 0 && !pl.Forma.Despertou("ssj2");
			EmPaz();
			f.BP = precoComum2 * 1.01;
			List<(string De, string Para, DegrauDeCena Degrau)> subidaComum =
				AnunciosDe(() => { AoPerderALuta(amigo, algoz, morreu: false); UmTique(); });
			Checa("(e) na BASE, com o Super Saiyajin ja dele, o amigo CAI: entre os dois precos do 2 nada se move; com 1,2x a porta do 2 ele sobe pelo 1 (sem cena) e ESTREIA o 2 -- a subida vale pras duas raivas",
				  ficouNaBase && pl.Forma.Atual == "ssj2" && subidaComum.Count == 2
				  && subidaComum[0] is { De: "base", Para: "ssj1", Degrau: DegrauDeCena.Nenhuma }
				  && subidaComum[1] is { De: "ssj1", Para: "ssj2", Degrau: DegrauDeCena.Estreia } && Cenas() == 0,
				  $"entre os precos: {(ficouNaBase ? "ficou na base" : $"MEXEU ({Lista(semOPrecoDoDois)})")}; com o preco: {Lista(subidaComum)}");

			// ---- contra-exemplo: a comum pelo preco da extrema ----
			ParaABase(esquecerAsFormas: true);
			EmPaz();
			KiEm(0.30);
			f.BP = entreOsPrecos1;
			EstadoDeForma.RaivaComumSemAgioDeTeste = true;
			try
			{
				AoPerderALuta(amigo, algoz, morreu: false);
				UmTique();
				Checa("(defeito injetado: a raiva comum pelo preco da extrema) ele VIRA com o poder entre os dois precos -- a checagem (b) REPROVA",
					  pl.Forma.Atual == "ssj1", $"forma {pl.Forma.Atual}");
			}
			finally { EstadoDeForma.RaivaComumSemAgioDeTeste = false; }

			// ---- contra-exemplo: a comum sem transformar (a regra de antes) ----
			ParaABase(esquecerAsFormas: true);
			EmPaz();
			KiEm(0.30);
			f.BP = precoComum1 * 1.01;
			Catalogo.SoAFuriaAbreOTroncoDeTeste = true;
			try
			{
				AoPerderALuta(amigo, algoz, morreu: false);
				UmTique();
				Checa("(defeito injetado: a raiva comum nao abre o tronco -- a regra ate 2026-10-11) ele FICA NA BASE com o preco pago -- a checagem (d) REPROVA",
					  pl.Forma.NaBase && !pl.Forma.Despertou("ssj1"), $"forma {pl.Forma.Atual}");
			}
			finally { Catalogo.SoAFuriaAbreOTroncoDeTeste = false; }

			ParaABase(esquecerAsFormas: true);
			EmPaz();
			f.BP = 1;

			// ============================ 12. (2026-10-18) O AMIGO QUE DERRUBA O AMIGO, E O GRITO ============================
			// Ver `GameServer.RaivaGritoTeste.cs`.
			OAmigoQueDerrubaEOGrito(pl, amigo, Checa, forjados);

			// ============================ 13. (2026-10-18) A FURIA AO ACHAR O CORPO DO AMIGO ============================
			// Ver `GameServer.RaivaDoCorpoTeste.cs`.
			AFuriaAoAcharOCorpo(pl, Checa, forjados);
		}
		catch (Exception e)
		{
			falhou++;
			GD.PrintErr($"[raiva]   FALHA a bancada rodou inteira (estourou: {e.Message})   {e.StackTrace}");
		}
		finally
		{
			RaivaEmDegraus.NocauteNoTopoDeTeste = false;
			RaivaEmDegraus.SemSaltoDeKiDeTeste = false;
			RaivaEmDegraus.SaltoNaRaivaComumDeTeste = false;
			CenaSoSaiDaCalmaDeTeste = false;
			FuriaProPlanetaInteiroDeTeste = false;
			FuriaCedeAoGestoDeTeste = false;
			MeditarNaoAcalmaDeTeste = false;
			SubidaPelaRaiva.NaoSobeDegrausDeTeste = false;
			SubidaPelaRaiva.PraBaixoDeTeste = false;
			EstadoDeForma.RaivaComumSemAgioDeTeste = false;
			Catalogo.SoAFuriaAbreOTroncoDeTeste = false;
			EscutaDeFurias = furiasDeFora;
			EscutaDeAnuncios = anunciosDeFora;
			EscutaDeQuemViuAFuria = null;
			EscutaDeAvisos = null;

			foreach (ServerPlayer c in forjados)
			{
				pl.Social.Conhecidos.Remove(c.Assinatura);
				RemoverNpc(c);
			}

			f.MeditateGivesKiRegen = unlockedAntes;
			f.canPower = controleAntes;
			f.fd_ki_locked = travaAntes;
			// a escada do host volta pra onde estava: a forma, o que ele ja tinha despertado e a cena
			f.KO = caidoAntes;
			pl.Forma.Entrar(Catalogo.IdBase);
			pl.Forma.Liberadas.Clear();
			pl.Forma.Liberadas.UnionWith(liberadasAntes);
			pl.Forma.EstreiaVista.Clear();
			pl.Forma.EstreiaVista.UnionWith(estreiasAntes);
			if (formaAntes != Catalogo.IdBase) pl.Forma.Entrar(formaAntes);
			pl.CenaSegundos = cenaSegundosAntes;
			pl.Forma.Maestria.Por("ssj1", maestriaDoSsj1Antes);
			RaivaNaoTransformaDeTeste = false;
			FuriaNaoCedeACenaDeTeste = false;
			// o que a secao 8 mexeu: ele volta a fazer o que fazia (sem pacote: o cliente nunca soube da bancada)
			pl.Voando = voavaAntes;
			SoltarAAncoraDaAtividade(pl, avisarOCliente: false);
			f.train = treinavaAntes;
			f.med = meditavaAntes;
			// ...e os marcos de ganho que a secao 7 pagou pelo funil de producao
			f.bp_milestones_done.Clear();
			f.bp_milestones_done.UnionWith(marcosAntes);
			f.bp_milestone_mult = ganhoDosMarcosAntes;
			f.powerupcap = capAntes;
			f.Class = classeAntes;
			f.BP = bpAntes;
			f.Ki = kiAntes;
			f.overcharge = overAntes;
			pl.FuriaExtremaAte = extremaAntes;
			pl.RaivaComumAte = comumAntes;
			pl.FuriaCenaAte = cenaAntes;
			f.Statify();
			AplicarForma(pl);
			ProjetarRaiva(pl);   // a raiva do host volta a ser a das janelas dele (o numero e derivado, nao guardado)
			TickDaCarga(pl, 1 / 30.0);   // a aura de excesso apaga pelo caminho dela, agora que o Ki voltou
			if (entrouPelaBancada) daZona.Remove(pl);

			GD.Print($"[raiva] ================ {ok} OK, {falhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// O PALCO DA BANCADA DE JANELA (`--diagraiva`, `Client/RoboDaRaiva.cs`)
	// =====================================================================

	/// <summary>Os corpos que o palco da `--diagraiva` pos no mundo (o amigo e o algoz). Vazio fora dela.</summary>
	private readonly List<ServerPlayer> _palcoDaRaiva = [];

	/// <summary>
	/// SO PRA BANCADA DE JANELA: poe um AMIGO declarado e um ALGOZ ao lado do host, da ao host o controle de ki com
	/// o tanque em 20% e o poe SEM poder pra forma nenhuma (a furia nao vira transformacao: a cena e dela). Tudo o
	/// que acontece depois -- a queda, a morte, a cena, o tema, o salto -- e o caminho de producao.
	/// </summary>
	internal (int Amigo, int Algoz)? PalcoDaRaivaDeTeste(int hostId)
	{
		if (!_players.TryGetValue(hostId, out ServerPlayer? host)) return null;
		LimparPalcoDaRaivaDeTeste(hostId);

		// A CARA DOS DOIS e a do host (a raca junto: o corpo da aparencia e um indice na lista da raca), com o
		// amigo careca e o algoz de roupa roxa -- so pra a foto dizer quem e quem.
		Jandirus.Core.Appearance.Appearance caraDoAmigo = host.Visual.Copiar();
		caraDoAmigo.Cabelo = "Bald";
		Jandirus.Core.Appearance.Appearance caraDoAlgoz = host.Visual.Copiar();
		caraDoAlgoz.Roupa = [.. caraDoAlgoz.Roupa.Select(p => p with { Cor = new Jandirus.Core.Appearance.Rgb(150, 60, 190) })];

		ServerPlayer amigo = ForjarPessoaDaRaiva(host, 11, "Kuririn", 2, host.Race, caraDoAmigo);
		ServerPlayer algoz = ForjarPessoaDaRaiva(host, 12, "Freeza", 3.5f, host.Race, caraDoAlgoz);
		_palcoDaRaiva.Add(amigo);
		_palcoDaRaiva.Add(algoz);
		DeclararDeTeste(host, amigo, Relacao.MuitoBom);

		// A CHAMA DO HOST E O AZUL-CLARO DO KI, e nao a cor sorteada: a cor pessoal sai do nome e da hora em que o
		// personagem foi criado (`CorDeAura.De`), ou seja muda a cada rodada -- e a regua de "nada vermelho na cena"
		// da `--diagraiva` precisa de uma chama que nao seja vermelha por sorteio.
		host.Visual.CorAura = new Jandirus.Core.Appearance.Rgb(158, 204, 255);
		TrocarAparencias(host);

		// (a classe do tronco comum, e nao a sorteada: o passo da transformacao do robo e o Super Saiyajin)
		host.Ficha.Class = "Normal";
		host.Ficha.MeditateGivesKiRegen = 1;
		host.Ficha.canPower = 1;
		host.Ficha.BP = 1;
		AcalmarDeTeste(hostId, kiEm: 0.2);
		return (amigo.Id, algoz.Id);
	}

	/// <summary>SO PRA BANCADA DE JANELA: fecha as janelas de raiva e a recarga da cena do host, e poe o Ki dele nesta razao do tanque.</summary>
	internal void AcalmarDeTeste(int hostId, double kiEm)
	{
		if (!_players.TryGetValue(hostId, out ServerPlayer? host)) return;
		host.FuriaExtremaAte = 0;
		host.RaivaComumAte = 0;
		host.FuriaCenaAte = 0;
		host.Ficha.Ki = host.Ficha.MaxKi * kiEm;
		host.Ficha.overcharge = false;
		RepercutirPoder(host);
		MandarFicha(host);
	}

	/// <summary>
	/// SO PRA BANCADA DE JANELA: o amigo do palco CAI (fica nocauteado no chao, de verdade) ou MORRE nas maos do
	/// algoz -- pelo funil de producao da derrota. A morte nao tira o corpo forjado do mundo (ele continua caido,
	/// pra foto): o que se mede e o que ela causa em quem assiste.
	/// </summary>
	internal bool AbaterOAmigoDeTeste(bool morreu)
	{
		if (_palcoDaRaiva.Count < 2) return false;
		ServerPlayer amigo = _palcoDaRaiva[0], algoz = _palcoDaRaiva[1];
		if (!amigo.Ficha.KO) amigo.Combate.Nocautear(MeleeResolver.TetoDoNocaute, porVital: true);
		AoPerderALuta(amigo, algoz, morreu);
		return true;
	}

	/// <summary>SO PRA BANCADA DE JANELA: o que o SERVIDOR diz da raiva e do Ki do host (degrau, numero, razao de Ki, teto de carga).</summary>
	internal (NivelDeRaiva Nivel, double Raiva, double RazaoDeKi, double Teto)? RaivaDeTeste(int hostId) =>
		_players.TryGetValue(hostId, out ServerPlayer? host)
			? (NivelDaRaivaDe(host), host.Ficha.Anger, host.Ficha.Ki / Math.Max(host.Ficha.MaxKi, 1e-9), CargaDeKi.TetoEmRazao(host.Ficha))
			: null;

	/// <summary>
	/// SO PRA BANCADA DE JANELA: da ao host o poder da porta pessoal desta forma -- e NAO o do degrau seguinte, pra
	/// a raiva despertar UMA forma. O Ki fica onde esta (o que se quer ver e o salto).
	/// </summary>
	internal void DarPoderPraFormaDeTeste(int hostId, string forma, string seguinte)
	{
		if (!_players.TryGetValue(hostId, out ServerPlayer? host)) return;
		double porta = PortaDeTeste(host, forma), proxima = PortaDeTeste(host, seguinte);
		host.Ficha.BP = proxima > porta ? Math.Sqrt(porta * proxima) : porta * 1.01;
		RepercutirPoder(host);
		MandarFicha(host);
	}

	/// <summary>SO PRA BANCADA DE JANELA: em que forma o SERVIDOR diz que o host esta, e se ele ja a tinha despertado.</summary>
	internal (string Forma, bool Despertou)? FormaDeTeste(int hostId, string id) =>
		_players.TryGetValue(hostId, out ServerPlayer? host) ? (host.Forma.Atual, host.Forma.Despertou(id)) : null;

	/// <summary>
	/// SO PRA BANCADA DE JANELA: o host senta pra meditar (ou se levanta) pelo `DefinirAtividade` -- o que o pacote
	/// da tecla M chama, com a conferencia do servidor. Devolve se o servidor o deixou meditando.
	/// </summary>
	internal bool MeditarDeTeste(int hostId, bool sentar)
	{
		if (!_players.TryGetValue(hostId, out ServerPlayer? host)) return false;
		DefinirAtividade(host, sentar ? Jandirus.Net.Protocol.Activity.Meditando : Jandirus.Net.Protocol.Activity.Parado);
		return host.Ficha.med;
	}

	/// <summary>
	/// SO PRA BANCADA DE JANELA: da ou tira do host o CONTROLE de ki (`canPower`, o que a skill de controle escreve).
	/// Sem ele a furia extrema nao salta o Ki -- e sem Ki acima do tanque nao ha aura nenhuma.
	/// </summary>
	internal void ControleDeKiDeTeste(int hostId, bool tem)
	{
		if (_players.TryGetValue(hostId, out ServerPlayer? host)) host.Ficha.canPower = tem ? 1 : 0;
	}

	/// <summary>
	/// SO PRA BANCADA DE JANELA: o host volta pra BASE pelo recuo de producao (o mesmo da tecla X), com a cena que
	/// ainda estivesse prendendo o corpo dada por encerrada. As formas que ele ja despertou continuam dele.
	/// </summary>
	internal void VoltarPraBaseDeTeste(int hostId)
	{
		if (!_players.TryGetValue(hostId, out ServerPlayer? host)) return;
		host.CenaSegundos = 0;
		for (int i = 0; i < 8 && !host.Forma.NaBase; i++) Transformar(host, subir: false);
	}

	/// <summary>
	/// SO PRA BANCADA DE JANELA: o host volta pra BASE e deixa de ter forma despertada -- como quem nunca virou nada.
	/// E o palco do passo em que a raiva COMUM estreia o Super Saiyajin (a estreia so existe pra quem nao a viu).
	/// </summary>
	internal void EsquecerAsFormasDeTeste(int hostId)
	{
		if (!_players.TryGetValue(hostId, out ServerPlayer? host)) return;
		VoltarPraBaseDeTeste(hostId);
		host.Forma.Liberadas.Clear();
		host.Forma.EstreiaVista.Clear();
		AplicarForma(host);
	}

	/// <summary>
	/// SO PRA BANCADA DE JANELA: da ao host o poder ENTRE a porta desta forma e o preco que a raiva comum cobra por
	/// ela (`noPreco` falso) ou logo acima desse preco (`noPreco` verdadeiro) -- `FormaDef.AgioNaRaivaComum`. Devolve
	/// quantas vezes a porta pessoal o poder ficou valendo (so a bancada le o numero; ele nao vai pra tela).
	/// </summary>
	internal double DarPoderPraRaivaComumDeTeste(int hostId, string forma, bool noPreco)
	{
		if (!_players.TryGetValue(hostId, out ServerPlayer? host) || Catalogo.Def(forma) is not { } d) return 0;
		double porta = PortaDeTeste(host, forma), preco = porta * d.AgioNaRaivaComum;
		host.Ficha.BP = noPreco ? preco * 1.02 : (porta + preco) / 2;
		RepercutirPoder(host);
		MandarFicha(host);
		return porta > 0 ? host.Ficha.BP / porta : 0;
	}

	/// <summary>SO PRA BANCADA DE JANELA: tira o palco do mundo e esquece o amigo forjado na lista do host.</summary>
	internal void LimparPalcoDaRaivaDeTeste(int hostId)
	{
		_players.TryGetValue(hostId, out ServerPlayer? host);
		foreach (ServerPlayer c in _palcoDaRaiva)
		{
			host?.Social.Conhecidos.Remove(c.Assinatura);
			RemoverNpc(c);
		}
		_palcoDaRaiva.Clear();
	}
}
