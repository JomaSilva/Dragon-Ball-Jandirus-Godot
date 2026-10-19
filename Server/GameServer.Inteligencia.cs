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
		}
		catch (Exception e)
		{
			falhou++;
			GD.PrintErr($"[inteligencia]   FALHA a bancada rodou inteira   {e}");
		}
		GD.Print($"[inteligencia] ===== {ok} OK, {falhou} FALHA(S) =====");
	}
}
