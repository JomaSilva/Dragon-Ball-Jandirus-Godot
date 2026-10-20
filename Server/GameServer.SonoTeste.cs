using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Races;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--sonoteste` -- O SLEEP DO MAJIN (dono, 2026-10-18): *"a skill de regeneraçao mude o nome pra sleep, e o personagem ficaria 5
/// segundos parados, ao qual ao final ele regeneraria grande parte do seu hp dos membros, mas se for interrompido nesses 5s nada acontece e
/// tem um cooldown de 1 minuto mesmo cancelando, regeneração passiva dos majins mesmo em combate esta mt boa n precisa mexer nela"*.
///
/// Um cliente cru entra pela porta (o recado do login e de uma pessoa), e cada familia mede pela porta de PRODUCAO -- a carga do catalogo,
/// o `PrepararSkills` do login, o `UsarHabilidade`, o tique do sono:
///   C. O CATALOGO -- o Sleep existe com os numeros da `Regenerate` (custo, tier, o gene `Regeneration`); a arvore do Majin o pendura NO
///      LUGAR dela, e as outras cinco arvores continuam com a `Regenerate`; o verb e tecnica viva. Contra-exemplo: o catalogo carregado sem o
///      Sleep (`SonoDoMajin.SemOSonoDeTeste`).
///   L. O LOGIN -- o Majin que tinha a `Regenerate` comprada sai com o Sleep comprado, e com o recado; o Namekuseijin, nao.
///   S. O SONO INTEIRO -- a ocupacao e o recado; nos 5 s cada parte ferida se refaz em 60%, a decepada nao volta; a recarga.
///   D. (2026-10-19) DORME EM PE -- a pose de quem esta parado, pra onde olhava, sem o bit `Deitado`, no corpo e no snapshot; no ar, a de quem
///      paira. Contra-exemplo: o corpo deitado no quadro de nocaute, o jogo da 0.4.1 (`SonoDeitadoDeTeste`).
///   E. (2026-10-19) ESMAGADO NAO DORME -- o peso ou a gravidade acima do limite recusam o sono, com o aviso do que afrouxar e sem gastar a
///      recarga nem o Ki; e o esmagamento que comeca no meio do sono acorda. Contra-exemplo: o peso deixa de barrar (`SonoSobOPesoDeTeste`).
///   K. (2026-10-19) O KI ACIMA DE 100% VOLTA PRA 100% AO DORMIR -- e com 100% ou menos nada muda. Contra-exemplo: dormir nao mexe no Ki
///      (`SonoGuardaASobrecargaDeTeste`).
///   I. O QUE ACORDA -- andar, sair do lugar, apanhar, o golpe sem dano, apertar outra habilidade, erguer a guarda, cair: nada se refaz, e a
///      recarga de 1 min corre do mesmo jeito. Contra-exemplo: andar deixa de acordar (`SonoSemInterrupcaoDeTeste`).
///
/// O RELOGIO E PULADO, e so ele: a bancada roda dentro de uma chamada (o servidor nao tica no meio), entao "os 5 s passaram" e o comeco do
/// sono empurrado 5 s pra tras. Os 5 s de relogio de verdade, na tela, sao do robo `--diagsono`.
/// </summary>
public sealed partial class GameServer
{
	private const string ContaDaBancadaDoSono = "bancada_sono_ag";

	private bool _sonoRodando, _sonoJaRodou;
	private int _sonoOk, _sonoFalhou;

	private void AfirmarSono(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _sonoOk++; GD.Print($"[sono]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _sonoFalhou++; GD.PrintErr($"[sono]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDoSono(int porta)
	{
		if (_sonoRodando || _sonoJaRodou) return;
		_sonoRodando = _sonoJaRodou = true;
		_sonoOk = _sonoFalhou = 0;
		GD.Print("[sono] ================ O SLEEP DO MAJIN (dono, 2026-10-18) ================");

		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		try
		{
			ServerPlayer? pl = mesa.EntraNoMundo(cli, ContaDaBancadaDoSono, "Dorminhoco");
			AfirmarSono("(montagem) o cliente entra pela porta e e jogador, e ha catalogo de skills", pl != null && EhJogador(pl) && _skills != null);
			if (pl == null || _skills == null || pl.Combate == null) return;

			OCatalogoDoSono();
			OLoginDoSono(pl);
			OSonoInteiro(pl);
			ODormirEmPe(pl);
			OEsmagadoNaoDorme(pl);
			OKiAoDormir(pl);
			OQueAcorda(pl);
		}
		catch (Exception e)
		{
			AfirmarSono("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			SonoDoMajin.SemOSonoDeTeste = false;
			SonoSemInterrupcaoDeTeste = false;
			SonoDeitadoDeTeste = false;
			SonoSobOPesoDeTeste = false;
			SonoGuardaASobrecargaDeTeste = false;
			LimparTudoDaBancada();
			mesa.Fecha();
			if (_store != null)
				try
				{
					string caminho = System.IO.Path.Combine(_store.Pasta, ContaDaBancadaDoSono + ".json");
					if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
				}
				catch (Exception e) { GD.PushWarning($"[sono] nao apaguei '{ContaDaBancadaDoSono}': {e.Message}"); }
			_sonoRodando = false;
			GD.Print($"[sono] ================ {_sonoOk} OK, {_sonoFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// C) O CATALOGO
	// =====================================================================
	private void OCatalogoDoSono()
	{
		GD.Print("[sono] -- C) o catalogo: o Sleep no lugar da Regenerate, so na arvore do Majin --");
		Skill? sono = _skills!.Get(SonoDoMajin.Path), regen = _skills.Get(SonoDoMajin.Regenerate), arvore = _skills.Get(SonoDoMajin.ArvoreDoMajin);
		AfirmarSono("o SLEEP existe, com os numeros da Regenerate (custo, tier, o gene `Regeneration` -- a regeneracao passiva nao muda) e o verb `Sleep`",
				  sono != null && regen != null && sono.Nome == "Sleep" && sono.Custo == regen.Custo && sono.Tier == regen.Tier
				  && regen.Genes.ContainsKey("Regeneration") && sono.Genes.GetValueOrDefault("Regeneration") == regen.Genes["Regeneration"]
				  && sono.Verbos.SequenceEqual([SonoDoMajin.Verbo]),
				  $"custo {sono?.Custo}/{regen?.Custo}; tier {sono?.Tier}/{regen?.Tier}; gene {sono?.Genes.GetValueOrDefault("Regeneration")}/{regen?.Genes.GetValueOrDefault("Regeneration")}; verbos {string.Join(",", sono?.Verbos ?? [])}");
		AfirmarSono("a arvore `Majin Racials` pendura o Sleep NO LUGAR da Regenerate",
				  arvore != null && arvore.Galhos.Contains(SonoDoMajin.Path) && !arvore.Galhos.Contains(SonoDoMajin.Regenerate, StringComparer.OrdinalIgnoreCase),
				  string.Join(", ", arvore?.Galhos ?? []));
		string[] outras = [.. _skills.Arvores.Where(a => a.Path != SonoDoMajin.ArvoreDoMajin
													  && a.Galhos.Contains(SonoDoMajin.Regenerate, StringComparer.OrdinalIgnoreCase)).Select(a => a.Nome)];
		AfirmarSono("...e as OUTRAS CINCO arvores com a Regenerate continuam com ela (Alien, Arlian, Bio-Android, meio-Saiyajin, Heran): o pedido e do Majin",
				  outras.Length == 5 && !_skills.Arvores.Any(a => a.Path != SonoDoMajin.ArvoreDoMajin && a.Galhos.Contains(SonoDoMajin.Path)),
				  string.Join(", ", outras));
		AfirmarSono("o verb `Sleep` e tecnica viva, com a ficha portada (o botao sai sozinho do livro)",
				  Tecnicas.Vivas.Contains(SonoDoMajin.Verbo, StringComparer.OrdinalIgnoreCase) && Tecnicas.Get(SonoDoMajin.Verbo) is { Modo: not Modo.NaoPortada });

		SonoDoMajin.SemOSonoDeTeste = true;
		SkillCatalog semOSono;
		try
		{
			semOSono = SkillCatalog.Parse(Godot.FileAccess.GetFileAsString("res://Assets/Data/skills.json"),
										   Godot.FileAccess.GetFileAsString("res://Assets/Data/skilltrees.json"));
		}
		finally { SonoDoMajin.SemOSonoDeTeste = false; }
		AfirmarSono("(defeito injetado: o catalogo carregado sem o Sleep) a arvore do Majin volta a pendurar a Regenerate e o Sleep nao existe -- a regua de cima REPROVA",
				  semOSono.Get(SonoDoMajin.Path) == null && semOSono.Get(SonoDoMajin.ArvoreDoMajin) is { } velha
				  && velha.Galhos.Contains(SonoDoMajin.Regenerate, StringComparer.OrdinalIgnoreCase));
	}

	// =====================================================================
	// L) O LOGIN
	// =====================================================================
	private void OLoginDoSono(ServerPlayer pl)
	{
		GD.Print("[sono] -- L) o login: o Majin com a Regenerate sai com o Sleep --");
		string raca = pl.Race;
		CharacterSave ComARegenerate() => new()
		{
			Skills = [SonoDoMajin.Regenerate], SkillsCompradas = [SonoDoMajin.Regenerate], MarcosTotais = 5, MarcosLivres = 1,
		};
		try
		{
			pl.Race = SonoDoMajin.Raca;
			PrepararSkills(pl, ComARegenerate());
			List<string> falas = Ouvir(() => AvisarDasAposentadas(pl));
			AfirmarSono("o MAJIN que tinha a Regenerate COMPRADA sai do login com o Sleep comprado -- os marcos nao mudam (1 livre) -- e o recado diz o que mudou",
					  pl.Livro.Sabe(SonoDoMajin.Path) && !pl.Livro.Sabe(SonoDoMajin.Regenerate) && pl.Livro.FoiComprada(SonoDoMajin.Path)
					  && pl.Livro.MarcosLivres == 1 && Disse(falas, "agora se chama Sleep"),
					  $"livres {pl.Livro.MarcosLivres}; {Ultimos(falas)}");

			pl.Race = "Namekian";
			PrepararSkills(pl, ComARegenerate());
			List<string> doNamek = Ouvir(() => AvisarDasAposentadas(pl));
			AfirmarSono("...e o NAMEKUSEIJIN com a mesma skill sai com a Regenerate de sempre, sem recado nenhum",
					  pl.Livro.Sabe(SonoDoMajin.Regenerate) && !pl.Livro.Sabe(SonoDoMajin.Path) && !Disse(doNamek, "Sleep"), Ultimos(doNamek));

			pl.Race = SonoDoMajin.Raca;
			SonoDoMajin.SemOSonoDeTeste = true;
			try { PrepararSkills(pl, ComARegenerate()); }
			finally { SonoDoMajin.SemOSonoDeTeste = false; }
			AfirmarSono("(defeito injetado: sem o Sleep) o Majin sai do login com a Regenerate de antes -- a regua de cima REPROVA",
					  pl.Livro.Sabe(SonoDoMajin.Regenerate) && !pl.Livro.Sabe(SonoDoMajin.Path));
		}
		finally
		{
			pl.Race = raca;
			PrepararSkills(pl, null);
			pl.SonoTrocado = false;
		}
	}

	// =====================================================================
	// S) O SONO INTEIRO
	// =====================================================================
	private void OSonoInteiro(ServerPlayer pl)
	{
		GD.Print("[sono] -- S) os 5 s inteiros: cada parte ferida se refaz em 60%, e a recarga de 1 min --");
		CombatState c = pl.Combate!;
		ProntoParaDormir(pl);
		BodyPart mao = c.Corpo.Achar("Mao esquerda")!;
		try
		{
			foreach (BodyPart p in c.Corpo.Partes) p.Vida = p.VidaMax * 0.3;
			mao.Decepado = true;
			mao.Vida = 0;
			c.SincronizarVida();
			double[] antes = [.. c.Corpo.Partes.Select(p => p.Vida)];

			List<string> falas = ApertarEOuvir(pl, SonoDoMajin.Verbo);
			bool dormiu = pl.SonoDesde >= 0 && OcupacaoDe(pl) == Ocupacao.Dormindo;
			AfirmarSono("O SLEEP, pelo verb: o corpo DORME -- a ocupacao `Dormindo` (a tela desenha o \"z\"; a pose de pe e a familia D) -- e o servidor diz o que fazer",
					  dormiu && Disse(falas, "você dorme"), $"ocupacao {OcupacaoDe(pl)}; pose {pl.Pose(NowMs(), false)}; {Ultimos(falas)}");
			// (2026-10-19) A regua cravava o 17 como o ULTIMO do `enum` -- e quebrou no dia em que outro valor entrou depois dele. A pergunta de
			// verdade e a da `--v030teste`: o byte acima do maior do `enum` le Livre, nunca um estado inventado.
			AfirmarSono("...e o estado viaja no fio: o 17 (ocupacao estendida) volta igual do byte, e um valor acima do ultimo do `enum` le Livre",
					  CorpoOcupado.DeByte((byte)Ocupacao.Dormindo) == Ocupacao.Dormindo
					  && CorpoOcupado.DeByte((byte)((byte)Enum.GetValues<Ocupacao>().Max() + 1)) == Ocupacao.Livre);

			TickDosSonos();
			AfirmarSono("no meio do sono nada se refaz ainda (o tique so retrata)",
					  pl.SonoDesde >= 0 && c.Corpo.Partes.Select(p => p.Vida).SequenceEqual(antes));

			pl.SonoDesde -= (long)(SonoDoMajin.Segundos * 1000);   // os 5 s passaram (ver o cabecalho)
			List<string> doFim = Ouvir(TickDosSonos);
			bool refeitas = c.Corpo.Partes.Where(p => !p.Decepado).All(p => Math.Abs(p.Vida - p.VidaMax * 0.9) < 1e-6);
			AfirmarSono("**OS 5 S INTEIROS**: cada parte ferida se refaz em 60% da vida dela (de 30% pra 90%), a decepada NAO volta, e o corpo acorda refeito",
					  pl.SonoDesde < 0 && refeitas && mao.Decepado && mao.Vida == 0 && Disse(doFim, "o sono refez o seu corpo"),
					  $"partes {string.Join(", ", c.Corpo.Partes.Where(p => !p.Decepado).Select(p => $"{p.Nome} {p.Fracao:0.##}"))}; mao decepada {mao.Decepado}; {Ultimos(doFim)}");
			long recarga = pl.SonoLivreEm - NowMs();
			AfirmarSono("...e a recarga de 1 min fica armada", recarga is > 59_000 and <= 60_000, $"{recarga} ms");
			List<string> deNovo = ApertarEOuvir(pl, SonoDoMajin.Verbo);
			AfirmarSono("...e o Sleep apertado de novo ESPERA", pl.SonoDesde < 0 && Disse(deNovo, "ainda não quer dormir"), Ultimos(deNovo));
		}
		finally
		{
			mao.Decepado = false;
			mao.Vida = mao.VidaMax;
			c.SincronizarVida();
		}
	}

	// =====================================================================
	// D) DORME EM PE
	// =====================================================================
	/// <summary>
	/// (2026-10-19) QUEM DORME DORME EM PE -- o dono: *"ele ta deitando pra dormir na skill de regeneraçao dele, ele deveria dormir em pé"*. O
	/// sono nao mexe na pose: no chao, a de quem esta parado (`Normal`), olhando pra onde olhava e sem o bit `Deitado`; no ar, a de quem paira.
	/// A regua olha o corpo (`ServerPlayer.Pose`) e o fio (o snapshot que os outros recebem); o corpo local escolhe a pose dele pela mesma escada,
	/// e quem o fotografa e o robo `--diagsono`.
	/// </summary>
	private void ODormirEmPe(ServerPlayer pl)
	{
		GD.Print("[sono] -- D) dorme em pe: a pose de quem esta parado (no ar, pairando), pra onde olhava, sem o bit `Deitado` --");
		(Facing Olhar, bool Dormiu, Protocol.Pose Pose, Protocol.Pose NoFio, Facing Snapshot, bool DeitadoNoFio) Dormir(Facing olhar)
		{
			ProntoParaDormir(pl);
			pl.Facing = olhar;
			ApertarEOuvir(pl, SonoDoMajin.Verbo);
			EntityState e = EstadoDe(pl, NowMs());
			var r = (olhar, pl.SonoDesde >= 0, pl.Pose(NowMs(), false), e.Pose, (Facing)e.Facing, e.Deitado);
			ProntoParaDormir(pl);
			return r;
		}
		string Contar((Facing Olhar, bool Dormiu, Protocol.Pose Pose, Protocol.Pose NoFio, Facing Snapshot, bool DeitadoNoFio) x) =>
			$"olhando {x.Olhar}: {(x.Dormiu ? "dormiu" : "NAO dormiu")}, pose {x.Pose} (no fio {x.NoFio}), snapshot olhando {x.Snapshot}"
			+ (x.DeitadoNoFio ? " DEITADO" : " de pe");

		var certos = new[] { Facing.South, Facing.North, Facing.East, Facing.West }.Select(Dormir).ToList();
		AfirmarSono("**DORME EM PE**: olhando pro sul, pro norte, pro leste ou pro oeste, a pose e a de quem esta parado (`Normal`) -- no corpo e no fio "
					  + "--, olhando pra onde olhava e sem o bit `Deitado`: o sono nao mexe no corpo, e quem o mostra na tela e o \"z\"",
					  certos.All(x => x.Dormiu && x.Pose == Protocol.Pose.Normal && x.NoFio == Protocol.Pose.Normal && x.Snapshot == x.Olhar && !x.DeitadoNoFio),
					  string.Join("; ", certos.Select(Contar)));

		// NO AR: o voo e ESTADO do corpo (`Voando` e a altura), e a bancada o poe direto -- quem decola de verdade e outra bancada. Nenhum tique
		// roda no meio, entao nada desce nem acorda; o que se mede e so a pose de quem dorme pairando.
		bool voava = pl.Voando;
		float altura = pl.Altitude;
		(bool Dormiu, Protocol.Pose Pose, Protocol.Pose NoFio) noAr;
		try
		{
			ProntoParaDormir(pl);
			pl.Voando = true;
			pl.Altitude = 24;
			ApertarEOuvir(pl, SonoDoMajin.Verbo);
			noAr = (pl.SonoDesde >= 0, pl.Pose(NowMs(), false), EstadoDe(pl, NowMs()).Pose);
		}
		finally
		{
			ProntoParaDormir(pl);
			pl.Voando = voava;
			pl.Altitude = altura;
		}
		AfirmarSono("...e NO AR ele dorme PAIRANDO: a pose de voo, no corpo e no fio -- e nao a de nocaute no meio do ceu",
					  noAr.Dormiu && noAr.Pose == Protocol.Pose.Voando && noAr.NoFio == Protocol.Pose.Voando,
					  $"{(noAr.Dormiu ? "dormiu" : "NAO dormiu")}, pose {noAr.Pose} (no fio {noAr.NoFio})");

		SonoDeitadoDeTeste = true;
		(Facing Olhar, bool Dormiu, Protocol.Pose Pose, Protocol.Pose NoFio, Facing Snapshot, bool DeitadoNoFio) comODefeito;
		try { comODefeito = Dormir(Facing.South); }
		finally { SonoDeitadoDeTeste = false; }
		AfirmarSono("(defeito injetado: quem dorme deita no quadro de nocaute, o jogo da 0.4.1) a pose sai `Nocauteado`, no corpo e no fio -- e a regua "
					  + "de cima REPROVA",
					  comODefeito.Dormiu && comODefeito.Pose == Protocol.Pose.Nocauteado && comODefeito.NoFio == Protocol.Pose.Nocauteado, Contar(comODefeito));
	}

	// =====================================================================
	// E) ESMAGADO NAO DORME
	// =====================================================================
	/// <summary>
	/// (2026-10-19) O CORPO ESMAGADO PELO PESO OU PELA GRAVIDADE NAO DORME -- o dono: *"majin q esta sofrendo dano de peso ou gravidade o jogo n
	/// deveria deixa-lo dormir, dando um aviso"*. A pergunta e a do castigo (`EsmagadoAgora`, a do `TickDoEsmagamento`): a razao acima de 1. O
	/// aviso diz o que afrouxar (tirar os pesos, ou sair da gravidade), e a recusa nao gasta a recarga nem mexe no Ki. E o esmagamento que
	/// comeca no meio do sono (alguem sobe a maquina de gravidade) acorda, com o recado dele, antes mesmo do primeiro dano do castigo.
	///
	/// O ESMAGAMENTO E POSTO NA FICHA, que e onde o castigo o le: o `weight_ratio` (que o `WeightTick` escreveria a partir dos pesos vestidos) e
	/// o `gravmult` (a maquina de gravidade). Nenhum tique roda no meio, entao nada os reescreve.
	/// </summary>
	private void OEsmagadoNaoDorme(ServerPlayer pl)
	{
		GD.Print("[sono] -- E) esmagado pelo peso ou pela gravidade nao dorme: o aviso, sem gastar a recarga nem o Ki --");
		Fighter f = pl.Ficha;
		CombatState c = pl.Combate!;
		(double Grav, double Peso, double Ki) antes = (f.gravmult, f.weight_ratio, f.Ki);
		// O PESO: a razao do peso acima de 1 E acima da da gravidade (quem manda e o peso). A GRAVIDADE: a maquina tres vezes acima da maestria.
		void PeloPeso() { f.gravmult = 0; f.weight_ratio = 2 + Esmagamento.Gravidade(f) / Math.Max(f.GravMastered, 1); }
		void PelaGravidade() { f.weight_ratio = 0; f.gravmult = 3 * Math.Max(f.GravMastered, 1); }
		void Solto() { f.gravmult = antes.Grav; f.weight_ratio = antes.Peso; }
		string Razao() => $"razao {Esmagamento.Razao(f):0.##}, pelo peso {Esmagamento.PorPeso(f)}";
		try
		{
			AfirmarSono("(montagem) solto, o corpo NAO e esmagado -- a razao do castigo esta dentro do limite", !EsmagadoAgora(pl), Razao());

			ProntoParaDormir(pl);
			f.Ki = f.MaxKi * 1.5;
			PeloPeso();
			bool esmagadoPeloPeso = EsmagadoAgora(pl);
			List<string> peso = ApertarEOuvir(pl, SonoDoMajin.Verbo);
			AfirmarSono("**O PESO ESMAGANDO NAO DEIXA DORMIR**: o Sleep e recusado com o aviso do que afrouxar (tirar os pesos) -- e a recusa nao gasta a "
						  + "recarga nem mexe no Ki (os 150% continuam)",
						  esmagadoPeloPeso && pl.SonoDesde < 0 && Disse(peso, "PESO esmagando") && Disse(peso, "Tire os pesos") && pl.SonoLivreEm == 0
						  && Math.Abs(f.Ki - f.MaxKi * 1.5) < 1e-6,
						  $"{Razao()}; dormindo {pl.SonoDesde >= 0}; recarga {pl.SonoLivreEm}; Ki {f.Ki / f.MaxKi:P0}; {Ultimos(peso)}");

			ProntoParaDormir(pl);
			PelaGravidade();
			bool esmagadoPelaGravidade = EsmagadoAgora(pl) && !Esmagamento.PorPeso(f);
			List<string> grav = ApertarEOuvir(pl, SonoDoMajin.Verbo);
			AfirmarSono("**...E A GRAVIDADE ESMAGANDO TAMBEM NAO**: recusado com o aviso de sair dela, sem gastar a recarga",
						  esmagadoPelaGravidade && pl.SonoDesde < 0 && Disse(grav, "GRAVIDADE esmagando") && Disse(grav, "Saia daqui") && pl.SonoLivreEm == 0,
						  $"{Razao()}; dormindo {pl.SonoDesde >= 0}; recarga {pl.SonoLivreEm}; {Ultimos(grav)}");

			ProntoParaDormir(pl);
			Solto();
			ApertarEOuvir(pl, SonoDoMajin.Verbo);
			bool dormiuSolto = pl.SonoDesde >= 0;
			long golpes = c.GolpesQueFeriram;
			PelaGravidade();
			List<string> acordou = Ouvir(TickDosSonos);
			AfirmarSono("**O ESMAGAMENTO QUE COMECA NO MEIO DO SONO ACORDA** (alguem sobe a maquina de gravidade): o tique do sono o acorda com o recado da "
						  + "gravidade -- sem dano nenhum ainda (o castigo nem rodou), e a recarga de 1 min corre como em todo sono perdido",
						  dormiuSolto && pl.SonoDesde < 0 && Disse(acordou, "a gravidade esmaga o seu corpo e acorda") && Disse(acordou, "o sono se perdeu")
						  && c.GolpesQueFeriram == golpes && pl.SonoLivreEm - NowMs() > 59_000,
						  $"dormiu solto {dormiuSolto}; dormindo agora {pl.SonoDesde >= 0}; danos pelo funil {c.GolpesQueFeriram - golpes}; {Ultimos(acordou)}");

			// ---- o contra-exemplo ----
			ProntoParaDormir(pl);
			PeloPeso();
			SonoSobOPesoDeTeste = true;
			bool dormiuEsmagado, seguiuDormindo;
			try
			{
				ApertarEOuvir(pl, SonoDoMajin.Verbo);
				dormiuEsmagado = pl.SonoDesde >= 0;
				TickDosSonos();
				seguiuDormindo = pl.SonoDesde >= 0;
			}
			finally { SonoSobOPesoDeTeste = false; }
			AfirmarSono("(defeito injetado: o peso deixa de barrar o sono, o jogo da 0.4.1) o corpo esmagado DORME, e o tique do sono nao o acorda -- as "
						  + "reguas de cima REPROVAM",
						  dormiuEsmagado && seguiuDormindo, $"{Razao()}; dormiu {dormiuEsmagado}; seguiu dormindo {seguiuDormindo}");
		}
		finally
		{
			Solto();
			f.Ki = antes.Ki;
			ProntoParaDormir(pl);
		}
	}

	// =====================================================================
	// K) O KI ACIMA DE 100% VOLTA PRA 100%
	// =====================================================================
	/// <summary>
	/// (2026-10-19) AO DORMIR, O KI ACIMA DE 100% VOLTA PRA 100% -- o dono: *"ao dormir caso o ki dele esteja acima de 100% ele volta pra
	/// 100%"*. Com 100% ou menos nada muda (a mesma regra do nocaute, `CombatState.Nocautear`).
	/// </summary>
	private void OKiAoDormir(ServerPlayer pl)
	{
		GD.Print("[sono] -- K) ao dormir, o Ki acima de 100% volta pra 100% (com 100% ou menos, nada muda) --");
		Fighter f = pl.Ficha;
		double kiAntes = f.Ki;
		try
		{
			ProntoParaDormir(pl);
			f.Ki = f.MaxKi * 1.5;
			List<string> acima = ApertarEOuvir(pl, SonoDoMajin.Verbo);
			AfirmarSono("**O KI ACIMA DE 100% VOLTA PRA 100%**: quem dorme a 150% dorme a 100% -- e o servidor diz por que a barra caiu",
						  pl.SonoDesde >= 0 && Math.Abs(f.Ki - f.MaxKi) < 1e-6 && Disse(acima, "Ki acima de 100% se dispersa"),
						  $"Ki {f.Ki / f.MaxKi:P0} de {f.MaxKi:0.#}; {Ultimos(acima)}");

			ProntoParaDormir(pl);
			f.Ki = f.MaxKi * 0.6;
			List<string> abaixo = ApertarEOuvir(pl, SonoDoMajin.Verbo);
			AfirmarSono("...e com 100% ou menos NADA muda: quem dorme a 60% continua a 60%, sem recado de Ki",
						  pl.SonoDesde >= 0 && Math.Abs(f.Ki - f.MaxKi * 0.6) < 1e-6 && !Disse(abaixo, "Ki acima"),
						  $"Ki {f.Ki / f.MaxKi:P0}; {Ultimos(abaixo)}");

			ProntoParaDormir(pl);
			f.Ki = f.MaxKi * 1.5;
			SonoGuardaASobrecargaDeTeste = true;
			try { ApertarEOuvir(pl, SonoDoMajin.Verbo); }
			finally { SonoGuardaASobrecargaDeTeste = false; }
			AfirmarSono("(defeito injetado: dormir nao mexe no Ki, o jogo da 0.4.1) quem dorme a 150% continua a 150% -- a regua de cima REPROVA",
						  pl.SonoDesde >= 0 && f.Ki > f.MaxKi * 1.4, $"Ki {f.Ki / f.MaxKi:P0}");
		}
		finally
		{
			ProntoParaDormir(pl);
			f.Ki = kiAntes;
		}
	}

	// =====================================================================
	// I) O QUE ACORDA
	// =====================================================================
	private void OQueAcorda(ServerPlayer pl)
	{
		GD.Print("[sono] -- I) o que acorda: nada se refaz, e a recarga corre do mesmo jeito --");
		CombatState c = pl.Combate!;
		(string Nome, Action Incomodo, Action Desfazer)[] incomodos =
		[
			("andar (o pedido de passo)", () => pl.Moving = true, () => pl.Moving = false),
			("sair do lugar (um empurrao de 3 px)", () => pl.Pos += new Vec2(3, 0), () => pl.Pos -= new Vec2(3, 0)),
			("apanhar (um dano pelo funil unico, `CombatState.Ferir`)", () => c.Ferir(c.Corpo.Partes.First(p => !p.Decepado), 1, letal: false), () => { }),
			("um golpe que acerta SEM dano (o relogio de luta renovado)", () => c.EntrarEmCombate(), () => { }),
			("apertar outra habilidade (o voo)", () => UsarHabilidade(pl, "voar"), () => { if (pl.Voando) AlternarVoo(pl); }),
			("erguer a guarda (ALT)", () => ErguerAGuarda(pl, true), () => ErguerAGuarda(pl, false)),
			("cair (o nocaute)", () => c.Nocautear(3), () => c.Levantar()),
		];
		foreach ((string nome, Action incomodo, Action desfazer) in incomodos)
		{
			ProntoParaDormir(pl);
			foreach (BodyPart p in c.Corpo.Partes) p.Vida = p.VidaMax * 0.3;
			c.SincronizarVida();
			UsarHabilidade(pl, SonoDoMajin.Verbo);
			bool dormiu = pl.SonoDesde >= 0;
			List<string> falas = Ouvir(() => { incomodo(); TickDosSonos(); });
			bool acordou = pl.SonoDesde < 0;
			// MESMO QUE OS 5 S "PASSEM" AGORA, nada se refaz: o sono ja se perdeu
			if (pl.SonoDesde >= 0) pl.SonoDesde -= (long)(SonoDoMajin.Segundos * 1000);
			TickDosSonos();
			bool semCura = c.Corpo.Partes.All(p => p.Vida <= p.VidaMax * 0.3 + 1e-6);
			bool recarga = pl.SonoLivreEm - NowMs() > 59_000;
			AfirmarSono($"**{nome.ToUpperInvariant()}** ACORDA: o sono se perde (nada se refaz) e a recarga de 1 min corre do mesmo jeito",
					  dormiu && acordou && semCura && recarga && Disse(falas, "o sono se perdeu"),
					  $"dormiu {dormiu}; acordou {acordou}; sem cura {semCura}; recarga {recarga}; {Ultimos(falas)}");
			desfazer();
		}

		// ---- o contra-exemplo ----
		ProntoParaDormir(pl);
		foreach (BodyPart p in c.Corpo.Partes) p.Vida = p.VidaMax * 0.3;
		c.SincronizarVida();
		SonoSemInterrupcaoDeTeste = true;
		bool refezAndando;
		try
		{
			UsarHabilidade(pl, SonoDoMajin.Verbo);
			pl.Moving = true;
			TickDosSonos();
			pl.SonoDesde -= (long)(SonoDoMajin.Segundos * 1000);
			TickDosSonos();
			refezAndando = c.Corpo.Partes.All(p => p.Vida > p.VidaMax * 0.8);
		}
		finally
		{
			SonoSemInterrupcaoDeTeste = false;
			pl.Moving = false;
		}
		AfirmarSono("(defeito injetado: andar nao acorda) quem se mexe dormindo acorda REFEITO do mesmo jeito -- a regua de cima REPROVA", refezAndando);
		ProntoParaDormir(pl);
		foreach (BodyPart p in c.Corpo.Partes) p.Vida = p.VidaMax;
		c.SincronizarVida();
	}

	/// <summary>O corpo no ponto de dormir: de pe, sem guarda, sem golpe, parado, fora de luta, com o Sleep no livro e a recarga livre.</summary>
	private void ProntoParaDormir(ServerPlayer pl)
	{
		CombatState c = pl.Combate!;
		if (pl.Ficha.KO) c.Levantar();
		if (c.Bloqueando) ErguerAGuarda(pl, false);
		pl.SonoDesde = -1;
		pl.SonoLivreEm = 0;
		pl.Moving = false;
		pl.AtaqueAte = 0;
		c.LutandoDeVerdade = 0;
		pl.Livro.Dar(SonoDoMajin.Path);
	}
}
