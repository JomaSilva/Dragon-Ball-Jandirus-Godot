using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Items;
using Jandirus.Core.Npc;
using Jandirus.Core.Ranks;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--v030teste` -- O PEDIDO 15 (2026-10-15, o playtest depois da v0.2.1: a versao 0.3.0), do lado do servidor
/// e do Core.
///
///     Godot --headless --path . --server --port 8421 --v030teste
///
/// Cada familia e um item do pedido do dono, medido pela porta de PRODUCAO e com o defeito injetado do jogo de antes:
///
///   K. O EMPURRAO NO MORTO DE PE -- o fantasma do Outro Mundo voa com o golpe como um vivo; so o cadaver nao (item 1).
///   V. O VIGOR E O KI QUE NAO ACABAM -- o Majin com o vigor travado no cheio; o androide perpetuo com o vigor e o Ki
///      (item 12); e todo MORTO com o vigor no cheio (item 29).
///   W. OS PESOS QUE LEMBRAM A PORCENTAGEM -- o "ajustar" guarda, o "tirar" nao apaga, o "equipar" veste (item 28).
///   Q. O SCOUTER QUE VOLTA LIGADO -- o save leva o bit e o login o devolve, so com o aparelho na mochila (item 27).
///   X. O SANGUE DO BIO-ANDROIDE -- as regras do `SangueDoBio`: a raca com sangue, sozinho o bio nao tem nada, o Zenkai so
///      com Saiyajin, o Majin no sangue, a regeneracao da melhor doadora e o motor infinito (itens 24, 25 e 25b; a cadeia
///      inteira -- agulha, gestacao, parto, o livro no nivel do doador e as tecnicas customizadas -- e da `--bioteste`, 16).
///   Z. O MORTO NA MEDITACAO PROFUNDA -- a regra no Core: so o cadaver e recusado, e a mente e lugar de morto (item 26; o
///      mergulho de verdade e da `--transeteste`, secao 9, e da `--diagv030`, familia P).
///   Y. OS RECURSOS DEZ VEZES -- a pa, a furadeira e a Drill (item 32).
///   O. O CHUTE GIRATORIO -- o Spin Attack anuncia a volta inteira e segura a pose de golpe por ela (item 30; o desenho
///      girando e da `--diagv030`, G).
///   MI. AS MIRAGENS DA TECNICA -- longas, e com alvo o corpo ronda o alvo dando zanzokens; o duplo clique continua o de
///      sempre (item 33; quanto a miragem vive na tela e da `--diagv030`, MI).
///   PV. MAIS GENTE NOS PLANETAS CIVILIZADOS -- o plano a 2,5x cabendo nos tetos, e o raio do embaralho pelo tempo vazio
///      (item 31; o congelamento, o embaralho e o fio inteiros sao da `--embaralhoteste`).
///   RA. O MODO DAS RACAS -- a tabela do dono, o sorteio medido, a vaga que guarda o sorteio, a criacao que o obedece e o
///      admin que muda o modo (item 34; a aba Admin e a tela de criacao sao da `--diagv030`, RA).
///   CS. O CHEFE DE SAGA -- a cena da chegada pra todos, o ki que nao acaba e a cena de cada degrau que troca o corpo (item
///      35; a cena na tela e da `--diagv030`, CS).
///   B. O NPC ABSORVIDO PELO MAJIN -- some sem cadaver, e as frases dizem "absorve" (item 16).
///   S. O SOM DE VEGETA -- o ambiente e o da Terra (item 8).
///   C. OS AVISOS COM COR -- a raiva e o zenkai saem do servidor com o tom proprio (item 20; a cor desenhada no painel e
///      da `--diagv030`).
///   R. O RABO QUE NAO VOLTA CICATRIZA -- a ferida fecha em dez minutos e sai da mascara e do boneco (itens 3 e 19).
///   H. A RAIVA NO RETRATO DO PODER -- os bits que o HUD le pra escrever "COM RAIVA" e "FURIOSO" (item 4).
///   P. OS PESOS -- ate o limite do corpo de uma vez, e a arte escolhida que cai no chao junto (itens 9 e 10).
///   G. O MORTO DE PE ATIRA -- o fantasma do Outro Mundo usa ki e tecnicas e e alvo delas; o salto continua fechado (1b).
///   I. AS ILHAS DO OUTRO MUNDO PUXAM 10x -- o planeta do Sr. Kaioh (a ilha do norte) e as outras; o Caminho e o Enma,
///      1x (item 13).
///   M. O MAJIN -- desmaia de verdade e levanta antes de todo mundo; o golpe que o mataria o desfaz em pedacos por 30 s;
///      so o ki o mata (itens 2a e 2b).
///   A. O GOLPE AUTOMATICO -- as tecnicas que socam sozinhas viram o corpo pro alvo e armam a pose de soco (item 21).
///   E. A BEIRADA DO PLANETA -- a volta so na ultima celula (a porta do castelo de Vegeta se alcanca), e quem voa alto
///      nao sai do mapa (itens 6a e 6b).
///   N. O SR. KAIOH E O KORIN -- no lugar do DM (o Sr. Kaioh na ilha do norte), com a conversa e as licoes dele (item 18).
///   J. O PASSE DO ENMA -- 100.000 zeni, sete dias do jogo entre os vivos: morto, de aureola e DE PE; de volta a mesa do Enma
///      no fim (item 11).
///   L. A REENCARNACAO -- o personagem acaba pra sempre, a conta guarda o eco, e o proximo nasce com o BP da raca mais 10% do BP
///      do anterior e com o banco do potencial dele (item 7; 2026-10-26, como no DU). E o banco que nao mexe no Ki nem na raiva
///      (2026-10-26: o "modo Uub" do potencial antigo saiu).
///   T. O TRONO DE VEGETA -- o Rei Vegeta e o Principe nascem so com o trono vago; matar o Rei sendo Saiyajin o toma
///      (item 17).
///   D. O DESEJO DE LIBERAR O POTENCIAL -- de toda raca (so o potencial); o Namekuseijin com o poder do ki divino ganha
///      tambem o Mistico e o Orange (item 23).
///   U. A SUPERNOVA -- o ultimato do Freeza: o chefe ergue o sol e fica intocavel e parado ate a explosao (item 22).
///   F. OS ATAQUES DOS CHEFES -- o Freeza com o Death Beam e a Death Ball no arsenal da IA (item 5).
///   CC, GC, KK, GD. O ADENDO 12 -- o cano, a Genkidama em cima da cabeca, o Kaio-ken e a Genkidama que arrasta (itens 37 a
///      43): ver `GameServer.V030AdendoTeste.cs`.
///   KP. (adendo 16, v0.3.1) O KAIO-KEN PRESO NO SAVE -- o disco nao leva o buff aceso, e o login cura quem ja ficou preso
///      (item 47): ver `GameServer.V030AdendoTeste.cs`.
///   KS. (v0.4.2) O KAIO-KEN NAS FORMAS DE SUPER SAIYAJIN -- so no Blue, e so pra classe baixa e media com 50% de maestria
///      divina: ver `GameServer.V030AdendoTeste.cs`.
///   GS. (adendo 15, v0.3.1) A GENKIDAMA SO SOCA AO SAIR -- nada de pose ao formar e crescer, e o soco do lancamento ate a
///      trava soltar (item 46): ver `GameServer.V030AdendoTeste.cs`.
/// </summary>
public sealed partial class GameServer
{
	private int _v30Ok, _v30Falhou;

	private void AfirmarV30(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _v30Ok++; GD.Print($"[v030]   OK    {oque}"); return; }
		_v30Falhou++;
		GD.PrintErr($"[v030]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDaV030()
	{
		_v30Ok = _v30Falhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[v030] ================ A VERSAO 0.3.0: O PEDIDO DE 2026-10-15 ================");
		AfirmarV30("a zona da bancada tem colisao carregada", _pjMapa != null);
		if (_pjMapa == null) { GD.Print($"[v030] ================ {_v30Ok} passaram, {_v30Falhou} falharam ================"); return; }

		try
		{
			OEmpurraoNoMortoDePeV30();
			OVigorEOKiQueNaoAcabamV30();
			ONpcAbsorvidoSomeV30();
			OSomDeVegetaV30();
			OsAvisosComCorV30();
			OZenkaiEmBancoV30();
			ORaboQueNaoVoltaCicatrizaV30();
			ARaivaNoRetratoV30();
			OsPesosAteOLimiteEComArteV30();
			OsPesosQueLembramV30();
			OScouterQueVoltaLigadoV30();
			OSangueDoBioV30();
			OMortoNaMenteV30();
			OsRecursosDezVezesV30();
			OGiroV30();
			AsMiragensDaTecnicaV30();
			OPovoV30();
			OModoDasRacasV30();
			OChefeDeSagaV30();
			OMortoDePeAtiraV30();
			AsIlhasDoOutroMundoV30();
			OMajinV30();
			OGolpeAutomaticoV30();
			ABeiradaV30();
			OsNpcsDeConversaV30();
			OPasseDoEnmaV30();
			AReencarnacaoV30();
			OTronoDeVegetaV30();
			ODesejoDoPotencialV30();
			ASupernovaV30();
			OsAtaquesDosChefesV30();

			// O ADENDO 12 (`GameServer.V030AdendoTeste.cs`), NO FIM E NESTA ORDEM: as duas primeiras precisam de muro de pe, e o
			// estouro colossal da ultima racha o chao em volta.
			OCanoV30();
			AGenkidamaEmCimaDaCabecaV30();
			AGenkidamaSoSocaAoSairV30();   // (adendo 15) antes da GD, pelo corredor de terra firme que ela racha
			OKaiokenV30();
			OKaiokenNoSuperSaiyajinV30();   // (v0.4.2) antes da GD, pelo mesmo motivo da KP logo abaixo
			OKaiokenPresoNoSaveV30();   // (adendo 16) antes da GD: nao precisa de chao, mas a GD racha o que houver
			ADeathBallSoSocaAoSairV30();   // (adendo 18) antes da GD, pelo corredor de terra firme devolvido no fim
			AGenkidamaQueArrastaV30();
			// (adendo 18) DEPOIS da GD, porque nao precisam de muro: o Inferno, o Outro Mundo e um corredor novo
			OMortoLutaComoOVivoV30();
			APassagemDoMortoV30();
		}
		catch (Exception e)
		{
			AfirmarV30("a bancada rodou inteira, sem excecao", false, e.ToString());
		}
		finally
		{
			DesligarOsDefeitosV30();
			EscutaDeAvisos = null;
			EscutaDeTons = null;
			EscutaDeGestos = null;
			EscutaDeMiragens = null;
			LimparTudoDaBancada();
		}

		GD.Print($"[v030] ================ {_v30Ok} passaram, {_v30Falhou} falharam ================");
	}

	/// <summary>Nenhum botao de defeito sobrevive a bancada.</summary>
	private static void DesligarOsDefeitosV30()
	{
		MortoNaoVoaDeTeste = false;
		Nutricao.VigorDoMajinComPisoDeTeste = false;
		Nutricao.KiDoAndroideSemPisoDeTeste = false;
		Nutricao.MotorParaNaMorteDeTeste = false;
		AbsorvidoDeixaCorpoDeTeste = false;
		SoNpcDoMundoSomeDeTeste = false;
		AvisoSemTomDeTeste = false;
		Feridas.RaboPerdidoSangraPraSempreDeTeste = false;
		Jandirus.Core.Items.PesoDeTreino.SoDobrandoDeTeste = false;
		Jandirus.Core.Items.PesoDeTreino.ArteSoNoCorpoDeTeste = false;
		MortoNaoAtiraDeTeste = false;
		Alem.IlhasComoOCaminhoDeTeste = false;
		CombatState.DesmaioSemMinimoDeTeste = false;
		CombatState.MajinNaoSeDesfazDeTeste = false;
		Body.KaiEmComaDeTeste = false;
		BioKaiEmComaDeTeste = false;
		Jandirus.Core.Races.SangueDoBio.EmpateSemOKaiDeTeste = false;
		GolpeSemPoseDeTeste = false;
		VoltaNaFaixaDoDmDeTeste = false;
		ZoneCollision.QuemVoaSaiDoMapaDeTeste = false;
		EcoDaReencarnacaoIgnoradoDeTeste = false;
		PasseDeitaDeTeste = false;
		PasseSemVoltaDeTeste = false;
		Povoamento.TronoIgnoradoDeTeste = false;
		ReiNpcNaoEntregaOTronoDeTeste = false;
		RequisitoDoPotencialIgnoradoDeTeste = false;
		SupernovaSemImunidadeDeTeste = false;
		UltimatoSoSupernovaDeTeste = false;
		SupernovaComAsMaosLivresDeTeste = false;
		ChefeComRaioComumDeTeste = false;
		Nutricao.MortoComMeioTanqueDeTeste = false;
		Jandirus.Core.Races.SangueDoBio.SangueNaoPassaDeTeste = false;
		Jandirus.Core.Races.SangueDoBio.BioComOsPassivosDeAntesDeTeste = false;
		Alem.MenteDeitaOMortoDeTeste = false;
		ScouterAcordaDesligadoDeTeste = false;
		PesoDeTreino.PorcentagemEsquecidaDeTeste = false;
		GiroSemVoltaDeTeste = false;
		MiragemCurtaDeTeste = false;
		MiragemSemRondaDeTeste = false;
		Povoamento.RaioFixoDeTeste = false;
		SorteioIgnoradoDeTeste = false;
		Jandirus.Core.Races.SorteioDeRaca.PesosDeTeste = null;
		ChegadaSemCenaDeTeste = false;
		ChefeSemCenaDeTeste = false;
		FormaDeChefeSemEstreiaDeTeste = false;
		Nutricao.KiDoChefeFinitoDeTeste = false;
		EscutaDeChegadas = null;
		EscutaDeCenasDeChefe = null;
		CanoCegoDeTeste = false;
		GenkidamaComoAntesDeTeste = false;
		XNaoApagaOKaiokenDeTeste = false;
		KaiokenNoSuperSaiyajinDeTeste = false;
		ColossalMoiNoMuroDeTeste = false;
		BuffVaiProDiscoDeTeste = false;
		KaiokenAcesoAtravessaOLoginDeTeste = false;
		GenkidamaSocaAoCrescerDeTeste = false;
		GenkidamaLancaSemSocoDeTeste = false;
		CombatState.MortoNaoBloqueiaDeTeste = false;
		DeathBallSocaAoCrescerDeTeste = false;
		DeathBallLancaSemSocoDeTeste = false;
		MortoComoCadaverDeTeste = false;
		MortoNaoTransmiteDeTeste = false;
		PassagemSemVoltaDeTeste = false;
		PasseSemListaDeTeste = false;
		EscutaDeEscolhas = null;
	}

	// =====================================================================
	// U) O ULTIMATO DO FREEZA: A SUPERNOVA (item 22)
	// =====================================================================
	private void ASupernovaV30()
	{
		GD.Print("[v030] --- U) a Supernova: o chefe ergue um sol e fica imune e parado ate a explosao (item 22) ---");
		LimparTudoDaBancada();

		// OS PRAZOS DO CORE, que as duas pontas leem: brasa no comeco, cheio no fim da carga, os tres tiles do DM na queda
		bool prazos = Supernova.Crescimento(0) == Supernova.TamanhoDoPrimeiroBrilho && Supernova.Crescimento(Supernova.SegundosDeCarga) == 1
					  && Supernova.Crescimento(Supernova.SegundosDeCarga / 2) is > 0.5f and < 0.6f
					  && Supernova.ProgressoDaQueda(0) == 0 && Supernova.ProgressoDaQueda(Supernova.SegundosDaQueda) == 1
					  && Supernova.SegundosAteAExplosao == Supernova.SegundosDeCarga + Supernova.SegundosDaQueda
					  && Supernova.TilesDaQueda == 3;
		AfirmarV30("os prazos da Supernova: o sol nasce brasa e chega ao cheio no fim da carga, cai os tres tiles do DM, e a explosao e a carga mais a queda",
				   prazos);

		ServerPlayer chefe = ForjarV2("Freeza", TerraFirmeG12(4), 500_000, "Frost Demon");
		bool livreAntes = OcupacaoDe(chefe) == Ocupacao.Livre && PodeMexerOCorpo(chefe) && !chefe.Combate!.Intocavel;
		List<string> fala = Ouvir(() => ComecarASupernova(chefe));
		bool concentrando = OcupacaoDe(chefe) == Ocupacao.Supernova && chefe.Combate!.Intocavel && !PodeMexerOCorpo(chefe)
							&& chefe.CenaSegundos >= Supernova.SegundosAteAExplosao;
		AfirmarV30("**A SUPERNOVA**: o chefe a ergue e fica `Supernova` (o que a tela le pra desenhar o sol), INTOCAVEL e PARADO ate a explosao -- *\"nesse momento ele fica imune aos ataques\"*",
				   livreAntes && concentrando,
				   $"ocupacao {OcupacaoDe(chefe)}; intocavel {chefe.Combate.Intocavel}; anda {PodeMexerOCorpo(chefe)}; cena {chefe.CenaSegundos:0.#} | {Ultimos(fala)}");

		// ...E COM O SOL NA MAO ELE NAO AGE. Um soco pesado num alvo a tres tiles (o que INVESTE ate ele) pelo atuador da IA
		// de producao (`AplicarComando`): a cena prende o passo, mas a investida do soco passa por fora dela -- quem recusa e
		// a porta das maos ocupadas (`MaosOcupadasDaIa`).
		ServerPlayer alvo = ForjarV2("Alvo", chefe.Pos + new Vec2(3 * ZoneCollision.TileSize, 0), 1_000);
		bool AgeComOSol()
		{
			Vec2 onde = chefe.Pos;
			long ataque = chefe.AtaqueAte;
			double vida = alvo.Ficha.HP;
			chefe.Combate!.Recarga = 0;
			AplicarComando(chefe, new Jandirus.Core.Ai.Comando { Pesado = true, Marcar = alvo.Id }, Protocol.TickSeconds);
			return (chefe.Pos - onde).Length > 1 || chefe.AtaqueAte != ataque || alvo.Ficha.HP < vida;
		}
		bool quieto = !AgeComOSol();
		AfirmarV30("...e com o sol na mao ELE NAO AGE: o soco pesado num alvo a tres tiles (o que investe ate ele) e recusado pelo atuador da IA -- na primeira rodada o Freeza nocauteava quem estivesse perto",
				   quieto, $"ocupacao {OcupacaoDe(chefe)}; ataque {chefe.AtaqueAte}");
		SupernovaComAsMaosLivresDeTeste = true;
		bool agiu;
		try { agiu = AgeComOSol(); }
		finally
		{
			SupernovaComAsMaosLivresDeTeste = false;
			// o soco que o defeito deixou sair fica de fora das linhas seguintes (o corpo leria "atacando" ate o prazo dele)
			chefe.AtaqueAte = 0;
			chefe.Combate!.Recarga = 0;
		}
		AfirmarV30("   DEFEITO INJETADO (a Supernova com as maos livres): o mesmo comando sai e o chefe investe e soca com o sol erguido -- a afirmacao de cima ficaria vermelha (defeito injetado: ele agiu)",
				   agiu);

		AcabarASupernova(chefe);
		AfirmarV30("...e desfeita antes da hora (o servidor que esvaziou no meio da carga), o corpo volta a ser livre",
				   OcupacaoDe(chefe) == Ocupacao.Livre && PodeMexerOCorpo(chefe) && !chefe.Combate.Intocavel, $"ocupacao {OcupacaoDe(chefe)}");

		SupernovaSemImunidadeDeTeste = true;
		ComecarASupernova(chefe);
		bool tocavel = OcupacaoDe(chefe) == Ocupacao.Supernova && !chefe.Combate.Intocavel;
		AcabarASupernova(chefe);
		SupernovaSemImunidadeDeTeste = false;
		AfirmarV30("   DEFEITO INJETADO (a Supernova sem imunidade): o chefe concentraria o sol tomando golpe -- a afirmacao de cima ficaria vermelha (defeito injetado: o chefe tocavel)",
				   tocavel);

		// ============================ (2026-10-17) A DEATH BALL DE NAMEK, PELA MESMA PORTA ============================
		// O dono: *"a super nova e so quando ele explode o planeta vegeta, namek e death ball ao qual ele joga pra baixo seguindo a
		// mesma logica da super nova"*. Quem diz a esfera e a saga (`ataqueDoUltimato` no `npcs.json`); o resto e o mesmo metodo.
		// =================================================================================================================
		EloDaSaga? deVegeta = _moldes?.Cadeia.FirstOrDefault(e => e.Id == "freeza_vegeta");
		EloDaSaga? deNamek = _moldes?.Cadeia.FirstOrDefault(e => e.Id == "freeza_namek");
		AfirmarV30("**CADA SAGA COM A SUA ESFERA** (o `npcs.json`): o Freeza de Vegeta ergue a SUPERNOVA, o de Namek a DEATH BALL -- e o anuncio de Namek diz Death Ball",
				   deVegeta?.AtaqueDoUltimato == AtaqueDoUltimato.Supernova && deNamek?.AtaqueDoUltimato == AtaqueDoUltimato.DeathBall
				   && deNamek.AnuncioDoUltimato.Contains("DEATH BALL", StringComparison.Ordinal),
				   $"Vegeta {deVegeta?.AtaqueDoUltimato}; Namek {deNamek?.AtaqueDoUltimato}: \"{deNamek?.AnuncioDoUltimato}\"");
		List<string> falaDaBola = Ouvir(() => ComecarASupernova(chefe, AtaqueDoUltimato.DeathBall));
		bool bola = OcupacaoDe(chefe) == Ocupacao.DeathBallDoUltimato && chefe.Combate!.Intocavel && !PodeMexerOCorpo(chefe)
							  && chefe.CenaSegundos >= Supernova.SegundosAteAExplosao;
		AfirmarV30("**A DEATH BALL DE NAMEK**: a mesma regra da Supernova -- o chefe a ergue e fica `DeathBallDoUltimato` (o que a tela le pra desenhar a esfera roxa), INTOCAVEL e PARADO ate a explosao",
				   bola, $"ocupacao {OcupacaoDe(chefe)}; intocavel {chefe.Combate.Intocavel}; anda {PodeMexerOCorpo(chefe)}; cena {chefe.CenaSegundos:0.#} | {Ultimos(falaDaBola)}");
		AcabarASupernova(chefe);
		UltimatoSoSupernovaDeTeste = true;
		bool soOSol;
		try
		{
			ComecarASupernova(chefe, AtaqueDoUltimato.DeathBall);
			soOSol = OcupacaoDe(chefe) == Ocupacao.Supernova;
		}
		finally
		{
			AcabarASupernova(chefe);
			UltimatoSoSupernovaDeTeste = false;
		}
		AfirmarV30("   DEFEITO INJETADO (todo ultimato e Supernova): o Freeza de Namek ergueria o sol de Vegeta -- a afirmacao de cima ficaria vermelha (defeito injetado: a ocupacao e Supernova)",
				   soOSol);

		AfirmarV30("...e o byte do fio conhece os estados novos (`CorpoOcupado.DeByte`) -- e um valor acima deles continua lendo Livre, nunca um estado inventado",
				   CorpoOcupado.DeByte((byte)Ocupacao.Supernova) == Ocupacao.Supernova
				   && CorpoOcupado.DeByte((byte)Ocupacao.DeathBallDoUltimato) == Ocupacao.DeathBallDoUltimato
				   && CorpoOcupado.DeByte((byte)Ocupacao.CargaDemoniaca) == Ocupacao.CargaDemoniaca
				   && CorpoOcupado.DeByte((byte)Ocupacao.Secando) == Ocupacao.Secando
				   // (2026-10-17) o grito do desabafo do Lendario -- os de 15 pra cima viajam pela porta da ocupacao estendida
				   && CorpoOcupado.DeByte((byte)Ocupacao.GritoDoDesabafo) == Ocupacao.GritoDoDesabafo
				   // (2026-10-18) e o Sleep do Majin, que veio depois dele. O "acima deles" e o MAIOR do `enum`, e nao um nome cravado: a regua
				   // cravava o 17, e o 17 virou o `Dormindo`.
				   && CorpoOcupado.DeByte((byte)Ocupacao.Dormindo) == Ocupacao.Dormindo
				   && CorpoOcupado.DeByte((byte)((byte)Enum.GetValues<Ocupacao>().Max() + 1)) == Ocupacao.Livre);

		// (2026-10-17) A PORTA DA OCUPACAO ESTENDIDA, NO FIO: com 15 nos 4 bits o valor de verdade vai num byte no FIM do pacote, depois da
		// altitude, do canal e de quem leva no colo -- e so quem esta num estado de 15 pra cima o paga.
		static EntityState Volta(EntityState e, out int bytes)
		{
			var w = new LiteNetLib.Utils.NetDataWriter();
			e.Write(w);
			bytes = w.Length;
			return EntityState.Read(new LiteNetLib.Utils.NetDataReader(w.CopyData()));
		}
		EntityState Cheio(Ocupacao o) => new()
		{
			Id = 7, Voando = true, Altitude = 64, Pose = Protocol.Pose.Canalizando, Canal = 3, LevadoPor = 9, Ocupacao = o, Kaioken = true,
		};
		EntityState demonico = Volta(Cheio(Ocupacao.CargaDemoniaca), out int bytesDoDemonico);
		EntityState secando = Volta(Cheio(Ocupacao.Secando), out int bytesDoSecando);
		EntityState grito = Volta(Cheio(Ocupacao.GritoDoDesabafo), out int bytesDoGrito);
		AfirmarV30("...e a PORTA DA OCUPACAO ESTENDIDA vai e volta pelo fio: o 15 e o 16 chegam inteiros (junto da altitude, do canal, do colo e do "
				   + "Kaio-ken), e custam UM byte a mais que o 14 -- so pra quem esta neles",
				   demonico.Ocupacao == Ocupacao.CargaDemoniaca && secando.Ocupacao == Ocupacao.Secando && grito.Ocupacao == Ocupacao.GritoDoDesabafo
				   && grito.LevadoPor == 9 && grito.Canal == 3 && grito.Voando && grito.Kaioken
				   && bytesDoSecando == bytesDoDemonico + 1 && bytesDoGrito == bytesDoDemonico + 1);
	}

	// =====================================================================
	// F) OS CHEFES COM OS ATAQUES DELES (item 5)
	// =====================================================================
	private void OsAtaquesDosChefesV30()
	{
		GD.Print("[v030] --- F) os chefes de saga com os ataques deles: o Freeza com o Death Beam e a Death Ball (item 5) ---");
		LimparTudoDaBancada();
		Vec2 c = TerraFirmeG12(4);
		var nascidos = new List<ServerPlayer>();
		try
		{
			ServerPlayer? Nascer(string molde)
			{
				ServerPlayer? n = NascerNpc(molde, ZonaDaBancadaDeProjetil, c + new Vec2(nascidos.Count * 2 * ZoneCollision.TileSize, 0),
											9_100 + (ulong)nascidos.Count);
				if (n != null) nascidos.Add(n);
				return n;
			}
			ServerPlayer? freezaV = Nascer("freeza_vegeta"), freezaN = Nascer("freeza_namek"), cell = Nascer("cell"), boo = Nascer("majin_boo");

			bool Sabe(ServerPlayer? p, params string[] ids) => p != null && ids.All(id => SabeTecnica(p, id));
			bool NoArsenal(ServerPlayer? p, params string[] ids)
			{
				if (p == null) return false;
				Jandirus.Core.Ai.Arsenal a = ArsenalDeLonge(p);
				return ids.All(id => Enumerable.Range(0, a.Quantas).Any(i => a[i].Id == id));
			}
			string Lista(ServerPlayer? p)
			{
				if (p == null) return "(nao nasceu)";
				Jandirus.Core.Ai.Arsenal a = ArsenalDeLonge(p);
				return string.Join(",", Enumerable.Range(0, a.Quantas).Select(i => a[i].Id));
			}

			AfirmarV30("**O FREEZA TEM OS ATAQUES DELE**: o das duas sagas sabe o Death Beam e a Death Ball -- e os dois estao no arsenal de longe que o cerebro dele usa (o mesmo funil do jogador)",
					   Sabe(freezaV, "Death_Beam", "Death_Ball") && Sabe(freezaN, "Death_Beam", "Death_Ball")
					   && NoArsenal(freezaV, "Death_Beam", "Death_Ball") && NoArsenal(freezaN, "Death_Beam", "Death_Ball"),
					   $"Vegeta [{Lista(freezaV)}]; Namek [{Lista(freezaN)}]");
			AfirmarV30("...e os outros chefes os deles: o Cell com o Kamehameha e o Death Beam, o Majin Boo com o Kamehameha",
					   Sabe(cell, "Kamehameha", "Death_Beam") && Sabe(boo, "Kamehameha") && NoArsenal(cell, "Kamehameha", "Death_Beam") && NoArsenal(boo, "Kamehameha"),
					   $"Cell [{Lista(cell)}]; Boo [{Lista(boo)}]");
		}
		finally
		{
			foreach (ServerPlayer n in nascidos)
				if (_players.ContainsKey(n.Id)) RemoverNpc(n);
		}
	}

	// =====================================================================
	// D) O DESEJO DE LIBERAR O POTENCIAL (item 23)
	// =====================================================================
	private void ODesejoDoPotencialV30()
	{
		GD.Print("[v030] --- D) o dragao libera o potencial de qualquer raca; o Namekuseijin com o poder do ki divino ganha o Mistico e o Orange (item 23) ---");
		LimparTudoDaBancada();
		Vec2 c = TerraFirmeG12(4);
		const float tile = ZoneCollision.TileSize;
		var set = new SetDeEsferas();
		string mistico = Jandirus.Core.Forms.Catalogo.IdMistico, orange = Jandirus.Core.Forms.Catalogo.IdOrange;

		bool naTabela = Jandirus.Core.Magic.Desejos.Menu(1e12, 1, false, false, 0).Any(d => d.Id == "potencial")
						&& Jandirus.Core.Magic.Desejos.Menu(1e12, 3, false, false, 100).Any(d => d.Id == "potencial");
		AfirmarV30("**O DESEJO E DE TODA RACA**: a tabela do dragao o oferece sem olhar a raca (o Shenlong de um pedido e o Porunga de tres)", naTabela);

		// O SAIYAJIN: o potencial, e mais nada -- e uma vez so
		ServerPlayer saiyajin = ForjarV2("Kakarotto", c, 5_000_000, "Saiyan");
		double bp0 = saiyajin.Ficha.BP;
		List<string> doSaiyajin = Ouvir(() => DesejoDePotencial(saiyajin, set));
		bool soPotencial = saiyajin.Ficha.unlockPotential >= 1 && saiyajin.Ficha.BP > bp0
						   && !saiyajin.Forma.Despertou(mistico) && !saiyajin.Forma.Despertou(orange);
		bool deNovoNao = !DesejoDePotencial(saiyajin, set) && PorQueNaoLiberaOPotencial(saiyajin) != null;
		AfirmarV30("...as outras racas pedem a QUALQUER momento e so ganham a liberacao do potencial: o Saiyajin de cinco milhoes desperta o potencial e mais nada -- e pedir de novo e recusado sem gastar",
				   soPotencial && deNovoNao, $"potencial {saiyajin.Ficha.unlockPotential}; BP {bp0:0} -> {saiyajin.Ficha.BP:0} | {Ultimos(doSaiyajin)}");

		// O NAMEKUSEIJIN SEM O PODER DO KI DIVINO: nao pede (nada desperta, nada e gasto) -- e o motivo nao diz numero
		ServerPlayer namek = ForjarV2("Nail", c + new Vec2(2 * tile, 0), 5_000_000, "Namekian");
		string? porqueFraco = PorQueNaoLiberaOPotencial(namek);
		bool fracoRecusado = !DesejoDePotencial(namek, set) && namek.Ficha.unlockPotential < 1
							 && !namek.Forma.Despertou(mistico) && !namek.Forma.Despertou(orange);
		AfirmarV30("**O NAMEKUSEIJIN PRECISA DO PODER DO KI DIVINO DELE**: abaixo do requisito pessoal ele nao pede -- e a recusa nao diz numero de poder (a regra do BP)",
				   porqueFraco != null && porqueFraco.Contains("ki divino", StringComparison.Ordinal) && !porqueFraco.Any(char.IsDigit) && fracoRecusado,
				   porqueFraco ?? "(sem recusa)");

		RequisitoDoPotencialIgnoradoDeTeste = true;
		bool semRequisito = PorQueNaoLiberaOPotencial(namek) == null;
		RequisitoDoPotencialIgnoradoDeTeste = false;
		AfirmarV30("   DEFEITO INJETADO (o requisito ignorado): o Namekuseijin de cinco milhoes ganharia o Mistico e o Orange -- a afirmacao de cima ficaria vermelha (defeito injetado: o Namek sem requisito)",
				   semRequisito);

		// COM O PODER: o potencial, o MISTICO e o ORANGE, num pedido so -- e depois nao ha mais o que pedir
		namek.Ficha.BP = Math.Max(namek.Ficha.BP, RequisitoDeGodKi(namek));
		List<string> doNamek = Ouvir(() => DesejoDePotencial(namek, set));
		bool tudo = namek.Ficha.unlockPotential >= 1 && namek.Forma.Despertou(mistico) && namek.Forma.Despertou(orange);
		bool acabou = PorQueNaoLiberaOPotencial(namek) is { } fim && fim.Contains("tudo", StringComparison.Ordinal) && !DesejoDePotencial(namek, set);
		AfirmarV30("...com o poder do ki divino (o BP base no requisito pessoal) o Namekuseijin ganha o potencial, o MISTICO e o ORANGE num pedido so -- e depois nao ha mais o que pedir",
				   tudo && acabou && Disse(doNamek, "Potential Unleashed") && Disse(doNamek, "ORANGE"), Ultimos(doNamek));

		// (2026-10-23, o dono) A FORMA DO DESEJO NAO SOME NO TIQUE SEGUINTE. Ate o Ritual Mistico do Velho Kaiohshin, a forma do Mistico
		// (hoje o Potential Unleashed) seguia a skill `kai/Mystic` do kit do Kaioshin: a reconciliacao de 1 Hz dos cargos
		// (`ReconciliarDadiva`, pelo `ReconciliarODomDoMistico`) fechava a forma de quem nao tinha a skill -- e o Namekuseijin que a
		// ganhava do dragao a perdia 1 s depois. Agora a forma e um direito gravado: tres reconciliacoes depois, ela continua.
		for (int i = 0; i < 3; i++) ReconciliarDadiva(namek);
		bool ficou = namek.Forma.Despertou(mistico) && namek.Forma.Despertou(orange);
		string comoFicou = $"mistico {namek.Forma.Despertou(mistico)}; orange {namek.Forma.Despertou(orange)}";
		FormaSegueASkillDeTeste = true;
		try { ReconciliarDadiva(namek); }
		finally { FormaSegueASkillDeTeste = false; }
		bool comODefeito = !namek.Forma.Despertou(mistico);
		AfirmarV30("**A FORMA DO DESEJO NAO SOME NO TIQUE SEGUINTE** (2026-10-23): o Potential Unleashed que o dragao deu ao Namekuseijin continua depois de tres reconciliacoes dos cargos (a de 1 Hz) -- a forma nao segue mais a skill do Kaioshin",
				   ficou, comoFicou);
		AfirmarV30("   DEFEITO INJETADO (a forma que segue a skill, como antes do ritual): a mesma reconciliacao fecharia a forma do desejo -- a afirmacao de cima ficaria vermelha (defeito injetado: a forma segue a skill)",
				   comODefeito);
	}

	// =====================================================================
	// T) O TRONO DE VEGETA: o Rei e o Principe NPCs (item 17)
	// =====================================================================
	private void OTronoDeVegetaV30()
	{
		GD.Print("[v030] --- T) o Rei Vegeta e o Principe nascem com o trono vago, e matar o Rei sendo Saiyajin toma o trono (item 17) ---");
		LimparTudoDaBancada();
		ZoneKey vegeta = ZoneKey.Premade("Vegeta");
		LinhaDePovoamento? linhaDoRei = _moldes?.Plano.FirstOrDefault(l => l.Molde == MoldeDoReiVegeta);
		LinhaDePovoamento? linhaDoPrincipe = _moldes?.Plano.FirstOrDefault(l => l.Molde == MoldeDoPrincipeVegeta);
		bool vegetaViva = _catalogo?.Get(vegeta) != null && !ZonaCondenada(vegeta);
		AfirmarV30("**O PLANO DE VEGETA TEM O REI E O PRINCIPE**: um de cada, e os dois so com o trono de Vegeta VAGO (`tronoVago: kov`) -- e Vegeta esta de pe",
				   linhaDoRei is { Planeta: "Vegeta", Quantos: 1 } && linhaDoRei.TronoVago == Sucessao.ReiDeVegeta
				   && linhaDoPrincipe is { Planeta: "Vegeta", Quantos: 1 } && linhaDoPrincipe.TronoVago == Sucessao.ReiDeVegeta && vegetaViva,
				   $"rei {linhaDoRei?.Planeta}/{linhaDoRei?.Quantos}/{linhaDoRei?.TronoVago}; principe {linhaDoPrincipe?.Planeta}/{linhaDoPrincipe?.Quantos}/{linhaDoPrincipe?.TronoVago}; vegeta {vegetaViva}");
		if (linhaDoRei == null || linhaDoPrincipe == null || !vegetaViva) return;

		// A MANUTENCAO DE PRODUCAO, nas duas maos. A realeza que ja estiver de pe (o boot a poe no mundo) conta como morta
		// durante a medida -- e o que faz a manutencao QUERER repor -- e volta no fim; a fila e os tronos tambem voltam.
		List<ServerPlayer> realeza = [.. _players.Values.Where(p => EhNpcDoMundo(p) && !p.Ficha.dead
									  && p.Papel!.Molde.Id is MoldeDoReiVegeta or MoldeDoPrincipeVegeta)];
		var filaAntes = new List<(string Molde, ZoneKey Zona, ulong Lugar)>(_filaDoPovoamento);
		var tronosAntes = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		int RealezaNaFila() => _filaDoPovoamento.Count(f => f.Molde is MoldeDoReiVegeta or MoldeDoPrincipeVegeta);
		int comRei, vago, defeito;
		try
		{
			foreach (ServerPlayer r in realeza) r.Ficha.dead = true;
			_tronos[Sucessao.ReiDeVegeta] = "bancada_outro_rei";
			_filaDoPovoamento.Clear();
			Manutencao();
			comRei = RealezaNaFila();
			Povoamento.TronoIgnoradoDeTeste = true;
			_filaDoPovoamento.Clear();
			Manutencao();
			defeito = RealezaNaFila();
			Povoamento.TronoIgnoradoDeTeste = false;
			_tronos.Remove(Sucessao.ReiDeVegeta);
			_filaDoPovoamento.Clear();
			Manutencao();
			vago = RealezaNaFila();
		}
		finally
		{
			Povoamento.TronoIgnoradoDeTeste = false;
			_filaDoPovoamento.Clear();
			foreach (var f in filaAntes) _filaDoPovoamento.Enqueue(f);
			_tronos.Clear();
			foreach (KeyValuePair<string, string> kv in tronosAntes) _tronos[kv.Key] = kv.Value;
			foreach (ServerPlayer r in realeza) r.Ficha.dead = false;
		}
		AfirmarV30("**A MANUTENCAO SO OS POE COM O TRONO VAGO** (`populate_vegeta`, PlanetPopulation.dm:435): com um jogador no trono nem o Rei nem o Principe entram na fila; vago, os dois",
				   comRei == 0 && vago == 2, $"com rei {comRei}; vago {vago}; ja de pe {realeza.Count}");
		AfirmarV30("   DEFEITO INJETADO (o trono ignorado): com um jogador no trono, a manutencao poria OUTRO Rei no mundo -- a afirmacao de cima ficaria vermelha (defeito injetado: dois reis)",
				   defeito == 2, $"{defeito}");

		// A FABRICA DE PRODUCAO: o kit de elite, o nome, o cabelo e o poder do DM
		Vec2 sala = PontoDeNascimento(vegeta);
		ServerPlayer? rei = NascerNpc(MoldeDoReiVegeta, vegeta, sala, 9_001);
		ServerPlayer? principe = NascerNpc(MoldeDoPrincipeVegeta, vegeta, sala + new Vec2(2 * ZoneCollision.TileSize, 0), 9_002);
		var tronosAntes2 = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		try
		{
			static bool Veste(ServerPlayer p, string folha) => p.Visual.Roupa.Exists(r => r.Caminho.Contains(folha, StringComparison.Ordinal));
			AfirmarV30("**O REI VEGETA E O PRINCIPE**: Saiyajins de elite com o nome deles e o cabelo do Vegeta; o Rei de armadura de elite e CAPA, o Principe com a da saga Saiyajin; o poder do DM (Rei 18-22 mil, Principe 14-18 mil)",
					   rei is { Name: "Rei Vegeta", Race: "Saiyan" } && principe is { Name: "Príncipe Vegeta", Race: "Saiyan" }
					   && rei.Ficha.BP is >= 18_000 and <= 22_000 && principe.Ficha.BP is >= 14_000 and <= 18_000
					   && rei.Visual.Cabelo == "Vegeta" && principe.Visual.Cabelo == "Vegeta"
					   && Veste(rei, "Armor_Elite") && Veste(rei, "KingVegetaCape") && Veste(principe, "VegetaSaiyanSagaArmor") && !Veste(principe, "Cape"),
					   $"rei {rei?.Name} {rei?.Race} BP {rei?.Ficha.BP:0} cabelo {rei?.Visual.Cabelo} [{string.Join(", ", rei?.Visual.Roupa.Select(r => r.Caminho) ?? [])}]; "
					   + $"principe {principe?.Name} {principe?.Race} BP {principe?.Ficha.BP:0} cabelo {principe?.Visual.Cabelo}");
			if (rei == null || principe == null) return;

			// QUEM MATA O REI: o humano so o derruba (o trono continua vago); o Saiyajin toma o trono
			_tronos.Remove(Sucessao.ReiDeVegeta);
			ServerPlayer humano = ForjarV2("Plebeu", rei.Pos + new Vec2(ZoneCollision.TileSize, 0), 30_000, "Human");
			MoveToZone(humano.Id, vegeta, rei.Pos + new Vec2(ZoneCollision.TileSize, 0));
			rei.Ficha.dead = true;
			List<string> caiu = Ouvir(() => AoPerderALuta(rei, humano, morreu: true));
			bool vagoDepoisDoHumano = TronoVago(Sucessao.ReiDeVegeta);

			ServerPlayer saiyajin = ForjarV2("Regicida", rei.Pos + new Vec2(-ZoneCollision.TileSize, 0), 30_000, "Saiyan");
			MoveToZone(saiyajin.Id, vegeta, rei.Pos + new Vec2(-ZoneCollision.TileSize, 0));
			ServerPlayer? rei2 = NascerNpc(MoldeDoReiVegeta, vegeta, sala, 9_003);
			ReiNpcNaoEntregaOTronoDeTeste = true;
			if (rei2 != null) { rei2.Ficha.dead = true; AoPerderALuta(rei2, saiyajin, morreu: true); }
			bool semTronoComDefeito = TronoVago(Sucessao.ReiDeVegeta);
			ReiNpcNaoEntregaOTronoDeTeste = false;
			if (rei2 != null) RemoverNpc(rei2);

			ServerPlayer? rei3 = NascerNpc(MoldeDoReiVegeta, vegeta, sala, 9_004);
			List<string> tomou = rei3 == null ? [] : Ouvir(() => { rei3.Ficha.dead = true; AoPerderALuta(rei3, saiyajin, morreu: true); });
			bool reiAgora = _tronos.TryGetValue(Sucessao.ReiDeVegeta, out string? dono) && string.Equals(dono, saiyajin.Conta, StringComparison.OrdinalIgnoreCase);
			if (rei3 != null) RemoverNpc(rei3);

			AfirmarV30("**MATAR O REI SENDO SAIYAJIN TOMA O TRONO** (`grant_throne()`, PlanetPopulation.dm:193-211): o humano so o derruba (\"o Rei de Vegeta caiu!\", o trono continua vago); o Saiyajin vira o Rei de Vegeta",
					   Disse(caiu, "o Rei de Vegeta caiu") && vagoDepoisDoHumano && reiAgora && Disse(tomou, "Rei de Vegeta"),
					   $"vago depois do humano {vagoDepoisDoHumano}; rei agora {reiAgora} ({dono}) | {Ultimos(caiu)} / {Ultimos(tomou)}");
			AfirmarV30("   DEFEITO INJETADO (o Rei NPC nao entrega o trono): o Saiyajin mataria o Rei e o trono continuaria vago -- a afirmacao de cima ficaria vermelha (defeito injetado: o trono que nao passa)",
					   semTronoComDefeito);
		}
		finally
		{
			ReiNpcNaoEntregaOTronoDeTeste = false;
			if (rei != null && _players.ContainsKey(rei.Id)) RemoverNpc(rei);
			if (principe != null && _players.ContainsKey(principe.Id)) RemoverNpc(principe);
			_tronos.Clear();
			foreach (KeyValuePair<string, string> kv in tronosAntes2) _tronos[kv.Key] = kv.Value;
			SalvarCargos();
		}
	}

	// =====================================================================
	// J) O PASSE DO ENMA (item 11)
	// =====================================================================
	private void OPasseDoEnmaV30()
	{
		GD.Print("[v030] --- J) o passe do Enma: 100.000 zeni, sete dias entre os vivos, morto e de pe (item 11) ---");
		LimparTudoDaBancada();
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo), terra = ZoneKey.Premade("Earth");
		Obra? enma = _noChao.Find(o => o.Tipo == Alem.TipoDoEnma && o.Zona.Equals(alem));
		bool temTerra = _catalogo?.Get(terra) != null;
		AfirmarV30("(montagem) o Enma esta na mesa dele, e a Terra tem mapa", enma != null && temTerra);
		if (enma == null || !temTerra) return;
		Vec2 aoLado = new(enma.X + ZoneCollision.TileSize, enma.Y);
		List<NomeDePlaneta> livroAntes = [.. _batismos.Todos];

		ServerPlayer pl = ForjarV2("Visitante", TerraFirmeG12(4), 10_000);
		void NaMesa(double zeni)
		{
			MoveToZone(pl.Id, alem, aoLado);
			pl.Ficha.dead = true;
			pl.Ficha.KO = false;
			pl.MorteJaViajou = true;
			pl.RelogioDaMorte = long.MaxValue;
			pl.Karma = 0;
			pl.Ficha.passe_do_enma_ate = 0;
			pl.Ficha.Zeni = zeni;
		}
		try
		{
			// A LISTA E AS RECUSAS: sem destino o Enma abre o livro (os pre-feitos pelo nome que se le); sem o dinheiro, nada;
			// e mundo que ele nao conhece, tambem nao
			NaMesa(Alem.PrecoDoPasseDoEnma - 1);
			List<string> livro = Ouvir(() => EnmaPasse(pl, ""));
			List<string> pobre = Ouvir(() => EnmaPasse(pl, "Terra"));
			List<string> ninguem = Ouvir(() => EnmaPasse(pl, "Mundo Que Nao Existe"));
			// (adendo 18) A MESMA LISTA VAI PRO MENU (`S2C.Escolhas`): um botao por mundo, e o clique manda o nome -- nada de digitar
			var escolhas = new List<(int Quem, string Verbo, List<string> Itens)>();
			EscutaDeEscolhas = escolhas;
			try { Ouvir(() => EnmaPasse(pl, "")); }
			finally { EscutaDeEscolhas = null; }
			List<string> nomes = [.. DestinosDoPasse().Select(x => x.Nome)];
			bool lista = escolhas.Count == 1 && escolhas[0].Quem == pl.Id && escolhas[0].Verbo == "enma_passe"
							  && escolhas[0].Itens.SequenceEqual(nomes) && nomes.Contains("Terra") && nomes.Contains("Namek");
			var semLista = new List<(int Quem, string Verbo, List<string> Itens)>();
			EscutaDeEscolhas = semLista;
			PasseSemListaDeTeste = true;
			try { Ouvir(() => EnmaPasse(pl, "")); }
			finally { PasseSemListaDeTeste = false; EscutaDeEscolhas = null; }
			AfirmarV30("**A LISTA DOS MUNDOS VAI PRO MENU** (*\"deveria ser uma lista e nao ter q digitar o nome\"*): a pergunta sem nome manda as escolhas (`S2C.Escolhas`) -- exatamente os destinos do passe, a Terra e Namek entre eles",
						   lista, escolhas.Count == 0 ? "nada saiu" : string.Join(", ", escolhas[0].Itens));
			AfirmarV30("   DEFEITO INJETADO (o livro so no chat, o jogo de antes): nenhuma lista sai -- a afirmacao de cima ficaria vermelha (defeito injetado: o passe sem lista)",
						   semLista.Count == 0);
			AfirmarV30("o Enma abre o livro dos mundos (a Terra pelo nome que se le, Namek) e cobra em zeni: sem os 100.000 ninguem desce -- e mundo desconhecido e recusado",
					   Disse(livro, "Terra") && Disse(livro, "Namek") && Disse(livro, "zeni") && Disse(pobre, "custa") && Disse(ninguem, "Não conheço")
					   && Alem.EhOAlem(pl.Zone) && pl.Ficha.passe_do_enma_ate == 0 && pl.Ficha.Zeni == Alem.PrecoDoPasseDoEnma - 1,
					   $"{Ultimos(livro)} / {Ultimos(pobre)} / {Ultimos(ninguem)}");

			// O PASSE: pago, desce na Terra MORTO e DE PE, com a aureola, por sete dias do jogo
			NaMesa(Alem.PrecoDoPasseDoEnma + 5);
			long antes = NowMs();
			List<string> desce = Ouvir(() => EnmaPasse(pl, "terra"));
			long prazo = pl.Ficha.passe_do_enma_ate - antes;
			AfirmarV30("**O PASSE DO ENMA**: 100.000 zeni e o morto desce na Terra -- CONTINUA MORTO, de auréola, mas DE PE: anda (o funil do passo), nao e cadaver (os golpes e as tecnicas o enxergam) e o cliente sabe (o bit da ficha)",
					   pl.Zone.Equals(terra) && pl.Ficha.dead && pl.MortoDePe && !EhCadaver(pl) && PodeMexerOCorpo(pl) && !pl.Deitado
					   && Alem.TemAureola(pl.Ficha.dead, pl.MorteJaViajou) && pl.Sheet().EmVisita && Math.Abs(pl.Ficha.Zeni - 5) < 1e-6,
					   $"zona {pl.Zone.Name}; morto {pl.Ficha.dead}; de pe {pl.MortoDePe}; cadaver {EhCadaver(pl)}; anda {PodeMexerOCorpo(pl)}; zeni {pl.Ficha.Zeni} | {Ultimos(desce)}");
			AfirmarV30("...por SETE DIAS DO JOGO (7 x 24 min de relogio) -- e quem reloga no meio volta de auréola e de pe (o palpite do login le o passe)",
					   Math.Abs(prazo - Alem.MsDoPasseDoEnma) < 2000 && Alem.MsDoPasseDoEnma == (long)(7 * Espaco.SegundosPorDiaInGame * 1000) && JaViajouAoEntrar(pl),
					   $"prazo {prazo} ms (esperado {Alem.MsDoPasseDoEnma})");

			PasseDeitaDeTeste = true;
			bool deitado = !pl.MortoDePe && EhCadaver(pl) && !PodeMexerOCorpo(pl) && pl.Deitado;
			PasseDeitaDeTeste = false;
			AfirmarV30("   DEFEITO INJETADO (o passe nao levanta o morto, o jogo de antes): entre os vivos ele seria o cadaver deitado -- a afirmacao de cima ficaria vermelha (defeito injetado: o morto do passe deitado)",
					   deitado);

			// FALTANDO UM DIA, o aviso; VENCIDO, de volta a mesa do Enma (morto, de auréola, sem passe)
			pl.Ficha.passe_do_enma_ate = NowMs() + Alem.MsDoPasseDoEnma / Alem.DiasDoPasseDoEnma - 1000;
			List<string> falta = Ouvir(() => CumprirOPasseDoEnma(pl));
			pl.Ficha.passe_do_enma_ate = NowMs() - 1;
			PasseSemVoltaDeTeste = true;
			CumprirOPasseDoEnma(pl);
			bool ficou = pl.Zone.Equals(terra) && pl.Ficha.passe_do_enma_ate != 0;
			PasseSemVoltaDeTeste = false;
			List<string> volta = Ouvir(() => CumprirOPasseDoEnma(pl));
			AfirmarV30("**VENCIDO, A ALMA VOLTA**: faltando um dia, o aviso; no fim do prazo o morto e puxado de volta pra mesa do Enma -- morto, de auréola, sem passe (e de pe, agora pelo lugar)",
					   Disse(falta, "falta um dia") && Alem.EhOAlem(pl.Zone) && pl.Ficha.dead && pl.Ficha.passe_do_enma_ate == 0 && pl.MortoDePe && Disse(volta, "venceu"),
					   $"zona {pl.Zone.Name}; passe {pl.Ficha.passe_do_enma_ate} | {Ultimos(falta)} / {Ultimos(volta)}");
			AfirmarV30("   DEFEITO INJETADO (o passe que nunca vence): o morto ficaria entre os vivos pra sempre -- a afirmacao de cima ficaria vermelha (defeito injetado: o passe sem volta)",
					   ficou);

			// VOLTAR A VIDA NO MEIO: o passe some calado; CAIR entre os vivos: a mesa do Enma, e o passe acaba
			NaMesa(Alem.PrecoDoPasseDoEnma);
			EnmaPasse(pl, "Terra");
			pl.Ficha.dead = false;
			CumprirOPasseDoEnma(pl);
			bool vivoSemPasse = pl.Ficha.passe_do_enma_ate == 0 && pl.Zone.Equals(terra);
			NaMesa(Alem.PrecoDoPasseDoEnma);
			EnmaPasse(pl, "Terra");
			bool desceu = pl.Zone.Equals(terra);
			pl.Combate!.Morrer();   // o fantasma "morre" de novo entre os vivos -- o `else if(dead)` do `Death()`
			AfirmarV30("...quem VOLTA A VIDA no meio perde o passe calado (e fica onde esta); quem CAI entre os vivos acorda na mesa do Enma, e o passe acabou",
					   vivoSemPasse && desceu && Alem.EhOAlem(pl.Zone) && pl.Ficha.passe_do_enma_ate == 0,
					   $"vivo sem passe {vivoSemPasse}; desceu {desceu}; zona {pl.Zone.Name}; passe {pl.Ficha.passe_do_enma_ate}");

			// UM MUNDO GERADO: fora do livro o Enma nao o conhece; DESCOBERTO por um jogador, o morto desce nele (frio, na
			// ORBITA dele, de pe -- quem pousa e o `TickDoEspaco`)
			if (MundoGeradoForaDoLivroV30() is { } g)
			{
				string chave = ChaveDePlaneta.De(g).Texto;
				NaMesa(Alem.PrecoDoPasseDoEnma);
				List<string> semDescobrir = Ouvir(() => EnmaPasse(pl, g.Nome));
				NomeDePlaneta n = _batismos.Abrir(chave, g.Nome);
				n.Descobridor = "Pioneira";
				n.AssinaturaDoDescobridor = "bancada_v030";
				List<string> descoberto = Ouvir(() => EnmaPasse(pl, g.Nome));
				ZoneKey zonaG = ZoneKey.Procedural(g.Nome, g.Seed);
				bool laOuEmCima = pl.Zone.Equals(zonaG)
								  || (Espaco.EhEspaco(pl.Zone) && Espaco.PlanetaSob(SeedDoUniverso, pl.Pos) is { } sob && sob.Seed == g.Seed);
				AfirmarV30("**OS MUNDOS DESCOBERTOS TAMBEM**: o mundo gerado que nenhum jogador pisou nao esta no livro do Enma; descoberto, o morto desce nele (frio, na ORBITA dele, de pe -- o pouso e do `TickDoEspaco`)",
						   Disse(semDescobrir, "Não conheço") && laOuEmCima && pl.MortoDePe && pl.Ficha.passe_do_enma_ate != 0,
						   $"{g.Nome} ({chave}); zona {pl.Zone.Name}; {Ultimos(semDescobrir)} / {Ultimos(descoberto)}");
			}
			else AfirmarV30("(montagem) ha um mundo gerado fora do livro, perto da Terra", false);
		}
		finally
		{
			PasseDeitaDeTeste = false;
			PasseSemVoltaDeTeste = false;
			_batismos.Substituir(livroAntes);
		}
	}

	/// <summary>Um mundo GERADO de verdade nos sistemas em volta da Terra (a carta estelar e funcao da seed), vivo e fora do livro dos nomes.</summary>
	private PlanetaNoEspaco? MundoGeradoForaDoLivroV30()
	{
		var perto = new List<SistemaSolar>();
		Sistemas.PorPerto(SeedDoUniverso, Vec2.Zero, perto);
		foreach (SistemaSolar s in perto)
			for (int k = 0; k < s.Orbitas; k++)
				if (s.Planeta(k) is { Premade: false } p && _batismos.De(ChaveDePlaneta.De(p).Texto) == null
					&& !ZonaMorta(ZoneKey.Procedural(p.Nome, p.Seed)))
					return p;
		return null;
	}

	// =====================================================================
	// L) A REENCARNACAO (item 7)
	// =====================================================================
	private void AReencarnacaoV30()
	{
		GD.Print("[v030] --- L) a reencarnacao pelo Enma: o personagem acaba, e o proximo da conta nasce com o BP da raca mais 10% do dele, e com o banco dele (item 7; 2026-10-26, como no DU) ---");
		LimparTudoDaBancada();
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		Obra? enma = _noChao.Find(o => o.Tipo == Alem.TipoDoEnma && o.Zona.Equals(alem));
		AfirmarV30("(montagem) o Enma esta na mesa dele", enma != null);
		if (enma == null) return;
		Vec2 aoLado = new(enma.X + ZoneCollision.TileSize, enma.Y);

		// O PALCO DE APAGAMENTOS: a conta e emprestada e o disco e uma pasta temporaria -- a pasta de saves nao e tocada
		using PalcoDeApagamentos palco = PalcoDeApagamentosDeBancada();
		ServerPlayer pl = ForjarV2("Renascente", TerraFirmeG12(4), 80_000);
		MoveToZone(pl.Id, alem, aoLado);
		pl.Ficha.dead = true;
		pl.MorteJaViajou = true;
		pl.RelogioDaMorte = long.MaxValue;
		pl.Karma = 0;
		AccountSave acc = palco.Emprestar(pl.Conta, pl.Slot, AccountStore.DeJogador(pl, NowMs()));
		var ficha = new Jandirus.Core.Races.CharacterDraft { Name = "Renascido", Race = "Human", Planet = "Earth", Gender = "Male", Age = 18 };

		// O VIVO, O NOME ERRADO E O CORACAO NEGRO: nada se apaga
		pl.Ficha.dead = false;
		List<string> vivo = Ouvir(() => EnmaReencarnar(pl, pl.Name));
		pl.Ficha.dead = true;
		List<string> errado = Ouvir(() => EnmaReencarnar(pl, "outro nome"));
		pl.Karma = -10;
		Ouvir(() => EnmaReencarnar(pl, pl.Name));
		bool noInferno = pl.Zone.Name == Alem.ZonaDoInferno;
		pl.Karma = 0;
		pl.Ficha.hell_lockout_until = 0;
		MoveToZone(pl.Id, alem, aoLado);
		AfirmarV30("o VIVO e enxotado, o NOME ERRADO nao apaga nada (o Enma pede o nome) e o CORACAO NEGRO vai pro Inferno antes de qualquer oferta -- o personagem continua no slot, sem eco",
				   Disse(vivo, "vivos") && Disse(errado, "escreva o nome") && noInferno && acc.Slots[pl.Slot] != null && acc.Reencarnacao == null && !pl.PersonagemConsumido,
				   $"{Ultimos(vivo)} / {Ultimos(errado)} / inferno {noInferno}");

		// O NOME CERTO (caixa e espacos nao importam): o personagem acaba, e a conta guarda o eco -- no disco tambem. Com 777 no banco.
		double bpAntigo = pl.Ficha.BP, bancoAntigo = 777;
		pl.Ficha.hiddenpotential = bancoAntigo;
		List<string> certo = Ouvir(() => EnmaReencarnar(pl, "  renascente "));
		Reencarnacao? noDisco = _store?.Carregar(pl.Conta)?.Reencarnacao;
		AfirmarV30("**REENCARNAR APAGA O PERSONAGEM PARA SEMPRE**: o slot vaga, o corpo nao se grava mais, e a conta guarda o ECO -- o BP base inteiro e o banco do potencial --, gravado no MESMO arquivo que perdeu o slot",
				   acc.Slots[pl.Slot] == null && pl.PersonagemConsumido && acc.Reencarnacao?.BPAntigo == bpAntigo && noDisco?.BPAntigo == bpAntigo
				   && acc.Reencarnacao?.BancoAntigo == bancoAntigo && noDisco?.BancoAntigo == bancoAntigo,
				   $"slot {(acc.Slots[pl.Slot] == null ? "vazio" : "cheio")}; consumido {pl.PersonagemConsumido}; eco {acc.Reencarnacao?.BPAntigo}/{acc.Reencarnacao?.BancoAntigo} (disco {noDisco?.BPAntigo}/{noDisco?.BancoAntigo}), esperado {bpAntigo}/{bancoAntigo}");
		AfirmarV30("**...E O ENMA PROMETE UM POTENCIAL MUITO MAIOR** (dono, 2026-10-22: *\"n faça o enma dizer em detalhes q o hidden dele vai ser o bp\"*): a frase do DM (\"Uma alma que escolhe recomecar...\"), sem numero de poder e sem a regra por extenso",
				   Disse(certo, "recomeçar") && Disse(certo, "potencial muito maior") && Disse(errado, "potencial muito maior")
				   && !certo.Exists(l => l.Contains($"{bpAntigo:0}") || l.Contains("10%")),
				   $"{Ultimos(certo)} / {Ultimos(errado)}");

		// O PROXIMO NASCE COM O ECO, UMA VEZ SO -- como no DU (o dono, 2026-10-26): 10% do BP base antigo SOMA no da raca, e o banco vem
		// junto. A REGUA e uma so, pra ela reprovar no defeito de baixo e nos mutantes das duas linhas (`ciclo.py` do ag/potencialfora).
		double dezPorCento = DmMath.Round(bpAntigo * Alem.FracaoDaReencarnacao);
		bool NasceuPelaRegra(Fighter f, double bpNoNascimento, double bancoNoNascimento) =>
			f.BP == bpNoNascimento + dezPorCento && f.hiddenpotential == bancoNoNascimento + bancoAntigo;
		Fighter novo = Nascer(ficha, "Renascido");
		double bpDoNovo = novo.BP, bancoDoNovo = novo.hiddenpotential;
		Reencarnacao? aplicado = AplicarOEcoDaReencarnacao(acc, novo);
		Fighter segundo = Nascer(ficha, "Segundo");
		double bpDoSegundo = segundo.BP, bancoDoSegundo = segundo.hiddenpotential;
		Reencarnacao? deNovo = AplicarOEcoDaReencarnacao(acc, segundo);
		AfirmarV30("**O PROXIMO PERSONAGEM NASCE COM O BP DA RACA MAIS 10% DO ANTERIOR, E COM O BANCO DO POTENCIAL DELE** (o dono, 2026-10-26: \"como no DU\") -- e o eco e consumido: o segundo a nascer nasce sem ele",
				   aplicado != null && dezPorCento > 0 && NasceuPelaRegra(novo, bpDoNovo, bancoDoNovo)
				   && deNovo == null && acc.Reencarnacao == null && segundo.BP == bpDoSegundo && segundo.hiddenpotential == bancoDoSegundo,
				   $"BP {bpDoNovo} -> {novo.BP} (esperado +{dezPorCento}); banco {bancoDoNovo} -> {novo.hiddenpotential} (esperado +{bancoAntigo}); segundo {bpDoSegundo}/{bancoDoSegundo} -> {segundo.BP}/{segundo.hiddenpotential}");
		AfirmarV30("**...E NASCE DESCANSADO**: o renascido entra com o tanque de Ki cheio", novo.Ki == novo.MaxKi, $"Ki {novo.Ki:0} de {novo.MaxKi:0}");
		bool marcado = novo.potencialAntigoZerado, zerouNoLogin = novo.ZerarOPotencialAntigo();
		AfirmarV30("**...E O BANCO QUE O ECO TROUXE NAO SE ZERA NO LOGIN**: quem nasce depois do banco ja vem marcado, e o zerar do estoque antigo (o do primeiro login de um save de antes) nao o toca",
				   marcado && !zerouNoLogin && novo.hiddenpotential == bancoDoNovo + bancoAntigo, $"marcado {marcado}; zerou {zerouNoLogin}; banco {novo.hiddenpotential}");

		acc.Reencarnacao = new Reencarnacao { De = "Renascente", BPAntigo = bpAntigo, Fracao = Alem.FracaoDaReencarnacao, BancoAntigo = bancoAntigo };
		EcoDaReencarnacaoIgnoradoDeTeste = true;
		Fighter semEco = Nascer(ficha, "SemEco");
		double bpSemEco = semEco.BP, bancoSemEco = semEco.hiddenpotential;
		try { AplicarOEcoDaReencarnacao(acc, semEco); }
		finally { EcoDaReencarnacaoIgnoradoDeTeste = false; }
		AfirmarV30("   DEFEITO INJETADO (o eco ignorado, o jogo de antes da v0.3.0): o novo nasceria sem nada -- a regua de cima REPROVA (defeito injetado: o eco que nao nasce)",
				   !NasceuPelaRegra(semEco, bpSemEco, bancoSemEco) && semEco.BP == bpSemEco && semEco.hiddenpotential == bancoSemEco, $"BP {semEco.BP}, banco {semEco.hiddenpotential}");

		// A DESPEDIDA NO FIO: o cliente le o motivo, a vaga e a frase -- e carga que nao e despedida le nulo
		byte[] carga = FioDaDespedida.Escrever(FioDaDespedida.Motivo.Reencarnou, 2, FraseDaReencarnacao("Renascente"));
		FioDaDespedida.Despedida? lida = FioDaDespedida.Ler(new LiteNetLib.Utils.NetDataReader(carga));
		bool lixo = FioDaDespedida.Ler(new LiteNetLib.Utils.NetDataReader(new byte[] { 1, 2, 3, 4, 5, 6, 7 })) == null
					&& FioDaDespedida.Ler(new LiteNetLib.Utils.NetDataReader(Array.Empty<byte>())) == null && FioDaDespedida.Ler(null) == null;
		AfirmarV30("...e a desconexao leva a DESPEDIDA (`FioDaDespedida`): o motivo, a vaga e a frase atravessam o fio -- e carga vazia ou de lixo nao vira despedida",
				   lida is { Motivo: FioDaDespedida.Motivo.Reencarnou, Slot: 2 } d && d.Texto == FraseDaReencarnacao("Renascente") && lixo && carga.Length < 1000,
				   $"{lida}; {carga.Length} bytes");

		// ---- O BANCO NAO MEXE NO KI NEM NA RAIVA (dono, 2026-10-26) ----
		// O "modo Uub" do potencial antigo saiu: a razao potencial/BP engordava o Ki maximo e a raiva maxima (e o Ki nao caia mais quando o
		// BP a alcancava). Um recem-nascido com um banco de 1B, posto a MAO, e com as pericias de circulacao e de reuniao em 100 (o teto
		// seguro sai do piso de 130%), tem o MESMO Ki maximo, a mesma regen, o mesmo teto seguro e a mesma raiva -- a regua que os mutantes
		// da volta do modo Uub reprovam (`ciclo.py` do ag/potencialfora).
		Fighter potente = Nascer(ficha, "Potente");
		potente.kicirculationskill = potente.kigatheringskill = 100;
		potente.Statify();
		double kiSem = potente.MaxKi, regenSem = potente.KIregen, cargaSem = potente.kicapacity, raivaSem = potente.MaxAnger;
		potente.hiddenpotential += 1e9;
		potente.Statify();
		static bool Igual(double a, double b) => Math.Abs(a - b) <= 1e-9 * Math.Max(Math.Abs(b), 1);
		AfirmarV30("**O BANCO DO POTENCIAL NAO MEXE NO KI NEM NA RAIVA** (o dono, 2026-10-26: o modo Uub saiu junto com o potencial antigo): com 1B guardado, o Ki maximo, a regen, o teto seguro da carga e a raiva maxima sao os de antes",
				   Igual(potente.MaxKi, kiSem) && Igual(potente.KIregen, regenSem) && Igual(potente.kicapacity, cargaSem) && Igual(potente.MaxAnger, raivaSem),
				   $"Ki {kiSem:0} -> {potente.MaxKi:0}; regen {regenSem:0.####} -> {potente.KIregen:0.####}; carga {cargaSem:0} -> {potente.kicapacity:0}; raiva {raivaSem:0} -> {potente.MaxAnger:0}");
	}

	// =====================================================================
	// G) O MORTO DE PE ATIRA (item 1b)
	// =====================================================================
	private void OMortoDePeAtiraV30()
	{
		GD.Print("[v030] --- G) o morto de pe atira, usa as tecnicas e e alvo delas (item 1b) ---");
		LimparTudoDaBancada();
		Vec2 c = TerraFirmeG12(4);
		ServerPlayer a = ForjarV2("Atirador", c, 50_000);
		ServerPlayer d = ForjarV2("Mirado", c + new Vec2(ZoneCollision.TileSize * 3, 0), 1_000);
		a.Facing = Facing.East;
		ZoneKey daqui = a.Zone;

		// O CADAVER: morto no mundo dos vivos -- nem tiro nem tecnica; e o teleguiado nao persegue um cadaver marcado.
		a.Ficha.dead = true;
		bool cadaverAtira = PodeAtirar(a, 0, out string porqueDoCadaver);
		bool cadaverGolpeia = ProntoPraGolpeG3(a, out _);
		ErguerAGuarda(a, true);   // (2026-10-16) a porta do ALT, a mesma do jogador e da IA
		bool cadaverGuarda = a.Combate.Bloqueando;
		ErguerAGuarda(a, false);
		a.Ficha.dead = false;
		d.Ficha.dead = true;
		a.AlvoId = d.Id;
		bool cadaverMirado = AlvoDeTeleguiado(a) == d;
		d.Ficha.dead = false;

		// O MORTO DE PE: os dois no Outro Mundo -- e o LUGAR que poe o morto de pe (`Alem.MortoDePe`).
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		Vec2 mesa = MesaDoEnma(alem);
		MoveToZone(a.Id, alem, mesa);
		MoveToZone(d.Id, alem, mesa + new Vec2(ZoneCollision.TileSize * 3, 0));
		a.Ficha.dead = true;
		d.Ficha.dead = true;
		a.Combate.Carencia = 0;
		d.Combate.Carencia = 0;
		a.Ficha.Ki = a.Ficha.MaxKi;
		bool dePe = a.MortoDePe && d.MortoDePe;
		bool fantasmaAtira = PodeAtirar(a, 0, out string porqueDoFantasma);
		bool fantasmaGolpeia = ProntoPraGolpeG3(a, out string porqueDoGolpe);
		ErguerAGuarda(a, true);
		bool fantasmaGuarda = a.Combate.Bloqueando;
		ErguerAGuarda(a, false);
		CombatState.MortoNaoBloqueiaDeTeste = true;
		bool guardaComODefeito;
		try { ErguerAGuarda(a, true); guardaComODefeito = a.Combate.Bloqueando; ErguerAGuarda(a, false); }
		finally { CombatState.MortoNaoBloqueiaDeTeste = false; }

		// O TIRO DE VERDADE, pela tecla de producao: a bola basica sai da mao do fantasma.
		List<Projetil> noAlem = ProjeteisDaZona(alem.Hash);
		int antes = noAlem.Count(p => p.Dono == a.Id);
		_blastPronto.Remove(a.Id);
		BasicBlast(a);
		int saiu = noAlem.Count(p => p.Dono == a.Id) - antes;
		_projeteisVivos -= noAlem.RemoveAll(p => p.Dono == a.Id);

		// O TELEGUIADO persegue o fantasma marcado.
		a.AlvoId = d.Id;
		bool fantasmaMirado = AlvoDeTeleguiado(a) == d;

		// O BUFF do fantasma passa pelo tique dos buffs -- o Kaio-ken que o Sr. Kaioh ensina e de morto.
		LigarBuff(a, "bancada_v030", "Bancada", new());
		TickDosBuffs();
		bool buffFica = TemBuff(a, "bancada_v030");
		DesligarBuff(a, "bancada_v030");

		// E O SALTO CONTINUA FECHADO: o morto sai do Outro Mundo pagando, e nao por conta propria -- com a frase certa.
		bool salta = PodeSaltarDePlaneta(a, out string porqueDoSalto);

		MortoNaoAtiraDeTeste = true;
		bool atiraComODefeito;
		string porqueComODefeito;
		try { atiraComODefeito = PodeAtirar(a, 0, out porqueComODefeito); }
		finally { MortoNaoAtiraDeTeste = false; }

		AfirmarV30("**O MORTO DE PE ATIRA**: no Outro Mundo o fantasma passa na porta do tiro e na das tecnicas, e a bola basica SAI da mao dele -- o cadaver dos vivos continua sem nenhum dos dois",
						   dePe && fantasmaAtira && fantasmaGolpeia && saiu == 1 && !cadaverAtira && !cadaverGolpeia,
						   $"de pe {dePe}; fantasma atira {fantasmaAtira} ({porqueDoFantasma}) golpeia {fantasmaGolpeia} ({porqueDoGolpe}); tiros {saiu}; cadaver atira {cadaverAtira} ({porqueDoCadaver}) golpeia {cadaverGolpeia}");
		AfirmarV30("...o teleguiado persegue o fantasma marcado (e nao o cadaver), e o buff do fantasma sobrevive ao tique dos buffs",
						   fantasmaMirado && !cadaverMirado && buffFica, $"fantasma mirado {fantasmaMirado}; cadaver mirado {cadaverMirado}; buff {buffFica}");
		AfirmarV30("...e o SALTO de planeta continua fechado pro morto, dizendo por que (e nao \"caido\")",
						   !salta && porqueDoSalto.Contains("Outro Mundo"), porqueDoSalto);
		AfirmarV30("(defeito injetado: o tiro pula todo morto) o fantasma volta a ouvir \"voce esta morto\" -- *\"n ta dando pra usar ataques de ki tb como morto\"*",
						   dePe && !atiraComODefeito && porqueComODefeito.Contains("morto"), porqueComODefeito);
		AfirmarV30("**O MORTO DE PE ERGUE A GUARDA** (*\"n tem como dar bloqueio morto? deveria\"*): no Outro Mundo o fantasma levanta o braco pelo ALT (a mesma porta do jogador e da IA) -- o cadaver dos vivos continua sem",
						   dePe && fantasmaGuarda && !cadaverGuarda, $"fantasma {fantasmaGuarda}; cadaver {cadaverGuarda}");
		AfirmarV30("(defeito injetado: a guarda pula todo morto, o jogo de antes) o fantasma volta a nao erguer a guarda -- a regua de cima REPROVA",
						   dePe && !guardaComODefeito);

		a.Ficha.dead = false;
		d.Ficha.dead = false;
		a.AlvoId = 0;
		MoveToZone(a.Id, daqui, c);
		MoveToZone(d.Id, daqui, c + new Vec2(ZoneCollision.TileSize * 3, 0));
		LimparTudoDaBancada();
	}

	// =====================================================================
	// I) AS ILHAS DO OUTRO MUNDO PUXAM 10x (item 13)
	// =====================================================================
	private void AsIlhasDoOutroMundoV30()
	{
		GD.Print("[v030] --- I) as ilhas do Outro Mundo puxam 10x; o Caminho e o Enma, 1x (item 13) ---");
		LimparTudoDaBancada();
		Vec2 c = TerraFirmeG12(4);
		ServerPlayer p = ForjarV2("Peregrino", c, 50_000);
		ZoneKey daqui = p.Zone;
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		static Vec2 Celula(int x, int y) => new((x + 0.5f) * ZoneCollision.TileSize, (y + 0.5f) * ZoneCollision.TileSize);

		// A MESA DO ENMA, pela troca de zona: 1x.
		MoveToZone(p.Id, alem, MesaDoEnma(alem));
		double naMesa = p.Ficha.Planetgrav;
		string? ilhaNaMesa = p.IlhaDoOutroMundo;

		// O PLANETA DO SR. KAIOH (a ilha do NORTE), pela troca de zona tambem -- e a frase diz o nome dele.
		List<string> ouvido = Ouvir(() => MoveToZone(p.Id, alem, Celula(150, 20)));
		double noKaioh = p.Ficha.Planetgrav;
		string? ilhaDoKaioh = p.IlhaDoOutroMundo;

		// ANDANDO DENTRO DA ZONA, pelo tique de 1 Hz: o Caminho da Serpente (1x) e a ilha do oeste (10x).
		p.Pos = Celula(175, 70);
		TickDasIlhas();
		double noCaminho = p.Ficha.Planetgrav;
		p.Pos = Celula(32, 200);
		TickDasIlhas();
		double noOeste = p.Ficha.Planetgrav;
		string? ilhaDoOeste = p.IlhaDoOutroMundo;

		// E FORA DO OUTRO MUNDO a ilha sai da ficha (o chao volta a ser o do planeta).
		MoveToZone(p.Id, daqui, c);
		double deVolta = p.Ficha.Planetgrav;
		string? ilhaDeVolta = p.IlhaDoOutroMundo;

		Alem.IlhasComoOCaminhoDeTeste = true;
		double comODefeito;
		try
		{
			MoveToZone(p.Id, alem, Celula(150, 20));
			comODefeito = p.Ficha.Planetgrav;
		}
		finally { Alem.IlhasComoOCaminhoDeTeste = false; }

		AfirmarV30("**AS ILHAS PUXAM 10x**: o planeta do Sr. Kaioh (a ilha do norte) e a do oeste a 10x; a mesa do Enma e o Caminho da Serpente a 1x -- na troca de zona E andando (o tique de 1 Hz)",
						   naMesa == 1 && ilhaNaMesa == null && noKaioh == Alem.GravidadeDasIlhas && ilhaDoKaioh == "o planeta do Sr. Kaioh"
						   && noCaminho == 1 && noOeste == Alem.GravidadeDasIlhas && ilhaDoOeste == "o planeta do Kaioh do Oeste",
						   $"mesa {naMesa} ({ilhaNaMesa}); Kaioh {noKaioh} ({ilhaDoKaioh}); caminho {noCaminho}; oeste {noOeste} ({ilhaDoOeste})");
		AfirmarV30("...a frase da chegada diz o lugar (\"O planeta do Sr. Kaioh puxa 10 vezes...\"), e fora do Outro Mundo a ilha sai da ficha",
						   Disse(ouvido, "O planeta do Sr. Kaioh puxa 10 vezes") && ilhaDeVolta == null && deVolta == FichaDoChao(daqui).Gravidade,
						   $"{Ultimos(ouvido)}; de volta {deVolta} ({ilhaDeVolta})");
		AfirmarV30("(defeito injetado: as ilhas como o Caminho) o planeta do Sr. Kaioh puxa 1x -- *\"as ilhas q estao no outro mundo/ceu deveriam ter gravidade de 10x\"*",
						   comODefeito == 1, $"{comODefeito}");

		MoveToZone(p.Id, daqui, c);
		LimparTudoDaBancada();
	}

	// =====================================================================
	// M) O MAJIN: DESMAIA DE VERDADE, SE DESFAZ NO GOLPE QUE O MATARIA, E SO O KI O MATA (itens 2a e 2b)
	//    -- e (2026-10-23) A RACA KAI MORRE NORMAL, pelas mesmas portas
	// =====================================================================
	private void OMajinV30()
	{
		GD.Print("[v030] --- M) o Majin desmaia e levanta antes de todo mundo, se desfaz no golpe que o mataria, e so o ki o mata (itens 2a e 2b) ---");
		LimparTudoDaBancada();
		Vec2 c = TerraFirmeG12(4);
		float t = ZoneCollision.TileSize;
		ServerPlayer algoz = ForjarV2("Algoz", c, 5_000_000);
		int fila = 0;
		ServerPlayer Corpo(string nome, string raca) => ForjarV2(nome, c + new Vec2(2 * t, ++fila * t), 50_000, raca: raca);
		var sorte = new Random(15);

		// ---------------- 2a: O DESMAIO POR NUCLEO TEM UM PISO ----------------
		// Os dois EM LUTA (a tag de 90 s: quem nao cura em combate nao sara nela) e o MESMO nocaute por nucleo, pela cauda de
		// producao do dano direto: o torso no piso do nao-letal, e a `ConsequenciaDoDano` derruba. Devolve em quantos segundos
		// o corpo levantou pelo tique de producao (`TickCombate`); -1 se nem caiu, nulo se ainda esta no chao no teto.
		double? Levanta(ServerPlayer v, double teto)
		{
			CombatState cs = v.Combate!;
			cs.EntrarEmCombate();
			cs.Ferir(cs.Corpo.Achar("Torso")!, 1e9, letal: false);
			cs.SincronizarVida();
			ConsequenciaDoDano(v, algoz, "na bancada v030", letal: false, porKi: false);
			if (!v.Ficha.KO) return -1;
			for (double s = Protocol.TickSeconds; s <= teto; s += Protocol.TickSeconds)
			{
				TickCombate(Protocol.TickSeconds);
				if (!v.Ficha.KO) return s;
			}
			return null;
		}

		double? doMajin = Levanta(Corpo("Majin que desmaia", "Majin"), 20);
		double? doHumano = Levanta(Corpo("Humano que desmaia", "Human"), 20);
		AfirmarV30("**O MAJIN DESMAIA DE VERDADE, E LEVANTA ANTES DE TODO MUNDO**: o nocaute por nucleo o deixa no chao pelo piso do desmaio (e nao some no tique seguinte); o Humano, que nao sara em luta, continua caido",
						   doMajin is { } m && m >= CombatState.SegundosMinimosDoDesmaio && m < CombatState.SegundosMinimosDoDesmaio + 1 && doHumano == null,
						   $"Majin levantou em {doMajin?.ToString("0.00") ?? "nunca"} s; Humano: {doHumano?.ToString("0.00") ?? "ainda no chao aos 20 s"}");

		CombatState.DesmaioSemMinimoDeTeste = true;
		double? semPiso;
		try { semPiso = Levanta(Corpo("Majin de antes", "Majin"), 20); }
		finally { CombatState.DesmaioSemMinimoDeTeste = false; }
		AfirmarV30("(defeito injetado: o desmaio sem piso) o Majin levanta em menos de um segundo -- *\"tem o efeito de KO mas ele n desmaia\"*",
						   semPiso is { } sp && sp >= 0 && sp < 1, $"{semPiso?.ToString("0.00") ?? "nunca"} s");

		// ---------------- 2b: O GOLPE QUE MATARIA ----------------
		// Pelo `AplicarNoMembro` de producao -- o mesmo do soco e do tiro, e o `porKi` e o que os separa -- num NUCLEO: os
		// membros de fora saem do sorteio (`Chance` zero) e a sorte tem semente. O desfecho sai pelo `ResolverDesfecho`.
		GolpeResultado Letal(ServerPlayer v, bool porKi)
		{
			foreach (BodyPart p in v.Combate!.Corpo.Partes)
				if (p.Papel != Vitalidade.Nucleo) p.Chance = 0;
			GolpeResultado r = MeleeResolver.AplicarDanoPronto(v.Combate, 1e9, letal: true, sorte, "cabeca", porKi);
			ResolverDesfecho(algoz, v, r);
			return r;
		}

		ServerPlayer emPedacos = Corpo("Majin socado", "Majin");
		GolpeResultado soco = Letal(emPedacos, porKi: false);
		CombatState pc = emPedacos.Combate!;
		bool desfez = soco.Desfez && !soco.Morreu && pc.Desfeito && emPedacos.Ficha.KO && !emPedacos.Ficha.dead
							  && pc.Intocavel && OcupacaoDe(emPedacos) == Ocupacao.Desfeito;
		AfirmarV30("**O MAJIN \"MORTO\" POR SOCO SE DESFAZ**: o golpe que o mataria o deixa em pedacos -- caido, intocavel, a tela avisada (`Ocupacao.Desfeito`), e VIVO",
						   desfez,
						   $"desfez {soco.Desfez} ({soco.Membro}); morreu {soco.Morreu}; desfeito {pc.Desfeito}; KO {emPedacos.Ficha.KO}; morto {emPedacos.Ficha.dead}; intocavel {pc.Intocavel}; ocupacao {OcupacaoDe(emPedacos)}");

		// O RELOGIO E O DO NOCAUTE, pelo tique de producao: um segundo antes do fim ainda em pedacos; passado o fim, de pe e INTEIRO.
		for (double s = 0; s < CombatState.SegundosDesfeito - 1; s += Protocol.TickSeconds) TickCombate(Protocol.TickSeconds);
		bool quaseLa = pc.Desfeito && emPedacos.Ficha.KO;
		for (double s = 0; s < 2; s += Protocol.TickSeconds) TickCombate(Protocol.TickSeconds);
		bool inteiro = pc.Corpo.Partes.All(p => !p.Decepado && p.Vida >= p.VidaMax - 1e-9);
		AfirmarV30($"...e se refaz pelo relogio: aos {CombatState.SegundosDesfeito - 1:0} s ainda em pedacos; passados os {CombatState.SegundosDesfeito:0}, de pe e INTEIRO (todo membro de volta, a vida cheia) -- *\"You will regenerate in 30 seconds\"* (`DeathRegen.dm:162`)",
						   quaseLa && !pc.Desfeito && !emPedacos.Ficha.KO && inteiro && !pc.Intocavel,
						   $"um segundo antes: {quaseLa}; agora desfeito {pc.Desfeito}, KO {emPedacos.Ficha.KO}, inteiro {inteiro}");

		// O KI MATA DE VEZ, e o corpo comum morre do soco.
		ServerPlayer peloKi = Corpo("Majin do raio", "Majin"), comum = Corpo("Humano socado", "Human");
		GolpeResultado raio = Letal(peloKi, porKi: true), socoNoHumano = Letal(comum, porKi: false);
		AfirmarV30("**SO O KI MATA O MAJIN**: o mesmo golpe letal, vindo de um tiro, o mata de verdade -- e o Humano morre do soco",
						   raio.Morreu && peloKi.Ficha.dead && !peloKi.Combate!.Desfeito && socoNoHumano.Morreu && comum.Ficha.dead,
						   $"ki: morreu {raio.Morreu} ({raio.Membro}); humano: morreu {socoNoHumano.Morreu}");

		// ---------------- (2026-10-23) A RACA KAI MORRE NORMAL ----------------
		// O dono: *"kaios n morrem e ficam regenerando"* -> *"Toda a raca Kai morre normal"* (NPC e jogador). Ate aqui esta familia
		// AFIRMAVA o contrario ("o Kai, que refaz membro, cai em coma") -- era a regra do DM (`Injuries.dm:276-279`, `statkai.dm:34`), e a
		// decisao do dono de 2026-10-23 a trocou. Pelas MESMAS portas de producao do Majin e do Humano logo acima: o soco e o tiro pelo
		// `AplicarNoMembro`, a explosao pela `ConsequenciaDoDano`, e o Kaio-ken que estoura (`EstourarG2`) -- todas perguntam o
		// `Body.RegeneraDecepado`. O nao-letal e a parte da raca que fica sao medidos junto, e os outros que refazem membro sao o controle.
		GolpeResultado GolpeNaoLetal(ServerPlayer v, bool porKi)
		{
			foreach (BodyPart p in v.Combate!.Corpo.Partes)
				if (p.Papel != Vitalidade.Nucleo) p.Chance = 0;
			GolpeResultado r = MeleeResolver.AplicarDanoPronto(v.Combate, 1e9, letal: false, sorte, "cabeca", porKi);
			ResolverDesfecho(algoz, v, r);
			return r;
		}

		ServerPlayer kai = Corpo("Kai socado", "Kai"), kaiDoRaio = Corpo("Kai do raio", "Kai"), kaiDaBomba = Corpo("Kai da bomba", "Kai");
		GolpeResultado socoNoKai = Letal(kai, porKi: false), raioNoKai = Letal(kaiDoRaio, porKi: true);
		EspalharDanoG3(kaiDaBomba, algoz, 1e9, letal: true);
		AfirmarV30("**O KAI MORRE COMO TODO MUNDO** (decisao do dono de 2026-10-23: *\"Toda a raca Kai morre normal\"*): o soco letal no nucleo o MATA, como mata o Humano, e nao o poe mais em coma pra se regenerar -- e o tiro de ki e a explosao tambem",
						   socoNoKai.Morreu && kai.Ficha.dead && !socoNoKai.Nocauteou && raioNoKai.Morreu && kaiDoRaio.Ficha.dead && kaiDaBomba.Ficha.dead,
						   $"soco: morreu {socoNoKai.Morreu} ({socoNoKai.Membro}), nocauteou {socoNoKai.Nocauteou}, morto {kai.Ficha.dead}; raio: morto {kaiDoRaio.Ficha.dead}; bomba: morto {kaiDaBomba.Ficha.dead}");

		ServerPlayer kaiDoKaioken = Corpo("Kai do Kaio-ken", "Kai");
		EstourarG2(kaiDoKaioken, "bancada: o Kaio-ken estoura");
		AfirmarV30("**...E NO KAIO-KEN QUE ESTOURA**: a outra porta que poupava quem refaz membro (`EstourarG2`) pergunta a mesma coisa -- o Kai morre, e nao entra em coma",
						   kaiDoKaioken.Ficha.dead && !kaiDoKaioken.Ficha.KO, $"morto {kaiDoKaioken.Ficha.dead}, KO {kaiDoKaioken.Ficha.KO}");

		ServerPlayer kaiNaoLetal = Corpo("Kai do soco nao letal", "Kai");
		GolpeResultado leve = GolpeNaoLetal(kaiNaoLetal, porKi: false);
		AfirmarV30("...o golpe NAO-LETAL so o derruba, como derruba todo mundo (o modo nao-letal continua sem matar)",
						   leve.Nocauteou && kaiNaoLetal.Ficha.KO && !kaiNaoLetal.Ficha.dead,
						   $"nocauteou {leve.Nocauteou}, KO {kaiNaoLetal.Ficha.KO}, morto {kaiNaoLetal.Ficha.dead}");
		AfirmarV30("...e o RESTO da raca fica, que nao foi pedido: o Kai continua refazendo membro e curando em combate (a Regeneration 10 do `races.json`)",
						   kai.Combate!.Corpo.Regen is { MembroVolta: true, CuraEmCombate: true, Regeneration: 10 }, $"{kai.Combate.Corpo.Regen}");

		// O BIO-ANDROIDE QUE HERDOU O SANGUE KAI TAMBEM MORRE -- a decisao do dono nas sobras de 2026-10-23 (*"O bio com sangue Kai tambem
		// morre"*; a r1 o deixava em coma, pela raca gravada). Ele regenera como a doadora que mais regenera (`SangueDoBio.RegeneracaoDoBio`),
		// e a pergunta da morte vem junto. O sangue entra depois do nascimento: a conta do `AplicarEfeitos` refaz o perfil (`EixoDeRegen`).
		ServerPlayer BioDeSangue(string nome, params string[] sangue)
		{
			ServerPlayer b = Corpo(nome, Jandirus.Core.Races.BioAndroids.Raca);
			b.Ficha.bio_racas_dna = [.. sangue];
			b.Combate!.Corpo.Regen = EixoDeRegen(b.Race, b.Ficha);
			return b;
		}
		ServerPlayer bioKai = BioDeSangue("Bio com sangue Kai", "Kai"), bioYardratKai = BioDeSangue("Bio com Yardrat e Kai", "Yardrat", "Kai");
		GolpeResultado socoNoBioKai = Letal(bioKai, porKi: false), socoNoBioYk = Letal(bioYardratKai, porKi: false);
		AfirmarV30("**O BIO COM SANGUE KAI TAMBEM MORRE** (decisao do dono, 2026-10-23: *\"O bio com sangue Kai tambem morre\"*): herdou a regeneracao do Kai (o membro volta) e morre do soco letal como ele",
						   bioKai.Combate!.Corpo.Regen.MembroVolta && socoNoBioKai.Morreu && bioKai.Ficha.dead,
						   $"membro volta {bioKai.Combate.Corpo.Regen.MembroVolta}, morreu {socoNoBioKai.Morreu}, nocauteou {socoNoBioKai.Nocauteou}, morto {bioKai.Ficha.dead}");
		AfirmarV30("**...MESMO COM O YARDRAT ANTES NA FILA DO SANGUE**: o empate em 10 (Kai, Yardrat, Meta e Tsujin, o mesmo eixo de cura) fica com o Kai, e o bio morre",
						   socoNoBioYk.Morreu && bioYardratKai.Ficha.dead,
						   $"regenera como {Jandirus.Core.Races.SangueDoBio.RegeneracaoDoBio(bioYardratKai.Ficha, RegeneracaoDaRaca).Raca}, morreu {socoNoBioYk.Morreu}, morto {bioYardratKai.Ficha.dead}");

		// OS OUTROS QUE REFAZEM MEMBRO CONTINUAM EM COMA: a regra e da raca Kai (e de quem herdou a regeneracao dela) -- nao da Regeneration 10
		// (o Yardrat tem a mesma). E com sangue MAJIN junto a regeneracao e a do Majin (100), e manda a semi-imortalidade dele: se desfaz.
		ServerPlayer yardrat = Corpo("Yardrat socado", "Yardrat"), bioYardrat = BioDeSangue("Bio com sangue Yardrat", "Yardrat"),
					 bioMajinKai = BioDeSangue("Bio com Majin e Kai", "Majin", "Kai");
		GolpeResultado socoNoYardrat = Letal(yardrat, porKi: false), socoNoBioY = Letal(bioYardrat, porKi: false), socoNoBioMk = Letal(bioMajinKai, porKi: false);
		AfirmarV30("...e os OUTROS que refazem membro continuam em coma: o Yardrat (a mesma Regeneration 10) e o bio SEM sangue Kai (com o do Yardrat) -- e o bio com Majin e Kai se desfaz no soco, como o Majin",
						   socoNoYardrat.Nocauteou && yardrat.Ficha.KO && !yardrat.Ficha.dead
						   && bioYardrat.Combate!.Corpo.Regen.MembroVolta && socoNoBioY.Nocauteou && bioYardrat.Ficha.KO && !bioYardrat.Ficha.dead
						   && socoNoBioMk.Desfez && !bioMajinKai.Ficha.dead,
						   $"yardrat: nocauteou {socoNoYardrat.Nocauteou}, morto {yardrat.Ficha.dead}; bio-yardrat: nocauteou {socoNoBioY.Nocauteou}, morto {bioYardrat.Ficha.dead}; "
						   + $"bio-majin-kai: desfez {socoNoBioMk.Desfez}, morto {bioMajinKai.Ficha.dead}");

		// OS DOIS DEFEITOS DA DECISAO NOVA, cada um lido pela propria linha: o perfil do bio pela raca gravada (a r1) e o empate que fica com
		// quem chegou primeiro.
		ServerPlayer bioDeAntes, empateDeAntes;
		BioKaiEmComaDeTeste = true;
		try { bioDeAntes = BioDeSangue("Bio de antes", "Kai"); }
		finally { BioKaiEmComaDeTeste = false; }
		Jandirus.Core.Races.SangueDoBio.EmpateSemOKaiDeTeste = true;
		try { empateDeAntes = BioDeSangue("Bio do empate de antes", "Yardrat", "Kai"); }
		finally { Jandirus.Core.Races.SangueDoBio.EmpateSemOKaiDeTeste = false; }
		GolpeResultado socoNoBioDeAntes = Letal(bioDeAntes, porKi: false), socoNoEmpateDeAntes = Letal(empateDeAntes, porKi: false);
		AfirmarV30("(defeito injetado: o bio pela raca gravada, a r1) o bio com sangue Kai cai em COMA -- a regua do \"BIO COM SANGUE KAI\" REPROVA",
						   socoNoBioDeAntes.Nocauteou && !bioDeAntes.Ficha.dead, $"nocauteou {socoNoBioDeAntes.Nocauteou}, morto {bioDeAntes.Ficha.dead}");
		AfirmarV30("(defeito injetado: o empate fica com quem chegou primeiro) com o Yardrat antes do Kai, o bio cai em COMA -- a regua do \"MESMO COM O YARDRAT\" REPROVA",
						   socoNoEmpateDeAntes.Nocauteou && !empateDeAntes.Ficha.dead, $"nocauteou {socoNoEmpateDeAntes.Nocauteou}, morto {empateDeAntes.Ficha.dead}");

		ServerPlayer kaiDeAntes = Corpo("Kai de antes", "Kai");
		Body.KaiEmComaDeTeste = true;
		GolpeResultado socoDeAntesNoKai;
		try { socoDeAntesNoKai = Letal(kaiDeAntes, porKi: false); }
		finally { Body.KaiEmComaDeTeste = false; }
		AfirmarV30("(defeito injetado: a regra de antes de 2026-10-23) o Kai socado cai em COMA, regenerando, e nao morre -- a regua do \"O KAI MORRE\" REPROVA",
						   socoDeAntesNoKai.Nocauteou && !socoDeAntesNoKai.Morreu && kaiDeAntes.Ficha.KO && !kaiDeAntes.Ficha.dead,
						   $"nocauteou {socoDeAntesNoKai.Nocauteou}, morreu {socoDeAntesNoKai.Morreu}, KO {kaiDeAntes.Ficha.KO}, morto {kaiDeAntes.Ficha.dead}");

		// E PELA CAUDA DO DANO DIRETO TAMBEM (o dano em area): a explosao comum desfaz, a de ki mata.
		ServerPlayer naBomba = Corpo("Majin da bomba", "Majin"), naExplosao = Corpo("Majin da explosao de ki", "Majin");
		EspalharDanoG3(naBomba, algoz, 1e9, letal: true);
		EspalharDanoG3(naExplosao, algoz, 1e9, letal: true, porKi: true);
		AfirmarV30("...e o dano em area pergunta a MESMA coisa: a explosao comum desfaz o Majin, a de ki o mata",
						   naBomba.Combate!.Desfeito && !naBomba.Ficha.dead && naExplosao.Ficha.dead,
						   $"bomba: desfeito {naBomba.Combate.Desfeito}, morto {naBomba.Ficha.dead}; ki: morto {naExplosao.Ficha.dead}");

		ServerPlayer antesDoSoco = Corpo("Majin de antes do soco", "Majin"), antesDoRaio = Corpo("Majin de antes do raio", "Majin");
		CombatState.MajinNaoSeDesfazDeTeste = true;
		GolpeResultado socoDeAntes, raioDeAntes;
		try
		{
			socoDeAntes = Letal(antesDoSoco, porKi: false);
			raioDeAntes = Letal(antesDoRaio, porKi: true);
		}
		finally { CombatState.MajinNaoSeDesfazDeTeste = false; }
		AfirmarV30("(defeito injetado: o jogo de antes) o Majin \"morto\" vira um coma sem pedaco nenhum, e o raio nem o mata -- *\"morrer pra ki é oq mata definitivamente\"*",
						   !socoDeAntes.Desfez && socoDeAntes.Nocauteou && !antesDoSoco.Combate!.Desfeito && !raioDeAntes.Morreu && !antesDoRaio.Ficha.dead,
						   $"soco: desfez {socoDeAntes.Desfez}, nocauteou {socoDeAntes.Nocauteou}; raio: morreu {raioDeAntes.Morreu}");

		LimparTudoDaBancada();
	}

	// =====================================================================
	// A) O GOLPE AUTOMATICO SOCA NA TELA E OLHA PRO ALVO (item 21)
	// =====================================================================
	private void OGolpeAutomaticoV30()
	{
		GD.Print("[v030] --- A) o golpe automatico das tecnicas soca na tela e olha pro alvo (item 21) ---");
		LimparTudoDaBancada();
		Vec2 c = TerraFirmeG12(6);
		float t = ZoneCollision.TileSize;
		ServerPlayer a = ForjarV2("Lutador", c, 50_000), d = ForjarV2("Saco de pancada", c + new Vec2(3 * t, 0), 50_000);

		// PELO FUNIL DE TODA TECNICA QUE SOCA SOZINHA (`GolpeG3`): o lutador olhando pro NORTE, o alvo a LESTE
		(bool Olhou, bool Posou) Golpe()
		{
			a.Facing = Facing.North;
			a.AtaqueAte = 0;
			GolpeG3(a, d, addDano: 0, nivel: 2);
			return (a.Facing == Facing.East, a.Pose(NowMs(), false) == Protocol.Pose.Atacando);
		}
		var (olhou, posou) = Golpe();

		// E POR UMA TECNICA INTEIRA: o Lariat (o alvo MARCADO, a corrida, o golpe)
		a.Pos = c;
		a.AtaqueAte = 0;
		a.AlvoId = d.Id;
		a.Ficha.Ki = a.Ficha.MaxKi;
		_prontoG3.Remove(a.Id);
		LariatG7(a);
		bool lariat = a.Pose(NowMs(), false) == Protocol.Pose.Atacando;
		AfirmarV30("**O GOLPE AUTOMATICO SOCA E OLHA PRO ALVO**: o funil das tecnicas que socam sozinhas vira o corpo pro alvo e arma a pose de soco (o snapshot leva as duas) -- e o Lariat inteiro sai socando",
						   olhou && posou && lariat, $"olhou {olhou}; posou {posou}; lariat {lariat}");

		GolpeSemPoseDeTeste = true;
		bool olhouAntes, posouAntes;
		try { (olhouAntes, posouAntes) = Golpe(); }
		finally { GolpeSemPoseDeTeste = false; }
		AfirmarV30("(defeito injetado: o jogo de antes) o golpe sai com o corpo olhando pro NORTE e parado -- *\"nao estao tendo animaçao de soco ao dar o golpe e nem olham pro target\"*",
						   !olhouAntes && !posouAntes, $"olhou {olhouAntes}; posou {posouAntes}");
		LimparTudoDaBancada();
	}

	// =====================================================================
	// E) A BEIRADA DO PLANETA: A VOLTA SO NA ULTIMA CELULA, E NINGUEM VOA PRA FORA (itens 6a e 6b)
	// =====================================================================
	private void ABeiradaV30()
	{
		GD.Print("[v030] --- E) a beirada do planeta: a volta so na ultima celula, e ninguem voa pra fora do mapa (itens 6a e 6b) ---");
		LimparTudoDaBancada();
		const float t = ZoneCollision.TileSize;
		ZoneKey terra = ZoneKey.Premade("Earth"), vegeta = ZoneKey.Premade("Vegeta");
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(terra);
		Passagem? castelo = _passagens.GetValueOrDefault("Vegeta")?.Find(x => x.Y == 0);
		if (mapa == null || castelo == null) { AfirmarV30("(montagem) o mapa da Terra e a boca do castelo de Vegeta na linha 0", false); return; }

		// UMA FILEIRA DA TERRA com as seis primeiras celulas livres -- e o centro de uma celula pelos PES (a conta da volta).
		int fila = -1;
		for (int y = 20; y < mapa.Height - 20 && fila < 0; y++)
		{
			bool livre = true;
			for (int x = 0; x < 6 && livre; x++) livre = !mapa.BlockedCell(x, y);
			if (livre) fila = y;
		}
		static Vec2 Centro(int cx, int cy) =>
			new((cx + 0.5f) * ZoneCollision.TileSize, (cy + 0.5f) * ZoneCollision.TileSize - MoveRules.FeetOffsetY);

		ServerPlayer p = ForjarV2("Andarilho da beirada", TerraFirmeG12(4), 50_000);
		ZoneKey daqui = p.Zone;
		Vec2 c0 = p.Pos;

		// 6b: A VOLTA SO NA ULTIMA CELULA (pela porta de producao, `DarAVolta`)
		MoveToZone(p.Id, terra, Centro(1, fila));
		bool naSegunda = DarAVolta(p);
		p.Pos = Centro(0, fila);
		bool naUltima = DarAVolta(p);
		int cxDepois = (int)MathF.Floor(p.Pos.X / t);

		// ...e a boca do castelo de Vegeta: o corpo na linha 1, olhando pro norte, empurra a passagem -- e nao da a volta
		bool BocaDoCastelo()
		{
			MoveToZone(p.Id, vegeta, Centro(castelo!.X, 1));
			return castelo!.NoPasso(p.Pos, Facing.North) && !DarAVolta(p);
		}
		bool alcanca = BocaDoCastelo();

		AfirmarV30("**A VOLTA SO QUANDO ENCOSTA NA BORDA**: na segunda celula o corpo continua onde esta; na ULTIMA ele sai pela borda oposta",
						   fila >= 0 && !naSegunda && naUltima && cxDepois == mapa.Width - 1 - PousoDaVolta,
						   $"fileira {fila}; na segunda {naSegunda}; na ultima {naUltima}; foi pra celula {cxDepois}");
		AfirmarV30("...e a boca do castelo de Vegeta, colada na borda (linha 0), se alcanca: o corpo para na linha 1 empurrando pro norte, sem dar a volta",
						   alcanca, $"boca ({castelo.X},{castelo.Y}) -> {castelo.Zona}");

		VoltaNaFaixaDoDmDeTeste = true;
		bool alcancaAntes;
		try { alcancaAntes = BocaDoCastelo(); }
		finally { VoltaNaFaixaDoDmDeTeste = false; }
		AfirmarV30("(defeito injetado: a faixa de tres do DM) a duas celulas da porta o corpo ja esta do outro lado do mundo -- *\"tem portas coladas na borda q n da pra chegar por conta do loop do planeta\"*",
						   !alcancaAntes);

		// 6a: VOANDO ALTO (o modo `PorCima`), o passo que sai do retangulo e barrado -- pela validacao de producao do passo
		Vec2 rente = new(12, (fila + 0.5f) * t);
		Vec2 OndeFica()
		{
			float orcamento = 1000f;
			MoveRules.ValidateStep(rente, rente + new Vec2(-16, 0), 0.1f, 100f, mapa, ref orcamento, out Vec2 ok, false, ModoDeTravessia.PorCima);
			return ok;
		}
		Vec2 voando = OndeFica();
		AfirmarV30("**NINGUEM VOA PRA FORA DO MAPA**: o passo do voo alto que atravessaria a borda e barrado -- fora do retangulo e parede pra todo modo",
						   mapa.Bloqueia(-1, fila, ModoDeTravessia.PorCima) && voando.X > 0, $"x depois do passo {voando.X:0.#}");

		ZoneCollision.QuemVoaSaiDoMapaDeTeste = true;
		Vec2 voandoAntes;
		try { voandoAntes = OndeFica(); }
		finally { ZoneCollision.QuemVoaSaiDoMapaDeTeste = false; }
		AfirmarV30("(defeito injetado: o voo alto sem borda) o mesmo passo leva o corpo pra fora do mapa, no preto -- *\"da pra voar pra fora da regiao do mapa\"*",
						   voandoAntes.X < 0, $"x depois do passo {voandoAntes.X:0.#}");

		MoveToZone(p.Id, daqui, c0);
		LimparTudoDaBancada();
	}

	// =====================================================================
	// N) O SR. KAIOH E O KORIN (item 18)
	// =====================================================================
	private void OsNpcsDeConversaV30()
	{
		GD.Print("[v030] --- N) o Sr. Kaioh (na ilha do norte) e o Korin (no topo da Torre), com a conversa do DM (item 18) ---");
		LimparTudoDaBancada();
		const float t = ZoneCollision.TileSize;
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo), torre = ZoneKey.Premade(NpcsDeConversa.ZonaDoKorin);
		Obra? kaioh = _noChao.Find(o => o.Tipo == NpcsDeConversa.TipoDoSrKaioh);
		Obra? korin = _noChao.Find(o => o.Tipo == NpcsDeConversa.TipoDoKorin);
		static (int, int) Celula(Obra o) => ((int)MathF.Floor(o.X / ZoneCollision.TileSize), (int)MathF.Floor(o.Y / ZoneCollision.TileSize));
		static bool Folha(string c) => ResourceLoader.Load<SpriteFrames>(c) is { } f && f.HasAnimation("default")
																	   && f.GetFrameTexture("default", 0)?.GetSize() == new Vector2(32, 32);

		bool noNorte = kaioh != null && kaioh.Zona.Equals(alem) && Celula(kaioh) == (NpcsDeConversa.SrKaiohX, NpcsDeConversa.SrKaiohY)
							   && Alem.IlhaEm(alem.Name, NpcsDeConversa.SrKaiohX, NpcsDeConversa.SrKaiohY) == "o planeta do Sr. Kaioh";
		ZoneCollision? mapaDaTorre = MapaDaZonaOuCatalogo(torre);
		bool naTorre = korin != null && mapaDaTorre != null && korin.Zona.Equals(torre)
							   && Celula(korin) == NpcsDeConversa.CelulaDoKorin(mapaDaTorre.Height);
		bool arte = Folha("res://Assets/Sprites/NPCs/KingKai.tres") && Folha("res://Assets/Sprites/NPCs/Korin.tres");
		bool menus = Jandirus.Core.Tech.Interacoes.De(NpcsDeConversa.TipoDoSrKaioh).Any(a => a.Verbo == "kaioh_falar")
							  && Jandirus.Core.Tech.Interacoes.De(NpcsDeConversa.TipoDoKorin).Any(a => a.Verbo == "korin_falar");
		AfirmarV30("**O SR. KAIOH E O KORIN ESTAO NO MUNDO**: o Sr. Kaioh na frente da casa do planeta dele (a ilha do NORTE), o Korin no topo da Torre (o ponto do DM) -- os dois com a arte do `DBZ.dmi` parada no primeiro quadro e o menu E",
						   noNorte && naTorre && arte && menus,
						   $"kaioh {kaioh?.Zona.Name} {(kaioh != null ? Celula(kaioh) : default)}; korin {korin?.Zona.Name} {(korin != null ? Celula(korin) : default)}; arte {arte}; menus {menus}");

		// A CONVERSA DO SR. KAIOH (`kingkai_interact()`), pelo verbo de producao, um passo de cada vez
		ServerPlayer p = ForjarV2("Visitante do Kaioh", TerraFirmeG12(4), 50_000);
		ZoneKey daqui = p.Zone;
		Vec2 c0 = p.Pos;
		MoveToZone(p.Id, alem, new Vec2((NpcsDeConversa.SrKaiohX + 0.5f) * t, (NpcsDeConversa.SrKaiohY + 1.5f) * t));
		p.Karma = 0;
		List<string> vivo = Ouvir(() => SrKaiohFalar(p));
		p.Karma = 60;
		List<string> kaioken = Ouvir(() => SrKaiohFalar(p));
		bool sabeKaioken = p.Livro.Sabe(NpcsDeConversa.SkillDoKaioken);
		p.Karma = 100;
		List<string> genki = Ouvir(() => SrKaiohFalar(p));
		bool sabeGenki = p.Livro.Sabe(NpcsDeConversa.SkillDaGenkidama);
		AfirmarV30("**A CONVERSA DO SR. KAIOH**: o vivo sem karma ouve a piada; com o coração limpo e força ele ensina o KAIO-KEN; com o coração no máximo, a GENKIDAMA (as duas entram no livro)",
						   Disse(vivo, "ainda está vivo") && Disse(kaioken, "KAIO-KEN") && sabeKaioken && Disse(genki, "GENKIDAMA") && sabeGenki,
						   $"{Ultimos(vivo)} / {Ultimos(kaioken)} / {Ultimos(genki)}");

		// O MORTO SEM LICAO NENHUMA: a acolhida e a dica pelo KARMA -- e o BP nao aparece (a regra principal do dono)
		ServerPlayer q = ForjarV2("Alma do Kaioh", TerraFirmeG12(4), 50_000);
		MoveToZone(q.Id, alem, new Vec2((NpcsDeConversa.SrKaiohX - 0.5f) * t, (NpcsDeConversa.SrKaiohY + 1.5f) * t));
		q.Ficha.dead = true;
		q.Karma = 10;
		List<string> morto = Ouvir(() => SrKaiohFalar(q));
		bool semBp = !morto.Exists(l => l.Contains("8000") || l.Contains("8.000") || l.Contains($"{q.Ficha.BP:0}"));
		AfirmarV30("...o morto e acolhido (\"Bem-vindo ao meu pequeno planeta!\") e ouve a dica pelo karma -- SEM o BP, que no DM aparecia: *\"sem scouter vc n tem como saber seu bp\"*",
						   Disse(morto, "Bem-vindo") && Disse(morto, "karma 10/50") && semBp, Ultimos(morto));
		q.Ficha.dead = false;

		// O KORIN, no topo da Torre: na entrada (a celula logo acima dele) a fala; longe dela, a recusa do alcance
		(int kx, int ky) = mapaDaTorre != null ? NpcsDeConversa.CelulaDoKorin(mapaDaTorre.Height) : (0, 0);
		MoveToZone(p.Id, torre, new Vec2((kx + 0.5f) * t, (ky - 0.5f) * t));
		List<string> korinFala = Ouvir(() => KorinFalar(p));
		p.Pos = new Vec2((kx + 0.5f) * t, (ky - 10.5f) * t);
		List<string> longe = Ouvir(() => KorinFalar(p));
		AfirmarV30("...e o Korin fala do topo da Torre com quem chegou na entrada -- e a dez tiles dele, a recusa do alcance",
						   Disse(korinFala, "topo da Torre") && Disse(longe, "não está ao alcance"), $"{Ultimos(korinFala)} / {Ultimos(longe)}");

		p.Karma = 0;
		MoveToZone(p.Id, daqui, c0);
		MoveToZone(q.Id, daqui, c0);
		LimparTudoDaBancada();
	}

	// =====================================================================
	// K) O EMPURRAO NO MORTO DE PE (item 1)
	// =====================================================================
	private void OEmpurraoNoMortoDePeV30()
	{
		GD.Print("[v030] --- K) o empurrao no morto de pe (item 1) ---");
		LimparTudoDaBancada();
		Vec2 c = TerraFirmeG12(4);
		ServerPlayer a = ForjarV2("Empurrador", c, 50_000);
		ServerPlayer d = ForjarV2("Empurrado", c + new Vec2(ZoneCollision.TileSize * 0.9f, 0), 1_000);
		a.Facing = Facing.East;
		ZoneKey daqui = a.Zone;

		// O PESADO GARANTIDO, pela porta de producao (a mesma que o soco que encostou chama): voou ou nao?
		bool Voa()
		{
			d.TiquesDeVoo = 0;
			TentarEmpurrar(a, d, 50, Protocol.Golpe.Pesado, garantido: true);
			bool voou = d.TiquesDeVoo > 0;
			d.TiquesDeVoo = 0;
			d.ArremessadoNoAr = false;
			return voou;
		}
		bool vivoVoa = Voa();

		// O CADAVER: morto no mundo dos vivos.
		d.Ficha.dead = true;
		bool cadaverVoa = Voa();

		// O MORTO DE PE: os dois no Outro Mundo -- e o LUGAR que poe o morto de pe (`Alem.MortoDePe`).
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		Vec2 mesa = MesaDoEnma(alem);
		MoveToZone(a.Id, alem, mesa);
		MoveToZone(d.Id, alem, mesa + new Vec2(ZoneCollision.TileSize * 0.9f, 0));
		bool dePe = d.MortoDePe;
		bool fantasmaVoa = Voa();
		MortoNaoVoaDeTeste = true;
		bool fantasmaVoaComODefeito;
		try { fantasmaVoaComODefeito = Voa(); }
		finally { MortoNaoVoaDeTeste = false; }

		// e o vivo que visita o Outro Mundo tambem voa: o lugar nao trava nada
		d.Ficha.dead = false;
		bool vivoNoAlemVoa = Voa();

		AfirmarV30("**O MORTO DE PE E EMPURRADO**: o fantasma do Outro Mundo voa com o pesado como um vivo (e o vivo que visita la tambem) -- so o CADAVER nao sai do lugar",
				   vivoVoa && !cadaverVoa && dePe && fantasmaVoa && vivoNoAlemVoa,
				   $"vivo {vivoVoa}; cadaver {cadaverVoa}; de pe {dePe}; fantasma {fantasmaVoa}; vivo no alem {vivoNoAlemVoa}");
		AfirmarV30("(defeito injetado: o empurrao pula todo morto) o fantasma nao sai do lugar -- *\"n ta dando pra dar knock back no inferno\"*",
				   dePe && !fantasmaVoaComODefeito);

		MoveToZone(a.Id, daqui, c);
		MoveToZone(d.Id, daqui, c + new Vec2(ZoneCollision.TileSize * 0.9f, 0));
		LimparTudoDaBancada();
	}

	// =====================================================================
	// V) O VIGOR E O KI QUE NAO ACABAM (item 12)
	// =====================================================================
	private void OVigorEOKiQueNaoAcabamV30()
	{
		GD.Print("[v030] --- V) o vigor do Majin, e o vigor e o Ki do androide perpetuo (item 12) ---");
		LimparTudoDaBancada();
		Vec2 c = TerraFirmeG12(4);
		ServerPlayer majin = ForjarV2("Majin", c, 10_000, raca: "Majin");
		ServerPlayer droide = ForjarV2("Perpetuo", c + new Vec2(0, ZoneCollision.TileSize), 10_000);
		droide.Ficha.AndroideInfinito = true;
		ServerPlayer humano = ForjarV2("Humano", c + new Vec2(0, 2 * ZoneCollision.TileSize), 10_000);
		Fighter m = majin.Ficha, dr = droide.Ficha, h = humano.Ficha;

		void Gastar()
		{
			m.stamina = m.maxstamina * 0.03;
			dr.stamina = dr.maxstamina * 0.10;
			dr.Ki = dr.MaxKi * 0.30;
			h.stamina = h.maxstamina * 0.03;
			h.Ki = h.MaxKi * 0.30;
		}

		// PELO RELOGIO DO CORPO DE PRODUCAO: os relogios de todo corpo, e o cadeado por ultimo.
		Gastar();
		TickDosRelogiosDoCorpo(Protocol.TickSeconds);
		bool majinCheio = m.stamina >= m.maxstamina - 1e-9;
		bool droideCheio = dr.stamina >= dr.maxstamina - 1e-9 && dr.Ki >= dr.MaxKi - 1e-9;
		bool humanoNao = h.stamina < h.maxstamina * 0.5 && h.Ki < h.MaxKi * 0.5;
		AfirmarV30("**O MAJIN TRAVA O VIGOR NO CHEIO** (e nao cai a 50% pra travar), e **o androide perpetuo trava o vigor E o Ki** -- num tique do corpo, de 3% e de 10%/30% pra 100%; o Humano continua onde estava",
				   majinCheio && droideCheio && humanoNao,
				   $"majin vigor {m.stamina / m.maxstamina:P0}; droide vigor {dr.stamina / dr.maxstamina:P0} ki {dr.Ki / dr.MaxKi:P0}; humano vigor {h.stamina / h.maxstamina:P0} ki {h.Ki / h.MaxKi:P0}");

		// O KI ACIMA DE 100% DO ANDROIDE NAO E CORTADO: o cadeado so segura o piso.
		dr.Ki = dr.MaxKi * 1.3;
		Nutricao.ManterOsInfinitos(dr);
		AfirmarV30("...o cadeado do Ki do androide so segura o PISO: carregado a 130%, ele continua em 130%",
				   Math.Abs(dr.Ki / dr.MaxKi - 1.3) < 1e-9, $"{dr.Ki / dr.MaxKi:P1}");

		// MORTO (item 29 -- o dono: *"mortos tem vigor infinito"*): TODO morto trava o vigor no cheio -- o Majin, o androide e o
		// Humano. E o Ki do androide morto TAMBEM trava (dono, 2026-10-20: *"eles mortos ficam sem energia infinita"* -- o motor perpetuo
		// parava com a morte, e isso era o bug); o Humano morto continua com o Ki onde estava. O bio com o motor no sangue vai junto.
		m.dead = true;
		dr.dead = true;
		h.dead = true;
		Gastar();
		Nutricao.QueimarVigor(h);   // o estomago nao o queima nem o crava em meio tanque
		Nutricao.ManterOsInfinitos(m);
		Nutricao.ManterOsInfinitos(dr);
		Nutricao.ManterOsInfinitos(h);
		bool mortosCheios = m.stamina >= m.maxstamina - 1e-9 && dr.stamina >= dr.maxstamina - 1e-9 && h.stamina >= h.maxstamina - 1e-9;
		string vigoresMortos = $"majin {m.stamina / m.maxstamina:P0}; droide vigor {dr.stamina / dr.maxstamina:P0} ki {dr.Ki / dr.MaxKi:P0}; humano {h.stamina / h.maxstamina:P0}";
		var bioMorto = new Fighter { Race = Jandirus.Core.Races.BioAndroids.Raca, bio_racas_dna = ["Android"], bio_dna_infinito = true, dead = true };
		bioMorto.Ki = bioMorto.MaxKi * 0.30;
		Nutricao.ManterOsInfinitos(bioMorto);
		bool motorDosMortos = dr.Ki >= dr.MaxKi - 1e-9 && bioMorto.Ki >= bioMorto.MaxKi - 1e-9 && h.Ki < h.MaxKi * 0.5;
		string kisMortos = $"droide ki {dr.Ki / dr.MaxKi:P0}; bio do motor ki {bioMorto.Ki / bioMorto.MaxKi:P0}; humano ki {h.Ki / h.MaxKi:P0}";
		Nutricao.MotorParaNaMorteDeTeste = true;
		double kiDoDroideComDefeito, kiDoBioComDefeito;
		try
		{
			dr.Ki = dr.MaxKi * 0.30;
			bioMorto.Ki = bioMorto.MaxKi * 0.30;
			Nutricao.ManterOsInfinitos(dr);
			Nutricao.ManterOsInfinitos(bioMorto);
			kiDoDroideComDefeito = dr.Ki / dr.MaxKi;
			kiDoBioComDefeito = bioMorto.Ki / bioMorto.MaxKi;
		}
		finally { Nutricao.MotorParaNaMorteDeTeste = false; }
		Nutricao.MortoComMeioTanqueDeTeste = true;
		double humanoComDefeito;
		try
		{
			Gastar();
			Nutricao.QueimarVigor(h);
			Nutricao.ManterOsInfinitos(h);
			humanoComDefeito = h.stamina / h.maxstamina;
		}
		finally { Nutricao.MortoComMeioTanqueDeTeste = false; }
		m.dead = false;
		dr.dead = false;
		h.dead = false;
		AfirmarV30("**O MORTO TEM VIGOR INFINITO** (item 29: *\"mortos tem vigor infinito\"*): o Majin, o androide e o Humano mortos ficam com o vigor no CHEIO",
				   mortosCheios, vigoresMortos);
		AfirmarV30("**O MOTOR PERPETUO NAO PARA COM A MORTE** (dono, 2026-10-20: *\"eles mortos ficam sem energia infinita\"*): o androide de energia infinita morto -- e o bio com o motor dele no sangue -- volta ao Ki CHEIO no cadeado do corpo; o Humano morto, nao",
				   motorDosMortos, kisMortos);
		AfirmarV30("(defeito injetado: o motor para com a morte, o jogo de antes) o Ki do androide morto e o do bio com o motor ficam nos 30% -- a regua de cima REPROVA",
				   kiDoDroideComDefeito < 0.31 && kiDoBioComDefeito < 0.31, $"droide {kiDoDroideComDefeito:P0}; bio {kiDoBioComDefeito:P0}");
		AfirmarV30("(defeito injetado: o meio tanque do DM, `StaminaDrain.dm:35-36`) o Humano morto fica cravado em 50% -- *\"Mortos atualmente o vigor fica preso em 50%\"*",
				   Math.Abs(humanoComDefeito - 0.5) < 1e-9, $"{humanoComDefeito:P1}");

		Nutricao.VigorDoMajinComPisoDeTeste = true;
		Nutricao.KiDoAndroideSemPisoDeTeste = true;
		double vigorDoMajin, depoisDoEstomago, kiDoDroide;
		try
		{
			Gastar();
			TickDosRelogiosDoCorpo(Protocol.TickSeconds);
			vigorDoMajin = m.stamina / m.maxstamina;
			kiDoDroide = dr.Ki / dr.MaxKi;
			Nutricao.QueimarVigor(m);
			depoisDoEstomago = m.stamina / m.maxstamina;
		}
		finally
		{
			Nutricao.VigorDoMajinComPisoDeTeste = false;
			Nutricao.KiDoAndroideSemPisoDeTeste = false;
		}
		AfirmarV30("(defeito injetado: o jogo de antes) o Majin fica nos 3% ate a passada do estomago o levar a 50% e travar la, e o Ki do androide fica nos 30% -- *\"n precisa fazer o vigor deles cair pra 50% e dps travar\"*",
				   vigorDoMajin < 0.05 && depoisDoEstomago is > 0.49 and <= 0.5 && kiDoDroide < 0.31,
				   $"majin {vigorDoMajin:P0} -> {depoisDoEstomago:P0}; droide ki {kiDoDroide:P0}");
		LimparTudoDaBancada();
	}

	// =====================================================================
	// B) O NPC ABSORVIDO PELO MAJIN SOME (item 16)
	// =====================================================================
	private void ONpcAbsorvidoSomeV30()
	{
		GD.Print("[v030] --- B) o NPC absorvido pelo Majin some, e o jogo diz que ele foi absorvido (item 16) ---");
		if (_moldes?.Get("cidadao") is not { } molde) { AfirmarV30("(montagem) o molde de cidadao existe", false); return; }
		_mjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDoMajin);   // a praca do Majin e procurada no mapa da bancada dele
		Vec2 praca = PracaDoMajin();
		ServerPlayer majin = ForjarMj("bancada v030: o Majin", praca, 1_000_000, majin: true);
		ZoneKey zona = majin.Zone;
		int Cadaveres() => ZoneList(zona.Hash).Count(x => x.ECadaver);

		// UM NPC DO MUNDO (com papel de cidadao), derrubado e absorvido pelo botao de producao; depois, o dreno do fim do tique.
		(bool Saiu, int NovosCadaveres, List<string> Ouvido) Absorver(string nome)
		{
			ServerPlayer npc = ForjarMj(nome, praca + new Vec2(ZoneCollision.TileSize, 0), 80_000, pessoa: false);
			npc.Papel = new PapelDeNpc(molde, (ulong)npc.Id);
			DerrubarMj(npc);
			int antes = Cadaveres();
			List<string> ouvido = Ouvir(() => ApertarAbsorver(majin));
			TirarOsNpcsMortos();
			bool saiu = !_players.ContainsKey(npc.Id) && !ZoneList(zona.Hash).Contains(npc);
			return (saiu, Cadaveres() - antes, ouvido);
		}

		var (saiu, cadaveres, ouvido) = Absorver("bancada v030: o cidadao");
		AfirmarV30("**O NPC ABSORVIDO SOME**: sai do mundo no fim do tique e NAO deixa cadaver pra enterrar -- e a frase diz que ele foi ABSORVIDO, e nao devorado",
				   saiu && cadaveres == 0 && Disse(ouvido, "absorve") && !Disse(ouvido, "devora"),
				   $"saiu {saiu}; cadaveres novos {cadaveres}; ouvido: {string.Join(" | ", ouvido)}");
		UsarHabilidade(majin, "expelir");

		AbsorvidoDeixaCorpoDeTeste = true;
		try
		{
			var (saiu2, _, ouvido2) = Absorver("bancada v030: o cidadao de antes");
			AfirmarV30("(defeito injetado: o jogo de antes) o NPC \"devorado\" fica no chao, morto, esperando a vez de virar cadaver -- *\"o npc so morre dando pra enterrar\"*",
					   !saiu2 && Disse(ouvido2, "devora"), $"saiu {saiu2}; ouvido: {string.Join(" | ", ouvido2)}");
		}
		finally { AbsorvidoDeixaCorpoDeTeste = false; }
		UsarHabilidade(majin, "expelir");

		// (2026-10-16) O CORPO SEM PAPEL TAMBEM SOME -- *"lembrando q majin absorver npc faz ele sumir sem deixar um corpo"*. A fera solta e o
		// corpo forjado nao sao NPC do mundo (nao tem papel), e a regra de 10-15 os deixava mortos no chao pra sempre: as fotos da saga
		// mostravam a vitima caida, com o "[E] revistar" em cima. Ver `SomeAoSerAbsorvido`.
		(bool Saiu, int NovosCadaveres, bool Morto) AbsorverSemPapel(string nome)
		{
			ServerPlayer corpo = ForjarMj(nome, praca + new Vec2(ZoneCollision.TileSize, 0), 80_000, pessoa: false);
			DerrubarMj(corpo);
			int antes = Cadaveres();
			ApertarAbsorver(majin);
			TirarOsNpcsMortos();
			bool saiu = !_players.ContainsKey(corpo.Id) && !ZoneList(zona.Hash).Contains(corpo);
			return (saiu, Cadaveres() - antes, corpo.Ficha.dead);
		}

		var semPapel = AbsorverSemPapel("bancada v030: o corpo sem papel");
		AfirmarV30("**O CORPO SEM PAPEL TAMBEM SOME** (a fera solta, o corpo forjado): sai do mundo no fim do tique e NAO deixa cadaver -- *\"lembrando q majin absorver npc faz ele sumir sem deixar um corpo\"*",
						   semPapel.Saiu && semPapel.NovosCadaveres == 0, $"saiu {semPapel.Saiu}; cadaveres novos {semPapel.NovosCadaveres}");
		UsarHabilidade(majin, "expelir");

		SoNpcDoMundoSomeDeTeste = true;
		try
		{
			var deAntes = AbsorverSemPapel("bancada v030: o corpo sem papel de antes");
			AfirmarV30("(defeito injetado: a regra de 10-15, so o NPC do mundo some) o corpo sem papel fica MORTO NO CHAO -- o \"[E] revistar\" das fotos da saga",
							   !deAntes.Saiu && deAntes.Morto, $"saiu {deAntes.Saiu}; morto {deAntes.Morto}");
		}
		finally { SoNpcDoMundoSomeDeTeste = false; }
		UsarHabilidade(majin, "expelir");
	}

	// =====================================================================
	// S) O SOM DE VEGETA (item 8)
	// =====================================================================
	private void OSomDeVegetaV30()
	{
		GD.Print("[v030] --- S) o som ambiente de Vegeta (item 8) ---");
		string? vegeta = Jandirus.Client.Trilha.AmbienteDe("Vegeta"), terra = Jandirus.Client.Trilha.AmbienteDe("Earth");
		AfirmarV30("**VEGETA SOA COMO A TERRA**: o ambiente e o mesmo arquivo -- e nao o deserto de antes",
				   !string.IsNullOrEmpty(vegeta) && vegeta == terra && !vegeta!.EndsWith("desert.ogg"), $"vegeta {vegeta}; terra {terra}");
	}

	// =====================================================================
	// C) OS AVISOS COM COR (item 20)
	// =====================================================================
	private void OsAvisosComCorV30()
	{
		GD.Print("[v030] --- C) os avisos de raiva e de zenkai saem com o tom proprio (item 20) ---");
		LimparTudoDaBancada();
		Vec2 c = TerraFirmeG12(4);
		ServerPlayer enlutado = ForjarV2("Enlutado", c, 10_000);
		ServerPlayer saiyajin = ForjarV2("Zenkai", c + new Vec2(0, ZoneCollision.TileSize), 1_000, raca: "Saiyan");
		ServerPlayer forte = ForjarV2("Carrasco", c + new Vec2(0, 2 * ZoneCollision.TileSize), 100_000);

		var tons = new List<(Protocol.Fala Tom, string Texto)>();
		EscutaDeTons = tons;
		try
		{
			AmigoAbatido(enlutado, "o amigo da bancada", NivelDeRaiva.Comum);
			saiyajin.Ficha.zenkaiGuardado = 1_000;   // (2026-10-26) o banco do Zenkai que a derrota solta
			ZenkaiPorDerrota(saiyajin, forte, morreu: false);
		}
		finally { EscutaDeTons = null; }

		bool raiva = tons.Exists(x => x.Tom == Protocol.Fala.AvisoDeRaiva && x.Texto.Contains("o amigo da bancada"));
		bool zenkai = tons.Exists(x => x.Tom == Protocol.Fala.AvisoDeZenkai);
		bool resto = tons.Where(x => x.Tom == Protocol.Fala.Sistema).All(x => !x.Texto.Contains("o amigo da bancada"));
		AfirmarV30("**A RAIVA E O ZENKAI SAEM PINTADOS**: o aviso do amigo que cai vai no tom de raiva, o do zenkai no tom de zenkai (e nao no `Sistema` de todo recado)",
				   raiva && zenkai && resto, string.Join(" | ", tons.Select(x => $"{x.Tom}: {x.Texto}")));
		var semTom = new List<(Protocol.Fala Tom, string Texto)>();
		EscutaDeTons = semTom;
		AvisoSemTomDeTeste = true;
		try { AmigoAbatido(enlutado, "o outro amigo da bancada", NivelDeRaiva.Extrema); }
		finally { AvisoSemTomDeTeste = false; EscutaDeTons = null; }
		AfirmarV30("(defeito injetado: o aviso sem tom) a raiva volta a sair no `Sistema` de todo recado, da mesma cor que \"zona carregada\"",
				   semTom.Exists(x => x.Tom == Protocol.Fala.Sistema && x.Texto.Contains("o outro amigo da bancada"))
				   && !semTom.Exists(x => x.Tom == Protocol.Fala.AvisoDeRaiva), string.Join(" | ", semTom.Select(x => $"{x.Tom}: {x.Texto}")));
		AfirmarV30("...e os dois tons novos sao recados do SERVIDOR: o cliente nao fala neles (o funil da fala so aceita os seis canais de gente)",
				   Protocol.Fala.AvisoDeRaiva > Protocol.Fala.Looc && Protocol.Fala.AvisoDeZenkai > Protocol.Fala.Looc);
		LimparTudoDaBancada();
	}

	// =====================================================================
	// ZB) O ZENKAI EM BANCO (o pedido 95b do dono, 2026-10-26)
	// =====================================================================
	/// <summary>
	/// O dono escolheu a "mistura" do DU com o de hoje: o treino guarda de 5 a 15% de cada ganho de quem tem sangue Saiyajin num banco do
	/// Zenkai; todo nocaute (sem recarga) solta de 20 a 80% dele PRA SEMPRE no BP base -- em dobro na morte e com a maioria do corpo em
	/// estado critico --; o teto e o requisito pessoal do SSJ3 (o que sai mingua ate ele, e la se aposenta); e uma frase so. Pelos funis de
	/// producao: o ganho pelo `GanhoDeAtividade`, a derrota pelo `ZenkaiPorDerrota` (o que o combate chama). Os contra-exemplos sao os
	/// mutantes do `ciclo.py` do ag/zenkaibanco. (Era "a recarga proporcional e o teto que mingua", de 2026-10-24.)
	/// </summary>
	private void OZenkaiEmBancoV30()
	{
		GD.Print("[v030] --- ZB) o Zenkai em banco: o treino guarda, o nocaute solta, a morte e o corpo em frangalhos dobram (dono, 2026-10-26) ---");
		LimparTudoDaBancada();
		Vec2 c = TerraFirmeG12(4);
		ServerPlayer s = ForjarV2("Zenkai", c, 1_000_000, raca: "Saiyan");
		ServerPlayer h = ForjarV2("Humano", c + new Vec2(0, ZoneCollision.TileSize), 1_000_000);
		ServerPlayer algoz = ForjarV2("Algoz", c + new Vec2(ZoneCollision.TileSize, 0), 1e6);
		// o limiar pessoal: o SSJ3 pede 1 bi de base (`ssj3at` de 10 bi) -- longe do BP de 1 mi, o que sai e quase inteiro
		s.Forma.Limiares = new Jandirus.Core.Forms.LimiaresPessoais { ssj3at = 1e10 };
		double requisito = RequisitoDoSsj3De(s);
		static bool Entre(double x, double lo, double hi) => x >= lo - 1e-6 && x <= hi + 1e-6;

		// ---- 1. O TREINO GUARDA ----
		s.Ficha.zenkaiGuardado = h.Ficha.zenkaiGuardado = 0;
		s.Ficha.GanhoDeAtividade(10_000);
		h.Ficha.GanhoDeAtividade(10_000);
		AfirmarV30("**O TREINO GUARDA NO BANCO DO ZENKAI** (o dono, 2026-10-26): um ganho de 10.000 do Saiyajin guarda de 500 a 1.500 (5 a 15%, sem bonus) -- e o do Humano, nada",
				   Entre(s.Ficha.zenkaiGuardado, 500, 1500) && h.Ficha.zenkaiGuardado == 0,
				   $"Saiyajin {s.Ficha.zenkaiGuardado:0.##}; Humano {h.Ficha.zenkaiGuardado:0.##}");

		// ---- 2. TODO NOCAUTE SOLTA, PRA SEMPRE E SEM RECARGA ----
		(double Ganho, double Saiu) Derrota(bool morreu, double banco, double bp)
		{
			s.Ficha.BP = bp;
			s.Ficha.dead = false;
			s.Ficha.zenkaiGuardado = banco;
			ZenkaiPorDerrota(s, algoz, morreu);
			return (s.Ficha.BP - bp, banco - s.Ficha.zenkaiGuardado);
		}
		double fator = Fighter.FatorDoTetoDoZenkai(1_000_000, requisito);
		var um = Derrota(false, 100_000, 1_000_000);
		double bpDepoisDoUm = s.Ficha.BP, bancoDepoisDoUm = s.Ficha.zenkaiGuardado;
		ZenkaiPorDerrota(s, algoz, false);
		double segundo = s.Ficha.BP - bpDepoisDoUm;
		AfirmarV30("**TODO NOCAUTE SOLTA DE 20 A 80% DO BANCO, PRA SEMPRE E SEM RECARGA** (o dono: *\"todo nocaute sem recarga\"*, *\"oq sair do banco fica pra sempre\"*): de 100.000 guardados o primeiro solta de 20.000 a 80.000 pro BP base (o fator do requisito do SSJ3, longe dali, quase 1), e o segundo nocaute, logo em seguida, solta de novo",
				   requisito > 0 && fator > 0.99 && Entre(um.Saiu, 20_000, 80_000) && Math.Abs(um.Ganho - um.Saiu * fator) < 1e-3
				   && segundo > 0 && s.Ficha.zenkaiGuardado < bancoDepoisDoUm,
				   $"requisito {requisito:N0}; saiu {um.Saiu:N0}, ganhou {um.Ganho:N0} (fator {fator:0.####}); o segundo +{segundo:N0}");

		// ---- 3. A MORTE E A MAIORIA DO CORPO EM ESTADO CRITICO, EM DOBRO ----
		// a morte pelo FUNIL DA DERROTA (`AoPerderALuta`, o que o combate chama), que leva o "morreu" ate o Zenkai
		s.Ficha.BP = 1_000_000;
		s.Ficha.dead = false;
		s.Ficha.zenkaiGuardado = 100_000;
		AoPerderALuta(s, algoz, morreu: true);
		var naMorte = (Ganho: s.Ficha.BP - 1_000_000, Saiu: 100_000 - s.Ficha.zenkaiGuardado);
		Body corpo = s.Combate.Corpo;
		int metade = corpo.Partes.Count / 2 + 1, postos = 0;
		foreach (BodyPart p in corpo.Partes)
			if (!p.Decepado && postos < metade) { p.Vida = p.VidaMax * 0.3; postos++; }
		bool maioria = corpo.MaioriaEmEstadoCritico();
		var critico = Derrota(false, 100_000, 1_000_000);
		foreach (BodyPart p in corpo.Partes) p.Vida = p.VidaMax;
		AfirmarV30("**NA MORTE, EM DOBRO; E COM A MAIORIA DO CORPO EM ESTADO CRITICO, TAMBEM -- SEM MORRER** (o dono: *\"se a maioria do corpo do saiyajin estiver em estado crtico, ele ganha o boost igual da morte (2x)\"*): o que sai do banco vale 2x no BP",
				   maioria && naMorte.Saiu > 0 && Math.Abs(naMorte.Ganho - 2 * naMorte.Saiu * fator) < 1e-3
				   && critico.Saiu > 0 && Math.Abs(critico.Ganho - 2 * critico.Saiu * fator) < 1e-3,
				   $"morte: saiu {naMorte.Saiu:N0}, ganhou {naMorte.Ganho:N0}; maioria critica={maioria}: saiu {critico.Saiu:N0}, ganhou {critico.Ganho:N0}");

		// ---- 4. QUEM JA ESTAVA MORTO NAO GANHA ----
		s.Ficha.BP = 1_000_000;
		s.Ficha.dead = true;
		s.Ficha.zenkaiGuardado = 100_000;
		ZenkaiPorDerrota(s, algoz, morreu: false);
		bool almaNaoGanha = s.Ficha.BP == 1_000_000 && s.Ficha.zenkaiGuardado == 100_000;
		s.Ficha.dead = false;
		AfirmarV30("...e QUEM JA ESTAVA MORTO nao ganha (o nocaute de uma alma, o torneio do Ceu): o BP fica e o banco tambem", almaNaoGanha,
				   $"BP {s.Ficha.BP:N0}, banco {s.Ficha.zenkaiGuardado:N0}");

		// ---- 5. O TETO: O REQUISITO DO SSJ3, COMO HOJE ----
		var naMetade = Derrota(false, 100_000, requisito / 2);
		var colado = Derrota(false, 1e15, requisito * 0.999);
		var noRequisito = Derrota(false, 100_000, requisito);
		AfirmarV30("**O TETO E O REQUISITO PESSOAL DO SSJ3, COMO HOJE** (o dono: *\"o limite dele é no req do ssj3 como é hoje\"*): na metade dele o que sai vale a metade; um banco enorme a um passo dele nao o passa; e no requisito nada sai (o banco fica)",
				   naMetade.Saiu > 0 && Math.Abs(naMetade.Ganho - naMetade.Saiu * 0.5) < 1e-3
				   && colado.Ganho > 0 && requisito * 0.999 + colado.Ganho <= requisito + 1e-3
				   && noRequisito.Ganho == 0 && noRequisito.Saiu == 0,
				   $"metade: saiu {naMetade.Saiu:N0}, ganhou {naMetade.Ganho:N0}; colado +{colado.Ganho:N0}; no requisito {noRequisito.Ganho:N0}/{noRequisito.Saiu:N0}");

		// ---- 6. UMA FRASE SO ----
		var tons = new List<(Protocol.Fala Tom, string Texto)>();
		EscutaDeTons = tons;
		try { Derrota(false, 100_000, 1_000_000); }
		finally { EscutaDeTons = null; }
		var doZenkai = tons.Where(x => x.Tom == Protocol.Fala.AvisoDeZenkai).ToList();
		AfirmarV30("**UMA FRASE SO** (o dono: *\"pode ser apenas uma mensagem algo como 'seu corpo se torna mais forte apos sofrer'\"*): o nocaute que solta o Zenkai diz so isso, no tom do Zenkai, sem numero",
				   doZenkai.Count == 1 && doZenkai[0].Texto == FraseDoZenkai && !doZenkai[0].Texto.Any(char.IsDigit),
				   string.Join(" | ", tons.Select(x => $"{x.Tom}: {x.Texto}")));
		LimparTudoDaBancada();
	}

	// =====================================================================
	// R) O RABO QUE NAO VOLTA CICATRIZA (itens 3 e 19)
	// =====================================================================
	private void ORaboQueNaoVoltaCicatrizaV30()
	{
		GD.Print("[v030] --- R) o rabo que nao volta: a ferida fecha em dez minutos e sai do boneco (itens 3 e 19) ---");
		LimparTudoDaBancada();
		Vec2 c = TerraFirmeG12(4);
		const double Ferida = Jandirus.Core.Races.RaboDoSaiyajin.SegundosDaFerida;
		ServerPlayer s = ForjarV2("SemRabo", c, 30_000, raca: "Saiyan");
		if (s.Combate?.Corpo.Achar(Jandirus.Core.Races.RaboDoSaiyajin.Membro) is not { } rabo)
		{ AfirmarV30("(montagem) o Saiyajin forjado nasce com rabo", false); return; }
		Body corpo = s.Combate.Corpo;

		// ARRANCADO PELO FUNIL DE PRODUCAO DA PERDA (o Saiyajin nao-Primal acima de 20 mil nao o refaz)
		rabo.Decepado = true;
		MarcarORaboPerdido(s, [rabo]);
		bool aberta = rabo.Perdido && Math.Abs(rabo.FeridaAberta - Ferida) < 1e-9 && Feridas.Grave(Feridas.De(corpo)) && ParteVaiPraTela(rabo);
		TickDaFeridaDoRabo(Ferida - 1);
		bool aindaAberta = !rabo.CicatrizouDeVez && Feridas.Grave(Feridas.De(corpo)) && ParteVaiPraTela(rabo);
		TickDaFeridaDoRabo(2);
		bool fechou = rabo.CicatrizouDeVez && !Feridas.Grave(Feridas.De(corpo)) && !ParteVaiPraTela(rabo);
		double[] foto = FotografarCorpo(s.Combate)[rabo.Nome];
		AfirmarV30("**O RABO QUE NAO VOLTA CICATRIZA EM DEZ MINUTOS**: arrancado, o abdomen sangra (ferida grave) e o boneco mostra o rabo; aos 599 s ainda esta aberto; aos 601 s fecha -- sai da mascara de feridas e do boneco, e o save guarda o relogio",
				   aberta && aindaAberta && fechou && foto.Length == 4 && foto[3] == 0,
				   $"aberta {aberta}; ainda aberta {aindaAberta}; fechou {fechou}; foto [{string.Join(", ", foto)}]");

		// O RABO QUE VOLTA (o Saiyajin abaixo do limiar) nao ganha prazo nenhum: e ferida ate crescer de novo.
		ServerPlayer novo = ForjarV2("RaboQueVolta", c + new Vec2(0, ZoneCollision.TileSize), 1_000, raca: "Saiyan");
		if (novo.Combate?.Corpo.Achar(Jandirus.Core.Races.RaboDoSaiyajin.Membro) is { } raboNovo)
		{
			raboNovo.Decepado = true;
			MarcarORaboPerdido(novo, [raboNovo]);
			TickDaFeridaDoRabo(10_000);
			AfirmarV30("...e o rabo que VOLTA nao e tocado: nao e perdido, nao cicatriza, continua ferida (e no boneco) ate crescer",
					   !raboNovo.Perdido && !raboNovo.CicatrizouDeVez && ParteVaiPraTela(raboNovo) && Feridas.Grave(Feridas.De(novo.Combate.Corpo)));
		}

		Feridas.RaboPerdidoSangraPraSempreDeTeste = true;
		bool sangra, noBoneco;
		try { sangra = Feridas.Grave(Feridas.De(corpo)); noBoneco = ParteVaiPraTela(rabo); }
		finally { Feridas.RaboPerdidoSangraPraSempreDeTeste = false; }
		AfirmarV30("(defeito injetado: o rabo sangra pra sempre) cicatrizado, o abdomen continua sangrando e o boneco continua com o rabo -- *\"fica com efeito de ferimento eterno\"*",
				   sangra && noBoneco);
		LimparTudoDaBancada();
	}

	// =====================================================================
	// H) A RAIVA NO RETRATO DO PODER (item 4)
	// =====================================================================
	private void ARaivaNoRetratoV30()
	{
		GD.Print("[v030] --- H) a raiva vai no retrato do poder, que o HUD le (item 4) ---");
		LimparTudoDaBancada();
		ServerPlayer p = ForjarV2("Raivoso", TerraFirmeG12(4), 10_000);
		long agora = NowMs();
		PoderEmUsoState calmo = PoderEmUsoDe(p);
		p.RaivaComumAte = agora + 60_000;
		PoderEmUsoState comRaiva = PoderEmUsoDe(p);
		p.FuriaExtremaAte = agora + 60_000;
		PoderEmUsoState furioso = PoderEmUsoDe(p);
		p.RaivaComumAte = p.FuriaExtremaAte = 0;
		AfirmarV30("**A RAIVA VAI NO RETRATO DO PODER**: calmo nenhum bit, com raiva o `ComRaiva`, furioso o `Furioso` -- o HUD escreve \"COM RAIVA\" e \"FURIOSO\", e a linha do PODER continua sem nada a dizer (o poder esta inteiro e a vista)",
				   !calmo.ComRaiva && !calmo.Furioso && comRaiva.ComRaiva && !comRaiva.Furioso && furioso.Furioso
				   && calmo.Padrao && comRaiva.Padrao && furioso.Padrao
				   && Jandirus.Client.Hud.NaRaiva(calmo) == "" && Jandirus.Client.Hud.NaRaiva(comRaiva) == "COM RAIVA" && Jandirus.Client.Hud.NaRaiva(furioso) == "FURIOSO",
				   $"calmo {calmo.Bits}; com raiva {comRaiva.Bits}; furioso {furioso.Bits}");
		LimparTudoDaBancada();
	}

	// =====================================================================
	// P) OS PESOS: ATE O LIMITE DO CORPO, E A ARTE NO CHAO (itens 9 e 10)
	// =====================================================================
	private void OsPesosAteOLimiteEComArteV30()
	{
		GD.Print("[v030] --- P) os pesos: ate o limite do corpo de uma vez, e a arte que cai no chao (itens 9 e 10) ---");
		bool conta = Jandirus.Core.Items.PesoDeTreino.MelhoriasAteOLimite(0, 1000) == 5
							 && Jandirus.Core.Items.PesoDeTreino.MaximoDepoisDe(0, 5) == 1600
							 && Jandirus.Core.Items.PesoDeTreino.PrecoDeVariasEmZeni(0, 5) == 7500
							 && Jandirus.Core.Items.PesoDeTreino.MelhoriasAteOLimite(1600, 1000) == 0
							 && Jandirus.Core.Items.PesoDeTreino.MelhoriasAteOLimite(0, 50) == 0
							 && Jandirus.Core.Items.PesoDeTreino.MelhoriasAteOLimite(0, 51) == 1;
		AfirmarV30("**A CONTA DO LIMITE**: de 50 kg pra um corpo de 1.000 kg sao 5 melhorias (1.600 kg) por 7.500 zeni -- a soma das cinco, o mesmo dinheiro de dobrar uma a uma; e quem ja passou do corpo nao tem o que comprar",
				   conta);

		LimparTudoDaBancada();
		ServerPlayer p = ForjarV2("Halterofilista", TerraFirmeG12(4), 10_000);
		p.Mochila.Guardar(Jandirus.Core.Items.CatalogoDeItens.Pesos);
		p.Ficha.weight_cap_hw = 1000;   // o limite do corpo, escrito: e um recorde que so a luta sobe
		p.Ficha.Zeni = 12_000;
		// (2026-10-18) o peso e um item com estado: melhorar e trocar de arte mudam o id dele -- a bancada segue o de AGORA
		string Pesos() => p.Mochila.Pilhas.FindLast(x => Jandirus.Core.Items.PesoDeTreino.EhPeso(x.Id)).Id ?? "";
		double Max() => Jandirus.Core.Items.PesoDeTreino.MaximoDe(Pesos());
		List<string> Fazer(string acao) => Ouvir(() => ComandoDeItem(p, "item_" + acao, Pesos()));

		Jandirus.Core.Items.PesoDeTreino.SoDobrandoDeTeste = true;
		List<string> semABotao;
		try { semABotao = Fazer(Jandirus.Core.Items.PesoDeTreino.AcaoAteOLimite); }
		finally { Jandirus.Core.Items.PesoDeTreino.SoDobrandoDeTeste = false; }
		bool soDobrando = Max() == 50 && p.Ficha.Zeni == 12_000;

		Fazer(Jandirus.Core.Items.PesoDeTreino.AcaoAteOLimite);
		double depois = Max(), zeni = p.Ficha.Zeni;
		List<string> deNovo = Fazer(Jandirus.Core.Items.PesoDeTreino.AcaoAteOLimite);
		Fazer("melhorar");
		AfirmarV30("**ATE O LIMITE DO CORPO PELA MOCHILA**: um clique leva os pesos de 50 a 1.600 kg por 7.500 zeni; o segundo diz que ja chegou; e dobrar continua valendo (3.200 kg por mais 3.000)",
				   depois == 1600 && zeni == 4500 && Disse(deNovo, "já chegam") && Max() == 3200 && p.Ficha.Zeni == 1500,
				   $"depois {depois}; zeni {zeni}; de novo: {string.Join(" | ", deNovo)}; agora {Max()} kg e {p.Ficha.Zeni} zeni");
		AfirmarV30("(defeito injetado: so dobrando) o botao nao leva a lugar nenhum -- *\"sem ter q ficar dobrando o peso varias vezes ate chegar no q vc quer\"*",
				   soDobrando && Disse(semABotao, "já chegam"), string.Join(" | ", semABotao));

		// A ARTE QUE CAI NO CHAO: escolhida pela grade (a porta de producao) e largada pela mochila
		(string nome, string caminho) = PalcoV30_ArteDosPesos(p.Id);
		Fazer("largar");
		Largado? comArte = LargadosDe(p.Zone.Hash).FindLast(l => Jandirus.Core.Items.PesoDeTreino.EhPeso(l.Item));
		// o segundo peso tem a MESMA arte (no id dele): com o defeito, ele cai sem ela -- sem o defeito, cairia com ela
		p.Mochila.Guardar(Jandirus.Core.Items.PesoDeTreino.IdDe(Jandirus.Core.Items.PesoDeTreino.KgDeFabrica, nome));
		Jandirus.Core.Items.PesoDeTreino.ArteSoNoCorpoDeTeste = true;
		try { Fazer("largar"); }
		finally { Jandirus.Core.Items.PesoDeTreino.ArteSoNoCorpoDeTeste = false; }
		Largado? semArte = LargadosDe(p.Zone.Hash).FindLast(l => Jandirus.Core.Items.PesoDeTreino.EhPeso(l.Item));
		AfirmarV30("**OS PESOS CAEM COM A ARTE QUE O DONO ESCOLHEU**: a folha vai no monte do chao (e dali pro fio e pro desenho)",
				   nome.Length > 0 && comArte != null && comArte.Arte == caminho, $"arte '{nome}' ({caminho}); no chao '{comArte?.Arte}'");
		AfirmarV30("(defeito injetado: a arte so no corpo) os pesos largados caem com a camisa de fabrica",
				   semArte != null && !ReferenceEquals(semArte, comArte) && semArte.Arte.Length == 0, $"no chao '{semArte?.Arte}'");
		LimparTudoDaBancada();
	}

	// =====================================================================
	// W) OS PESOS QUE LEMBRAM A PORCENTAGEM (item 28)
	// =====================================================================
	private void OsPesosQueLembramV30()
	{
		GD.Print("[v030] --- W) os pesos que lembram a porcentagem: o \"equipar\" veste a ultima escolhida (item 28) ---");
		LimparTudoDaBancada();
		ServerPlayer p = ForjarV2("Pesista", TerraFirmeG12(4), 10_000);
		p.Mochila.Guardar(CatalogoDeItens.Pesos);   // os 50 kg de fabrica
		List<string> Fazer(ServerPlayer quem, string acao, string valor = "") =>
			Ouvir(() => ComandoDeItem(quem, "item_" + acao, valor.Length > 0 ? $"{CatalogoDeItens.Pesos}/{valor}" : CatalogoDeItens.Pesos));
		double quarenta = 0.4 * PesoDeTreino.Maximo(0);

		List<string> nuncaEscolheu = Fazer(p, "equipar");
		bool nadaVestido = p.Ficha.Weighted == 0 && Disse(nuncaEscolheu, "escolha antes");
		Fazer(p, "ajustar", "40");
		bool ajustou = Math.Abs(p.Ficha.Weighted - quarenta) < 1e-6 && p.Ficha.pesoPct == 40;
		Fazer(p, "tirar");
		bool tirou = p.Ficha.Weighted == 0 && p.Ficha.pesoPct == 40;
		List<string> equipou = Fazer(p, "equipar");
		bool vestiuDeNovo = Math.Abs(p.Ficha.Weighted - quarenta) < 1e-6;
		AfirmarV30("**A PORCENTAGEM FICA GUARDADA** (*\"dps de setar a % do peso maximo ele salvaria aquele valor\"*): o \"ajustar\" guarda os 40%, o \"tirar\" nao os apaga, e o \"equipar\" veste os mesmos 40% sem perguntar -- e sem escolha nenhuma ainda, ele pede a porcentagem antes",
				   nadaVestido && ajustou && tirou && vestiuDeNovo && Disse(equipou, "40%"),
				   $"nunca: {string.Join(" | ", nuncaEscolheu)}; guardada {p.Ficha.pesoPct}; vestindo {p.Ficha.Weighted:N1} de {quarenta:N1} kg | {string.Join(" | ", equipou)}");

		// O DISCO: a porcentagem e campo da ficha, e a ficha vai inteira pro save
		var opcoes = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
		Fighter lida = System.Text.Json.JsonSerializer.Deserialize<Fighter>(System.Text.Json.JsonSerializer.Serialize(p.Ficha, opcoes), opcoes)!;
		AfirmarV30("...e ela atravessa o relog: vai com a ficha", lida.pesoPct == 40, $"{lida.pesoPct}");

		// O JOGO DE ANTES: a % nao fica guardada
		ServerPlayer q = ForjarV2("Esquecido", TerraFirmeG12(4) + new Vec2(0, ZoneCollision.TileSize), 10_000);
		q.Mochila.Guardar(CatalogoDeItens.Pesos);
		PesoDeTreino.PorcentagemEsquecidaDeTeste = true;
		List<string> semMemoria;
		try
		{
			Fazer(q, "ajustar", "40");
			Fazer(q, "tirar");
			semMemoria = Fazer(q, "equipar");
		}
		finally { PesoDeTreino.PorcentagemEsquecidaDeTeste = false; }
		AfirmarV30("(defeito injetado: a % nao fica guardada) o \"equipar\" nao tem com o que vestir -- a porcentagem volta a ser pedida toda vez",
				   q.Ficha.Weighted == 0 && Disse(semMemoria, "escolha antes"), string.Join(" | ", semMemoria));
		LimparTudoDaBancada();
	}

	// =====================================================================
	// Q) O SCOUTER QUE VOLTA LIGADO (item 27)
	// =====================================================================
	private void OScouterQueVoltaLigadoV30()
	{
		GD.Print("[v030] --- Q) o scouter que volta ligado do relog (item 27) ---");
		LimparTudoDaBancada();
		ServerPlayer p = ForjarV2("Leitor", TerraFirmeG12(4), 10_000);
		p.Mochila.Guardar(CatalogoDeItens.Scouter);
		List<string> ligou = Ouvir(() => ComandoDeItem(p, "item_equipar", CatalogoDeItens.Scouter));
		bool ligado = (p.PoderesConcedidos & Protocol.Poder.Scouter) != 0;

		// O CAMINHO DO DISCO: o save montado pelo `DeJogador`, serializado e lido de volta num corpo NOVO, como o login faz.
		var opcoes = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
		ServerPlayer Relogar(CharacterSave s, bool semScouter = false)
		{
			CharacterSave lido = System.Text.Json.JsonSerializer.Deserialize<CharacterSave>(System.Text.Json.JsonSerializer.Serialize(s, opcoes), opcoes)!;
			if (semScouter) lido.Mochila.Tirar(CatalogoDeItens.Scouter, 1);
			var novo = new ServerPlayer();
			AccountStore.ParaJogador(lido, novo);
			return novo;
		}
		static bool ComScouter(ServerPlayer o) => (o.PoderesConcedidos & Protocol.Poder.Scouter) != 0;

		CharacterSave save = AccountStore.DeJogador(p, NowMs());
		ServerPlayer voltou = Relogar(save), semAparelho = Relogar(save, semScouter: true);
		AfirmarV30("**O SCOUTER LIGADO VOLTA LIGADO** (*\"ao sair do server usando scouter, e dps entrar, o scouter e desequipado\"*): o save leva o bit e o login o devolve",
				   ligado && save.ScouterLigado && ComScouter(voltou), $"ligado={ligado} save={save.ScouterLigado} | {string.Join(" | ", ligou)}");
		AfirmarV30("...so com o aparelho na mochila: um scouter que saiu dela nao religa sozinho", !ComScouter(semAparelho));

		ComandoDeItem(p, "item_equipar", CatalogoDeItens.Scouter);   // desliga
		AfirmarV30("...e o desligado volta desligado", !ComScouter(p) && !ComScouter(Relogar(AccountStore.DeJogador(p, NowMs()))));

		ComandoDeItem(p, "item_equipar", CatalogoDeItens.Scouter);   // liga de novo
		ScouterAcordaDesligadoDeTeste = true;
		ServerPlayer comDefeito;
		try { comDefeito = Relogar(AccountStore.DeJogador(p, NowMs())); }
		finally { ScouterAcordaDesligadoDeTeste = false; }
		AfirmarV30("(defeito injetado: o jogo de antes) o scouter ligado acorda desligado no login", ComScouter(p) && !ComScouter(comDefeito));
		LimparTudoDaBancada();
	}

	// =====================================================================
	// X) O SANGUE DO BIO-ANDROIDE (itens 24, 25 e 25b)
	// =====================================================================
	private void OSangueDoBioV30()
	{
		GD.Print("[v030] --- X) o sangue do bio-androide: as regras do `SangueDoBio` (itens 24, 25 e 25b; o parto inteiro e da `--bioteste`, secao 16) ---");
		static Fighter Bio(params string[] sangue) => new() { Race = Jandirus.Core.Races.BioAndroids.Raca, bio_racas_dna = [.. sangue] };
		Fighter sozinho = Bio(), saiyajinMajin = Bio("Saiyan", "Majin");
		var antigo = new Fighter { Race = Jandirus.Core.Races.BioAndroids.Raca, bio_saiyan_dna = true };
		var humano = new Fighter { Race = "Human" };
		string comSangue = Jandirus.Core.Races.SangueDoBio.RacaComSangue(saiyajinMajin.Race, saiyajinMajin);
		string bio = Jandirus.Core.Races.BioAndroids.Raca;
		AfirmarV30("**A RACA COM SANGUE**: o bio com doadoras e `BioAndroid+Saiyan+Majin`, o bio sozinho e so `BioAndroid`, quem nao e bio continua a propria raca -- e o bio de antes da v0.3.0 carrega so o Saiyajin do bit que ele tinha",
				   comSangue == bio + "+Saiyan+Majin" && Jandirus.Core.Races.SangueDoBio.RacaComSangue(bio, sozinho) == bio
				   && Jandirus.Core.Races.SangueDoBio.RacaComSangue("Human", humano) == "Human"
				   && Jandirus.Core.Races.SangueDoBio.RacaComSangue(bio, antigo) == bio + "+Saiyan", comSangue);
		AfirmarV30("**SOZINHO O BIO NAO TEM NADA**: sem sangue Saiyajin, sem Zenkai; com ele, Zenkai (*\"zenkai se tiver saiyajin\"*) -- inclusive o bio de antes, pelo bit",
				   !sozinho.HasZenkai() && saiyajinMajin.HasZenkai() && antigo.HasZenkai());
		AfirmarV30("**O MAJIN NO SANGUE**: nao sangra, nao manca do ferimento e o vigor trava no cheio -- e o bio sozinho, nada disso",
				   Feridas.NaoSangra(comSangue) && DebuffsDeMembro.SemAtrasoDeFerimento(comSangue) && Nutricao.VigorSempreCheio(saiyajinMajin)
				   && !Feridas.NaoSangra(bio) && !DebuffsDeMembro.SemAtrasoDeFerimento(bio) && !Nutricao.VigorSempreCheio(sozinho));
		Fighter motor = Bio("Android");
		motor.bio_dna_infinito = true;
		AfirmarV30("**O MOTOR INFINITO NO SANGUE**: o bio de um androide de energia infinita trava o vigor E o Ki no cheio -- o de um androide comum, nao",
				   Nutricao.VigorSempreCheio(motor) && Nutricao.KiSempreCheio(motor) && !Nutricao.KiSempreCheio(Bio("Android")));
		(string r1, double v1) = Jandirus.Core.Races.SangueDoBio.RegeneracaoDoBio(saiyajinMajin, r => r == "Majin" ? 100 : 1);
		(string r2, _) = Jandirus.Core.Races.SangueDoBio.RegeneracaoDoBio(Bio("Namekian", "Kai"), r => r is "Namekian" or "Kai" ? 10 : 1);
		(string r3, double v3) = Jandirus.Core.Races.SangueDoBio.RegeneracaoDoBio(sozinho, _ => 50);
		AfirmarV30("**A REGENERACAO E A DA DOADORA QUE MAIS REGENERA** (o Majin da a dele); no empate ganha quem nao e Namekuseijin; e sozinho o bio tem a comum (Humano, 1)",
				   r1 == "Majin" && v1 == 100 && r2 == "Kai" && r3 == "Human" && v3 == 1, $"{r1}/{v1} {r2} {r3}/{v3}");
		List<string> fornada = Jandirus.Core.Races.SangueDoBio.DaFornada(
			new[] { new[] { "Saiyan" }, new[] { "Human", "Saiyan" }, new[] { bio }, new[] { "Majin" } });
		List<string> deUmBio = Jandirus.Core.Races.SangueDoBio.DeUmDoador(bio, bio, saiyajinMajin);
		List<string> meioSangue = Jandirus.Core.Races.SangueDoBio.DeUmDoador("Human", "Majin", humano);
		AfirmarV30("**O SANGUE DA FORNADA**: a uniao das amostras, na ordem, sem repetir e sem o proprio bio -- o bio colhido passa as doadoras DELE, e o meio-sangue vale pelas duas racas",
				   fornada.SequenceEqual(new[] { "Saiyan", "Human", "Majin" }) && deUmBio.SequenceEqual(new[] { "Saiyan", "Majin" })
				   && meioSangue.SequenceEqual(new[] { "Human", "Majin" }),
				   $"{string.Join("+", fornada)} | {string.Join("+", deUmBio)} | {string.Join("+", meioSangue)}");
		bool arvores = _skills != null
					   && _skills.ArvoresDe(bio + "+Namekian", "").Any(s => s.Path.Contains("namek", StringComparison.OrdinalIgnoreCase))
					   && _skills.ArvoresDe(bio + "+Namekian", "").Any(s => s.Path.Contains("bioandroid", StringComparison.OrdinalIgnoreCase))
					   && !_skills.ArvoresDe(bio, "").Any(s => s.Path.Contains("namek", StringComparison.OrdinalIgnoreCase));
		AfirmarV30("**AS ARVORES RACIAIS DAS DOADORAS**: com sangue Namekuseijin a arvore namek se abre, e a do proprio bio continua (e nela que mora a absorcao que o faz evoluir); sozinho, so a dele",
				   arvores);

		Jandirus.Core.Races.SangueDoBio.SangueNaoPassaDeTeste = true;
		bool semSangue;
		try
		{
			semSangue = !saiyajinMajin.HasZenkai() && Jandirus.Core.Races.SangueDoBio.RacaComSangue(bio, saiyajinMajin) == bio
								&& !Nutricao.VigorSempreCheio(saiyajinMajin);
		}
		finally { Jandirus.Core.Races.SangueDoBio.SangueNaoPassaDeTeste = false; }
		AfirmarV30("(defeito injetado: o sangue nao passa) o bio de Saiyajin e Majin volta a nao herdar nada -- nem Zenkai, nem a arvore, nem o vigor", semSangue);
		Jandirus.Core.Races.SangueDoBio.BioComOsPassivosDeAntesDeTeste = true;
		bool comOsDoDm;
		try { comOsDoDm = sozinho.HasZenkai(); }
		finally { Jandirus.Core.Races.SangueDoBio.BioComOsPassivosDeAntesDeTeste = false; }
		AfirmarV30("(defeito injetado: as passivas do DM) o bio sozinho volta a ter o Zenkai de todo bio (`combatgains.dm:14`) -- *\"bio androide sozinho N tem nada\"*",
				   comOsDoDm);
	}

	// =====================================================================
	// Z) O MORTO NA MEDITACAO PROFUNDA (item 26)
	// =====================================================================
	private void OMortoNaMenteV30()
	{
		GD.Print("[v030] --- Z) o morto na meditacao profunda: a regra no Core (item 26; o mergulho de verdade e da `--transeteste`, secao 9, e da `--diagv030`, familia P) ---");
		AfirmarV30("**SO O CADAVER E RECUSADO NA PORTA DA MENTE**: o fantasma de pe passa; o corpo no chao antes da viagem, nao -- e a frase diz por que",
				   DimensaoMental.PorQueNaoMergulhar(false, false, cadaver: false).Length == 0
				   && DimensaoMental.PorQueNaoMergulhar(false, false, cadaver: true).Contains("corpo caído", StringComparison.Ordinal)
				   && DimensaoMental.PorQueNaoMergulhar(true, false, cadaver: false).Length > 0
				   && DimensaoMental.PorQueNaoMergulhar(false, true, cadaver: false).Length > 0);
		AfirmarV30("**A MENTE E LUGAR DE MORTO** (*\"na interdimension e a excecao\"*): o morto la dentro esta de pe, como no Outro Mundo -- e no mundo dos vivos (sem o passe) continua o cadaver",
				   Alem.MortoDePe(true, DimensaoMental.Zona) && Alem.MortoDePe(true, Alem.ZonaDoOutroMundo)
				   && !Alem.MortoDePe(true, "Earth") && !Alem.MortoDePe(false, DimensaoMental.Zona));
		Alem.MenteDeitaOMortoDeTeste = true;
		bool deitado;
		try { deitado = !Alem.MortoDePe(true, DimensaoMental.Zona); }
		finally { Alem.MenteDeitaOMortoDeTeste = false; }
		AfirmarV30("(defeito injetado: a mente nao e lugar de morto) o fantasma la dentro volta a ser o cadaver deitado", deitado);
	}

	// =====================================================================
	// Y) OS RECURSOS DEZ VEZES (item 32)
	// =====================================================================
	private void OsRecursosDezVezesV30()
	{
		GD.Print("[v030] --- Y) os recursos: a pa, a furadeira e a Drill rendem dez vezes (item 32; a mineracao e a Drill inteiras sao da `--recursosteste` e da `--drillteste`) ---");
		var mochila = new Inventario();
		double PorSegundo() => Jandirus.Core.Economia.Recurso.PorSegundo(Jandirus.Core.Economia.Recurso.MelhorFerramenta(mochila));
		double mao = PorSegundo();
		mochila.Guardar(CatalogoDeItens.Pa);
		double pa = PorSegundo();
		mochila.Guardar(CatalogoDeItens.Furadeira);
		double furadeira = PorSegundo();
		var drill = new Jandirus.Core.Tech.DrillDaObra { Nivel = 1, ContadoAteMs = 1_000_000 };
		double cemSegundos = Jandirus.Core.Tech.Drill.Pedido(drill, 1_000_000 + 100_000);
		AfirmarV30("**DEZ VEZES** (*\"aumente em 10x os ganhos de recursos da drill, furadeira e pa\"*): com a pa 60 por segundo (eram 6), com a furadeira 120 (eram 12), e a Drill de nivel 1 junta 1.000 em 100 s (eram 100) -- e a mao, que nao estava no pedido, continua nos 3",
				   mao == 3 && pa == 60 && furadeira == 120 && Math.Abs(cemSegundos - 1000) < 1e-6 && Jandirus.Core.Tech.Drill.Taxa(10) == 100,
				   $"mao {mao}; pa {pa}; furadeira {furadeira}; Drill em 100 s {cemSegundos:0.##}; nivel 10 {Jandirus.Core.Tech.Drill.Taxa(10)}/s");
	}

	// =====================================================================
	// O) O CHUTE GIRATORIO DA A VOLTA INTEIRA (item 30)
	// =====================================================================
	private void OGiroV30()
	{
		GD.Print("[v030] --- O) o chute giratorio: o gesto do giro e a pose de golpe pela volta inteira (item 30; o desenho girando e da `--diagv030`, G) ---");
		LimparTudoDaBancada();
		ServerPlayer p = ForjarV2("Piao", TerraFirmeG12(4), 10_000);
		(bool Girou, long Pose) Girar()
		{
			p.Ficha.Ki = p.Ficha.MaxKi;
			_prontoG3.Remove(p.Id);
			p.AtaqueAte = 0;
			EscutaDeGestos = [];
			try
			{
				GiratorioG7(p);
				return (EscutaDeGestos!.Contains((p.Id, (byte)Protocol.GestoDoCorpo.Giro)), p.AtaqueAte - NowMs());
			}
			finally { EscutaDeGestos = null; }
		}
		(bool girou, long pose) = Girar();
		AfirmarV30("**O SPIN ATTACK DA A VOLTA INTEIRA** (*\"faca o personagem realmente dar um 360 pra representar o giro\"*): o gesto `Giro` sai pra zona, e o corpo segura a pose de golpe pela volta toda",
				   girou && pose > Protocol.GiroMs - 100, $"gesto={girou} pose={pose} ms (a volta e de {Protocol.GiroMs} ms)");
		GiroSemVoltaDeTeste = true;
		try { (girou, _) = Girar(); }
		finally { GiroSemVoltaDeTeste = false; }
		AfirmarV30("(defeito injetado: o jogo de antes) o Spin Attack bate sem girar -- nenhum gesto sai", !girou, $"gesto={girou}");
		LimparTudoDaBancada();
	}

	// =====================================================================
	// MI) AS MIRAGENS DA TECNICA: LONGAS, E EM VOLTA DO ALVO (item 33)
	// =====================================================================
	private void AsMiragensDaTecnicaV30()
	{
		GD.Print("[v030] --- MI) as miragens da Zanzoken Afterimage: longas, e com alvo o corpo ronda o alvo (item 33; quanto a miragem vive na tela e da `--diagv030`, MI) ---");
		LimparTudoDaBancada();
		const float T = ZoneCollision.TileSize;
		Vec2 c = TerraFirmeG12(6);
		ServerPlayer p = ForjarV2("Miragista", c, 10_000);
		ServerPlayer alvo = ForjarV2("Rodeado", c + new Vec2(4 * T, 0), 10_000);

		// O RELOGIO DO LOTE, passo a passo: a proxima miragem vence agora, e o `TickG14` de producao a solta.
		(List<(int Quem, Vec2 De, bool Longa)> Miragens, List<Vec2> Posicoes) Passos(int quantos)
		{
			var posicoes = new List<Vec2>();
			EscutaDeMiragens = [];
			try
			{
				for (int k = 0; k < quantos; k++)
				{
					if (_miragensG14.TryGetValue(p.Id, out MiragensDePeG14? m)) m.ProximaEm = NowMs();
					TickG14();
					posicoes.Add(p.Pos);
				}
				return ([.. EscutaDeMiragens!], posicoes);
			}
			finally { EscutaDeMiragens = null; }
		}
		void Ligar()
		{
			EsquecerG14(p.Id);
			p.Ficha.Ki = p.Ficha.MaxKi;
			p.Ficha.stamina = p.Ficha.maxstamina;
			_prontoG3.Remove(p.Id);
			MiragensG14(p);
		}
		static string EmTexto(IEnumerable<(int Quem, Vec2 De, bool Longa)> l) => string.Join(" | ", l.Select(x => $"{x.De} longa={x.Longa}"));

		// SEM ALVO: o de sempre -- a miragem onde o corpo esta --, mas a LONGA
		p.AlvoId = 0;
		Ligar();
		Vec2 parado = p.Pos;
		var semAlvo = Passos(2);
		AfirmarV30("**SEM ALVO, A MIRAGEM NASCE ONDE O CORPO ESTA** (*\"se n tiver target ai fica o normal\"*) -- e e a LONGA, a da tecnica (*\"as miragens nesse caso deveriam durar mais\"*)",
				   semAlvo.Miragens.Count == 2 && semAlvo.Miragens.All(x => x.Longa && x.De.Equals(parado)) && p.Pos.Equals(parado),
				   EmTexto(semAlvo.Miragens));

		// COM ALVO: a ronda
		Mirar(p, alvo.Id);
		var ronda = Passos(4);
		bool emVolta = ronda.Posicoes.All(q => Vec2.Distance(q, alvo.Pos) is > 1.4f * T and < 3.2f * T);
		bool quatroLugares = ronda.Posicoes.Distinct().Count() == 4;
		bool deixouAtras = ronda.Miragens.Count == 4 && ronda.Miragens.All(x => x.Longa) && ronda.Miragens[0].De.Equals(parado)
								   && Enumerable.Range(1, 3).All(i => ronda.Miragens[i].De.Equals(ronda.Posicoes[i - 1]));
		bool olhando = p.Facing == MoveRules.FacingFrom(alvo.Pos - p.Pos, p.Facing);
		AfirmarV30("**COM ALVO, A RONDA** (*\"o personagem vai ficar dando zanzokens em volta do target criando miragens rodeando o alvo\"*): quatro passos, quatro lugares em volta dele (a ~2 tiles), chegando olhando pra ele -- e a miragem longa fica em cada lugar por onde o corpo passou",
				   emVolta && quatroLugares && deixouAtras && olhando,
				   $"posicoes [{string.Join(" ", ronda.Posicoes)}] alvo {alvo.Pos} | {EmTexto(ronda.Miragens)} | olhando={olhando}");

		// O DUPLO CLIQUE NAO MUDOU: a piscada do Zanzoken deixa a miragem COMUM. O piscador tem o CORREDOR proprio (tres
		// tiles de terra firme pro leste) e o treino das outras bancadas que piscam (`--v021teste` G3, `--g12teste`): sem
		// pericia (`Ekiskill * Espeed/2`) o `Zanzoken` recusa calado, e a lista vazia nao diria nada da miragem.
		Vec2 cq = TerraFirmeG12(4);
		ServerPlayer q = ForjarV2("Piscador", cq, 10_000);
		q.Livro.Dar(PathDoZanzoken);
		q.Niveis.Por(PathDoZanzoken, 1);
		q.Ficha.kiskill = 3;
		q.Ficha.speed = 3;
		q.Ficha.Statify();
		q.Ficha.Ki = q.Ficha.MaxKi;
		EscutaDeMiragens = [];
		List<(int Quem, Vec2 De, bool Longa)> piscada;
		try
		{
			Zanzoken(q, cq + new Vec2(3 * T, 0));
			piscada = [.. EscutaDeMiragens!];
		}
		finally { EscutaDeMiragens = null; }
		AfirmarV30("**A MIRAGEM DO DUPLO CLIQUE CONTINUA A DE SEMPRE** (*\"a miragem padrao ao dar double click se mantem igual a atual\"*): a piscada deixa a miragem COMUM",
				   piscada.Count == 1 && !piscada[0].Longa, EmTexto(piscada));

		// OS DOIS DEFEITOS DO JOGO DE ANTES
		MiragemCurtaDeTeste = true;
		List<(int Quem, Vec2 De, bool Longa)> curtas;
		try
		{
			p.AlvoId = 0;
			Ligar();
			curtas = Passos(1).Miragens;
		}
		finally { MiragemCurtaDeTeste = false; }
		AfirmarV30("(defeito injetado: a miragem curta) a da tecnica volta a ser a comum -- *\"fica sem sentido a miragem durar milisegundos nesse caso\"*",
				   curtas.Count == 1 && !curtas[0].Longa, EmTexto(curtas));

		MiragemSemRondaDeTeste = true;
		Vec2 antesSemRonda;
		List<Vec2> semRonda;
		try
		{
			Ligar();
			Mirar(p, alvo.Id);
			antesSemRonda = p.Pos;
			semRonda = Passos(2).Posicoes;
		}
		finally { MiragemSemRondaDeTeste = false; }
		AfirmarV30("(defeito injetado: sem ronda) com o alvo marcado o corpo fica parado -- o jogo de antes",
				   semRonda.All(x => x.Equals(antesSemRonda)), string.Join(" ", semRonda));
		EsquecerG14(p.Id);
		LimparTudoDaBancada();
	}

	// =====================================================================
	// PV) MAIS GENTE NOS PLANETAS CIVILIZADOS (item 31)
	// =====================================================================
	private void OPovoV30()
	{
		GD.Print("[v030] --- PV) mais gente nos planetas civilizados, e o embaralho pelo tempo vazio (item 31; o congelamento, o embaralho e o fio inteiros sao da `--embaralhoteste`) ---");
		var antes = new Dictionary<string, int> { ["Earth"] = 40, ["Vegeta"] = 40, ["Namek"] = 32, ["Icer"] = 12, ["Arlia"] = 12, ["Makyo_Star"] = 12 };
		LinhaDePovoamento[] plano = _moldes?.Plano ?? [];
		int Hoje(string planeta) => plano.Where(l => l.Planeta == planeta && l.Molde == "cidadao").Sum(l => l.Quantos);
		bool subiu = antes.All(kv => Hoje(kv.Key) >= kv.Value * 2.5);
		List<string> problemas = _moldes == null ? ["sem npcs.json"] : Povoamento.Problemas(plano, _moldes);
		AfirmarV30("**MAIS GENTE** (*\"aumentar a quantidade de npcs consideravelmente\"*): os seis planetas civilizados tem 2,5x os habitantes de antes -- e o plano inteiro cabe nos tetos de zona e de servidor",
				   subiu && problemas.Count == 0,
				   string.Join("; ", antes.Select(kv => $"{kv.Key} {kv.Value} -> {Hoje(kv.Key)}")) + (problemas.Count > 0 ? " | " + string.Join(" / ", problemas) : ""));

		(double Segundos, int Raio)[] pontos = [(1, 0), (60, 0), (90, 3), (5 * 60, 10), (60 * 60, Povoamento.TilesMaxDoEmbaralho)];
		bool reta = pontos.All(x => Povoamento.RaioDoEmbaralho(x.Segundos) == x.Raio);
		AfirmarV30("**O EMBARALHO PELO TEMPO VAZIO** (*\"pro cara n sair e voltar em 1 segundo e o npc parar no outro lado do planeta\"*): 1 s e 1 min nao mexem ninguem, 90 s ja dao o passo minimo (3 tiles), 5 min dao os 10 tiles do embaralho antigo, e uma hora para no teto",
				   reta, string.Join("; ", pontos.Select(x => $"{x.Segundos:0} s -> {Povoamento.RaioDoEmbaralho(x.Segundos)} (esperado {x.Raio})")));

		int raioCurto;
		Povoamento.RaioFixoDeTeste = true;
		try { raioCurto = Povoamento.RaioDoEmbaralho(150); }
		finally { Povoamento.RaioFixoDeTeste = false; }
		AfirmarV30("(defeito injetado: o raio fixo) dois minutos e meio de planeta vazio levariam o teto inteiro -- a afirmacao de cima sabe ficar vermelha",
				   raioCurto == Povoamento.TilesMaxDoEmbaralho && Povoamento.RaioDoEmbaralho(150) == 5, $"com o defeito {raioCurto}, sem ele {Povoamento.RaioDoEmbaralho(150)}");
	}

	// =====================================================================
	// CS) O CHEFE DE SAGA (item 35)
	// =====================================================================
	private void OChefeDeSagaV30()
	{
		GD.Print("[v030] --- CS) o chefe de saga: a cena da chegada pra todos, o ki que nao acaba e a cena de cada degrau (item 35; a cena na tela e da `--diagv030`, CS) ---");
		LimparTudoDaBancada();
		var nascidos = new List<ServerPlayer>();

		// O CHEFE NASCE PELA PORTA DA SAGA (`NascerChefeDaSaga`), com um elo de bancada -- e a chegada e ouvida pela escuta.
		(ServerPlayer? Corpo, List<(string Nome, string Planeta, string Frase, int Para)> Chegadas) Chegar(string molde, ComoChegou como, int degrau = 0)
		{
			List<(string Nome, string Planeta, string Frase, int Para)> ouvidas = [];
			if (_moldes?.Get(molde) is not { } m) return (null, ouvidas);
			var ec = new EstadoDoChefe { Molde = molde, Bps = [.. m.Estagios.Select(s => s.Bp)], Degrau = degrau };
			var est = new EstadoDoElo { Id = "bancada_cs_" + molde, Chefes = [ec] };
			var elo = new EloDaSaga
			{
				Id = est.Id, Nome = "bancada", Planeta = "Namek",
				Chefes = [new ChefeDaSaga { Molde = molde, AnuncioDaChegada = $"[bancada] {m.Nome} chegou." }],
			};
			EscutaDeChegadas = ouvidas;
			try { NascerChefeDaSaga(elo, est, ec, como); }
			finally { EscutaDeChegadas = null; }
			ServerPlayer? c = ec.Corpo != 0 ? _players.GetValueOrDefault(ec.Corpo) : null;
			if (c != null) nascidos.Add(c);
			return (c, ouvidas);
		}

		try
		{
			// ---- A CHEGADA ----
			var agora = Chegar("freeza_namek", ComoChegou.Agora);
			var retomando = Chegar("freeza_namek", ComoChegou.Retomando, degrau: 2);
			var ressurgindo = Chegar("cell", ComoChegou.Ressurgindo, degrau: 3);
			bool chegou = agora.Corpo != null && agora.Chegadas.Count == 1 && agora.Chegadas[0].Nome == agora.Corpo.Name
								  && agora.Chegadas[0].Planeta == "Namek" && agora.Chegadas[0].Frase.StartsWith("[bancada]", StringComparison.Ordinal);
			AfirmarV30("**A CHEGADA PRA TODOS** (*\"uma cutscene q aparece pra todos quando um boss de saga aparece\"*): o chefe que chega pela saga manda a cena -- o nome, o planeta e a frase da chegada -- pra todo jogador do mundo",
				   chegou, string.Join(" | ", agora.Chegadas.Select(x => $"{x.Nome} em {x.Planeta}: {x.Frase} (pra {x.Para})")));
			AfirmarV30("...quem VOLTA DA MORTE tambem chega com cena (com a frase do degrau), e quem so RETOMA (o reinicio com o chefe ja no mundo) nao: ele nao chegou",
				   ressurgindo.Corpo != null && ressurgindo.Chegadas.Count == 1 && retomando.Corpo != null && retomando.Chegadas.Count == 0,
				   $"ressurgindo {ressurgindo.Chegadas.Count} ('{ressurgindo.Chegadas.FirstOrDefault().Frase}'); retomando {retomando.Chegadas.Count}");
			ChegadaSemCenaDeTeste = true;
			(ServerPlayer? Corpo, List<(string Nome, string Planeta, string Frase, int Para)> Chegadas) muda;
			try { muda = Chegar("freeza_namek", ComoChegou.Agora); }
			finally { ChegadaSemCenaDeTeste = false; }
			AfirmarV30("(defeito injetado: a chegada so no chat) o chefe chega e ninguem recebe cena -- a linha de cima sabe ficar vermelha",
				   muda.Corpo != null && muda.Chegadas.Count == 0, $"{muda.Chegadas.Count} cena(s)");

			// ---- O KI SEM FIM ----
			if (agora.Corpo is { } freeza && NascerNpc("cidadao", freeza.Zone, freeza.Pos, 9_351) is { } cidadao)
			{
				nascidos.Add(cidadao);
				(double Chefe, double Cidadao) DepoisDeUmTique()
				{
					freeza.Ficha.Ki = 0;
					cidadao.Ficha.Ki = 0;
					TickDosRelogiosDoCorpo(Protocol.TickSeconds);
					return (freeza.Ficha.Ki / freeza.Ficha.MaxKi, cidadao.Ficha.Ki / cidadao.Ficha.MaxKi);
				}
				(double kiChefe, double kiCidadao) = DepoisDeUmTique();
				AfirmarV30("**O KI DO CHEFE DE SAGA NAO ACABA** (*\"faça boss de saga de ki infinito pra deixar mais desafiador\"*): zerado, ele volta ao cheio no mesmo tique do corpo -- e o cidadao do lado (o controle) continua no zero",
					   freeza.Ficha.KiDeChefe && kiChefe >= 0.999 && kiCidadao < 0.5, $"chefe {kiChefe:P0}, cidadao {kiCidadao:P0}");
				Nutricao.KiDoChefeFinitoDeTeste = true;
				double kiFinito;
				try { kiFinito = DepoisDeUmTique().Chefe; }
				finally { Nutricao.KiDoChefeFinitoDeTeste = false; }
				AfirmarV30("(defeito injetado: o ki do chefe acaba) zerado, ele fica no zero -- a linha de cima sabe ficar vermelha", kiFinito < 0.5, $"{kiFinito:P0}");
			}
			else AfirmarV30("(montagem) o chefe e o cidadao de controle nasceram", false);

			// ---- A CENA DE CADA DEGRAU ----
			List<(int Quem, Jandirus.Core.Forms.Cinematicas.CenaDeChefe Cena)> cenas = [];
			EscutaDeCenasDeChefe = cenas;
			try
			{
				if (agora.Corpo is { } freeza2)
				{
					AvancarEstagio(freeza2);
					bool corpoNovo = cenas.Count == 1 && cenas[0] == (freeza2.Id, Jandirus.Core.Forms.Cinematicas.CenaDeChefe.CorpoNovo)
										   && freeza2.Combate.Intocavel && Math.Abs(freeza2.CenaSegundos - Jandirus.Core.Forms.Cinematicas.ChefeCorpoNovo.SegundosPreso) < 0.01;
					AvancarEstagio(freeza2); AvancarEstagio(freeza2); AvancarEstagio(freeza2);
					bool final = cenas.Count == 4 && cenas[^1].Cena == Jandirus.Core.Forms.Cinematicas.CenaDeChefe.FormaFinal
									  && freeza2.Visual.FormasDeFrost.FirstOrDefault() == "Changeling Frieza 100% 3";
					AfirmarV30("**O CHEFE SE TRANSFORMA COM CENA** (*\"quando boss se transoforma ele tem cinematica igual um player\"*): cada corpo novo do Freeza toca o surto da escada do Frost Demon -- preso e intocavel nele, como o jogador -- e a forma final toca a cena grande",
						   corpoNovo && final, string.Join(" ", cenas.Select(c => c.Cena)) + $" | corpo '{freeza2.Visual.FormasDeFrost.FirstOrDefault()}'");
				}

				var cell = Chegar("cell", ComoChegou.Agora);
				if (cell.Corpo is { } c1)
				{
					int corpoDeNascer = c1.Visual.Corpo;
					cenas.Clear();
					AvancarEstagio(c1);
					bool semi = cenas.Count == 1 && cenas[0].Cena == Jandirus.Core.Forms.Cinematicas.CenaDeChefe.BioSemiPerfeito
								   && c1.Visual.Corpo == Jandirus.Core.Races.BioAndroids.IndiceDoCorpo(Jandirus.Core.Races.BioAndroids.SemiPerfeito);
					AvancarEstagio(c1);
					bool perfeito = cenas.Count == 2 && cenas[1].Cena == Jandirus.Core.Forms.Cinematicas.CenaDeChefe.BioPerfeito
									   && c1.Visual.Corpo == Jandirus.Core.Races.BioAndroids.IndiceDoCorpo(Jandirus.Core.Races.BioAndroids.Perfeito);
					AfirmarV30("**O CELL EVOLUI PELA CENA DO JOGADOR BIO**: nasce no corpo imperfeito, e o semi-perfeito e o perfeito tocam as cenas do bio-androide jogador -- e vestem o corpo do degrau",
						   corpoDeNascer == Jandirus.Core.Races.BioAndroids.IndiceDoCorpo(Jandirus.Core.Races.BioAndroids.Imperfeito) && semi && perfeito,
						   $"corpo ao nascer {corpoDeNascer}; {string.Join(" ", cenas.Select(c => c.Cena))}; corpo {c1.Visual.Corpo}");
				}

				var guardiao = Chegar("guardiao_saiyajin", ComoChegou.Agora);
				if (guardiao.Corpo is { } g)
				{
					cenas.Clear();
					List<(int Quem, string De, string Para, DegrauDeCena Degrau)> formas = [];
					EscutaDeAnuncios = formas;
					try { AvancarEstagio(g); }
					finally { EscutaDeAnuncios = null; }
					AfirmarV30("...e o degrau de FORMA passa pelo funil do jogador como ESTREIA: o guardiao (60% de maestria no molde) vira SSJ com a cena cheia da forma -- antes a maestria a dispensava e ele virava calado --, e sem cena de chefe por cima",
						   cenas.Count == 0 && formas.Any(f => f.Quem == g.Id && f.Para == "ssj1" && f.Degrau == DegrauDeCena.Estreia),
						   $"{cenas.Count} cena(s) de chefe; formas: {string.Join(" ", formas.Select(f => $"{f.Para}/{f.Degrau}"))}");
				}

				List<(int Quem, string De, string Para, DegrauDeCena Degrau)> formasSemEstreia = [];
				if (Chegar("guardiao_saiyajin", ComoChegou.Agora).Corpo is { } g2)
				{
					EscutaDeAnuncios = formasSemEstreia;
					FormaDeChefeSemEstreiaDeTeste = true;
					try { AvancarEstagio(g2); }
					finally { FormaDeChefeSemEstreiaDeTeste = false; EscutaDeAnuncios = null; }
				}
				AfirmarV30("(defeito injetado: a forma de chefe pela maestria) o guardiao vira SSJ sem cena nenhuma -- a linha de cima sabe ficar vermelha",
					   formasSemEstreia.Count > 0 && formasSemEstreia.All(f => f.Degrau == DegrauDeCena.Nenhuma),
					   string.Join(" ", formasSemEstreia.Select(f => $"{f.Para}/{f.Degrau}")));

				if (Chegar("freeza_namek", ComoChegou.Agora).Corpo is { } freeza3)
				{
					cenas.Clear();
					ChefeSemCenaDeTeste = true;
					try { AvancarEstagio(freeza3); }
					finally { ChefeSemCenaDeTeste = false; }
					AfirmarV30("(defeito injetado: a troca sem cena) o Freeza muda de corpo e cena nenhuma sai -- a linha de cima sabe ficar vermelha",
						   cenas.Count == 0 && freeza3.Visual.FormasDeFrost.FirstOrDefault() == "Changling - Form 2", $"{cenas.Count} cena(s)");
				}
			}
			finally { EscutaDeCenasDeChefe = null; }

			// ---- O DADO ----
			var estagiosDoFreeza = _moldes?.Get("freeza_namek")?.Estagios ?? [];
			bool dado = estagiosDoFreeza.Length == 5 && estagiosDoFreeza[0].Cena == Jandirus.Core.Forms.Cinematicas.CenaDeChefe.Nenhuma
							  && estagiosDoFreeza.Skip(1).Take(3).All(e => e.Cena == Jandirus.Core.Forms.Cinematicas.CenaDeChefe.CorpoNovo)
							  && estagiosDoFreeza[4].Cena == Jandirus.Core.Forms.Cinematicas.CenaDeChefe.FormaFinal
							  && (_moldes?.Todos.SelectMany(m => m.Problemas()).All(p => !p.Contains("cena", StringComparison.Ordinal)) ?? false)
							  && Jandirus.Core.Forms.Cinematicas.CenaDeChefePorNome("cena_que_nao_existe") == null;
			AfirmarV30("o `npcs.json` nomeia a cena de cada degrau (o Freeza: nada ao nascer, o surto nos tres corpos do meio, a cena grande na forma final), nenhum molde pede cena que nao existe -- e um nome errado e recusado",
				   dado, string.Join(" ", estagiosDoFreeza.Select(e => e.Cena)));
		}
		finally
		{
			foreach (ServerPlayer c in nascidos) if (_players.ContainsKey(c.Id)) RemoverNpc(c);
			LimparTudoDaBancada();
		}
	}

	// =====================================================================
	// RA) O MODO DAS RACAS (item 34)
	// =====================================================================
	private void OModoDasRacasV30()
	{
		GD.Print("[v030] --- RA) o modo das racas: a tabela do dono, o sorteio, a vaga que guarda o sorteio, a criacao que o obedece e o admin que muda o modo (item 34; a aba e a tela de criacao sao da `--diagv030`, RA) ---");
		var aleatorio = Jandirus.Core.Races.ModoDeRacas.Aleatorio;
		var padrao = Jandirus.Core.Races.ModoDeRacas.Padrao;

		// ---- A TABELA ----
		var criacao = new HashSet<string>(Jandirus.Core.Races.CharacterDraft.Planetas.SelectMany(Jandirus.Core.Races.CharacterDraft.RacasDoPlaneta));
		List<string> tabela = [.. Jandirus.Core.Races.SorteioDeRaca.Pesos.Select(p => p.Raca)];
		int Peso(string r) => Jandirus.Core.Races.SorteioDeRaca.Pesos.First(p => p.Raca == r).Peso;
		bool cobre = tabela.Distinct().Count() == tabela.Count && criacao.SetEquals(tabela);
		bool doDono = Peso("Alien") == 22 && Peso("Human") == 20 && Peso("Namekian") == 18 && Peso("Saiyan") == 15 && Peso("Frost Demon") == 8 && Peso("Majin") == 4;
		var tresMaiores = Jandirus.Core.Races.SorteioDeRaca.Pesos.OrderByDescending(p => p.Peso).Take(3).Select(p => p.Raca).ToHashSet();
		AfirmarV30("**A TABELA DO DONO** (*\"alien 22%, terraqueo 20%, namek 18%, saiyajin 15%, frost demon 8%, majin 4% ... alien, terraqueo e namek sempre terao a maior chance\"*): os seis numeros literais, as outras treze racas da criacao com 1% cada, a soma em 100 -- e a tabela tem TODA raca que a criacao oferece, e nenhuma outra",
				   cobre && doDono && Jandirus.Core.Races.SorteioDeRaca.SomaDosPesos == 100 && tresMaiores.SetEquals(["Alien", "Human", "Namekian"]),
				   $"{tabela.Count} na tabela, {criacao.Count} na criacao, soma {Jandirus.Core.Races.SorteioDeRaca.SomaDosPesos}; fora da tabela: {string.Join(",", criacao.Except(tabela))}");

		// ---- O SORTEIO, MEDIDO ----
		const int N = 40_000;
		(Dictionary<string, int> Contagem, int Primal, int Saiyan, Dictionary<string, int> Clas) Medir()
		{
			var c = new Dictionary<string, int>();
			var clas = new Dictionary<string, int>();
			int primal = 0, saiyan = 0;
			for (int i = 0; i < N; i++)
			{
				(string raca, string linhagem) = Jandirus.Core.Races.SorteioDeRaca.Sortear(
					Jandirus.Core.Races.SorteioDeRaca.SementeDe(0x5EED_0034UL, "bancada_racas", i % 3, i / 3));
				c[raca] = c.GetValueOrDefault(raca) + 1;
				if (raca == "Saiyan") { saiyan++; if (linhagem == "Primal Saiyan") primal++; }
				if (raca == "Namekian") clas[linhagem] = clas.GetValueOrDefault(linhagem) + 1;
			}
			return (c, primal, saiyan, clas);
		}
		var m = Medir();
		double Pct(string r) => 100.0 * m.Contagem.GetValueOrDefault(r) / N;
		bool perto = Jandirus.Core.Races.SorteioDeRaca.Pesos.All(p => Math.Abs(Pct(p.Raca) - p.Peso) <= Math.Max(0.6, p.Peso * 0.08));
		AfirmarV30($"**O SORTEIO SEGUE A TABELA**: em {N} sorteios cada raca sai na porcentagem dela (Alien {Pct("Alien"):0.0}%, Humano {Pct("Human"):0.0}%, Namek {Pct("Namekian"):0.0}%, Saiyajin {Pct("Saiyan"):0.0}%, Frost {Pct("Frost Demon"):0.0}%, Majin {Pct("Majin"):0.0}%) -- e toda raca da criacao sai",
				   perto && Jandirus.Core.Races.SorteioDeRaca.Pesos.All(p => m.Contagem.GetValueOrDefault(p.Raca) > 0),
				   string.Join(" ", Jandirus.Core.Races.SorteioDeRaca.Pesos.Select(p => $"{p.Raca} {Pct(p.Raca):0.0}")));
		double pctPrimal = 100.0 * m.Primal / Math.Max(1, m.Saiyan);
		int nameks = m.Clas.Values.Sum();
		double Cla(string c) => 100.0 * m.Clas.GetValueOrDefault(c) / Math.Max(1, nameks);
		AfirmarV30("**A SUB-RACA** (*\"primal saiyan e sub raças sao 50%\"*): metade dos Saiyajins sai Primal, e o Namekuseijin sai Guerreiro na metade e Dragao/Demonio num quarto cada (a divisao do DM, `PlanetPopulation.dm:405`)",
				   Math.Abs(pctPrimal - 50) <= 3 && Math.Abs(Cla("Warrior clan") - 50) <= 3 && Math.Abs(Cla("Dragon clan") - 25) <= 3 && Math.Abs(Cla("Demon clan") - 25) <= 3,
				   $"Primal {pctPrimal:0.0}% de {m.Saiyan}; Guerreiro {Cla("Warrior clan"):0.0}%, Dragao {Cla("Dragon clan"):0.0}%, Demonio {Cla("Demon clan"):0.0}% de {nameks}");

		Jandirus.Core.Races.SorteioDeRaca.PesosDeTeste = [.. Jandirus.Core.Races.SorteioDeRaca.Pesos.Select(p => (p.Raca, 1))];
		double alienUniforme;
		try { alienUniforme = 100.0 * Medir().Contagem.GetValueOrDefault("Alien") / N; }
		finally { Jandirus.Core.Races.SorteioDeRaca.PesosDeTeste = null; }
		AfirmarV30("(defeito injetado: o sorteio uniforme, que ignora a tabela) o Alien cai pra perto de 1 em 19 -- a medida de cima sabe ficar vermelha",
				   Math.Abs(alienUniforme - 22) > 5, $"Alien {alienUniforme:0.0}%");

		// ---- A VAGA, A CRIACAO E O ADMIN, PELAS PORTAS DE PRODUCAO ----
		Jandirus.Core.Races.ModoDeRacas antes = ModoDeRacas;
		try
		{
			EscolherModoDeRacas(aleatorio, "bancada");
			var acc = new AccountSave { Conta = "bancada_racas_v30" };
			acc.Slots[1] = new CharacterSave { Nome = "Ocupante", Raca = "Human" };   // uma vaga ocupada
			PrepararVagas(acc);
			string?[] primeiro = [.. acc.Sorteios.Select(s => s?.Raca)];
			PrepararVagas(acc);
			bool mesmo = acc.Sorteios.Select(s => s?.Raca).SequenceEqual(primeiro);
			SlotInfo[] vistas = SlotsVisiveisDe(acc);
			bool naLista = !string.IsNullOrEmpty(vistas[0].RacaSorteada) && vistas[0].RacaSorteada == acc.Sorteios[0]?.Raca
								   && vistas[2].RacaSorteada == acc.Sorteios[2]?.Raca && string.IsNullOrEmpty(vistas[1].RacaSorteada);
			AfirmarV30("**CADA VAGA VAZIA GANHA O SEU SORTEIO** e ele vai na lista de vagas -- a ocupada nao ganha nenhum, e sair e entrar de novo NAO sorteia outra vez",
				   acc.Sorteios[0] != null && acc.Sorteios[1] == null && acc.Sorteios[2] != null && mesmo && naLista && acc.SorteiosFeitos == 2,
				   $"vagas: {string.Join(" | ", acc.Sorteios.Select(s => s == null ? "-" : $"{s.Raca}/{s.Linhagem}"))}; feitos {acc.SorteiosFeitos}");

			RacaSorteada s0 = acc.Sorteios[0]!;
			string outra = s0.Raca == "Human" ? "Alien" : "Human";
			Jandirus.Core.Races.CharacterDraft Ficha() =>
				new() { Name = "Sorteado", Race = outra, Planet = "Earth", Age = 25, Backstory = "um personagem da bancada" };
			Jandirus.Core.Races.CharacterDraft ficha = Ficha();
			AplicarOSorteio(acc, 0, ficha);
			bool obedece = ficha.Race == s0.Raca && ficha.ChosenClass == s0.Linhagem
								  && ficha.Planet == Jandirus.Core.Races.SorteioDeRaca.PlanetaDaRaca(s0.Raca) && ficha.Validar().Length == 0;
			AfirmarV30("**A CRIACAO OBEDECE O SORTEIO** (*\"o jogador n pode escolher sua raça inicial\"*): o cliente pediu outra raca e a ficha nasce com a da vaga, a sub-raca e o mundo dela -- esconder as etapas na tela e conveniencia, e nao trava",
				   obedece, $"pedia {outra}, nasce {ficha.Race}/{ficha.ChosenClass} de {ficha.Planet}; validar: '{ficha.Validar()}'");

			Jandirus.Core.Races.CharacterDraft fichaDoDefeito = Ficha();
			SorteioIgnoradoDeTeste = true;
			try { AplicarOSorteio(acc, 0, fichaDoDefeito); }
			finally { SorteioIgnoradoDeTeste = false; }
			AfirmarV30("(defeito injetado: a criacao ignora o sorteio) a ficha nasce com a raca que o cliente quis -- a linha de cima sabe ficar vermelha",
				   fichaDoDefeito.Race == outra, fichaDoDefeito.Race);

			EscolherModoDeRacas(padrao, "bancada");
			PrepararVagas(acc);
			Jandirus.Core.Races.CharacterDraft livre = Ficha();
			AplicarOSorteio(acc, 0, livre);
			AfirmarV30("**NO MODO PADRAO A ESCOLHA E DO JOGADOR**: os sorteios da conta somem, a lista de vagas nao mostra raca nenhuma, e a criacao fica com a raca que ele escolheu",
				   acc.Sorteios.All(s => s == null) && SlotsVisiveisDe(acc).All(v => string.IsNullOrEmpty(v.RacaSorteada)) && livre.Race == outra,
				   $"sorteios {string.Join(",", acc.Sorteios.Select(s => s?.Raca ?? "-"))}; ficha {livre.Race}");

			// O ADMIN MUDA E GRAVA; QUEM NAO E ADMIN E BARRADO.
			ServerPlayer adm = ForjarV2("AdminDasRacas", TerraFirmeG12(2), 1000);
			adm.Poderes |= Protocol.Poder.Admin;
			ServerPlayer comum = ForjarV2("JogadorComum", TerraFirmeG12(2), 1000);
			AdminModoDeRacas(adm, "aleatorio");
			bool mudou = ModoDeRacas == aleatorio && ModoDeRacasGravado() == aleatorio;
			ModoDeRacas = padrao;               // a memoria esquece...
			CarregarConfiguracaoDoServidor();   // ...e o boot le o servidor.json de volta
			bool voltou = ModoDeRacas == aleatorio;
			List<string> falas = Ouvir(() => Verbo(comum, "admin_modo_racas", "padrao"));
			bool barrado = ModoDeRacas == aleatorio && Disse(falas, "administrador");
			bool preservado = _sistemasDoMundo.Any(s => s.Preservar && s.Meu(NomeDoArquivoDoServidor));
			AfirmarV30("**O ADMIN MUDA O MODO DEPOIS DE O SERVIDOR EXISTIR** (*\"na aba de adm no meu P vai ter a opçao de mudar essa config mesmo dps do server ter sido criado\"*): o verb muda e grava no `servidor.json` (o boot seguinte le de volta), quem nao e admin e barrado, e o arquivo SOBREVIVE a limpeza total",
				   mudou && voltou && barrado && preservado,
				   $"mudou {mudou}, o boot leu de volta {voltou}, barrado {barrado} ({Ultimos(falas)}), preservado {preservado}");
		}
		finally
		{
			EscolherModoDeRacas(antes, "bancada (volta)");
			LimparTudoDaBancada();
		}
	}
}
