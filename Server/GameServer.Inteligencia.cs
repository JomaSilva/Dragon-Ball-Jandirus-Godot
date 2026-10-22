using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;

namespace Jandirus.Server;

/// <summary>
/// A INTELIGENCIA DA RACA NO SERVIDOR (2026-10-18): a migracao de quem ja existia, e a bancada `--inteligenciateste` -- a inteligencia da raca
/// (`InteligenciaDaRaca`), o nome Tsufurujin (`NomeDaRaca`) e o mainframe de androides em tech 70 (`CatalogoDeObras.TechDoMainframe`).
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// A INTELIGENCIA DA RACA, UMA VEZ: o personagem salvo antes de 2026-10-18 nasceu com o `techmod` 1 (o port nao lia a raca -- ver
	/// `InteligenciaDaRaca`), e ganha a diferenca agora, com o que ja tinha somado preservado (o desejo, o virus). Chamada no login, logo depois
	/// do save virar jogador; a versao vai pro disco no `DeJogador`, e a segunda entrada nao soma de novo.
	/// </summary>
	private void MigrarInteligencia(ServerPlayer pl)
	{
		if (pl.InteligenciaVersao >= CharacterSave.VersaoDaInteligencia || _racas == null) return;
		double antes = pl.Ficha.techmod;
		pl.Ficha.techmod += InteligenciaDaRaca.DaFicha(_racas, pl.Ficha) - 1;
		pl.InteligenciaVersao = CharacterSave.VersaoDaInteligencia;
		GD.Print($"[server] {pl.Name}: a inteligencia da raca entrou na ficha ({antes:0.##} -> {pl.Ficha.techmod:0.##})");
	}

	/// <summary>
	/// (2026-10-21) O "BATTLE POWER" QUE CADA RACA/CLASSE DAVA ANTES DO PEDIDO DO DONO -- so as tres que mudaram (*"deixa androide em 1.2 e majin
	/// corrupted em 1.3 e o majin normal em 1.0"*). A classe vazia e a do corpo, quando a classe nao tem "Battle Power" proprio.
	/// </summary>
	private static readonly Dictionary<(string Raca, string Classe), double> PoderDaRacaDeAntes = new()
	{
		[("Android", "")] = 2.0,
		[("Majin", "")] = 2.3,
		[("Majin", "Corrupted Majin")] = 2.5,
	};

	/// <summary>
	/// QUANTO A BASE DO `BPMod` MUDOU PRA ESTE GENOMA: por ancestral, a fracao dele vezes (o de agora - o de antes), so nas raca/classe da tabela
	/// <see cref="PoderDaRacaDeAntes"/>. O que a Saga do Majin e as skills somaram nao entra (nao mora no proto).
	/// </summary>
	internal static double DiferencaDoPoderDaRaca(RaceCatalog cat, Genome? g)
	{
		if (g == null) return 0;
		double d = 0;
		foreach ((string raca, double pct) in g.Ancestry)
		{
			if (!cat.Protos.TryGetValue(raca, out RaceProto? proto)) continue;
			bool daClasse = proto.ClassStats.TryGetValue(g.Class, out Dictionary<string, double>? doClasse) && doClasse.ContainsKey("Battle Power");
			if (!PoderDaRacaDeAntes.TryGetValue((raca, daClasse ? g.Class : ""), out double velho)) continue;
			double novo = daClasse ? doClasse!["Battle Power"] : proto.Stats.GetValueOrDefault("Battle Power", 1);
			d += (novo - velho) * pct / 100.0;
		}
		return d;
	}

	/// <summary>
	/// O PODER DE LUTA DA RACA, UMA VEZ (2026-10-21): a ficha salva antes guarda a base velha DENTRO do `BPMod`, e o login troca so a base
	/// (<see cref="DiferencaDoPoderDaRaca"/>). Chamada no login, logo depois do save virar jogador; a versao vai pro disco no `DeJogador`, e a
	/// segunda entrada nao troca de novo.
	/// </summary>
	private void MigrarPoderDaRaca(ServerPlayer pl)
	{
		if (pl.PoderDaRacaVersao >= CharacterSave.VersaoDoPoderDaRaca || _racas == null) return;
		double antes = pl.Ficha.BPMod;
		pl.Ficha.BPMod = Math.Max(0.1, pl.Ficha.BPMod + DiferencaDoPoderDaRaca(_racas, pl.Ficha.Genoma));
		pl.PoderDaRacaVersao = CharacterSave.VersaoDoPoderDaRaca;
		if (antes != pl.Ficha.BPMod)
			GD.Print($"[server] {pl.Name}: o poder de luta da raca mudou ({antes:0.##} -> {pl.Ficha.BPMod:0.##})");
	}

	// =====================================================================
	// A BANCADA `--inteligenciateste`
	// =====================================================================
	public void RodarBancadaDaInteligencia()
	{
		GD.Print("[inteligencia] ===== A INTELIGENCIA DA RACA, O TSUFURUJIN E O MAINFRAME EM TECH 70 (dono, 2026-10-18) =====");
		int ok = 0, falhou = 0;
		void Checa(string oque, bool passou, string detalhe = "")
		{
			if (passou) { ok++; GD.Print($"[inteligencia]   OK    {oque}"); }
			else { falhou++; GD.PrintErr($"[inteligencia]   FALHA {oque}   {detalhe}"); }
		}
		try
		{
			if (_racas == null || _obras == null) { Checa("(montagem) os catalogos de racas e de obras carregaram", false); return; }
			const string chave = InteligenciaDaRaca.Chave;

			// ---- 1. o catalogo ----
			double humano = _racas.Get("Human")!.Misc[chave], tsu = _racas.Get(InteligenciaDaRaca.Tsufurujin)!.Misc[chave];
			Checa("**O HUMANO TEM O DOBRO** (*\"dobre a inteligencia dos humanos\"*): os 3 do DM viram 6", humano == 6, $"{humano}");
			Checa("**O TSUFURUJIN TEM O DOBRO DO HUMANO** (*\"teria o dobro da inteligencia dos humanos\"*): 12", tsu == 12, $"{tsu}");
			RaceCatalog doDm;
			InteligenciaDaRaca.SemARegraDoDonoDeTeste = true;
			try { doDm = RaceCatalog.Parse(Godot.FileAccess.GetFileAsString("res://Assets/Data/races.json")); }
			finally { InteligenciaDaRaca.SemARegraDoDonoDeTeste = false; }
			Checa("   DEFEITO INJETADO (sem a regra do dono): o Humano e o Tsujin do DM, 3 e 7 -- a regua de cima reprovaria",
				  doDm.Get("Human")!.Misc[chave] == 3 && doDm.Get(InteligenciaDaRaca.Tsufurujin)!.Misc[chave] == 7,
				  $"{doDm.Get("Human")!.Misc[chave]} / {doDm.Get(InteligenciaDaRaca.Tsufurujin)!.Misc[chave]}");

			// ---- 2. a ficha nasce com a da raca ----
			var sorte = new Random(7);
			Fighter fh = Birth.Nascer(_racas, "Human", "", sorte, "Inteligente");
			Fighter ft = Birth.Nascer(_racas, InteligenciaDaRaca.Tsufurujin, "", sorte, "Genio");
			Fighter fy = Birth.Nascer(_racas, "Yardrat", "", sorte, "Distraido");
			Checa("**A FICHA NASCE COM A INTELIGENCIA DA RACA** (o `savant.techmod = misc_stats[\"Tech Modifier\"]` do DM, `Genetic_Datum.dm:302`): "
				  + "o Humano com 6, o Tsufurujin com 12 -- e o Yardrat com os 0,5 do DM",
				  fh.techmod == 6 && ft.techmod == 12 && Math.Abs(fy.techmod - 0.5) < 1e-9, $"{fh.techmod} / {ft.techmod} / {fy.techmod}");
			Fighter fAntes;
			InteligenciaDaRaca.FichaIgnoraARacaDeTeste = true;
			try { fAntes = Birth.Nascer(_racas, "Human", "", sorte, "DeAntes"); }
			finally { InteligenciaDaRaca.FichaIgnoraARacaDeTeste = false; }
			Checa("   DEFEITO INJETADO (o port de antes, que nao lia a raca): o Humano nasce com 1 -- a regua de cima reprovaria", fAntes.techmod == 1,
				  $"{fAntes.techmod}");

			// ---- 3. quem ja existia ----
			var velho = new ServerPlayer { Name = "Veterano", Race = "Human", Ficha = Birth.Nascer(_racas, "Human", "", sorte, "Veterano") };
			velho.Ficha.techmod = 1 + 2;   // o save de antes: o 1 de toda raca, mais um desejo de inteligencia (+2)
			velho.InteligenciaVersao = 0;
			MigrarInteligencia(velho);
			double depois = velho.Ficha.techmod;
			MigrarInteligencia(velho);
			Checa("**QUEM JA EXISTIA GANHA A DIFERENCA, UMA VEZ**: o Humano salvo com 1 + o desejo (3) vai pra 6 + o desejo (8) -- e a segunda "
				  + "entrada nao soma de novo", depois == 8 && velho.Ficha.techmod == 8 && velho.InteligenciaVersao == CharacterSave.VersaoDaInteligencia,
				  $"{depois} -> {velho.Ficha.techmod}; versao {velho.InteligenciaVersao}");

			// ---- 4. o mainframe ----
			Construcao? m = _obras.Get(CatalogoDeObras.Mainframe);
			var aprendiz = new Fighter { techskill = 60, Zeni = 10_000_000 };
			var mestre = new Fighter { techskill = 70, Zeni = 10_000_000 };
			Checa("**O MAINFRAME DE ANDROIDES SO SE CONSTROI COM TECH 70** (*\"faça q so posa construir com tech 70\"*): com 60 a obra e recusada "
				  + "por tecnologia; com 70, liberada -- a mesma que a instalacao do laboratorio pede",
				  m != null && m.Tech == 70 && CatalogoDeObras.Permitida(m, aprendiz, "Human") == RecusaObra.SemTech
				  && CatalogoDeObras.Permitida(m, mestre, "Human") == RecusaObra.Pode && TechDoLaboratorio == m.Tech,
				  $"tech {m?.Tech}; com 60 {(m == null ? "-" : CatalogoDeObras.Permitida(m, aprendiz, "Human"))}");
			CatalogoDeObras doDmObras;
			CatalogoDeObras.MainframeComOTechDoDmDeTeste = true;
			try { doDmObras = CatalogoDeObras.Parse(Godot.FileAccess.GetFileAsString("res://Assets/Data/construcoes.json")); }
			finally { CatalogoDeObras.MainframeComOTechDoDmDeTeste = false; }
			Checa("   DEFEITO INJETADO (o catalogo de antes): o mainframe em tech 50, e o aprendiz de 60 o constroi -- a regua de cima reprovaria",
				  doDmObras.Get(CatalogoDeObras.Mainframe) is { } m50 && m50.Tech == 50 && CatalogoDeObras.Permitida(m50, aprendiz, "Human") == RecusaObra.Pode,
				  "");

			// ---- 5. o nome ----
			Checa("**O TSUJIN SE CHAMA TSUFURUJIN NA TELA** (*\"a raça tsujin (q deveria ser tsufurujin no jogo)\"*); a chave continua a do DM",
				  NomeDaRaca.Bonito(InteligenciaDaRaca.Tsufurujin) == "Tsufurujin" && NomeDaRaca.EhChaveCrua("Tsujin"), NomeDaRaca.Bonito("Tsujin"));

			// ---- 6. o poder de luta da raca (dono, 2026-10-21: *"deixa androide em 1.2 e majin corrupted em 1.3 e o majin normal em 1.0"*) ----
			double BpModDe(string raca, string classe) => Birth.Nascer(_racas, raca, "", sorte, "Medido", classeForcada: classe).BPMod;
			static bool Perto(double a, double b) => Math.Abs(a - b) < 1e-9;
			double andro = BpModDe("Android", ""), majin = BpModDe("Majin", "Majin"), corrompido = BpModDe("Majin", "Corrupted Majin"),
				   yardrat = BpModDe("Yardrat", "");
			Checa("**O PODER DE LUTA DA RACA MUDOU** (o \"Battle Power\" do `races.json`, que multiplica todo ganho de BP e o teto de treino): o "
				  + "Androide nasce com 1,2, o Majin com 1 e o Majin Corrompido com 1,3 -- e o Yardrat, que nao e do pedido, com o 1,2 de sempre",
				  Perto(andro, 1.2) && Perto(majin, 1) && Perto(corrompido, 1.3) && Perto(yardrat, 1.2),
				  $"androide {andro} | majin {majin} | corrompido {corrompido} | yardrat {yardrat}");

			// quem ja existia: o save guarda a base VELHA dentro do `BPMod`, com o que a saga e as skills somaram por cima
			ServerPlayer Veterano(string raca, string classe, double bpModDoSave)
			{
				var v = new ServerPlayer { Name = "Veterano " + raca, Race = raca, Ficha = Birth.Nascer(_racas, raca, "", sorte, "Veterano", classeForcada: classe) };
				v.Ficha.BPMod = bpModDoSave;
				v.PoderDaRacaVersao = 0;
				return v;
			}
			ServerPlayer vCorrompido = Veterano("Majin", "Corrupted Majin", 2.5 + 2 * SagaDoMajin.PoderDeLutaPorDegrau);   // dois degraus da saga
			ServerPlayer vMajin = Veterano("Majin", "Majin", 2.3);
			ServerPlayer vAndro = Veterano("Android", "", 2.0 + 0.1);                                                      // uma skill genetica
			ServerPlayer vYardrat = Veterano("Yardrat", "", 1.2 + 0.3);
			foreach (ServerPlayer v in new[] { vCorrompido, vMajin, vAndro, vYardrat }) MigrarPoderDaRaca(v);
			double corrompido1 = vCorrompido.Ficha.BPMod;
			foreach (ServerPlayer v in new[] { vCorrompido, vMajin, vAndro, vYardrat }) MigrarPoderDaRaca(v);
			Checa("**QUEM JA EXISTIA TROCA SO A BASE, UMA VEZ**: o Corrompido com dois degraus da saga vai de 4,5 a 3,3, o Majin de 2,3 a 1, o "
				  + "Androide com uma skill de 2,1 a 1,3, o Yardrat fica nos 1,5 dele -- e a segunda entrada nao troca de novo",
				  Perto(corrompido1, 2.5 + 2 * SagaDoMajin.PoderDeLutaPorDegrau - 1.2) && Perto(vCorrompido.Ficha.BPMod, corrompido1)
				  && Perto(vMajin.Ficha.BPMod, 1) && Perto(vAndro.Ficha.BPMod, 1.3) && Perto(vYardrat.Ficha.BPMod, 1.5)
				  && vCorrompido.PoderDaRacaVersao == CharacterSave.VersaoDoPoderDaRaca,
				  $"corrompido {corrompido1} -> {vCorrompido.Ficha.BPMod} | majin {vMajin.Ficha.BPMod} | androide {vAndro.Ficha.BPMod} | "
				  + $"yardrat {vYardrat.Ficha.BPMod}; versao {vCorrompido.PoderDaRacaVersao}");

			// ---- 7. a regeneracao do Majin Corrompido (dono, 2026-10-21: *"faça o corrupted majin tb ter regeneraçao em 100 pq ele ta como 70"*) ----
			// O corpo ja regenerava como o Majin: o `EixoDeRegen` le a RACA (`RegeneracaoDaRaca`), e nao a classe. O 70 da classe no catalogo
			// era um numero que ninguem lia -- agora ele diz o mesmo que o jogo faz.
			double regDaClasse = _racas.Protos["Majin"].ClassStats["Corrupted Majin"]["Regeneration"];
			Checa("**O MAJIN CORROMPIDO REGENERA 100**: o catalogo diz 100 na classe (era 70, sem leitor) e o corpo regenera como o do Majin",
				  regDaClasse == 100 && RegeneracaoDaRaca("Majin") == 100, $"classe {regDaClasse}, corpo {RegeneracaoDaRaca("Majin")}");
		}
		catch (Exception e)
		{
			falhou++;
			GD.PrintErr($"[inteligencia]   FALHA a bancada rodou inteira   {e}");
		}
		GD.Print($"[inteligencia] ===== {ok} OK, {falhou} FALHA(S) =====");
	}
}
