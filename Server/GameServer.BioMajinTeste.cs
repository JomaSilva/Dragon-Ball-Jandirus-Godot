using System.Text.Json;
using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.Verbos;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// (2026-10-24) A FAMILIA 4c DA `--bioteste` -- O BIO COM SANGUE MAJIN TEM AS DUAS ABSORCOES (`GameServer.BioMajin.cs`). Num arquivo
/// proprio pra nao inchar a `GameServer.BioTeste.cs`; quem a chama e o `RodarBancadaDoBio`. As afirmacoes tem codigo ("4c3." ...): e por
/// ele que a contraprova por mutacao do ciclo acha cada uma.
///
/// Os botoes entram pelo funil de producao (`UsarHabilidade`), como o cliente os manda; quem ve cada botao e a tabela
/// (`RequisitosDosVerbos`), perguntada com a posse de servidor -- a mesma pergunta da `--verbsteste`.
/// </summary>
public partial class GameServer
{
	/// <summary>O canto da familia 4c, longe dos laboratorios das outras (o `PalcoDoSangue` e o ultimo, em 2240).</summary>
	private static readonly Vec2 PalcoDoBioMajin = new(2560, 0);

	/// <summary>Entre um corpo da familia e o seguinte: bem fora do raio da absorcao (`RaioDaAbsorcao`), pra ninguem pegar o caido alheio.</summary>
	private const float VaoDaFamilia4c = 6 * ZoneCollision.TileSize;

	private void MedirAsDuasAbsorcoesDoBioMajin(Checagem Checa, ServerPlayer host, List<ServerPlayer> forjados)
	{
		GD.Print("--- 4c. O BIO COM SANGUE MAJIN: AS DUAS ABSORCOES (2026-10-24) ---");
		VerbDeclarado? doSelo = RequisitosDosVerbos.Linha("hab:absorver_majin");
		VerbDeclarado? doBio = RequisitosDosVerbos.Linha("hab:absorver");
		if (doSelo == null || doBio == null) { Checa("PRECONDICAO: a tabela tem as linhas do Absorver e do Absorver como Majin", false); return; }

		// UM CORPO DA FAMILIA, cada um no seu canto. O bio e o da escada: saiu de um tanque e esta no primeiro degrau (o
		// `ContarAbsorcaoDoBio` so conta a partir dele), com o sangue pedido.
		int fila = 0;
		ServerPlayer? CorpoDa4c(string raca, string nome, bool sabeBuuAbsorb, params string[] sangue)
		{
			ServerPlayer? c = ForjarCorpo(host, raca, nome, forjados, PalcoDoBioMajin + new Vec2(0, VaoDaFamilia4c * fila++));
			if (c == null) return null;
			if (BioAndroids.EhBio(raca))
			{
				c.Ficha.bio_lab_born = true;
				c.Ficha.bio_stage = BioAndroids.Imperfeito;
				c.Ficha.bio_racas_dna = [.. sangue];
			}
			if (sabeBuuAbsorb)
			{
				// (a arvore do Majin so vende Buu Absorb pro sangue Majin; aqui ela ja foi comprada)
				c.Livro = new Jandirus.Core.Skills.SkillBook();
				c.Livro.Dar(AbsorcaoMajin.Skill);
			}
			return c;
		}

		// A REGUA DAS DUAS ABSORCOES: um NPC caido ao lado de quem aperta, o botao pelo funil de producao, e o que aconteceu -- abriu a
		// EXECUCAO do bio (bebe) ou SELOU (mais um registro dentro dele).
		(bool Bebe, bool Selou, List<string> Falas) ApertarNa4c(ServerPlayer quem, string botao)
		{
			ServerPlayer? vitima = NascerNpc("cidadao", quem.Zone, quem.Pos + new Vec2(8, 0), ++_lugarDaBancadaDeBio);
			if (vitima == null) return (false, false, new List<string> { "(o caido nao nasceu)" });
			forjados.Add(vitima);
			vitima.Ficha.BP = 100_000;   // (os 10% do selo e a media do bio saem dele: com o BP do cidadao, o piso do DM podia dar zero)
			vitima.Ficha.KO = true;
			quem.AlvoId = vitima.Id;
			_prontoAbsorver.Remove(quem.Id);
			int antes = AbsorvidosPor(quem.Id).Count;
			List<string> falas = Ouvir(() => UsarHabilidade(quem, botao));
			return (_execucoesDoBio.ContainsKey(quem.Id), AbsorvidosPor(quem.Id).Count > antes, falas);
		}

		// A EXECUCAO ATE O FIM: o relogio anda os 5 s e o tique de producao a consuma. Devolve se ela se consumou.
		bool BeberAteOFim(ServerPlayer quem)
		{
			AdiantoDoRelogioDeTeste += (long)(BioAndroids.SegundosDaExecucao * 1000) + 50;
			TickDasExecucoesDoBio();
			return !_execucoesDoBio.ContainsKey(quem.Id);
		}

		// O SELO DESFEITO: a regua seguinte parte do zero -- ninguem dentro, sem o poder emprestado, sem execucao e sem recarga.
		void DesfazerOSelo(ServerPlayer quem)
		{
			_execucoesDoBio.Remove(quem.Id);
			CuspirTodosDoMajin(quem.Id, "");
			quem.Ficha.majin_absorb_bp = 0;
			_prontoAbsorver.Remove(quem.Id);
		}

		bool VeNa4c(VerbDeclarado linha, ServerPlayer quem) => linha.Requisito.Atende(PosseDe(quem));

		ServerPlayer? bioMajin = CorpoDa4c(BioAndroids.Raca, "Bio de Sangue Majin", true, "Majin");
		ServerPlayer? bioComum = CorpoDa4c(BioAndroids.Raca, "Bio sem Sangue Majin", false, "Saiyan");
		ServerPlayer? majin = CorpoDa4c("Majin", "Majin de Verdade", true);
		ServerPlayer? larva = CorpoDa4c(BioAndroids.Raca, "Larva de Sangue Majin", true, "Majin");
		ServerPlayer? velho = CorpoDa4c(BioAndroids.Raca, "Bio Velho de Sangue Majin", true, "Majin");
		if (bioMajin == null || bioComum == null || majin == null || larva == null || velho == null)
		{ Checa("PRECONDICAO: os cinco corpos da familia 4c nasceram", false); return; }
		larva.Ficha.bio_stage = BioAndroids.Larva;

		// ---- quem ve qual botao ----
		Checa("4c1. O BOTAO NOVO: o bio de sangue Majin que sabe Buu Absorb ve o Absorver E o Absorver como Majin -- o bio sem o sangue e o "
			  + "Majin de verdade (que sela pelo Absorver) nao veem o segundo",
			  VeNa4c(doBio, bioMajin) && VeNa4c(doSelo, bioMajin) && VeNa4c(doBio, bioComum) && !VeNa4c(doSelo, bioComum)
			  && VeNa4c(doBio, majin) && !VeNa4c(doSelo, majin),
			  $"bioMajin {VeNa4c(doBio, bioMajin)}/{VeNa4c(doSelo, bioMajin)}, bioComum {VeNa4c(doBio, bioComum)}/{VeNa4c(doSelo, bioComum)}, "
			  + $"majin {VeNa4c(doBio, majin)}/{VeNa4c(doSelo, majin)}");
		Checa("4c2. (premissa) o sangue Majin faz dele corpo de Majin -- se desfaz em vez de morrer --, e ISSO NAO MUDA: o dono separou so os botoes",
			  CorpoDeMajin(bioMajin) && !CorpoDeMajin(bioComum));

		// ---- O CONSERTO: o Absorver do bio de sangue Majin BEBE, e a escada anda ----
		bioMajin.Ficha.bio_passos = 0;
		var bebeu = ApertarNa4c(bioMajin, "absorver");
		bool consumou = bebeu.Bebe && BeberAteOFim(bioMajin);
		Checa("4c3. **O BIO COM SANGUE MAJIN BEBE PELO ABSORVER** (o dono: *\"so consegue absorver igual um majin ... ai n consegue evoluir\"*): "
			  + "abre a execucao, nao sela ninguem, e no fim a escada ANDA (um NPC vale um quinto de marco)",
			  bebeu.Bebe && !bebeu.Selou && consumou && Math.Abs(bioMajin.Ficha.bio_passos - BioAndroids.PassoDoNpc) < 1e-9,
			  $"bebe={bebeu.Bebe} selou={bebeu.Selou} consumou={consumou} passos={bioMajin.Ficha.bio_passos} | {Ultimos(bebeu.Falas)}");
		DesfazerOSelo(bioMajin);

		BioMajinSelaPeloAbsorverDeTeste = true;
		try
		{
			var comODefeito = ApertarNa4c(bioMajin, "absorver");
			Checa("4c3x. (defeito injetado: o Absorver do bio com sangue Majin volta a selar, como da v0.3.0 a v0.4.4) a mesma regua REPROVA -- "
				  + "ele sela e nao bebe",
				  !comODefeito.Bebe && comODefeito.Selou, Ultimos(comODefeito.Falas));
		}
		finally { BioMajinSelaPeloAbsorverDeTeste = false; DesfazerOSelo(bioMajin); }

		// ---- o botao novo SELA ----
		double passosAntes = bioMajin.Ficha.bio_passos;
		var selou = ApertarNa4c(bioMajin, "absorver_majin");
		Checa("4c4. **O ABSORVER COMO MAJIN SELA**, pelas portas do Majin: o caido vai pra dentro dele (o NPC inteiro, com o poder emprestado), "
			  + "sem execucao -- e a escada NAO anda",
			  selou.Selou && !selou.Bebe && bioMajin.Ficha.majin_absorb_bp > 0 && bioMajin.Ficha.bio_passos == passosAntes,
			  $"selou={selou.Selou} bebe={selou.Bebe} emprestado={bioMajin.Ficha.majin_absorb_bp:0} passos {passosAntes} -> {bioMajin.Ficha.bio_passos} "
			  + $"| {Ultimos(selou.Falas)}");
		DesfazerOSelo(bioMajin);

		// ---- o botao novo e SO do bio com sangue Majin ----
		var semSangue = ApertarNa4c(bioComum, "absorver_majin");
		Checa("4c5. O BIO SEM SANGUE MAJIN que aperta o Absorver como Majin por fora do menu ouve a recusa -- e nao sela nem bebe",
			  !semSangue.Selou && !semSangue.Bebe && Disse(semSangue.Falas, RecusaDoAbsorverComoMajin), Ultimos(semSangue.Falas));
		DesfazerOSelo(bioComum);
		var doMajin = ApertarNa4c(majin, "absorver_majin");
		Checa("4c6. O MAJIN DE VERDADE nao sela pelo botao do bio (o dele e o Absorver): a mesma recusa, e ninguem dentro",
			  !doMajin.Selou && Disse(doMajin.Falas, RecusaDoAbsorverComoMajin), Ultimos(doMajin.Falas));
		DesfazerOSelo(majin);
		AbsorverComoMajinSemSangueDeTeste = true;
		try
		{
			var comODefeito = ApertarNa4c(majin, "absorver_majin");
			Checa("4c6x. (defeito injetado: o botao novo nao confere o sangue) a mesma regua REPROVA -- o Majin sela por ele",
				  comODefeito.Selou && !Disse(comODefeito.Falas, RecusaDoAbsorverComoMajin), Ultimos(comODefeito.Falas));
		}
		finally { AbsorverComoMajinSemSangueDeTeste = false; DesfazerOSelo(majin); }

		var peloAbsorver = ApertarNa4c(majin, "absorver");
		Checa("4c7. O MAJIN CONTINUA SELANDO PELO ABSORVER, como hoje: o caido vai pra dentro, sem execucao",
			  peloAbsorver.Selou && !peloAbsorver.Bebe, Ultimos(peloAbsorver.Falas));
		DesfazerOSelo(majin);

		// ---- a larva nao absorve, nem selando ----
		var daLarva = ApertarNa4c(larva, "absorver_majin");
		Checa("4c8. A LARVA de sangue Majin nao sela pelo botao novo (o dono: *\"A forma de barata do bio androide n consegue absorver\"*): "
			  + "a frase da carapaca, e ninguem dentro",
			  BioAndroids.EmLarva(larva.Ficha) && !daLarva.Selou && Disse(daLarva.Falas, "carapaça larval"), Ultimos(daLarva.Falas));
		DesfazerOSelo(larva);
		LarvaSelaComoMajinDeTeste = true;
		try
		{
			var comODefeito = ApertarNa4c(larva, "absorver_majin");
			Checa("4c8x. (defeito injetado: a larva sela pelo botao novo) a mesma regua REPROVA", comODefeito.Selou, Ultimos(comODefeito.Falas));
		}
		finally { LarvaSelaComoMajinDeTeste = false; DesfazerOSelo(larva); }

		// ---- O BIO VELHO: o personagem de antes do conserto, pelo save e pelo login ----
		// O que o defeito podia ter deixado escrito: o selo liga o `AbsorbDeterminesBP` em toda tentativa (com o `AbsorbBP` em zero), e o
		// servidor que caiu com alguem selado gravou o poder emprestado. A ficha vai pelo caminho do save (a copia sem os buffs, o JSON) e
		// volta pelo passo do login que toca no que o selo deixa (`AcordarSemAbsorcao`, no `Entrar`).
		velho.Ficha.AbsorbDeterminesBP = true;
		velho.Ficha.AbsorbBP = 0;
		velho.Ficha.majin_absorb_bp = 12_345;
		var opcoes = new JsonSerializerOptions { IncludeFields = true };
		Fighter? doDisco = JsonSerializer.Deserialize<Fighter>(JsonSerializer.Serialize(FichaSemOsBuffs(velho), opcoes), opcoes);
		if (doDisco == null) { Checa("PRECONDICAO: a ficha do bio velho voltou do disco", false); return; }
		velho.Ficha = doDisco;
		PrepararCombate(velho, null);
		AcordarSemAbsorcao(velho);
		Checa("4c9. (premissa) O BIO VELHO volta do save com o sangue Majin e com o que o selo ligou, e o login zera o poder emprestado",
			  SangueDoBio.Carrega(velho.Ficha, "Majin") && velho.Ficha.AbsorbDeterminesBP && velho.Ficha.majin_absorb_bp == 0
			  && velho.Ficha.bio_stage == BioAndroids.Imperfeito,
			  $"sangue {SangueDoBio.Texto(velho.Ficha)}, emprestado {velho.Ficha.majin_absorb_bp}, degrau {velho.Ficha.bio_stage}");
		velho.Ficha.bio_passos = 0;
		var doVelho = ApertarNa4c(velho, "absorver");
		bool consumouVelho = doVelho.Bebe && BeberAteOFim(velho);
		Checa("4c10. **O BIO VELHO JA BEBE PELO ABSORVER** ao entrar, sem passo de conserto nenhum: a escada anda, e o poder dela entra pela "
			  + "MEDIA (o `AbsorbBP` passa a existir)",
			  doVelho.Bebe && !doVelho.Selou && consumouVelho && Math.Abs(velho.Ficha.bio_passos - BioAndroids.PassoDoNpc) < 1e-9
			  && velho.Ficha.AbsorbBP > 0,
			  $"bebe={doVelho.Bebe} selou={doVelho.Selou} consumou={consumouVelho} passos={velho.Ficha.bio_passos} AbsorbBP={velho.Ficha.AbsorbBP:0} "
			  + $"| {Ultimos(doVelho.Falas)}");
		DesfazerOSelo(velho);
	}
}
