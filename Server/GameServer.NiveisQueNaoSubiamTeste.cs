using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--niveisteste` -- AS SKILLS QUE NAO SUBIAM DE NIVEL (adendo 18, 2026-10-16). O dono: *"tem varias skills q n tem como
/// subir de nivel, traga pro port formas de upar elas, tipo ki effusion etc"*.
///
/// Cada fonte de exp nova, pela porta de PRODUCAO (o tiro, o verb, o tique do efetor), num jogador de verdade -- um cliente cru
/// entra pela porta, porque o funil dos contadores (`CreditarContador`) so credita jogador e a Efusao passa por ele. E o efeito do
/// nivel onde o port tinha um numero cravado. Os contra-exemplos sao os dois defeitos injetados: `FontesDoAdendo18DesligadasDeTeste`
/// (as fontes somem juntas -- o jogo de antes, com as skills congeladas no 0) e `EfeitosDeNivelCravadosDeTeste` (os numeros de antes).
///
///   E. KI EFFUSION -- a bola (`blastcounter`) e o tiro carregado (`blastcounter` + `chargedcounter`) rendem o somatorio, na taxa
///      do DM (`KiSkillGains(10*n)`).
///   S. SPIRIT GUN -- um ponto por tiro; no ponto da barreira o nivel 1 sobe e o degrau corta o custo e dobra o dano.
///   F. SPIRIT FIST -- um ponto pelo punho que acha alguem (e nenhum no vazio).
///   R. RAPID MOVEMENT -- um ponto por tique em que o corpo VIROU (nenhum parado); no ponto da barreira, o nivel 1.
///   L. LAMINA E ESCUDO DE KI -- um ponto por tique com a ferramenta; no nivel 1 a lamina morde a metade, e o escudo custa a metade.
///   P. FULL POWER -- o Ki queimado: o primeiro ponto aos 1000.
///   G. A FORMA GIGANTE -- o banco do gigante rende pontos; o nivel 1 baixa a eficiencia pra 0,5.
///   M. (2026-10-18) O MORTO DE PE -- *"alguns players relataram n estar dando pra upar ki effusion morto (e talvez outras skills)"*: morto pela
///      porta, levado ao Outro Mundo pela porta, a bola do fantasma rende na Efusao o MESMO que de vivo, e a meditacao paga a Ki Unlocked no
///      tique de producao. Contra-exemplo: o tiro que recusa todo morto (`MortoNaoAtiraDeTeste`, o jogo de antes de 2026-10-15).
/// </summary>
public sealed partial class GameServer
{
	private const string ContaDaBancadaDosNiveis = "bancada_niveis_ag";

	private bool _niveisRodando, _niveisJaRodou;
	private int _nvOk, _nvFalhou;

	private void AfirmarNv(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _nvOk++; GD.Print($"[niveis]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _nvFalhou++; GD.PrintErr($"[niveis]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDosNiveis(int porta)
	{
		if (_niveisRodando || _niveisJaRodou) return;
		_niveisRodando = _niveisJaRodou = true;
		_nvOk = _nvFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[niveis] ================ AS SKILLS QUE NAO SUBIAM DE NIVEL (dono, 2026-10-16) ================");

		const float T = ZoneCollision.TileSize;
		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		try
		{
			ServerPlayer? pl = mesa.EntraNoMundo(cli, ContaDaBancadaDosNiveis, "Aprendiz");
			AfirmarNv("(montagem) o cliente entra pela porta e e JOGADOR (o funil dos contadores so credita jogador); a Terra tem mapa",
						   pl != null && EhJogador(pl) && _pjMapa != null);
			if (pl == null || _pjMapa == null) return;

			ZoneKey terra = ZonaDaBancadaDeProjetil;
			if (pl.Zone.Hash != terra.Hash) MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
			Vec2 pista = TerraFirmeG12(30);
			Fighter f = pl.Ficha;

			void Pronto()
			{
				pl.Pos = pista;
				pl.Facing = Facing.East;
				if (f.KO) pl.Combate.Levantar();
				pl.Combate.Corpo.Restaurar();
				pl.Combate.SincronizarVida();
				pl.Combate.Recarga = 0;
				pl.Combate.Carencia = 0;
				f.Ki = f.MaxKi;
				f.stamina = f.maxstamina;
				f.med = f.train = false;
				_blastPronto.Remove(pl.Id);
				_prontoG3.Remove(pl.Id);
				_prontoG1.Remove(pl.Id);
			}
			void Dar(string path)
			{
				pl.Livro.Dar(path);
				pl.Niveis.Por(path, 0);
			}
			double Exp(string path) => pl.Niveis.Exp(path);
			// UMA RODADA COM AS FONTES DESLIGADAS (o jogo de antes): devolve o que a acao rendeu com elas
			double SemAsFontes(string path, Action acao)
			{
				NiveisDeSkill.FontesDoAdendo18DesligadasDeTeste = true;
				try { double antes = Exp(path); acao(); return Exp(path) - antes; }
				finally { NiveisDeSkill.FontesDoAdendo18DesligadasDeTeste = false; }
			}

			// ------------------------------------------------------------ E) a Ki Effusion
			GD.Print("[niveis] -- E) a Ki Effusion: o somatorio dos oito contadores (`Effusion.dm:74`) --");
			const string Efusao = "/datum/skill/mind/Basic_Ki_Effusion";
			Dar(Efusao);
			Pronto();
			double e0 = Exp(Efusao);
			BasicBlast(pl);
			double daBola = Exp(Efusao) - e0;
			Pronto();
			double e1 = Exp(Efusao);
			TiroCarregadoG5(pl);
			double doCarregado = Exp(Efusao) - e1;
			double taxaDaBola = NiveisDeSkill.KiSkillGains(10 * 1, f), taxaDoCarregado = NiveisDeSkill.KiSkillGains(10 * (5 + 5), f);
			AfirmarNv("**A KI EFFUSION SOBE** (*\"tipo ki effusion\"*): a bola basica rende `KiSkillGains(10x1)` e o tiro carregado `KiSkillGains(10x(5+5))` -- o `blastcounter` e o `chargedcounter` no somatorio, na taxa do DM",
						   daBola > 0 && Math.Abs(daBola - taxaDaBola) < 1e-9 && Math.Abs(doCarregado - taxaDoCarregado) < 1e-9,
						   $"bola {daBola:0.#####} (esperado {taxaDaBola:0.#####}); carregado {doCarregado:0.#####} (esperado {taxaDoCarregado:0.#####})");
			Pronto();
			double efusaoDeAntes = SemAsFontes(Efusao, () => BasicBlast(pl));
			AfirmarNv("(defeito injetado: as fontes de antes) a bola nao rende nada na Efusao -- a regua de cima REPROVA", efusaoDeAntes == 0,
						   $"{efusaoDeAntes}");

			// A TAXA DO DONO (2026-10-17): *"faça as skills de ki evolurem mais rapido, estamos quase nivel super saiyajin e nem liberamos
			// o beam ainda"*. A MESMA bola, pela mesma porta, na taxa do DM (`GainKnobs.GlobalKiExpRateDoDm`) e na de agora: a razao e o
			// fator 5, e a conta do beam (Efusao 25 = a soma das barreiras `5000*1.03^n` de 0 a 24) vai no detalhe, em bolas. Uma taxa que
			// voltasse a do DM daria razao 1 e reprova.
			Pronto();
			double taxaDeAgora = GainKnobs.GlobalKiExpRate, naTaxaDoDm;
			GainKnobs.GlobalKiExpRate = GainKnobs.GlobalKiExpRateDoDm;
			try { double e2 = Exp(Efusao); BasicBlast(pl); naTaxaDoDm = Exp(Efusao) - e2; }
			finally { GainKnobs.GlobalKiExpRate = taxaDeAgora; }
			double ateOBeam = 0;
			for (int n = 0; n < 25; n++) ateOBeam += 5000 * Math.Pow(1.03, n);
			AfirmarNv("**AS SKILLS DE KI SOBEM 5x MAIS RAPIDO QUE NO DM** (*\"nem liberamos o beam ainda\"*): a mesma bola rende 5x na Efusao",
						   naTaxaDoDm > 0 && Math.Abs(daBola / naTaxaDoDm - 5) < 1e-6,
						   $"bola {daBola:0.##} contra {naTaxaDoDm:0.##} na taxa do DM; o beam (Efusao 25, {ateOBeam:N0} de exp) sai em {Math.Ceiling(ateOBeam / daBola):N0} bolas desta ficha, contra {Math.Ceiling(ateOBeam / naTaxaDoDm):N0}");

			// ------------------------------------------------------------ M) o morto de pe (2026-10-18)
			// A INVESTIGACAO, escrita: nenhuma porta da exp pergunta o `dead` cru. O tiro recusa so o CADAVER (`PodeAtirar`, desde 2026-10-15 -- o
			// dono: *"n ta dando pra usar ataques de ki tb como morto"*), o funil dos contadores so pergunta se e jogador (`CreditarContador`) e o tique
			// dos niveis pula so o cadaver (`TickDosNiveis`). Esta familia prova o caminho inteiro, pelas portas de producao.
			GD.Print("[niveis] -- M) o morto de pe no Outro Mundo: a Ki Effusion e a meditacao sobem como de vivo --");
			Pronto();
			double vivo0 = Exp(Efusao);
			BasicBlast(pl);
			double daBolaViva = Exp(Efusao) - vivo0;
			pl.Combate.Morrer();
			IrProAlem(pl);
			bool dePe = f.dead && pl.MortoDePe && Alem.EhOAlem(pl.Zone) && !EhCadaver(pl) && EhJogador(pl);
			AfirmarNv("(montagem) morto pela porta (`Combate.Morrer`) e levado ao Outro Mundo pela porta (`IrProAlem`): de pe la, e jogador",
							   dePe, $"zona {pl.Zone.Name}; morto {f.dead}; de pe {pl.MortoDePe}");
			_blastPronto.Remove(pl.Id);
			f.Ki = f.MaxKi;
			double morto0 = Exp(Efusao);
			List<string> falasDoTiro = Ouvir(() => BasicBlast(pl));
			double daBolaMorta = Exp(Efusao) - morto0;
			AfirmarNv("**O MORTO DE PE UPA A KI EFFUSION** (*\"n estar dando pra upar ki effusion morto\"*): a bola sai da mao do fantasma e rende na "
							+ "Efusao o MESMO que de vivo",
							   daBolaMorta > 0 && Math.Abs(daBolaMorta - daBolaViva) < 1e-9,
							   $"morto {daBolaMorta:0.#####}; vivo {daBolaViva:0.#####}; {Ultimos(falasDoTiro)}");

			// A DE ESTADO (*"e talvez outras skills"*): meditando no Outro Mundo, o tique de PRODUCAO dos niveis -- o das guardas de jogador e de
			// cadaver -- paga a Ki Unlocked (2 por tique meditando, `Mind.dm`).
			const string KiUnlocked = "/datum/skill/mind/Ki_Unlocked";
			if (!pl.Livro.Sabe(KiUnlocked)) Dar(KiUnlocked);
			f.med = true;
			double medit0 = Exp(KiUnlocked);
			int nivelAntes = pl.Niveis.Nivel(KiUnlocked);
			for (int i = 0; i < 10; i++) TickDosNiveis();
			double daMeditacao = Exp(KiUnlocked) - medit0;
			f.med = false;
			AfirmarNv("...e a de ESTADO tambem (*\"e talvez outras skills\"*): meditando no Outro Mundo, o tique de producao dos niveis paga a Ki Unlocked",
							   daMeditacao > 0 || pl.Niveis.Nivel(KiUnlocked) > nivelAntes, $"{daMeditacao:0.##} de exp em 10 tiques");

			MortoNaoAtiraDeTeste = true;
			double daBolaComODefeito;
			try
			{
				_blastPronto.Remove(pl.Id);
				f.Ki = f.MaxKi;
				double doMortoAntes = Exp(Efusao);
				BasicBlast(pl);
				daBolaComODefeito = Exp(Efusao) - doMortoAntes;
			}
			finally { MortoNaoAtiraDeTeste = false; }
			AfirmarNv("(defeito injetado: o tiro recusa todo morto, o jogo de antes de 2026-10-15) a bola do fantasma nao sai e a Efusao congela -- a "
							+ "regua de cima REPROVA", daBolaComODefeito == 0, $"{daBolaComODefeito}");

			// DE VOLTA A VIDA, na Terra: as familias seguintes medem um vivo -- e SEM O CADAVER que a viagem deixou na pista (`IrProAlem` deixa o
			// corpo no mundo dos vivos): o punho "no vazio" do Spirit Fist, logo abaixo, acertava ele.
			pl.Combate.Reviver();
			pl.MorteJaViajou = false;
			MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
			foreach (ServerPlayer cadaver in ZoneList(terra.Hash).Where(c => c.ECadaver && c.NomeDeQuemMorreu == pl.Name).ToList())
				DesfazerOCadaver(cadaver);

			// ------------------------------------------------------------ S) o Spirit Gun
			GD.Print("[niveis] -- S) o Spirit Gun: um ponto por tiro, e o degrau corta o custo e dobra o dano --");
			Dar(PathDoSpiritGun);
			Pronto();
			double s0 = Exp(PathDoSpiritGun);
			UsarHabilidade(pl, "Spirit_Gun");
			double umTiro = Exp(PathDoSpiritGun) - s0;
			double custoAntes = f.SpiritBallCost, danoAntes = f.SpiritBallDamage;
			pl.Niveis.Por(PathDoSpiritGun, 0, 9);   // a um ponto da barreira (10, `Spirit.dm:311`)
			Pronto();
			UsarHabilidade(pl, "Spirit_Gun");
			TicarNiveisDe(pl);
			bool subiu = pl.Niveis.Nivel(PathDoSpiritGun) == 1;
			AfirmarNv("**O SPIRIT GUN SOBE**: um ponto por tiro; no ponto da barreira o nivel 1 vem, e o degrau corta o custo pela metade e dobra o dano",
						   umTiro == 1 && subiu && Math.Abs(f.SpiritBallCost - custoAntes / 2) < 1e-9 && Math.Abs(f.SpiritBallDamage - danoAntes * 2) < 1e-9,
						   $"um tiro {umTiro}; nivel {pl.Niveis.Nivel(PathDoSpiritGun)}; custo {custoAntes} -> {f.SpiritBallCost}; dano {danoAntes} -> {f.SpiritBallDamage}");
			Pronto();
			double tiroDeAntes = SemAsFontes(PathDoSpiritGun, () => UsarHabilidade(pl, "Spirit_Gun"));
			AfirmarNv("(defeito injetado: as fontes de antes) o tiro nao rende ponto -- a regua de cima REPROVA", tiroDeAntes == 0, $"{tiroDeAntes}");

			// ------------------------------------------------------------ F) o Spirit Fist
			GD.Print("[niveis] -- F) o Spirit Fist: um ponto pelo punho que acha alguem --");
			Dar(PathDoSpiritFist);
			Pronto();
			double f0 = Exp(PathDoSpiritFist);
			UsarHabilidade(pl, "Spirit_Fist");   // ninguem na frente
			double noVazio = Exp(PathDoSpiritFist) - f0;
			ServerPlayer saco = ForjarV2("Saco NV", pista + new Vec2(T * 0.9f, 0), 1_000);
			saco.Combate.Carencia = 0;
			Pronto();
			double f1 = Exp(PathDoSpiritFist);
			UsarHabilidade(pl, "Spirit_Fist");
			double noSaco = Exp(PathDoSpiritFist) - f1;
			AfirmarNv("**O SPIRIT FIST SOBE**: um ponto pelo punho que acha alguem -- e nenhum pelo punho no vazio (`if(MeleeAttack(...))`)",
						   noSaco == 1 && noVazio == 0, $"no saco {noSaco}; no vazio {noVazio}");
			Pronto();
			saco.Pos = pista + new Vec2(T * 0.9f, 0);
			double punhoDeAntes = SemAsFontes(PathDoSpiritFist, () => UsarHabilidade(pl, "Spirit_Fist"));
			AfirmarNv("(defeito injetado: as fontes de antes) o punho que acha alguem nao rende ponto -- a regua de cima REPROVA", punhoDeAntes == 0,
						   $"{punhoDeAntes}");
			saco.Pos = pista + new Vec2(0, 40 * T);   // sai da pista

			// ------------------------------------------------------------ R) a Rapid Movement
			GD.Print("[niveis] -- R) a Rapid Movement: um ponto por tique em que o corpo virou --");
			Dar(PathDaRapidMovement);
			Pronto();
			double r0 = Exp(PathDaRapidMovement);
			pl.Facing = Facing.East; TicarNiveisDe(pl);
			pl.Facing = Facing.East; TicarNiveisDe(pl);   // parado: nada
			double parado = Exp(PathDaRapidMovement) - r0;
			pl.Facing = Facing.North; TicarNiveisDe(pl);
			pl.Facing = Facing.West; TicarNiveisDe(pl);
			double virando = Exp(PathDaRapidMovement) - r0 - parado;
			// o jogo de antes AINDA NO NIVEL 0 (no 1, que e o teto dela, nada mais rende -- o contra-exemplo nao provaria nada)
			double viradaDeAntes = SemAsFontes(PathDaRapidMovement, () => { pl.Facing = Facing.East; TicarNiveisDe(pl); pl.Facing = Facing.North; TicarNiveisDe(pl); });
			// (o verb `Rapid_Movement` o port ja da na compra -- o `login()` do DM extraido sem o `if(level >= 1)`, de antes deste lote --,
			// entao o que o nivel 1 entrega aqui e a subida, e nao o verb)
			pl.Niveis.Por(PathDaRapidMovement, 0, 99);   // a um ponto da barreira (100, `speedy.dm:84`)
			pl.Facing = Facing.South; TicarNiveisDe(pl);
			bool subiu1 = pl.Niveis.Nivel(PathDaRapidMovement) == 1;
			AfirmarNv("**A RAPID MOVEMENT SOBE** (*\"Wow, I just spin a bunch and get faster?\"*): um ponto por tique em que o corpo virou, nenhum parado -- e no ponto da barreira o nivel 1 vem",
						   parado == 0 && virando == 2 && subiu1, $"parado {parado}; virando {virando}; nivel 1 {subiu1}");
			AfirmarNv("(defeito injetado: as fontes de antes) virar nao rende -- a regua de cima REPROVA", viradaDeAntes == 0, $"{viradaDeAntes}");

			// ------------------------------------------------------------ L) a lamina e o escudo de Ki
			GD.Print("[niveis] -- L) a lamina e o escudo de Ki: um ponto por tique com a ferramenta, e o nivel barateia --");
			Dar(PathDaLamina);
			Dar(PathDoEscudoDeKi);
			double Acender(string id)
			{
				Pronto();
				double ki = f.Ki;
				UsarHabilidade(pl, id);
				return ki - f.Ki;
			}
			void Apagar(string id)
			{
				_prontoG1.Remove(pl.Id);
				if (id == "Ki_Shield" ? _escudoAtivo.ContainsKey(pl.Id) : TemBuff(pl, id)) UsarHabilidade(pl, id);
			}
			double mordidaNo0 = Acender("Ki_Blade");
			double l0 = Exp(PathDaLamina);
			TicarNiveisDe(pl);
			TicarNiveisDe(pl);
			double comALamina = Exp(PathDaLamina) - l0;
			Apagar("Ki_Blade");
			double l1 = Exp(PathDaLamina);
			TicarNiveisDe(pl);
			double semALamina = Exp(PathDaLamina) - l1;
			pl.Niveis.Por(PathDaLamina, 1);
			double mordidaNo1 = Acender("Ki_Blade");
			Apagar("Ki_Blade");
			double custoDoEscudoNo0 = Acender("Ki_Shield");
			double k0 = Exp(PathDoEscudoDeKi);
			TicarNiveisDe(pl);
			double comOEscudo = Exp(PathDoEscudoDeKi) - k0;
			Apagar("Ki_Shield");
			pl.Niveis.Por(PathDoEscudoDeKi, 1);
			double custoDoEscudoNo1 = Acender("Ki_Shield");
			Apagar("Ki_Shield");
			AfirmarNv("**A LAMINA E O ESCUDO DE KI SOBEM**: um ponto por tique com a ferramenta acesa, nenhum apagada",
						   comALamina == 2 && semALamina == 0 && comOEscudo == 1, $"lamina {comALamina} (apagada {semALamina}); escudo {comOEscudo}");
			AfirmarNv("**...E O NIVEL BARATEIA** (o `superdrain`): no nivel 1 a lamina morde a METADE, e o escudo custa a metade",
						   mordidaNo0 > 0 && Math.Abs(mordidaNo1 - mordidaNo0 / 2) < 1e-6 && custoDoEscudoNo0 > 0 && Math.Abs(custoDoEscudoNo1 - custoDoEscudoNo0 / 2) < 1e-6,
						   $"lamina {mordidaNo0:0.##} -> {mordidaNo1:0.##}; escudo {custoDoEscudoNo0:0.##} -> {custoDoEscudoNo1:0.##}");
			double mordidaCravada;
			EfeitosDeNivelCravadosDeTeste = true;
			try { mordidaCravada = Acender("Ki_Blade"); Apagar("Ki_Blade"); }
			finally { EfeitosDeNivelCravadosDeTeste = false; }
			Acender("Ki_Blade");
			double laminaDeAntes = SemAsFontes(PathDaLamina, () => TicarNiveisDe(pl));
			Apagar("Ki_Blade");
			AfirmarNv("(defeitos injetados) com a mordida cravada de antes o nivel 1 morde inteiro, e com as fontes de antes a lamina acesa nao rende -- as reguas de cima REPROVAM",
						   Math.Abs(mordidaCravada - mordidaNo0) < 1e-6 && laminaDeAntes == 0, $"mordida {mordidaCravada:0.##}; ponto {laminaDeAntes}");

			// ------------------------------------------------------------ P) o Full Power
			GD.Print("[niveis] -- P) o Full Power: o Ki queimado, o primeiro ponto aos 1000 --");
			Dar(PathDoFullPower);
			(double Queimado, double Exp) QueimarAteUmPonto()
			{
				double queimado = 0, p0 = Exp(PathDoFullPower);
				for (int i = 0; i < 400 && Exp(PathDoFullPower) == p0; i++)
				{
					f.Ki = f.MaxKi;
					TicarNiveisDe(pl);
					double antes = f.Ki;
					f.Ki = Math.Max(0, f.Ki - 50);
					queimado += antes - f.Ki;
					TicarNiveisDe(pl);
				}
				return (queimado, Exp(PathDoFullPower) - p0);
			}
			var fp = QueimarAteUmPonto();
			AfirmarNv("**O FULL POWER SOBE** (*\"You should try burning up Ki\"*): queimar Ki rende o ponto -- o primeiro aos 1000 queimados",
						   fp.Exp == 1 && fp.Queimado >= 1000 && fp.Queimado < 1050, $"ponto {fp.Exp} aos {fp.Queimado:0} de Ki queimado");
			NiveisDeSkill.FontesDoAdendo18DesligadasDeTeste = true;
			(double Queimado, double Exp) fpDeAntes;
			try { fpDeAntes = QueimarAteUmPonto(); }
			finally { NiveisDeSkill.FontesDoAdendo18DesligadasDeTeste = false; }
			AfirmarNv("(defeito injetado: as fontes de antes) 20.000 de Ki queimados e nenhum ponto -- a regua de cima REPROVA",
						   fpDeAntes.Exp == 0, $"ponto {fpDeAntes.Exp} aos {fpDeAntes.Queimado:0}");

			// ------------------------------------------------------------ G) a forma gigante
			GD.Print("[niveis] -- G) a forma gigante: o banco do gigante rende pontos, e o nivel 1 baixa a eficiencia --");
			Dar(PathDoGigante);
			Pronto();
			_giganteTravaG2.Remove(pl.Id);
			UsarHabilidade(pl, "Giant_Form");
			bool gigante = TemBuff(pl, BuffGiganteG2);
			double g0 = Exp(PathDoGigante);
			for (int i = 0; i < 40; i++) TicarNiveisDe(pl);
			double doGigante = Exp(PathDoGigante) - g0;
			pl.Niveis.Por(PathDoGigante, 0, 99.9);   // perto da barreira (100)
			for (int i = 0; i < 40 && pl.Niveis.Nivel(PathDoGigante) == 0; i++) TicarNiveisDe(pl);
			bool nivel1 = pl.Niveis.Nivel(PathDoGigante) == 1;
			double eficiencia = f.giant_form_efficiency, comODefeito;
			EfeitosDeNivelCravadosDeTeste = true;
			try { comODefeito = EficienciaDoGigante(pl); }
			finally { EfeitosDeNivelCravadosDeTeste = false; }
			AfirmarNv("**A FORMA GIGANTE SOBE**: de gigante, o banco rende pontos (`exp += 1*giant_form_efficiency` a cada 20); no nivel 1 a eficiencia cai pra 0,5 -- e e ela que o folego do gigante le",
						   gigante && doGigante >= 3 && nivel1 && Math.Abs(eficiencia - 0.5) < 1e-9 && Math.Abs(EficienciaDoGigante(pl) - 0.5) < 1e-9 && comODefeito == 1,
						   $"gigante {gigante}; pontos em 40 tiques {doGigante:0.##}; nivel 1 {nivel1}; eficiencia {eficiencia} (com o defeito {comODefeito})");
			double giganteDeAntes = SemAsFontes(PathDoGigante, () => { for (int i = 0; i < 40; i++) TicarNiveisDe(pl); });
			AfirmarNv("(defeito injetado: as fontes de antes) 40 tiques de gigante e nenhum ponto -- a regua de cima REPROVA", giganteDeAntes == 0,
						   $"{giganteDeAntes}");
			if (TemBuff(pl, BuffGiganteG2)) DesligarBuff(pl, BuffGiganteG2);
		}
		catch (Exception e)
		{
			AfirmarNv("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			NiveisDeSkill.FontesDoAdendo18DesligadasDeTeste = false;
			EfeitosDeNivelCravadosDeTeste = false;
			LimparTudoDaBancada();
			mesa.Fecha();
			if (_store != null)
				try
				{
					string caminho = System.IO.Path.Combine(_store.Pasta, ContaDaBancadaDosNiveis + ".json");
					if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
				}
				catch (Exception e) { GD.PushWarning($"[niveis] nao apaguei '{ContaDaBancadaDosNiveis}': {e.Message}"); }
			_niveisRodando = false;
			GD.Print($"[niveis] ================ {_nvOk} OK, {_nvFalhou} FALHA(S) ================");
		}
	}
}
