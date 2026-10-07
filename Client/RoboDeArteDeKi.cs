using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ BANCADA DA ARTE DOS ATAQUES DE KI ============================
/// Ela nasceu de um pedido do dono -- *"vc n ta utilizando os ICONES DE BEAM q era usado no byond pra
/// dar mais VARIEDADE nos ataques de ki"* -- e media tres coisas: a tabela escolhe folhas diferentes,
/// as folhas EXISTEM no disco, e o pixel desenhado muda.
///
/// ============================ AS FOLHAS SAIRAM (2026-10-07), E METADE DELA SAIU JUNTO ============================
/// O dono viu o trailer e pediu outro desenho: *"ao inves de usar sprite pra cada beam como era no
/// byond, fazer eles por efeitos, noise e shaders do proprio godot ... alem de beam na diagonal tb n
/// ficar aquela coisa picotada e poder ser continuo em todas as direcoes e poder ate dar curva. Pra
/// outros ataques de ki isso tb seria interessante trocar sprite por puro efeito, noise e shaders em
/// esferas como genkidama, super nova, blast etc"*.
///
/// Hoje um raio e UMA FITA da mao ate a ponta (`FeixeDeKi.gdshader`) e uma bola e UM QUADRO
/// (`EsferaDeKi.gdshader`) -- ver <see cref="ProjetilDesenhado"/> e <see cref="PintorDeKi"/>. Tudo o
/// que esta bancada media sobre FOLHA ficou sem objeto, e foi APAGADO em vez de adaptado:
///
///   * "as 41 folhas carregam do disco e tem `head`/`tail`/`origin`" -- nao ha folha pra carregar;
///   * o passo do TREM, a sombra de alpha de cada arte, a varredura de 2 a 1600 px do `MontarTrem`,
///     o custo de montar o trem e o teto de 60 estampas -- nao ha trem: o raio sao quatro vertices;
///   * "o passo nao depende da escala" e a fase das juntas da diagonal longa -- nao ha junta.
///
/// O QUE FICOU e o que continua sendo regra:
///
///   1. A TABELA (`ArteDeProjetil`): todo verb que atira tem arte, a bola muda com a raca e o
///      Kamehameha e sorteado por personagem. Sem pixel -- roda no headless.
///   2. TODO ESTILO EXISTE E SE VESTE: cada arte do enum tem estilo proprio, os tres shaders carregam
///      e um tiro de cada tipo se veste com cada arte. E a 2b fotografa as 44 como bola: vestir e um
///      campo, DESENHAR e um pixel.
///   3. O PIXEL: duas artes fotografam diferente, a tinta muda o pixel sem achatar o desenho, a
///      escala engorda, a cor de um personagem nao estoura em branco.
///   4 e 5. A CONTINUIDADE -- a queixa do dono. O raio nao tem um pixel de fundo no eixo, da mao a
///      ponta, e a tinta dele e UMA peca so: em doze comprimentos, nas 24 artes de raio, em 19 rumos
///      (os 16 da rosa mais 17, 33 e 61 graus) e atravessando a tela. Com os dois defeitos de
///      <see cref="ProjetilDesenhado.DefeitoDeTeste"/> religados pra provar que as duas reguas sabem
///      ficar vermelhas -- e voltar ao verde.
///   6. O DESENHO OBEDECE AS TABELAS DO CORE: a ponta SOLIDA cai em `FrenteDaCabeca x escala` e o
///      tronco tem `MeiaEspessuraDoTronco x Macico x escala`, medidos na FOTO. O servidor encosta e
///      corta por esses dois numeros; se o desenho mentir, o jogador ve o tiro acertar o vazio.
///
/// ============================ ELA MEDE PIXEL, E ISSO E O PONTO ============================
/// Este projeto tem registro de quatro defeitos visuais que passaram por quatro mil checagens verdes
/// porque a bancada media INTENCAO: uniform escrito nao e pixel desenhado, `Modulate` nao e tela, e
/// "as duas telas concordam" fica verde com as duas erradas igual. Aqui nenhuma familia de 2b em
/// diante pergunta ao node o que ele acha que desenhou: ela fotografa e le a foto.
///
/// ============================ E O DESENHO AGORA SE MEXE SOZINHO ============================
/// O shader anima com a idade do tiro (`tempo`) e cada material sorteia a propria `semente`: duas
/// fotos do MESMO tiro nao tem os mesmos pixels. A regua antiga -- um hash com a posicao de cada
/// pixel -- diria "diferente" pra qualquer par, inclusive pra a mesma arte duas vezes: verde por
/// construcao. Entao as comparacoes daqui sao DISTANCIAS (de silhueta e de paleta) contra um CONTROLE
/// medido na mesma foto (a mesma arte, duas vezes), e as medidas sao contagens e extensoes com folga,
/// nunca igualdade.
///
///     &lt;godot&gt; --path . --headless --diagartedeki                    (familias 1 e 2, sem janela)
///     &lt;godot&gt; --path . --diagartedeki --position 1920,0 --resolution 1280x720
///                                             (COM janela, no segundo monitor: as outras medem pixel)
/// ======================================================================================
///
/// SEM REDE E SEM MUNDO, como a `--diagtinta`: arte de tiro nao depende de zona, de servidor nem de
/// login. O que ela toca e estilo, shader e um quadro desenhado. As fotos saem em
/// `user://artedeki-*.png`.
/// </summary>
public partial class RoboDeArteDeKi : Node2D
{
	private readonly List<string> _linhas = [];

	/// <summary>Quantas conferencias cada familia fez. Sai no fim, logo antes do veredito.</summary>
	private readonly List<(string Nome, int Feitas, int Reprovadas)> _placar = [];

	private int _falhas, _t, _nascidos;
	private bool _acabou, _semFoto;

	/// <summary>O que ainda falta esperar antes do proximo passo. Ver <see cref="SegundosDeAssento"/>.</summary>
	private double _segundosDeFolga;
	private int _quadrosDeFolga;
	private ColorRect _fundo = null!;

	/// <summary>
	/// FUNDO VERDE PURO: nenhum estilo de ki tem verde cheio com os outros dois canais zerados (os
	/// tons sao cinzas, azuis, roxos e dourados, e a tinta de teste SOMA vermelho ou azul em cima),
	/// entao "nao e o fundo" separa o desenho do resto sem saber onde ele caiu. E o mesmo truque do
	/// magenta da `--diagtinta`, com outra cor porque aqui a tinta da regua E magenta.
	/// </summary>
	private static readonly Color Fundo = new(0f, 1f, 0f);

	/// <summary>A tinta de teste de sempre: um ki vermelho. Com ela todo tom solido tem o vermelho no teto.</summary>
	private static readonly Color Vermelho = new(1f, 0.2f, 0.2f);

	private static readonly Color Azul = new(0.2f, 0.4f, 1f);

	/// <summary>A tinta da REGUA (familia 6). Ver <see cref="Chapa.Solido"/> sobre por que magenta.</summary>
	private static readonly Color Magenta = new(1f, 0f, 1f);

	/// <summary>
	/// QUANTO UM PALCO ESPERA ANTES DA FOTO.
	///
	/// Dois motivos, e nenhum e interpolacao (o primeiro `Mirar` CRAVA a posicao): a foto e do quadro
	/// ANTERIOR ao passo que a tira, e o raio nasce com a boca 40% inchada (o clarao do disparo, o
	/// `nasce = exp(-tempo * 9)` do shader), que assenta em um terco de segundo. Fotografar no quadro
	/// seguinte mostraria o instante do tiro em todas as fotos -- e a maior parte da vida de um raio
	/// nao e esse instante.
	///
	/// EM SEGUNDOS, e nao em quadros: a idade do tiro e contada em tempo, e uma janela sem sincronia
	/// vertical atravessa dezoito quadros em um centesimo de segundo. Os quadros entram so como piso.
	/// </summary>
	private const double SegundosDeAssento = 0.3;

	/// <summary>O piso em quadros: o palco tem que ter sido DESENHADO antes de ser fotografado.</summary>
	private const int QuadrosDeAssento = 3;

	/// <summary>
	/// Meio tile: de quanto a MAO fica atras da boca que o servidor manda. A regra esta escrita em
	/// `ProjetilDesenhado.AMao`; aqui ela e repetida A MAO de proposito -- a sonda anda da mao ate a
	/// ponta, e se o desenho mudar de lugar sem a regra mudar junto, e pra esta bancada reprovar.
	/// </summary>
	private const float MeiaCelula = ZoneCollision.TileSize / 2f;

	private void Familia(string nome, string titulo)
	{
		_linhas.Add($"=== FAMILIA {nome}: {titulo} ===");
		_placar.Add((nome, 0, 0));
	}

	private void Ok(string oque, bool passou)
	{
		_linhas.Add((passou ? "  OK   " : "  FALHA") + "  " + oque);
		if (!passou) _falhas++;
		if (_placar.Count == 0) return;
		(string nome, int feitas, int reprovadas) = _placar[^1];
		_placar[^1] = (nome, feitas + 1, reprovadas + (passou ? 0 : 1));
	}

	private void Nota(string t) => _linhas.Add("   --    " + t);

	public override void _Ready()
	{
		_fundo = new ColorRect
		{
			Color = Fundo, Size = new Vector2(4000, 4000),
			Position = new Vector2(-500, -500), ZIndex = -100,
		};
		AddChild(_fundo);

		Familia1();
		Familia2();

		Assentar();   // a janela acaba de abrir: um instante pra ela ter o que fotografar
	}

	/// <summary>
	/// OS DEFEITOS INJETADOS NAO SOBREVIVEM A BANCADA. Os dois sao campos ESTATICOS de producao; se
	/// este node sair da arvore no meio de uma injecao (janela fechada na mao, excecao num passo), o
	/// proximo que desenhar um raio neste processo o desenharia picotado.
	/// </summary>
	public override void _ExitTree() => DesligarOsDefeitos();

	private static void DesligarOsDefeitos()
	{
		ProjetilDesenhado.DefeitoDeTeste = ProjetilDesenhado.DefeitoDoFeixe.Nenhum;
		Feixe.AlcanceFixoDeTeste = false;
	}

	// =====================================================================
	// FAMILIA 1 -- A TABELA
	// =====================================================================
	/// <summary>
	/// OS VERBOS DE PRODUCAO, um por um, com a arte que um HUMANO tira de cada e a linha do DM que
	/// manda. A lista e escrita A MAO e nao lida da propria tabela, e isso e deliberado: uma bancada
	/// que varre a tabela pra conferir a tabela sempre passa. O que esta escrito aqui e a lista dos
	/// verbos que o JOGO despacha (o registro de tecnicas vivas: o de projetil, os lotes G5, G6 e G7,
	/// o sopro, e os sete dos lotes G11 e G12 que voaram sem arte ate 2026-10-07), e ela reprova no dia
	/// em que alguem portar uma tecnica de tiro e esquecer a arte -- ou trocar a linha de uma que ja
	/// estava certa.
	///
	/// `Nenhuma` na coluna do meio quer dizer SORTEADA (so o Kamehameha): nao ha uma arte pra escrever,
	/// ha seis, e a familia 1c confere o sorteio.
	/// </summary>
	private static readonly (string Verbo, ArteDeKi NoHumano, string Dm)[] VerbosDeProducao =
	[
		// ---- os tres de producao (`GameServer.Projeteis.cs`)
		("Ki_Wave", ArteDeKi.Beam3, "beams.dm:289 + beams.dm:4"),
		("Basic_Blast", ArteDeKi.Blast1, "blasts.dm:55 + race.dm:62"),
		("Guided_Ball", ArteDeKi.Blast30, "blasts/GuidedBall.dm:35 + :23"),

		// ---- lote G5
		("Masenko", ArteDeKi.BeamMasenko, "beams.dm:322"),
		("Makkankosappo", ArteDeKi.BeamStaticBeam, "beams.dm:357"),
		("Massive_Beam", ArteDeKi.Beam11, "beams.dm:426"),
		("Final_Flash", ArteDeKi.BeamBigFire, "beams/FinalFlash.dm:23,37"),
		("Charged_Shot", ArteDeKi.Blast20, "blasts.dm:103"),
		("KillDriver", ArteDeKi.BasenioBlast, "blasts/KillDriver.dm:23,49"),
		("BusterShell", ArteDeKi.DeathBall2017Purple2, "blasts/BusterShell.dm:46 + blasts/GuidedBall.dm:94"),
		("Scattershot", ArteDeKi.Blast1, "blasts.dm:159"),
		("Energy_Barrage", ArteDeKi.Blast1, "blasts.dm:222"),
		("Ki_Bomb", ArteDeKi.Blast1, "blasts.dm:428"),
		("Hellzone_Grenade", ArteDeKi.Blast1, "blasts.dm:491"),
		("Kienzan", ArteDeKi.Kienzan, "discs.dm:49"),
		("Paralysis", ArteDeKi.KiHead, "Ki2.0/Debuffs.dm:18 + blasts/Paralysis.dm:23"),
		("Stunlock", ArteDeKi.KiHead, "Skill Trees/Race Trees/meta.dm:74 + blasts/Paralysis.dm:23"),

		// ---- lote G6
		("Kamehameha", ArteDeKi.Nenhuma, "beams/Kamehameha.dm:12-21"),
		("GalicGun", ArteDeKi.GalacticGun, "beams/GalicGun.dm:22,34"),
		("Death_Beam", ArteDeKi.Makkankosappo3, "beams/DeathBeam.dm:22,34"),
		("Dodompa", ArteDeKi.Dodompa, "beams.dm:442"),
		("Enkumei", ArteDeKi.Enkumei, "beams/Enkumei.dm:35"),
		("Boom_Wave", ArteDeKi.Beam4, "beams.dm:486"),
		("Kikoho", ArteDeKi.Kikoho, "blasts/Kikoho.dm:56"),

		// ---- lote G7
		("Scattering_Bullet", ArteDeKi.Blast28, "blasts.dm:574"),
		("Spirit_Gun", ArteDeKi.BlastSpiralingKi, "Core Trees/Spirit.dm:355"),

		// ---- o sopro
		("Kiai", ArteDeKi.Daitoppa, "Ki2.0/Kiai.dm:34-40"),

		// ---- os sete que voavam sem arte (lotes G11 e G12), conferidos no DM em 2026-10-07
		("Death_Ball", ArteDeKi.DeathBall2017Purple2, "blasts/DeathBall.dm:53"),
		("SpiritBomb", ArteDeKi.SpiritBomb, "blasts/SpiritBomb.dm:25,45"),
		("BusterBarrage", ArteDeKi.Blast1, "blasts/BusterBarrage.dm:43,60 + tools/copypaste.dm:12-13"),
		("Continuous_Energy_Bullets", ArteDeKi.Blast1, "blasts.dm:278 + tools/copypaste.dm:12-13"),
		("Spin_Blast", ArteDeKi.Blast1, "blasts.dm:348 + tools/copypaste.dm:12-13"),
		("Psycho_Thread", ArteDeKi.KiHead, "click.dm:18,26"),
		("Ki_Targets", ArteDeKi.Blast14, "Stats/Training/Meditate.dm:26"),
	];

	private void Familia1()
	{
		Familia("1", "a tabela responde por todo verbo que atira");

		var vistas = new Dictionary<ArteDeKi, List<string>>();
		int semArte = 0, trocadas = 0;
		foreach ((string v, ArteDeKi noHumano, string dm) in VerbosDeProducao)
		{
			ArteDeKi a = ArteDeProjetil.De(v, "Human", "", 12345);
			if (a == ArteDeKi.Nenhuma) { Nota($"SEM ARTE: {v} (`{dm}`)"); semArte++; continue; }
			(vistas.TryGetValue(a, out List<string>? l) ? l : vistas[a] = []).Add(v);

			bool certa = noHumano == ArteDeKi.Nenhuma
				? a is >= ArteDeKi.Kamehameha1 and <= ArteDeKi.Kamehameha6
				: a == noHumano;
			if (!certa) { Nota($"ARTE TROCADA: {v} veste {a}, e o DM (`{dm}`) manda {noHumano}"); trocadas++; }
		}
		Ok($"os {VerbosDeProducao.Length} verbos de producao tem arte declarada", semArte == 0);
		Ok("...e cada um veste a arte que a linha citada do DM manda", semArte == 0 && trocadas == 0);

		// RECUSAR CALADA E A RESPOSTA CERTA pra verb desconhecido (ver `ArteDeProjetil.De`), e e ela que
		// prova que as duas linhas de cima PODEM reprovar: a tabela nao devolve arte pra qualquer coisa.
		Ok("CONTRA-EXEMPLO: um verb fora da tabela (e o verb vazio) sai `Nenhuma`, sem chute",
		   ArteDeProjetil.De("Verbo_Que_Ninguem_Portou", "Human", "", 12345) == ArteDeKi.Nenhuma
		   && ArteDeProjetil.De("", "Human", "", 12345) == ArteDeKi.Nenhuma);

		// ============================ A LAMINA DE AR DO KIAI (dono, 2026-09-07) ============================
		// `Kiai.dm:34-40`: `Daitoppa.dmi` tingido de ki, igual pra toda raca, e INVISIVEL de nascenca.
		// Quem a ve e quem tinha `see_invisible = 1` no original -- e a pergunta e pura, entao ela e
		// medida aqui, raca por raca, com contra-exemplos.
		Ok("o Kiai veste o `Daitoppa.dmi` (`Kiai.dm:34`), literal e igual pra toda raca",
		   ArteDeProjetil.De("Kiai", "Human", "", 1) == ArteDeKi.Daitoppa
		   && ArteDeProjetil.De("Kiai", "Android", "", 1) == ArteDeKi.Daitoppa
		   && ArteDeProjetil.De("Kiai", "Namekian", "", 1) == ArteDeKi.Daitoppa);
		Ok("...e ele tem ESTILO PROPRIO: a meia-lua (`Forma.Lamina`), e nao a bola neutra",
		   ArteDeKiNoCliente.TemEstilo(ArteDeKi.Daitoppa)
		   && ArteDeKiNoCliente.EstiloDaBola(ArteDeKi.Daitoppa).Forma == ArteDeKiNoCliente.Forma.Lamina);
		Ok("Namekian, Kanassa, Spirit, Shapeshifter, Yardrat e o Demigod Genie enxergam a lamina (`see_invisible = 1`)",
		   VisaoDoInvisivel.Enxerga("Namekian", "") && VisaoDoInvisivel.Enxerga("Kanassa", "")
		   && VisaoDoInvisivel.Enxerga("Spirit", "") && VisaoDoInvisivel.Enxerga("Shapeshifter", "")
		   && VisaoDoInvisivel.Enxerga("Yardrat", "") && VisaoDoInvisivel.Enxerga("Demigod", "Genie"));
		Ok("CONTRA-EXEMPLO: Human, Saiyan, Android e o Demigod Ogre NAO enxergam",
		   !VisaoDoInvisivel.Enxerga("Human", "") && !VisaoDoInvisivel.Enxerga("Saiyan", "")
		   && !VisaoDoInvisivel.Enxerga("Android", "") && !VisaoDoInvisivel.Enxerga("Demigod", "Ogre")
		   && !VisaoDoInvisivel.Enxerga(null, null));

		var lamina = new ProjetilDesenhado { Name = "LaminaDeTeste", Tipo = TipoDeProjetil.Blast, Cor = Vermelho, Invisivel = true };
		lamina.Vestir(ArteDeKi.Daitoppa, 1f);
		AddChild(lamina);
		lamina.MostrarPara(veInvisivel: false);
		Ok("um tiro que nasceu invisivel SOME pra quem nao enxerga (o no fica, o desenho nao)", !lamina.Visible);
		lamina.MostrarPara(veInvisivel: true);
		Ok("...e APARECE pra quem enxerga", lamina.Visible);
		var comum = new ProjetilDesenhado { Name = "BolaDeTeste", Tipo = TipoDeProjetil.Blast, Cor = Vermelho };
		comum.Vestir(ArteDeKi.Blast12, 1f);
		AddChild(comum);
		comum.MostrarPara(veInvisivel: false);
		Ok("CONTRA-EXEMPLO: um tiro comum continua visivel pra quem NAO enxerga o invisivel", comum.Visible);
		lamina.QueueFree();
		comum.QueueFree();

		// ============================ A MEDIDA QUE RESPONDE O PEDIDO DE VARIEDADE ============================
		// "MAIS VARIEDADE" e um numero: quantas artes DISTINTAS os verbos usam. Antes da tabela era
		// UMA (nenhuma: o desenho era por primitiva, e eram duas formas pra tudo).
		//
		// O piso de 15 nao e chute -- e o que a leitura do DM produziu, com margem pra ninguem ter
		// que mexer aqui ao portar uma tecnica que compartilha arte (Paralysis e Stunlock dividem
		// a `KiHead`, e no DM elas dividem mesmo).
		// ==================================================================================================
		Ok($"os verbos usam {vistas.Count} artes DISTINTAS (o pedido era variedade)", vistas.Count >= 15);
		foreach ((ArteDeKi a, List<string> quem) in vistas.OrderBy(p => (int)p.Key))
			Nota($"{ArteDeKiNoCliente.Rotulo(a),-34} <- {string.Join(", ", quem)}");

		// ---- as que o DM manda dividir
		Ok("Paralysis e Stunlock dividem a MESMA arte (as duas leem `ParalysisIcon`)",
		   ArteDeProjetil.De("Paralysis", "Human", "", 1) == ArteDeProjetil.De("Stunlock", "Human", "", 1));
		Ok("...e o Psycho Thread tambem: o fio le a mesma `ParalysisIcon` (`click.dm:18`)",
		   ArteDeProjetil.De("Psycho_Thread", "Human", "", 1) == ArteDeProjetil.De("Paralysis", "Human", "", 1));

		// ---- a armadilha do nome: o verb `Makkankosappo` NAO usa a folha `Makkankosappo.dmi`
		Ok("o verb Makkankosappo usa BeamStaticBeam (`beams.dm:357`), e nao a arte de mesmo nome",
		   ArteDeProjetil.De("Makkankosappo", "Human", "", 1) == ArteDeKi.BeamStaticBeam);

		// ---- o Charged_Shot ignora a raca (`blasts.dm:103` escreve '20.dmi' na unha)
		Ok("o Tiro Carregado e igual pra toda raca (o DM escreve '20.dmi' literal)",
		   ArteDeProjetil.De("Charged_Shot", "Android", "", 1)
		   == ArteDeProjetil.De("Charged_Shot", "Tsujin", "", 1));

		// ============================ OS SETE QUE VOAVAM SEM ARTE ============================
		// Entraram em producao sem linha na tabela e saiam no desenho neutro -- inclusive as duas
		// maiores bolas do jogo, que o dono citou pelo nome ao pedir o shader (*"esferas como genkidama,
		// super nova, blast"*). A lista de cima ja confere a arte de cada um; estas tres linhas conferem
		// o que a arte de cada um IMPLICA.
		// ====================================================================================
		Ok("as tres rajadas do `Create_Blast()` saem da bola da RACA, e mudam com ela (`tools/copypaste.dm:12-13`)",
		   new[] { "BusterBarrage", "Continuous_Energy_Bullets", "Spin_Blast" }.All(v =>
			   ArteDeProjetil.De(v, "Human", "", 1) == ArteDeKi.Blast1
			   && ArteDeProjetil.De(v, "Android", "", 1) == ArteDeKi.Blast10
			   && ArteDeProjetil.De(v, "Tsujin", "", 1) == ArteDeKi.Blast5));
		Ok("a Genkidama NAO entra em menu nenhum da tecnica inventada (e imagem solta, fora de `Icons/`)",
		   !ArteDeProjetil.PermitidasPara(TipoDeProjetil.Beam).Contains(ArteDeKi.SpiritBomb)
		   && !ArteDeProjetil.PermitidasPara(TipoDeProjetil.Blast).Contains(ArteDeKi.SpiritBomb)
		   && !ArteDeProjetil.PermitidasPara(TipoDeProjetil.Guided).Contains(ArteDeKi.SpiritBomb));
		Ok("o alvo de treino (`14.dmi`) mora em `Blasts`: entra no menu de BOLA e so nele",
		   ArteDeProjetil.PermitidasPara(TipoDeProjetil.Blast).Contains(ArteDeKi.Blast14)
		   && !ArteDeProjetil.PermitidasPara(TipoDeProjetil.Beam).Contains(ArteDeKi.Blast14)
		   && !ArteDeProjetil.PermitidasPara(TipoDeProjetil.Guided).Contains(ArteDeKi.Blast14));

		// =====================================================================
		// A CAMADA RACIAL
		// =====================================================================
		Familia("1b", "a bola MUDA com a raca (`race.dm:62` + os quatro `stat*.dm`)");
		(string Raca, string Classe, ArteDeKi Esperada)[] racas =
		[
			("Human", "", ArteDeKi.Blast1),      // `race.dm:62`
			("Saiyan", "", ArteDeKi.Blast1),     // nao sobrescreve: fica no padrao
			("Android", "", ArteDeKi.Blast10),   // `statandroid.dm:9`
			("Tsujin", "", ArteDeKi.Blast5),     // `stattsujin.dm:6`
			("Yardrat", "", ArteDeKi.Blast7),    // `statyardrat.dm:7`
			("Demigod", "", ArteDeKi.Blast1),    // `statdemi.dm:13`
			("Demigod", "Ogre", ArteDeKi.Blast31),  // `statdemi.dm:29`
			("Demigod", "Genie", ArteDeKi.Blast19), // `statdemi.dm:45`
		];
		int erradas = 0;
		foreach ((string raca, string classe, ArteDeKi esp) in racas)
		{
			ArteDeKi teve = ArteDeProjetil.De("Basic_Blast", raca, classe, 1);
			if (teve != esp) { Nota($"{raca}/{classe}: esperava {esp}, veio {teve}"); erradas++; }
		}
		Ok("as oito linhas raciais de bola batem com o DM", erradas == 0);

		// A PROVA DE QUE A CAMADA VALE PRA VALER: quatro racas, quatro artes diferentes na MESMA
		// tecnica. Uma tabela racial escrita e nunca consultada passaria nas oito linhas acima (se
		// todas devolvessem o padrao a comparacao acusaria) -- esta linha e a que garante que o
		// caminho de producao (`Basic_Blast`) chega mesmo ate ela.
		Ok("quatro racas dao quatro bolas diferentes no MESMO verb",
		   new[] { "Human", "Android", "Tsujin", "Yardrat" }
			   .Select(r => ArteDeProjetil.De("Basic_Blast", r, "", 1)).Distinct().Count() == 4);

		// ============================ AS TRES CAMADAS QUE NENHUM VERB PORTADO ALCANCA ============================
		// `WaveIcon`, `CBLASTICON` e `Makkankoicon` estao lidas do DM e nao tem valor de `Fonte` --
		// ver o bloco daquele enum. Elas sao MEDIDAS aqui mesmo assim, e por uma razao pratica: uma
		// leitura de original que ninguem exercita envelhece igual a codigo morto, e a parte cara
		// daquela rodada foi justamente ler `race.dm` e os quatro `stat*.dm`. No dia em que o
		// Energy_Shot ou o Special Beam Cannon forem portados, estas tres linhas ja garantiram que a
		// resposta que eles vao consumir e a do jogo original.
		// ====================================================================================================
		Ok("o Makkanko racial tem as tres respostas do DM (padrao, Tsujin, Yardrat)",
		   ArteDeProjetil.Makkanko("Human") == ArteDeKi.Makkankosappo4
		   && ArteDeProjetil.Makkanko("Tsujin") == ArteDeKi.Makkankosappo3
		   && ArteDeProjetil.Makkanko("Yardrat") == ArteDeKi.Makkankosappo);

		// `WaveIcon` (`race.dm:57`): so o Demigod da linhagem Demigod troca (`statdemi.dm:9`).
		Ok("o `WaveIcon` racial: Beam3 pra todo mundo, Beam2 so pro Demigod-Demigod",
		   ArteDeProjetil.Onda("Human", "") == ArteDeKi.Beam3
		   && ArteDeProjetil.Onda("Demigod", "") == ArteDeKi.Beam2
		   && ArteDeProjetil.Onda("Demigod", "Ogre") == ArteDeKi.Beam3);

		// `CBLASTICON` (`race.dm:64`) -- e ele NAO e o do Tiro Carregado, que e literal.
		Ok("o `CBLASTICON` racial tem as cinco respostas do DM",
		   ArteDeProjetil.BolaCarregada("Human", "") == ArteDeKi.Blast18
		   && ArteDeProjetil.BolaCarregada("Android", "") == ArteDeKi.Blast11
		   && ArteDeProjetil.BolaCarregada("Tsujin", "") == ArteDeKi.Blast6
		   && ArteDeProjetil.BolaCarregada("Yardrat", "") == ArteDeKi.Blast8
		   && ArteDeProjetil.BolaCarregada("Demigod", "Ogre") == ArteDeKi.Blast35);

		// =====================================================================
		// O SORTEIO
		// =====================================================================
		Familia("1c", "o Kamehameha e SORTEADO por personagem (`Kamehameha.dm:14`)");

		var saiu = new HashSet<ArteDeKi>();
		for (int i = 0; i < 400; i++)
			saiu.Add(ArteDeProjetil.De("Kamehameha", "Human", "",
									   Jandirus.Core.Forms.LimiaresPessoais.SementeDe($"lutador{i}", 1700 + i)));

		Ok($"os SEIS Kamehamehas do `rand(1,6)` sao alcancaveis ({saiu.Count} de "
		   + $"{ArteDeProjetil.QuantosKamehamehas} em 400 personagens)",
		   saiu.Count == ArteDeProjetil.QuantosKamehamehas);

		// ============================ ESTAVEL E A OUTRA METADE DO PEDIDO ============================
		// No DM o sorteio e gravado numa `mob/var` salva: ele nao muda nunca mais. Aqui ele e uma
		// funcao PURA da identidade, e a prova de que isso equivale e esta: mil chamadas, mesma
		// resposta. Uma implementacao que sorteasse por disparo passaria na linha de cima (as seis
		// aparecem) e reprovaria aqui -- e o jogador veria o proprio Kamehameha trocar de arte a
		// cada tiro.
		// =======================================================================================
		ulong semente = Jandirus.Core.Forms.LimiaresPessoais.SementeDe("Goku", 1700);
		ArteDeKi primeiro = ArteDeProjetil.De("Kamehameha", "Human", "", semente);
		bool estavel = true;
		for (int i = 0; i < 1000; i++)
			if (ArteDeProjetil.De("Kamehameha", "Human", "", semente) != primeiro) estavel = false;
		Ok($"o mesmo personagem tira SEMPRE o mesmo ({primeiro}) -- 1000 chamadas", estavel);

		// E ele NAO pode andar junto da cor da aura: as duas partem da mesma semente de personagem, e
		// sem o sal por campo quem tem aura branca teria sempre Kamehameha1. Ver o `Hash64` no meio
		// do `SorteioDoKamehameha`.
		int mudou = 0;
		for (int i = 0; i < 200; i++)
		{
			ulong s = Jandirus.Core.Forms.LimiaresPessoais.SementeDe($"p{i}", 1700);
			if (ArteDeProjetil.De("Kamehameha", "Human", "", s) != primeiro) mudou++;
		}
		Ok($"personagens diferentes tiram Kamehamehas diferentes ({mudou} de 200 fugiram do {primeiro})",
		   mudou > 100);
	}

	// =====================================================================
	// FAMILIA 2 -- TODO ESTILO EXISTE E SE VESTE
	// =====================================================================
	/// <summary>
	/// ============================ O QUE ELA ERA, E O QUE SOBROU DA PERGUNTA ============================
	/// Esta familia carregava as 41 folhas do disco e perguntava a cada uma pelos estados que o desenho
	/// pedia -- porque este repo ja escreveu 35 atlas e nunca os importou, e uma tabela de arte perfeita
	/// apontando pra `.tres` que nao carregam da na tela o mesmo que nao ter tabela.
	///
	/// Nao ha mais folha, mas o MODO DE FALHA continua de pe com outro nome: uma entrada do enum sem
	/// linha na mesa de estilos (<see cref="ArteDeKiNoCliente"/>) nao da erro nenhum -- cai no estilo
	/// NEUTRO, calada, e a tecnica voa como um raio liso qualquer. Foi exatamente assim que a Genkidama
	/// e a Death Ball voaram por dois lotes. Entao a pergunta virou: toda arte tem estilo PROPRIO, os
	/// shaders que os estilos alimentam carregam, e um tiro de cada tipo sai vestido com cada uma.
	///
	/// ELA NAO DIZ QUE O TIRO APARECE: `Vestido` e "ha um material", e material nao e pixel. Quem
	/// fotografa e a 2b.
	/// ====================================================================================================
	/// </summary>
	private void Familia2()
	{
		Familia("2", "todo estilo existe e se veste");

		int total = 0, semEstilo = 0, semNome = 0;
		foreach (ArteDeKi a in ArteDeProjetil.Todas)
		{
			total++;
			if (ArteDeProjetil.Folha(a).Pasta.Length == 0)
			{
				Nota($"{a}: SEM pasta no `Folha()` -- sem nome no menu e fora de todo recorte por tipo");
				semNome++;
			}
			if (!ArteDeKiNoCliente.TemEstilo(a)) { Nota($"{a}: SEM ESTILO PROPRIO -- sairia no neutro, calada"); semEstilo++; }
		}
		Ok($"as {total} artes do catalogo tem ESTILO PROPRIO", total > 0 && semEstilo == 0);
		Ok("...e todas tem pasta e nome no `Folha()` (e o que o menu mostra e o que recorta por tipo)", semNome == 0);

		// O NEUTRO EXISTE DE PROPOSITO (ver `ArteDeKi.Nenhuma`): e pra onde cai o verb fora da tabela e
		// o numero que so uma versao mais nova conhece. Ele e tambem o que prova que `TemEstilo` sabe
		// dizer NAO -- sem isto a linha de cima seria verde pra um `TemEstilo` que devolvesse sempre true.
		Ok("CONTRA-EXEMPLO: `Nenhuma` e um numero que este cliente nao conhece NAO tem estilo proprio",
		   !ArteDeKiNoCliente.TemEstilo(ArteDeKi.Nenhuma) && !ArteDeKiNoCliente.TemEstilo((ArteDeKi)60000));

		var semShader = new List<string>();
		foreach (string caminho in new[] { PintorDeKi.ShaderDoFeixe, PintorDeKi.ShaderDaEsfera, PintorDeKi.ShaderDoChoque })
			if (ResourceLoader.Load<Shader>(caminho) == null) semShader.Add(caminho);
		foreach (string s in semShader) Nota($"NAO CARREGOU: {s}");
		Ok("os tres shaders carregam (o do feixe, o da esfera e o do choque)", semShader.Count == 0);

		// OS TRES TIPOS, e nao so raio e bola: o teleguiado entra pelo mesmo ramo da bola hoje, e e
		// justamente por isso que vale a linha -- no dia em que ele ganhar desenho proprio, quem
		// esquecer de vesti-lo descobre aqui e nao na tela.
		TipoDeProjetil[] tipos = [TipoDeProjetil.Beam, TipoDeProjetil.Blast, TipoDeProjetil.Guided];
		int vestidos = 0, nus = 0;
		foreach (TipoDeProjetil tipo in tipos)
			foreach (ArteDeKi a in ArteDeProjetil.Todas.Append(ArteDeKi.Nenhuma))
			{
				var p = new ProjetilDesenhado { Tipo = tipo };
				p.Vestir(a, 1f);
				if (p.Vestido) vestidos++;
				else { Nota($"{tipo} com {a}: o `Vestir` nao deixou material"); nus++; }
				p.Free();
			}
		Ok($"um tiro de cada tipo se VESTE com cada arte, e com nenhuma ({vestidos} combinacoes)",
		   nus == 0 && vestidos == tipos.Length * (total + 1));

		// A PRIMITIVA DE EMERGENCIA MORREU, e o papel dela ficou com o `_Ready`: um tiro posto na arvore
		// sem `Vestir` se veste sozinho, porque tiro invisivel o jogador nao distingue de "a tecnica nao
		// funciona". As duas metades sao medidas: antes de entrar ele esta nu (e isso prova que `Vestido`
		// sabe dizer nao), depois de entrar esta vestido.
		var cru = new ProjetilDesenhado { Name = "TiroCru", Tipo = TipoDeProjetil.Beam };
		Ok("CONTRA-EXEMPLO: um tiro que ninguem vestiu responde que NAO esta vestido", !cru.Vestido);
		AddChild(cru);
		Ok("...e ao entrar na arvore ele se veste sozinho (nao existe tiro invisivel por esquecimento)", cru.Vestido);
		cru.QueueFree();

		// ============================ O RECORTE POR TIPO E DO DM, E ELE TEM QUE MORDER ============================
		// `custom_icon_folders` (`customattacks.dm:558-562`). Se as tres listas fossem iguais, a
		// tecnica customizada deixaria pendurar arte de raio numa bola. Estas linhas sao o unico lugar
		// desta bancada que reprova isso.
		// ====================================================================================================
		int nRaio = ArteDeProjetil.PermitidasPara(TipoDeProjetil.Beam).Count();
		int nBola = ArteDeProjetil.PermitidasPara(TipoDeProjetil.Blast).Count();
		int nTele = ArteDeProjetil.PermitidasPara(TipoDeProjetil.Guided).Count();
		Ok($"o menu da tecnica inventada e RECORTADO pelo tipo (raio {nRaio}, bola {nBola}, teleguiado {nTele})",
		   nRaio > nBola && nRaio > nTele && nBola > 0 && nTele > 0);
		Ok("nenhuma arte de raio aparece no menu de bola",
		   !ArteDeProjetil.PermitidasPara(TipoDeProjetil.Blast).Contains(ArteDeKi.Beam3));

		// O padrao do custom e o do DM nos dois ramos (`:440` e `:500`), e ele tem que TER estilo: um
		// padrao no neutro faria toda tecnica inventada sem arte escolhida sair igual nos dois tipos.
		Ok("o padrao da tecnica inventada tem estilo proprio nos dois tipos (Beam3 e a 12.dmi)",
		   ArteDeKiNoCliente.TemEstilo(ArteDeProjetil.PadraoDoCustom(TipoDeProjetil.Beam))
		   && ArteDeKiNoCliente.TemEstilo(ArteDeProjetil.PadraoDoCustom(TipoDeProjetil.Blast)));
	}

	// =====================================================================
	// O ROTEIRO -- um passo poe o palco, o seguinte fotografa e mede
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (_segundosDeFolga > 0 || _quadrosDeFolga > 0)
		{
			_segundosDeFolga -= delta;
			_quadrosDeFolga--;
			return;
		}

		Passo(_t++);

		// SEM JANELA nao ha foto, e sem foto nenhuma familia daqui pra frente tem o que dizer. A
		// bancada diz que nao mediu (a `Foto` ja anotou) e fecha -- ela nao passa de graca.
		if (_semFoto) Encerrar();
	}

	private void Passo(int n)
	{
		switch (n)
		{
			// ============================ FAMILIA 2b -- TODO ESTILO DESENHA ============================
			case 0:
				Familia("2b", "todo estilo DESENHA -- as 44 artes como bola, na foto");
				PorTodasAsBolas();
				break;

			case 1: MedirTodasAsBolas(); break;
			case 2: PorAsEsferasGrandes(); break;
			case 3: MedirAsEsferasGrandes(); break;

			// ============================ FAMILIA 3 -- O PIXEL ============================
			case 4:
				Familia("3", "o PIXEL desenhado, e nao a intencao");
				PorAVitrine((ArteDeKi.Kamehameha1, 1f, Vermelho), (ArteDeKi.Kamehameha1, 1f, Vermelho),
							(ArteDeKi.BeamMasenko, 1f, Vermelho), (ArteDeKi.BeamMasenko, 1f, Vermelho));
				break;

			case 5: FotografarDuasArtes(); break;

			case 6:
				// A MESMA ARTE, OUTRA COR DE KI -- e pelo `Tingir`, que e o caminho da previa da mesa de
				// tecnicas (o seletor de cor retinge o node vivo). A `--diagmesa` confere que o CAMPO
				// `Cor` da previa mudou; quem confere que a tela mudou e esta foto.
				_palco[0].No.Tingir(Azul);
				Assentar();
				break;

			case 7: FotografarTinta(); break;

			case 8:
				// A ESCALA. `A.transform *= wavemult` (`beams.dm:149`): o Final Flash e um MURO. Dois
				// Masenkos a 1x e dois a 3x -- o par de 1x e o controle da medida, na mesma foto.
				PorAVitrine((ArteDeKi.BeamMasenko, 1f, Vermelho), (ArteDeKi.BeamMasenko, 1f, Vermelho),
							(ArteDeKi.BeamMasenko, 3f, Vermelho), (ArteDeKi.BeamMasenko, 3f, Vermelho));
				break;

			case 9: FotografarEscala(); break;
			case 10: PorAsCoresDePersonagem(); break;
			case 11: FotografarCoresDeVerdade(); break;
			case 12: PorAsBolasDaVitrine(); break;
			case 13: FotografarBolas(); break;

			// ============================ FAMILIA 4 -- A CONTINUIDADE EM TODO COMPRIMENTO ============================
			// O dono relatou *"o beam ta PICOTADO quando ele e lancado"*, e depois, vendo o trailer,
			// *"aquela coisa picotada"* na diagonal. No desenho de folhas a lei do defeito era
			// `fresta = comprimento mod passo`: sumia nos multiplos exatos do ladrilho e aparecia em todo
			// o resto -- e um raio que cresce passa por todos os comprimentos. A fita nao tem passo, mas
			// e a FOTO que tem que dizer isso, do raio que acabou de sair da boca ate o de vinte tiles.
			// ==========================================================================================================
			case 14:
				Familia("4", "a continuidade em TODO comprimento (doze, num angulo torto)");
				PorOsComprimentos();
				break;

			case 15:
				MedirAContinuidade("4-comprimentos",
								   $"{Comprimentos.Length} comprimentos a {AnguloDaEscada:0} graus", umPorUm: false);
				break;

			// ============================ FAMILIA 4b -- TODAS AS ARTES ============================
			// A continuidade de cima e a do `Beam3`. Cada arte e o MESMO shader com outros numeros -- e
			// sao os numeros que abrem buraco: um tronco que pulsa ate 45% da largura (o Boom Wave), um
			// macico de 2,5 px (a broca), uma cabeca em estouro de espinhos. Entao as 24 que um raio pode
			// vestir vao pro mesmo angulo torto, uma em cada celula.
			// =====================================================================================
			case 16:
				Familia("4b", "as 24 artes de raio numa diagonal torta");
				PorTodasAsArtesNaDiagonal();
				break;

			// SEM A CONTAGEM DE ILHAS, e so aqui: as artes de enfeite soltam tinta do corpo DE PROPOSITO (a
			// descarga do Static Beam rasteja a ate um raio do tronco), e uma ilha a mais nesta foto e o
			// estilo, nao um raio partido. Pra elas quem responde e o eixo.
			case 17:
				MedirAContinuidade("4b-todas-as-artes-a-33-graus",
								   $"{_palco.Count} artes a {AnguloDasArtes:0} graus", umPorUm: false, contarIlhas: false);
				break;

			// ============================ FAMILIA 5 -- A FOTO DO DONO ============================
			case 18:
				Familia("5", "a foto do dono, reproduzida (o feixe que atravessa a tela)");
				PorAFotoDoDono();
				break;

			case 19: MedirAContinuidade("5-a-foto-do-dono", "", umPorUm: true); break;

			// ============================ FAMILIA 5a -- LONGOS, FORA DOS OITO RUMOS ============================
			case 20:
				Familia("5a", "tres feixes LONGOS em angulos que nenhuma folha tinha (17, 33 e 61 graus)");
				PorOsLongosTortos();
				break;

			case 21: MedirAContinuidade("5a-longos-em-angulo-torto", "", umPorUm: true); break;

			// ============================ FAMILIA 5b -- OS 19 RUMOS ============================
			case 22:
				Familia("5b", $"os {Rumos.Length} rumos -- o mesmo raio longo, curto, no lancamento e nascendo");
				PorOsRumos(0f);
				break;

			case 23: MedirOsRumos("5b-1-rumos-longo", $"LONGO ({TotalDaGrade():0} px da mao a ponta)", Esperado.Inteiro); break;

			// O CURTO: 44 px de boca a cabeca. No trem de folhas cabia UM corpo e sobravam 13 -- e
			// aqueles 13 px eram a fresta inteira.
			case 24: PorOsRumos(44f); break;
			case 25: MedirOsRumos("5b-2-rumos-curto", "CURTO (44 px da boca a cabeca)", Esperado.Inteiro); break;

			// MAIS CURTO QUE UM TILE: o instante do LANCAMENTO, o que o dono fotografou.
			case 26: PorOsRumos(24f); break;
			case 27: MedirOsRumos("5b-3-rumos-lancamento", "NO LANCAMENTO (24 px)", Esperado.Inteiro); break;

			// E O PRIMEIRO PACOTE: a cabeca a 2 px da boca, o minimo pra o raio TER rumo (`Mirar` exige
			// mais de 1 px). O desenho inteiro sao a bola da mao e a da ponta, e elas tem que se tocar.
			case 28: PorOsRumos(2f); break;
			case 29: MedirOsRumos("5b-4-rumos-nascendo", "NASCENDO (2 px: a cabeca mal saiu da boca)", Esperado.Inteiro); break;

			// ============================ FAMILIA 5c -- O FIO E O MURO ============================
			case 30:
				Familia("5c", "o FIO e o MURO -- a escala engrossa o desenho?");
				PorOFioEOMuro();
				break;

			case 31: MedirOFioEOMuro(); break;

			// ============================ FAMILIA 5d -- OS DEFEITOS INJETADOS ============================
			// Os MESMOS dezenove raios, as MESMAS duas reguas (o eixo e as ilhas), e so o defeito muda --
			// ele e lido no `_Draw` de producao a cada quadro, entao religar o campo basta: os nodes nem
			// sao refeitos entre uma foto e a outra.
			case 32:
				Familia("5d", "os defeitos INJETADOS -- as reguas sabem ficar vermelhas, e voltar?");
				PorOsRumos(0f);
				ProjetilDesenhado.DefeitoDeTeste = ProjetilDesenhado.DefeitoDoFeixe.Picotado;
				break;

			case 33: MedirOsRumos("5d-1-defeito-picotado", "[injecao] `Picotado`", Esperado.TudoPicotado); break;

			case 34:
				ProjetilDesenhado.DefeitoDeTeste = ProjetilDesenhado.DefeitoDoFeixe.SemGirar;
				Assentar();
				break;

			case 35: MedirOsRumos("5d-2-defeito-sem-girar", "[injecao] `SemGirar`", Esperado.SoOsCardeais); break;

			case 36:
				// E DE VOLTA AO NORMAL, com os MESMOS nodes: sem esta fase a familia provaria so que a
				// sonda fica vermelha, e nao que ela distingue.
				ProjetilDesenhado.DefeitoDeTeste = ProjetilDesenhado.DefeitoDoFeixe.Nenhum;
				Assentar();
				break;

			case 37: MedirOsRumos("5d-3-defeito-desligado", "com o defeito DESLIGADO", Esperado.Inteiro); break;

			// ============================ FAMILIA 6 -- O DESENHO OBEDECE AS TABELAS DO CORE ============================
			case 38:
				Familia("6", "o desenho obedece as tabelas do Core -- a ponta e o tronco, medidos na foto");
				PorARegra(_paginaDaRegra = 0);
				break;

			// ESTE PASSO SE REPETE ate as paginas acabarem: mede a que esta no palco e, se ainda houver
			// arte sem medir, poe a seguinte e volta pra ca. Sao tres paginas hoje (24 artes, oito por
			// foto); tres passos cravados deixariam a vigesima quinta arte de fora, calada.
			case 39:
				MedirARegra($"6-1-regra-escala-1-{(char)('a' + _paginaDaRegra)}");
				if (++_paginaDaRegra < PaginasDaRegra()) { PorARegra(_paginaDaRegra); _t = 39; }
				break;

			case 40: PorARegraEmOutrasEscalas(); break;
			case 41: MedirARegra("6-2-regra-outras-escalas"); break;
			case 42: PorAContraprovaDaRegra(comDefeito: true); break;
			case 43: MedirAContraprovaDaRegra("6-3-regra-defeito-injetado", comDefeito: true); break;
			case 44: PorAContraprovaDaRegra(comDefeito: false); break;
			case 45: MedirAContraprovaDaRegra("6-4-regra-defeito-desligado", comDefeito: false); break;

			default: Encerrar(); break;
		}
	}

	private void Assentar()
	{
		_segundosDeFolga = SegundosDeAssento;
		_quadrosDeFolga = QuadrosDeAssento;
	}

	private void Encerrar()
	{
		if (_acabou) return;
		_acabou = true;
		DesligarOsDefeitos();

		GD.Print("\n[artedeki] ===== BANCADA DA ARTE DOS ATAQUES DE KI =====");
		foreach (string l in _linhas) GD.Print("[artedeki] " + l);
		GD.Print("[artedeki] placar por familia: "
				 + string.Join("  ", _placar.Select(p => $"{p.Nome}={p.Feitas}" + (p.Reprovadas > 0 ? $"({p.Reprovadas} FALHA)" : "")))
				 + $"  -- {_placar.Sum(p => p.Feitas)} conferencias");
		GD.Print(_falhas == 0
			? "[artedeki] ===== TUDO VERDE ====="
			: $"[artedeki] ===== {_falhas} FALHA(S) =====");
		GetTree().Quit(_falhas == 0 ? 0 : 1);
	}

	// =====================================================================
	// O PALCO
	// =====================================================================
	/// <summary>
	/// UM TIRO NO PALCO, com o que a bancada precisa lembrar dele pra medir: onde ela mandou por a
	/// cabeca e a cauda (a boca), e com que arte e escala ele DIZ estar vestido.
	///
	/// A MAO E A PONTA SAO CALCULADAS AQUI, PELA REGRA -- e nao perguntadas ao node. Sao as duas pontas
	/// de toda sonda de continuidade, e uma sonda que pedisse ao desenho "onde voce comeca e acaba?"
	/// mediria o desenho contra ele mesmo.
	/// </summary>
	private sealed record TiroNoPalco(ProjetilDesenhado No, ArteDeKi Arte, float Escala, Vector2 Cabeca, Vector2 Cauda, string Rotulo)
	{
		public Vector2 Rumo => (Cabeca - Cauda).Normalized();

		/// <summary>Sem corpo de dono na tela, a mao e meia celula ATRAS da boca, pelo eixo do raio.</summary>
		public Vector2 Mao => Cauda - Rumo * (MeiaCelula * (MathF.Abs(Rumo.X) + MathF.Abs(Rumo.Y)));

		/// <summary>A ponta que a TABELA do Core manda: `FrenteDaCabeca x escala` adiante da posicao do tiro.</summary>
		public Vector2 Ponta => Cabeca + Rumo * (ArteDeProjetil.FrenteDaCabeca(Arte) * Escala);
	}

	private readonly List<TiroNoPalco> _palco = [];

	/// <summary>
	/// UM RAIO NOVO a cada palco, e nao o de antes remirado -- e isso importa.
	///
	/// `Mirar` so CRAVA a posicao no primeiro pacote; do segundo em diante ela vira alvo de
	/// interpolacao (`Suavizacao`, 22 por segundo). Remirar um node vivo e fotografar em seguida mede
	/// um raio de comprimento DESCONHECIDO. Nascer de novo e a mesma coisa que o jogo faz (o tiro
	/// nasce, ai interpola) e da o comprimento pedido, exato.
	/// </summary>
	/// <param name="vestidoCom">
	/// So a contraprova da familia 6 passa isto: a escala com que o tiro e VESTIDO, quando ela nao e a
	/// que ele vai DIZER que tem. O campo `Escala` e escrito depois do `Vestir`, sem vestir de novo.
	/// </param>
	private TiroNoPalco NovoRaio(ArteDeKi arte, float escala, Vector2 cabeca, Vector2 cauda, Color cor,
								 string rotulo, float? vestidoCom = null)
	{
		var no = new ProjetilDesenhado
		{
			Name = $"Raio{_nascidos++}",
			Tipo = TipoDeProjetil.Beam,
			Cor = cor,
			Position = cabeca,
		};
		no.Vestir(arte, vestidoCom ?? escala);
		if (vestidoCom != null) no.Escala = escala;   // a mentira da contraprova: diz uma escala, veste outra
		no.Mirar(cabeca, cauda);
		AddChild(no);

		var tiro = new TiroNoPalco(no, arte, escala, cabeca, cauda, rotulo);
		_palco.Add(tiro);
		return tiro;
	}

	/// <summary>
	/// UMA BOLA PARADA, com o rastro inteiro. `SempreVoando` e a chave da previa da mesa de tecnicas:
	/// sem ela uma bola que nao anda e desenhada sem a cauda que tem em voo, e a foto mostraria um
	/// desenho que nao e o que sai da mao de ninguem. Cabeca e cauda no MESMO ponto: bola nao tem
	/// rastro de servidor (`Projetil.Comprimento` devolve zero pra ela).
	/// </summary>
	private TiroNoPalco NovaBola(ArteDeKi arte, float escala, Vector2 onde, Color cor, string rotulo)
	{
		var no = new ProjetilDesenhado
		{
			Name = $"Bola{_nascidos++}",
			Tipo = TipoDeProjetil.Blast,
			Cor = cor,
			Position = onde,
			SempreVoando = true,
		};
		no.Vestir(arte, escala);
		no.Mirar(onde, onde);
		AddChild(no);

		var tiro = new TiroNoPalco(no, arte, escala, onde, onde, rotulo);
		_palco.Add(tiro);
		return tiro;
	}

	/// <summary>Tira TODO tiro da tela. O `QueueFree` some com eles antes do proximo quadro desenhado.</summary>
	private void LimparOPalco()
	{
		foreach (TiroNoPalco t in _palco) t.No.QueueFree();
		_palco.Clear();
	}

	/// <summary>
	/// O TAMANHO DA TELA EM PIXEL DE MUNDO, e todo palco sai dele.
	///
	/// A `testar-variedade.bat` abre 1280x720; um berco cravado em pixel sairia da tela em outra
	/// janela, e um raio fora da tela mede fresta ENORME sem ter fresta nenhuma. Berco calculado nao
	/// tem esse jeito de mentir -- e a sonda ainda conta como FUNDO o que cair fora da foto.
	/// </summary>
	private Vector2 Tela => GetViewportRect().Size;

	/// <summary>O vetor de um rumo em graus, no sentido da TELA: 0 = leste, 90 = sul (o Y cresce pra baixo).</summary>
	private static Vector2 Rumo(float graus)
	{
		float rad = Mathf.DegToRad(graus);
		return new Vector2(MathF.Cos(rad), MathF.Sin(rad));
	}

	// =====================================================================
	// A FOTO
	// =====================================================================
	/// <summary>
	/// UMA FOTO JA REVELADA: os bytes, o tamanho e a transformada que leva um ponto do MUNDO ao pixel
	/// dela.
	///
	/// OS BYTES SAO COPIADOS UMA VEZ. O `Image.GetPixel` atravessa a fronteira C#/motor a cada chamada,
	/// e as sondas daqui leem a foto inteira varias vezes por passo.
	///
	/// A TRANSFORMADA NAO E A IDENTIDADE POR ACASO: o projeto estica em `canvas_items`, entao uma
	/// janela maior que 1280x720 desenha tudo ampliado e o pixel (x, y) da foto deixa de ser o ponto
	/// (x, y) do mundo. Toda sonda entra com coordenada de mundo e passa por aqui.
	/// </summary>
	private sealed class Chapa
	{
		public readonly int Largura, Altura;
		public readonly Transform2D DoMundo;

		/// <summary>Quantos pixels de foto cabem em um pixel de mundo (1 na janela de 1280x720).</summary>
		public readonly float PxPorMundo;

		private readonly byte[] _rgba;

		public Chapa(Image img, Transform2D doMundo)
		{
			if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
			Largura = img.GetWidth();
			Altura = img.GetHeight();
			_rgba = img.GetData();
			DoMundo = doMundo;
			PxPorMundo = doMundo.Scale.X;
		}

		public bool Dentro(int x, int y) => x >= 0 && y >= 0 && x < Largura && y < Altura;

		public (byte R, byte G, byte B) Cor(int x, int y)
		{
			int i = (y * Largura + x) * 4;
			return (_rgba[i], _rgba[i + 1], _rgba[i + 2]);
		}

		/// <summary>O pixel em que este ponto do mundo caiu (pode estar fora da foto).</summary>
		public Vector2I Pixel(Vector2 mundo)
		{
			Vector2 p = DoMundo * mundo;
			return new Vector2I(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y));
		}

		/// <summary>
		/// E TINTA? -- qualquer coisa que nao seja o verde cheio do fundo. Fora da foto NAO e tinta:
		/// um raio que sai da tela tem que ler como buraco, e nao como raio inteiro.
		/// </summary>
		public bool Tinta(int x, int y)
		{
			if (!Dentro(x, y)) return false;
			(byte r, byte g, byte b) = Cor(x, y);
			return !(g > 229 && r < 26 && b < 26);
		}

		/// <summary>
		/// E CORPO SOLIDO de um tiro tingido de MAGENTA? -- vermelho e azul acima de 40%.
		///
		/// ============================ POR QUE MAGENTA, E POR QUE 40% ============================
		/// A tinta do ki e SOMADA aos tres tons da arte, canal a canal, com teto (`Rampa.Com`). Com
		/// magenta puro o vermelho e o azul de casca, manto e nucleo batem no teto em TODA arte, e o que
		/// sobra por cima e so a textura do shader (os riscos de fluxo, de 0,80 a 1,22, e o granulado,
		/// que no pior caso desce a 0,48). O fundo tem vermelho e azul ZERO. Entao "quanto de magenta
		/// ha neste pixel" e, com folga, "quanto do pixel o corpo cobre" -- e 40% poe a beirada medida
		/// a menos de meio pixel do contorno do desenho, sem depender de que arte e.
		/// ========================================================================================
		/// </summary>
		public bool Solido(int x, int y)
		{
			if (!Dentro(x, y)) return false;
			(byte r, _, byte b) = Cor(x, y);
			return r > 102 && b > 102;
		}
	}

	/// <summary>
	/// Fotografa, GRAVA em `user://artedeki-*.png` e devolve a chapa.
	///
	/// A gravacao nao e enfeite: os numeros dizem que dois desenhos sao DIFERENTES ou que um raio e
	/// CONTINUO, e nao que ele esta BONITO. Um raio desenhado ao contrario, uma cabeca do lado errado
	/// ou um halo quadrado passam por qualquer contagem de pixel -- e este projeto ja tem correcoes
	/// visuais que so a FOTO mostrou. O olho e a ultima familia, e ela nao cabe num `if`.
	/// </summary>
	private Chapa? Foto(string nome)
	{
		Viewport? v = GetViewport();
		Image? img = v?.GetTexture()?.GetImage();
		if (v == null || img == null || img.IsEmpty())
		{
			Nota("SEM FOTO (headless nao renderiza) -- as familias 1 e 2 valem; as de PIXEL, da 2b em diante, NAO RODARAM");
			_semFoto = true;
			return null;
		}

		string caminho = $"user://artedeki-{nome}.png";
		img.SavePng(caminho);
		Nota($"foto: {ProjectSettings.GlobalizePath(caminho)}");

		var chapa = new Chapa(img, v.GetFinalTransform() * v.CanvasTransform * GlobalTransform);
		if (!_anoteiAEscala)
		{
			_anoteiAEscala = true;
			Nota($"a foto tem {chapa.Largura}x{chapa.Altura} px e o mundo {Tela.X:0}x{Tela.Y:0}: "
				 + $"{chapa.PxPorMundo:0.00} px de foto por px de mundo");

			// OS PALCOS FORAM DESENHADOS PRA 1280x720 OU MAIS (conferido tambem em 1600x900 e em tela
			// cheia, com 1,5 px de foto por px de mundo). Numa tela menor os raios vizinhos de uma mesma
			// foto se encostam, e a contagem de ilhas reprovaria um desenho que esta certo.
			if (Tela.X < 1280f || Tela.Y < 720f)
				Nota("ATENCAO: tela menor que 1280x720 -- os palcos nao cabem, e uma falha daqui pra frente pode ser so isso");
		}
		return chapa;
	}

	private bool _anoteiAEscala;

	/// <summary>Quantos pixels de TINTA ha num quadrado de meia largura `meia` (em px de mundo) em volta de um ponto.</summary>
	private static int TintaNoQuadro(Chapa c, Vector2 centro, float meia)
	{
		Vector2I a = c.Pixel(centro - new Vector2(meia, meia)), b = c.Pixel(centro + new Vector2(meia, meia));
		int n = 0;
		for (int y = a.Y; y <= b.Y; y++)
			for (int x = a.X; x <= b.X; x++)
				if (c.Tinta(x, y)) n++;
		return n;
	}

	// =====================================================================
	// FAMILIA 2b -- TODO ESTILO DESENHA
	// =====================================================================
	/// <summary>As duas esferas que nao cabem numa celula da grade: a Death Ball (36 px de raio) e a Genkidama (96).</summary>
	private static bool EhEsferaGrande(ArteDeKi a) => ArteDeKiNoCliente.EstiloDaBola(a).Raio > 30f;

	/// <summary>
	/// AS 42 ARTES PEQUENAS, CADA UMA COMO BOLA NA SUA CELULA.
	///
	/// TODAS, e nao so as de `Blasts`: arte de raio numa bola acontece (o teleguiado customizado
	/// escolhe em `Techniques`, onde moram os Kamehamehas) e sai como a CABECA daquele raio voando
	/// sozinha -- ver `ArteDeKiNoCliente.EstiloDaBola`. E um ramo de producao como outro qualquer.
	/// </summary>
	private void PorTodasAsBolas()
	{
		LimparOPalco();

		ArteDeKi[] pequenas = [.. ArteDeProjetil.Todas.Where(a => !EhEsferaGrande(a))];
		const int Colunas = 8;
		int linhas = (pequenas.Length + Colunas - 1) / Colunas;
		Vector2 t = Tela;
		float cw = t.X / Colunas, ch = t.Y / linhas;

		for (int i = 0; i < pequenas.Length; i++)
		{
			var centro = new Vector2(cw * (i % Colunas) + cw * 0.5f, ch * (i / Colunas) + ch * 0.5f);
			NovaBola(pequenas[i], 1f, centro, Vermelho, pequenas[i].ToString());
		}
		Assentar();
	}

	/// <summary>O raio que o ESTILO manda esta bola ter, ja na escala dela.</summary>
	private static float RaioDoEstilo(TiroNoPalco b) => ArteDeKiNoCliente.EstiloDaBola(b.Arte).Raio * b.Escala;

	/// <summary>
	/// A TINTA EM VOLTA DE UM PONTO, contada so no quadrado onde o CORPO de uma bola deste raio caberia
	/// (1,25 raio pra cada lado, mais 2 px) e dividida pelo raio ao quadrado -- pra que a bolinha de
	/// 3,5 px e a de 29 respondam na mesma unidade.
	///
	/// So o corpo, e nao a celula inteira, de proposito: o rastro e o halo de uma bola grande vazam pra
	/// celula da vizinha, e contar a celula deixaria uma arte que nao desenha NADA passar com a tinta
	/// do lado.
	///
	/// O NUMERO NAO E A AREA DA BOLA: o halo conta como tinta, e numa bola cheia ele enche o quadrado
	/// inteiro (de 6 a 14 raios^2, conforme o tamanho). O que ele separa e "ha um desenho aqui" de "nao
	/// ha nada" -- que e a pergunta desta familia.
	/// </summary>
	private static float TintaPorRaio2(Chapa c, Vector2 centro, float raio)
	{
		float px2 = TintaNoQuadro(c, centro, raio * 1.25f + 2f) / (c.PxPorMundo * c.PxPorMundo);
		return px2 / (raio * raio);
	}

	private void MedirTodasAsBolas()
	{
		Chapa? c = Foto("2b-1-todas-as-bolas");
		if (c == null) return;

		float menor = float.MaxValue;
		string qualMenor = "";
		var partes = new List<string>();
		foreach (TiroNoPalco b in _palco)
		{
			float tinta = TintaPorRaio2(c, b.Cabeca, RaioDoEstilo(b));
			partes.Add($"{b.Rotulo} {tinta:0.0}");
			if (tinta < menor) { menor = tinta; qualMenor = b.Rotulo; }
		}
		Nota("tinta (corpo e halo) no quadrado do corpo de cada bola, em raios ao quadrado -- " + string.Join("  ", partes));
		Ok($"as {_palco.Count} artes pequenas DESENHAM como bola (a mais rala: {qualMenor}, {menor:0.0} raios^2 de tinta)",
		   _palco.Count > 0 && menor >= PisoDeTintaDaBola);

		// CONTRA-EXEMPLO: a MESMA regua no canto de baixo da foto, onde a grade nao pos ninguem, com o
		// raio de uma bola de verdade. E o que uma arte que nao desenhasse nada daria -- e tem que reprovar.
		float nada = TintaPorRaio2(c, new Vector2(Tela.X - 14f, Tela.Y - 14f), RaioDoEstilo(_palco[0]));
		Ok($"CONTRA-EXEMPLO: a mesma regua num canto vazio da foto da {nada:0.0} -- ela reprovaria uma arte que nao desenha",
		   nada < PisoDeTintaDaBola);
	}

	/// <summary>
	/// O MINIMO DE TINTA NO QUADRADO DO CORPO DE UMA BOLA, em raios ao quadrado.
	///
	/// MEDIDO em 100 rodadas: a mais rala das 42 e a faisca do Genie (`Blast19`, um punhado de brilhos
	/// sorteados), de 4,0 a 5,2; depois a `KiHead` (4,5 a 6,0), o redemoinho da Spirit Gun (4,7 a 5,1) e
	/// a meia-lua do Kiai (5,3 a 5,5). O piso e a metade da menor leitura -- e o canto vazio da zero.
	/// </summary>
	private const float PisoDeTintaDaBola = 2f;

	/// <summary>
	/// AS QUE O DONO CITOU PELO NOME -- *"esferas como genkidama, super nova"*: a Genkidama a escala 1,
	/// e a Death Ball a 1 e a 2. Sozinhas na foto porque a Genkidama tem 96 px de raio e meio quadro de
	/// halo; na grade ela cobriria seis vizinhas.
	/// </summary>
	private void PorAsEsferasGrandes()
	{
		LimparOPalco();
		Vector2 t = Tela;
		NovaBola(ArteDeKi.SpiritBomb, 1f, new Vector2(t.X * 0.27f, t.Y * 0.5f), Vermelho, "Genkidama");
		NovaBola(ArteDeKi.DeathBall2017Purple2, 2f, new Vector2(t.X * 0.66f, t.Y * 0.5f), Vermelho, "Death Ball a 2x");
		NovaBola(ArteDeKi.DeathBall2017Purple2, 1f, new Vector2(t.X * 0.90f, t.Y * 0.5f), Vermelho, "Death Ball a 1x");
		Assentar();
	}

	private void MedirAsEsferasGrandes()
	{
		Chapa? c = Foto("2b-2-as-esferas-grandes");
		if (c == null) return;

		// O DIAMETRO, e nao a area do quadrado do corpo: com o halo, o quadrado de uma esfera destas esta
		// CHEIO de tinta, e duas areas cheias so repetem o tamanho dos dois quadrados. A corrida de tinta
		// que atravessa o centro para onde a tinta para.
		float[] diametro = new float[_palco.Count];
		for (int i = 0; i < _palco.Count; i++)
		{
			TiroNoPalco b = _palco[i];
			diametro[i] = CorridaDeTinta(c, b.Cabeca, Vector2.Right);
			Nota($"{b.Rotulo}: {diametro[i]:0} px de tinta atravessando o centro "
				 + $"(raio do estilo x escala: {RaioDoEstilo(b):0} px)");
		}

		Ok("a Genkidama e a Death Ball DESENHAM (as duas voaram no neutro ate 2026-10-07)",
		   _palco.All(b => TintaPorRaio2(c, b.Cabeca, RaioDoEstilo(b)) >= PisoDeTintaDaBola));

		// `_raioDaBola = estilo.Raio * Escala`: e o que faz a Genkidama CRESCER a cada pulso
		// (`SpiritBomb.dm:119-121` soma 0,1 na escala). O corpo dobra com a escala; o halo nao (o
		// alcance dele e `5 + 0,9 raio`), entao a tinta inteira cresce um pouco menos que 2x. Medido em
		// 84 rodadas: de 105 a 115 px a 1x e de 204 a 224 a 2x -- entre 1,8 e 2,1 vezes.
		Ok($"a ESCALA engorda a bola: a Death Ball a 2x tem {diametro[1]:0} px contra {diametro[2]:0} da de 1x "
		   + $"({diametro[1] / MathF.Max(1f, diametro[2]):0.00}x)", diametro[1] > diametro[2] * VezesADeathBallCresce);

		// 96 px de raio contra 36: e a maior coisa que voa no jogo. Medido: de 316 a 342 px de tinta,
		// 2,8 a 3,3 vezes a Death Ball.
		Ok($"a Genkidama e a MAIOR: {diametro[0]:0} px contra {diametro[2]:0} da Death Ball "
		   + $"({diametro[0] / MathF.Max(1f, diametro[2]):0.00}x)", diametro[0] > diametro[2] * 2f);
	}

	/// <summary>
	/// QUANTO A TINTA DA DEATH BALL TEM QUE CRESCER de 1x pra 2x: uma vez e meia, com o medido entre
	/// 1,8 e 2,1 (ver <see cref="MedirAsEsferasGrandes"/>). Uma escala que nao chegasse ao desenho daria 1,0.
	/// </summary>
	private const float VezesADeathBallCresce = 1.5f;

	/// <summary>
	/// A CORRIDA DE TINTA QUE ATRAVESSA UM PONTO, numa direcao e na oposta, em px de mundo. Zero se o
	/// proprio ponto for fundo.
	/// </summary>
	private static float CorridaDeTinta(Chapa c, Vector2 pontoNoMundo, Vector2 direcao)
	{
		Vector2 p0 = c.DoMundo * pontoNoMundo;
		int n = 0;
		foreach (int sentido in new[] { 1, -1 })
			for (int l = sentido > 0 ? 0 : 1; l < 4000; l++)
			{
				Vector2 p = p0 + direcao * (l * sentido);
				if (!c.Tinta(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y))) break;
				n++;
			}
		return n / c.PxPorMundo;
	}

	// =====================================================================
	// FAMILIA 3 -- O PIXEL
	// =====================================================================
	/// <summary>Quantas vagas a vitrine tem: quatro tiros lado a lado, cada um no meio do seu quarto de tela.</summary>
	private const int Vagas = 4;

	private Vector2 CentroDaVaga(int i) =>
		new(MathF.Floor(Tela.X * (0.5f + i) / Vagas), MathF.Floor(Tela.Y * 0.5f));

	/// <summary>
	/// QUATRO RAIOS VERTICAIS LADO A LADO, e em toda vitrine alguma coisa aparece DUAS VEZES.
	///
	/// O par repetido e o CONTROLE. Dois tiros iguais nao tem os mesmos pixels (cada material sorteia a
	/// sua `semente`), entao "sao diferentes" so quer dizer alguma coisa se for MAIS diferente do que
	/// um tiro e do seu gemeo -- e essa distancia e medida aqui, na mesma foto, e nao so escrita.
	///
	/// CABECA EM CIMA, CAUDA EMBAIXO, seis tiles de tronco: um raio de comprimento zero desenharia so
	/// as duas bolas, e duas artes concordariam por nao terem tronco pra discordar.
	/// </summary>
	private void PorAVitrine(params (ArteDeKi Arte, float Escala, Color Cor)[] vagas)
	{
		LimparOPalco();
		for (int i = 0; i < vagas.Length; i++)
		{
			Vector2 c = CentroDaVaga(i);
			NovoRaio(vagas[i].Arte, vagas[i].Escala, c + new Vector2(0, -96), c + new Vector2(0, 96),
					 vagas[i].Cor, vagas[i].Arte.ToString());
		}
		Assentar();
	}

	/// <summary>
	/// O RETRATO DE UMA VAGA: quantos pixels de tinta, ONDE eles estao (a mascara) e de que cores sao
	/// (um histograma grosso, quatro niveis por canal).
	/// </summary>
	private sealed record Retrato(int Pixels, bool[] Mascara, float[] Cores);

	private Retrato Retratar(Chapa c, int vaga)
	{
		// A JANELA TEM O MESMO TAMANHO NAS QUATRO VAGAS, em pixel inteiro: as mascaras sao comparadas
		// posicao a posicao, e uma janela um pixel mais larga que a outra desalinharia todas as linhas.
		int w = Mathf.FloorToInt(Tela.X / Vagas * c.PxPorMundo), h = c.Altura;
		int x0 = c.Pixel(CentroDaVaga(vaga)).X - w / 2;

		var mascara = new bool[w * h];
		var cores = new float[64];
		int n = 0;
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				if (!c.Tinta(x0 + x, y)) continue;
				mascara[y * w + x] = true;
				(byte r, byte g, byte b) = c.Cor(x0 + x, y);
				cores[((r >> 6) << 4) | ((g >> 6) << 2) | (b >> 6)]++;
				n++;
			}
		if (n > 0)
			for (int i = 0; i < cores.Length; i++) cores[i] /= n;
		return new Retrato(n, mascara, cores);
	}

	/// <summary>
	/// A DISTANCIA DE SILHUETA: dos pixels que pelo menos um dos dois pintou, que fracao so UM pintou.
	/// Zero = a mesma forma no mesmo lugar; um = nenhum pixel em comum.
	/// </summary>
	private static float Silhueta(Retrato a, Retrato b)
	{
		int ambos = 0, algum = 0;
		for (int i = 0; i < a.Mascara.Length; i++)
		{
			if (a.Mascara[i] && b.Mascara[i]) ambos++;
			if (a.Mascara[i] || b.Mascara[i]) algum++;
		}
		return algum == 0 ? 0f : 1f - (float)ambos / algum;
	}

	/// <summary>A DISTANCIA DE PALETA: zero = as mesmas cores nas mesmas proporcoes; um = nenhuma cor em comum.</summary>
	private static float Paleta(Retrato a, Retrato b)
	{
		float soma = 0f;
		for (int i = 0; i < a.Cores.Length; i++) soma += MathF.Abs(a.Cores[i] - b.Cores[i]);
		return soma * 0.5f;
	}

	/// <summary>
	/// ============================ QUANDO DUAS FOTOS SAO "DIFERENTES" ============================
	/// Dois tiros da MESMA arte nunca tem os mesmos pixels: cada material sorteia a propria `semente`
	/// e o shader anima. Em 100 rodadas desta bancada (2026-10-07, 1280x720):
	///
	///     a MESMA arte, duas vezes     silhueta ate 0,082 (raios) e 0,093 (bolas)   paleta ate 0,098
	///     artes DIFERENTES             silhueta de 0,183 pra cima                   paleta de 0,361 pra cima
	///
	/// Entao "diferente" tem dois pisos, e a distancia tem que passar dos DOIS:
	///
	///   * o FIXO (<see cref="PisoDaSilhueta"/>, <see cref="PisoDaPaleta"/>): o meio do vao entre os
	///     dois grupos da tabela acima (0,13 entre 0,093 e 0,183; 0,20 entre 0,098 e 0,361). E ele que
	///     da forca a regua: numa rodada em que o par de controle sorteia sementes vizinhas (aconteceu
	///     4 vezes nas 100, com 0,000 a 0,007 de distancia) QUALQUER coisa seria "muitas vezes o
	///     controle";
	///   * o VIVO: uma vez e meia o controle medido NA MESMA foto. Se uma maquina desenhar com mais
	///     ruido que a que calibrou o piso fixo, a barra sobe junto em vez de a bancada mentir.
	/// ============================================================================================
	/// </summary>
	private const float PisoDaSilhueta = 0.13f;

	/// <summary>Ver <see cref="PisoDaSilhueta"/>.</summary>
	private const float PisoDaPaleta = 0.20f;

	/// <summary>Ver <see cref="PisoDaSilhueta"/>.</summary>
	private const float VezesOControle = 1.5f;

	private static bool Difere(float distancia, float controle, float piso) =>
		distancia > MathF.Max(piso, controle * VezesOControle);

	private void FotografarDuasArtes()
	{
		Chapa? c = Foto("3-1-duas-artes");
		if (c == null) return;

		Retrato k1 = Retratar(c, 0), k1b = Retratar(c, 1), mas = Retratar(c, 2), masb = Retratar(c, 3);
		Nota($"Kamehameha1: {k1.Pixels} px e {k1b.Pixels} px de tinta   Masenko: {mas.Pixels} px e {masb.Pixels} px");

		Ok("o Kamehameha DESENHOU alguma coisa (o shader chegou na tela)", k1.Pixels > 100 && k1b.Pixels > 100);
		Ok("o Masenko DESENHOU alguma coisa", mas.Pixels > 100 && masb.Pixels > 100);

		float ctrlSilhueta = MathF.Max(Silhueta(k1, k1b), Silhueta(mas, masb));
		float ctrlPaleta = MathF.Max(Paleta(k1, k1b), Paleta(mas, masb));
		float silhueta = Silhueta(k1, mas), paleta = Paleta(k1, mas);
		Nota($"CONTROLE (a mesma arte, duas vezes): silhueta {Silhueta(k1, k1b):0.000} e {Silhueta(mas, masb):0.000}, "
			 + $"paleta {Paleta(k1, k1b):0.000} e {Paleta(mas, masb):0.000}");
		Nota($"Kamehameha x Masenko: silhueta {silhueta:0.000}, paleta {paleta:0.000}");

		Ok($"CONTROLE: a mesma arte, duas vezes, fica ABAIXO dos dois pisos (silhueta {ctrlSilhueta:0.000} < "
		   + $"{PisoDaSilhueta:0.00}, paleta {ctrlPaleta:0.000} < {PisoDaPaleta:0.00})",
		   ctrlSilhueta < PisoDaSilhueta && ctrlPaleta < PisoDaPaleta);

		// ============================ A LINHA QUE RESPONDE O PEDIDO DE VARIEDADE ============================
		// Duas tecnicas, dois estilos, dois desenhos DIFERENTES -- na forma e na cor, cada uma contra o
		// seu controle. Um `EstiloDoFeixe` que devolvesse o mesmo estilo pra toda arte passaria na
		// familia 2 inteira e reprovaria aqui: a distancia entre as duas cairia pra a do controle.
		// ==================================================================================================
		Ok($"Kamehameha e Masenko tem SILHUETAS diferentes ({silhueta:0.000}, acima dos dois pisos)",
		   Difere(silhueta, ctrlSilhueta, PisoDaSilhueta));
		Ok($"...e PALETAS diferentes ({paleta:0.000})", Difere(paleta, ctrlPaleta, PisoDaPaleta));
	}

	private void FotografarTinta()
	{
		Chapa? c = Foto("3-2-tinta-azul");
		if (c == null) return;

		Retrato azul = Retratar(c, 0), vermelho = Retratar(c, 1);
		float paleta = Paleta(azul, vermelho), silhueta = Silhueta(azul, vermelho);
		float controle = Paleta(Retratar(c, 2), Retratar(c, 3));   // os dois Masenkos, NESTA foto
		Nota($"Kamehameha1 azul x vermelho: paleta {paleta:0.000} (controle nesta foto {controle:0.000}), silhueta {silhueta:0.000}");

		// O `icon += rgb(...)` do DM chegando ao pixel. Sem isto o ki de todo mundo teria a cor crua
		// da arte.
		Ok($"trocar a cor do ki muda o PIXEL: a paleta anda {paleta:0.000}", Difere(paleta, controle, PisoDaPaleta));

		// ARTE E COR SAO ORTOGONAIS (o cabecalho do `ArteDeProjetil`): a tinta troca a cor do tiro e
		// nao o tiro. Uma troca de cor que refizesse o material com outra arte passaria na linha de
		// cima -- e reprovaria aqui, porque a silhueta dos dois pularia pra cima do piso.
		Ok($"...e NAO muda a silhueta: e o mesmo desenho em outra cor ({silhueta:0.000}, abaixo do piso de {PisoDaSilhueta:0.00})",
		   silhueta < PisoDaSilhueta);

		// E o DESENHO tem que sobreviver a tinta: somar clareia, mas nao pode achatar casca, manto e
		// nucleo num tom so. A medida e no MIOLO do raio (longe da beirada, onde o anti-serrilhado
		// fabrica tons de graca), e a contraprova e a mesma regua no fundo liso.
		TiroNoPalco t = _palco[0];
		Miolo miolo = LerOMiolo(c, t, 0f);
		Miolo liso = LerOMiolo(c, t, 60f);
		Nota($"miolo do Kamehameha azul: {miolo.Pixels} px, {miolo.Tons} tons   fundo ao lado: {liso.Pixels} px, {liso.Tons} tom");
		Ok($"o desenho sobreviveu a tinta ({miolo.Tons} tons no miolo, mais de um)", miolo.Pixels > 50 && miolo.Tons > 1);
		Ok($"CONTRA-EXEMPLO: a mesma regua no fundo liso conta UM tom ({liso.Tons})", liso.Pixels > 50 && liso.Tons == 1);
	}

	private void FotografarEscala()
	{
		Chapa? c = Foto("3-3-escala-3x");
		if (c == null) return;

		int[] tinta = [.. Enumerable.Range(0, Vagas).Select(i => Retratar(c, i).Pixels)];
		Nota($"Masenko a 1x: {tinta[0]} px e {tinta[1]} px de tinta   a 3x: {tinta[2]} px e {tinta[3]} px");

		// O PIOR CASO DOS DOIS LADOS: o menor dos de 3x contra o maior dos de 1x. O comprimento e o
		// mesmo nos quatro, entao a tinta cresce com a LARGURA -- perto de 3 vezes, e nao de 9.
		float razao = (float)Math.Min(tinta[2], tinta[3]) / Math.Max(1, Math.Max(tinta[0], tinta[1]));
		float controle = (float)tinta[0] / Math.Max(1, tinta[1]);

		// `A.transform *= wavemult` (`beams.dm:149`). O `wavemult` do Final Flash e 4 e o do Ki Wave
		// e 1 -- sem esta linha os dois sairiam do mesmo tamanho. Medido: 2,8 a 2,9 em 30 rodadas, e
		// o par de 1x entre 0,94 e 1,04.
		Ok($"a escala do `wavemult` chega ao pixel (3x desenha {razao:0.0} vezes a tinta de 1x)", razao > 2f);
		Ok($"CONTROLE: na MESMA escala os dois Masenkos tem a mesma tinta (razao {controle:0.00})",
		   controle is > 0.8f and < 1.25f);
	}

	/// <summary>A cor que o jogo daria ao tiro deste personagem. Ver `Appearance.CorDoTiro`.</summary>
	private static Color CorDeTeste(string nome)
	{
		Jandirus.Core.Appearance.Rgb c = Jandirus.Core.Appearance.CorDoTiro.De(nome, 1700);
		return new Color(c.R / 255f, c.G / 255f, c.B / 255f);
	}

	/// <summary>A cor da CHAMA do mesmo personagem -- a que NAO serve pra tiro. Ver <see cref="PorAsCoresDePersonagem"/>.</summary>
	private static Color CorDaChama(string nome)
	{
		Jandirus.Core.Appearance.Rgb c = Jandirus.Core.Appearance.CorDeAura.De(nome, 1700);
		return new Color(c.R / 255f, c.G / 255f, c.B / 255f);
	}

	/// <summary>
	/// ============================ A COR DE PERSONAGENS DE VERDADE ============================
	/// Ate aqui a tinta foi uma cor de teste escolhida a mao. Esta fase usa a cor que o JOGO daria a
	/// dois personagens (`CorDoTiro.De`, o `blastR/G/B = rand(0,255)` do `CharacterCreation.dm:28`)
	/// sobre o estilo de raio PADRAO do jogo (o `Beam3`, tres cinzas).
	///
	/// E ela tem historia: o cliente lia a cor da CHAMA (`Appearance.CorAura`), que neste port carrega
	/// um `200 +` exigido pelo shader da aura. Somada a um cinza, ela estoura os tres canais --
	/// **o jogo inteiro atirava branco**, e todas as comparacoes entre fotos ficavam VERDES assim mesmo,
	/// porque duas fotos brancas diferentes ainda sao diferentes.
	///
	/// A TERCEIRA VAGA E AQUELE DEFEITO, DE PROPOSITO: o mesmo raio tingido com a cor da chama do Goku.
	/// A regua que aprova as duas primeiras tem que reprovar essa.
	/// ====================================================================================
	/// </summary>
	private void PorAsCoresDePersonagem() =>
		PorAVitrine((ArteDeKi.Beam3, 1f, CorDeTeste("Goku")), (ArteDeKi.Beam3, 1f, CorDeTeste("Vegeta")),
					(ArteDeKi.Beam3, 1f, CorDaChama("Goku")), (ArteDeKi.Beam3, 1f, CorDeTeste("Goku")));

	private void FotografarCoresDeVerdade()
	{
		Chapa? c = Foto("3-4-cores-de-personagem");
		if (c == null) return;

		Miolo goku = LerOMiolo(c, _palco[0], 0f), vegeta = LerOMiolo(c, _palco[1], 0f), chama = LerOMiolo(c, _palco[2], 0f);
		Nota($"Goku   (#{CorDeTeste("Goku").ToHtml(false)}): {goku.Pixels} px de miolo, {goku.Brancos} brancos puros ({Fracao(goku):0%})");
		Nota($"Vegeta (#{CorDeTeste("Vegeta").ToHtml(false)}): {vegeta.Pixels} px de miolo, {vegeta.Brancos} brancos puros ({Fracao(vegeta):0%})");
		Nota($"a cor da CHAMA do Goku (#{CorDaChama("Goku").ToHtml(false)}): {chama.Pixels} px, {chama.Brancos} brancos puros ({Fracao(chama):0%})");

		Ok($"a cor do tiro NAO satura tudo em branco (Goku: {Fracao(goku):0%} do miolo)",
		   goku.Pixels > 100 && Fracao(goku) < TetoDeBranco);
		Ok($"a cor do tiro NAO satura tudo em branco (Vegeta: {Fracao(vegeta):0%} do miolo)",
		   vegeta.Pixels > 100 && Fracao(vegeta) < TetoDeBranco);
		Ok($"CONTRA-EXEMPLO: com a cor da CHAMA no lugar da do tiro a regua REPROVA ({Fracao(chama):0%} de branco)",
		   chama.Pixels > 100 && Fracao(chama) >= TetoDeBranco);

		// DOIS PERSONAGENS, O MESMO ESTILO, CORES DIFERENTES. E o `rand(0,255)` do DM chegando na tela:
		// o Ki Wave de um nao se parece com o do outro -- e o controle e o proprio Goku, duas vezes.
		float paleta = Paleta(Retratar(c, 0), Retratar(c, 1)), controle = Paleta(Retratar(c, 0), Retratar(c, 3));
		Nota($"Goku x Vegeta: paleta {paleta:0.000}   Goku x Goku: {controle:0.000}");
		Ok($"dois personagens atiram o MESMO estilo em cores diferentes (paleta {paleta:0.000})",
		   Difere(paleta, controle, PisoDaPaleta));
	}

	private static float Fracao(Miolo m) => m.Pixels == 0 ? 0f : (float)m.Brancos / m.Pixels;

	/// <summary>
	/// QUE FRACAO DO MIOLO PODE SER BRANCO PURO antes de a cor do personagem ter sumido: metade, como
	/// na regua antiga ("mais da metade tem que sobreviver").
	///
	/// MEDIDO em 100 rodadas: 0% no Goku (#8736fe) e 0% no Vegeta (#7fc613) -- com essas tintas nem o
	/// nucleo do `Beam3` chega ao branco --, e de 74% a 94% com a cor da chama (#ffffff). O que falta
	/// pra 100% naquela e a textura: os riscos de fluxo escurecem o manto ate 0,80.
	///
	/// O teto e generoso de proposito: uma tinta de sorteio alto nos tres canais embranquece o nucleo
	/// de verdade (o BYOND tambem satura as vezes, e isso e fidelidade e nao defeito), e o nucleo e
	/// mais da metade do miolo. O que ele reprova e o raio BRANCO DE PONTA A PONTA.
	/// </summary>
	private const float TetoDeBranco = 0.5f;

	/// <summary>O que se leu do miolo de um raio: quantos pixels, quantos brancos puros, quantos tons de verdade.</summary>
	private readonly record struct Miolo(int Pixels, int Brancos, int Tons);

	/// <summary>
	/// LE O MIOLO DE UM RAIO: a faixa em volta do eixo, da mao a ponta, um pixel e meio pra DENTRO da
	/// beirada do tronco macico. Ali so ha corpo -- nem halo, nem anti-serrilhado, nem fundo misturado.
	///
	/// UM TOM "DE VERDADE" e uma cor (dezesseis niveis por canal) com pelo menos 3% dos pixels: o
	/// degrade entre duas faixas fabrica dezenas de cores com um punhado de pixels cada, e contar
	/// qualquer uma delas faria um raio chapado parecer cheio de relevo.
	/// </summary>
	/// <param name="deLado">Desloca a faixa inteira de lado, em px de mundo. So a contraprova usa: le o fundo.</param>
	private static Miolo LerOMiolo(Chapa c, TiroNoPalco t, float deLado)
	{
		float macico = ArteDeProjetil.MeiaEspessuraDoTronco(t.Arte) * ArteDeKiNoCliente.EstiloDoFeixe(t.Arte).Macico * t.Escala;
		var perpMundo = new Vector2(-t.Rumo.Y, t.Rumo.X);
		Vector2 a = c.DoMundo * (t.Mao + t.Rumo * 3f + perpMundo * deLado);
		Vector2 b = c.DoMundo * (t.Ponta - t.Rumo * 3f + perpMundo * deLado);
		Vector2 d = b - a;
		float comp = d.Length();
		if (comp < 1f) return default;

		Vector2 u = d / comp;
		var perp = new Vector2(-u.Y, u.X);
		int meia = Mathf.Max(0, Mathf.FloorToInt((macico - 1.5f) * c.PxPorMundo));

		var tons = new int[4096];
		int n = 0, brancos = 0;
		for (int s = 0; s <= (int)comp; s++)
			for (int l = -meia; l <= meia; l++)
			{
				Vector2 p = a + u * s + perp * l;
				int px = Mathf.FloorToInt(p.X), py = Mathf.FloorToInt(p.Y);
				if (!c.Dentro(px, py)) continue;
				(byte r, byte g, byte bl) = c.Cor(px, py);
				n++;
				if (r > 250 && g > 250 && bl > 250) brancos++;
				tons[((r >> 4) << 8) | ((g >> 4) << 4) | (bl >> 4)]++;
			}

		int piso = Math.Max(1, n * 3 / 100);
		return new Miolo(n, brancos, tons.Count(q => q >= piso));
	}

	/// <summary>
	/// ============================ AS BOLAS ============================
	/// A esquerda, a bola RACIAL padrao (o `1.dmi`: uma bolinha com rastro de cometa); a direita, a
	/// `KiHead` da Paralysis (um estalo de faiscas e descarga). No desenho de folhas a `KiHead` era a
	/// armadilha -- a unica folha sem estado `default` --, e ja falhou uma vez nesta bancada desenhando
	/// por primitiva. Hoje a armadilha e outra: `forma` viaja como um INTEIRO do C# pro shader, e os
	/// numeros do enum `Forma` tem que ser os das constantes do `EsferaDeKi.gdshader`. Duas formas
	/// diferentes que saissem iguais na foto e exatamente o que essa troca daria.
	/// ==================================================================
	/// </summary>
	private void PorAsBolasDaVitrine()
	{
		LimparOPalco();
		Color cor = CorDeTeste("Goku");
		ArteDeKi[] artes = [ArteDeKi.Blast1, ArteDeKi.Blast1, ArteDeKi.KiHead, ArteDeKi.KiHead];
		for (int i = 0; i < Vagas; i++) NovaBola(artes[i], 1f, CentroDaVaga(i), cor, artes[i].ToString());
		Assentar();
	}

	private void FotografarBolas()
	{
		Chapa? c = Foto("3-5-bolas");
		if (c == null) return;

		Retrato racial = Retratar(c, 0), racialB = Retratar(c, 1), paralisia = Retratar(c, 2), paralisiaB = Retratar(c, 3);
		Nota($"bola racial (1.dmi): {racial.Pixels} px e {racialB.Pixels} px   Paralysis (KiHead): {paralisia.Pixels} px e {paralisiaB.Pixels} px");

		Ok("a bola RACIAL desenha", racial.Pixels > 20 && racialB.Pixels > 20);
		Ok("a Paralysis desenha", paralisia.Pixels > 20 && paralisiaB.Pixels > 20);

		// O CONTROLE E O PAR DA BOLA RACIAL, e nao o da Paralysis: a faisca e sorteada de novo dez vezes
		// por segundo e cada material tem a sua semente, entao duas Paralysis nunca tem a mesma
		// silhueta -- e isso e o estilo, nao ruido da medida.
		float ctrlSilhueta = Silhueta(racial, racialB), ctrlPaleta = Paleta(racial, racialB);
		float silhueta = Silhueta(racial, paralisia), paleta = Paleta(racial, paralisia);
		Nota($"racial x racial: silhueta {ctrlSilhueta:0.000}, paleta {ctrlPaleta:0.000}   "
			 + $"Paralysis x Paralysis: silhueta {Silhueta(paralisia, paralisiaB):0.000}, paleta {Paleta(paralisia, paralisiaB):0.000}");
		Nota($"racial x Paralysis: silhueta {silhueta:0.000}, paleta {paleta:0.000}");
		Ok($"CONTROLE: a mesma bola, duas vezes, fica abaixo dos dois pisos (silhueta {ctrlSilhueta:0.000}, paleta {ctrlPaleta:0.000})",
		   ctrlSilhueta < PisoDaSilhueta && ctrlPaleta < PisoDaPaleta);
		Ok($"as duas bolas sao desenhos diferentes: silhueta {silhueta:0.000} e paleta {paleta:0.000}",
		   Difere(silhueta, ctrlSilhueta, PisoDaSilhueta) && Difere(paleta, ctrlPaleta, PisoDaPaleta));
	}

	// =====================================================================
	// A SONDA DE CONTINUIDADE
	// =====================================================================
	/// <summary>Meia largura da FAIXA: 20 px pra cada lado do eixo. Ver <see cref="Varrer"/>.</summary>
	private const float MeiaFaixa = 20f;

	/// <summary>
	/// Meia largura da LINHA CENTRAL. Nao e zero, e nao pode ser: o eixo de um feixe torto cai entre
	/// pixels, e a geometria e arredondada pro pixel na hora de desenhar (`snap_2d_vertices_to_pixel`).
	/// Dois pixels ficam bem dentro do tronco do `Beam3` (5 de meia espessura), que e o raio de todas as
	/// familias de rumo e de comprimento: ali ela e uma sonda do MIOLO, que nao alcanca beirada nenhuma.
	/// Nas artes mais finas (o Boom Wave pulsa ate 1,3 px) ela ainda exige tinta COLADA ao eixo.
	/// </summary>
	private const float MeiaLinha = 2f;

	/// <summary>Quanto a sonda comeca DEPOIS da mao, em px: a beirada da bola da boca e anti-serrilhada.</summary>
	private const float MargemDaMao = 2f;

	/// <summary>
	/// QUANTO A SONDA PARA ANTES DA PONTA DA REGRA. Dois pixels pra beirada anti-serrilhada -- e mais
	/// um quinto do raio da cabeca nas artes de COROA (so o Static Beam): a cabeca dele e um estouro de
	/// espinhos que pisca, e o espinho do eixo tem de 0,83 a 1,35 raios (`FeixeDeKi.gdshader`). No
	/// quadro em que ele sai curto, a tinta acaba 2,6 px antes da ponta -- e isso e o estilo, nao um
	/// buraco. A familia 6 mede essa ponta com a folga declarada.
	/// </summary>
	private static float MargemDaPonta(TiroNoPalco t)
	{
		ArteDeKiNoCliente.EstiloDeFeixe e = ArteDeKiNoCliente.EstiloDoFeixe(t.Arte);
		return 2f + 0.2f * e.Cabeca * e.Coroa * t.Escala;
	}

	/// <summary>Uma fresta medida: a maior corrida, o total de passos vazios, onde a pior comeca e quantas amostras cairam fora da foto.</summary>
	private readonly record struct Fresta(int Maior, int Total, int Onde, int Fora);

	/// <summary>
	/// ANDA NO EIXO DO FEIXE E CONTA O FUNDO -- de `de` ate `ate`, um pixel de foto por passo.
	///
	/// ============================ DUAS LARGURAS, PORQUE SAO DUAS PERGUNTAS ============================
	/// Com <see cref="MeiaFaixa"/> a pergunta e *"o raio se interrompe?"*: 20 px pra cada lado cobrem o
	/// corpo de um raio comum inteiro, entao um passo sem tinta nenhuma na faixa e um pedaco que faltou
	/// -- e literalmente o buraco que o dono fotografou.
	///
	/// Com <see cref="MeiaLinha"/> a pergunta e a que o dono fez com essas palavras: *"varra a linha
	/// central do feixe e conte pixels transparentes entre o punho e a ponta"*. E a mais dura das duas,
	/// e ela pega o que a faixa nao pega: um feixe que SAI do proprio eixo (o `SemGirar`) continua com
	/// tinta por perto durante dezenas de pixels -- e ja nao tem miolo nenhum onde devia.
	/// ==============================================================================================
	///
	/// O `Total` existe pelo mesmo motivo que a `Maior`: um raio com dez furos de 1 px nao tem "maior
	/// fresta" nenhuma que impressione, e ainda assim esta picotado dez vezes.
	/// </summary>
	private static Fresta Varrer(Chapa c, Vector2 de, Vector2 ate, float meia)
	{
		Vector2 a = c.DoMundo * de, b = c.DoMundo * ate;
		Vector2 d = b - a;
		float comp = d.Length();
		if (comp < 1f) return new Fresta(0, 0, 0, 0);

		Vector2 u = d / comp;
		var perp = new Vector2(-u.Y, u.X);
		int m = Mathf.RoundToInt(meia * c.PxPorMundo);

		int maior = 0, corrida = 0, ondeMaior = 0, total = 0, fora = 0;
		for (int s = 0; s <= (int)comp; s++)
		{
			bool temTinta = false;
			for (int l = -m; l <= m && !temTinta; l++)
			{
				Vector2 p = a + u * s + perp * l;
				int px = Mathf.FloorToInt(p.X), py = Mathf.FloorToInt(p.Y);
				if (!c.Dentro(px, py)) { fora++; continue; }
				if (c.Tinta(px, py)) temTinta = true;
			}
			if (temTinta) { corrida = 0; continue; }

			total++;
			if (++corrida > maior) { maior = corrida; ondeMaior = s - corrida + 1; }
		}

		return new Fresta(maior, total, ondeMaior, fora);
	}

	/// <summary>As duas sondas num tiro do palco, DA MAO ATE A PONTA que a regra manda.</summary>
	private static (Fresta Faixa, Fresta Centro) Sondar(Chapa c, TiroNoPalco t)
	{
		Vector2 de = t.Mao + t.Rumo * MargemDaMao, ate = t.Ponta - t.Rumo * MargemDaPonta(t);
		return (Varrer(c, de, ate, MeiaFaixa), Varrer(c, de, ate, MeiaLinha));
	}

	/// <summary>
	/// EM QUANTOS PEDACOS A TINTA DA FOTO ESTA: as ilhas de pixels de tinta que se tocam (de lado ou
	/// de quina).
	///
	/// ============================ A SEGUNDA REGUA, E ELA NAO SABE ONDE O RAIO DEVIA ESTAR ============================
	/// A sonda do eixo pergunta *"ha tinta ONDE a regra manda?"*, e pra isso precisa da regra: de onde
	/// a mao sai, onde a ponta cai. Esta pergunta outra coisa -- *"a tinta e UMA PECA so?"* -- e nao
	/// precisa de nada: um raio inteiro e uma ilha, em qualquer rumo, reto ou CURVO; um raio picotado
	/// sao varias. As duas juntas separam os dois defeitos que a familia 5d injeta: o `Picotado` parte
	/// a ilha (e fura o eixo), o `SemGirar` tira o raio do eixo sem parti-lo.
	///
	/// E e a unica medida desta bancada que alcanca a CURVA (*"poder ate dar curva"*, pediu o dono):
	/// o `SemGirar` tira a mao do eixo, com a mao fora do eixo o desenho de producao sai pelo
	/// `PintorDeKi.FitaCurva`, e uma fita curva mal emendada abriria justamente em ilhas.
	/// ================================================================================================================
	/// </summary>
	private static int ContarIlhas(Chapa c)
	{
		int w = c.Largura, h = c.Altura;
		var vista = new bool[w * h];
		var pilha = new Stack<int>();
		int ilhas = 0;

		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				if (vista[y * w + x] || !c.Tinta(x, y)) continue;

				ilhas++;
				vista[y * w + x] = true;
				pilha.Push(y * w + x);
				while (pilha.Count > 0)
				{
					int p = pilha.Pop();
					for (int dy = -1; dy <= 1; dy++)
						for (int dx = -1; dx <= 1; dx++)
						{
							int nx = p % w + dx, ny = p / w + dy;
							if (nx < 0 || ny < 0 || nx >= w || ny >= h || vista[ny * w + nx] || !c.Tinta(nx, ny)) continue;
							vista[ny * w + nx] = true;
							pilha.Push(ny * w + nx);
						}
				}
			}
		return ilhas;
	}

	/// <summary>
	/// FOTOGRAFA O PALCO E EXIGE QUE TODO RAIO DELE ESTEJA INTEIRO: nenhuma fresta na faixa, nem um
	/// pixel de fundo na linha central, e uma ilha de tinta por raio.
	/// </summary>
	/// <param name="umPorUm">
	/// Dois ou tres feixes que sao cada um a sua cena (a foto do dono) ganham duas conferencias cada;
	/// uma duzia que sao a mesma cena variada ganha duas pro conjunto, com o pior nomeado.
	/// </param>
	/// <param name="contarIlhas">Falso so na foto das 24 artes -- ver o passo dela no roteiro.</param>
	private void MedirAContinuidade(string nome, string titulo, bool umPorUm, bool contarIlhas = true)
	{
		Chapa? c = Foto(nome);
		if (c == null) return;

		int pior = 0, piorCentro = 0, fora = 0;
		string ondePior = "", ondePiorCentro = "";
		var partes = new List<string>();

		foreach (TiroNoPalco t in _palco)
		{
			(Fresta faixa, Fresta centro) = Sondar(c, t);
			fora += centro.Fora;
			partes.Add($"{t.Rotulo} {faixa.Maior}/{centro.Total}");
			if (faixa.Maior > pior) { pior = faixa.Maior; ondePior = $"{t.Rotulo}, a {faixa.Onde} px da mao"; }
			if (centro.Total > piorCentro) { piorCentro = centro.Total; ondePiorCentro = t.Rotulo; }

			if (!umPorUm) continue;
			Ok($"{t.Rotulo}: o raio nao tem fresta ({faixa.Maior} px medidos na foto)", faixa.Maior == 0);
			Ok($"{t.Rotulo}: a LINHA CENTRAL nao tem um so pixel de fundo entre a mao e a ponta ({centro.Total} px)",
			   centro.Total == 0);
		}

		if (fora > 0) Nota($"ATENCAO: {fora} amostras da linha central cairam FORA da foto, e contam como fundo");

		if (!umPorUm)
		{
			Nota($"{titulo}: fresta na faixa / fundo na linha central, em px -- " + string.Join("   ", partes));
			Ok($"{titulo}: nenhum tem fresta no corpo" + (pior > 0 ? $" (pior: {pior} px em {ondePior})" : ""), pior == 0);
			Ok($"{titulo}: nenhum tem pixel de fundo na LINHA CENTRAL"
			   + (piorCentro > 0 ? $" (pior: {piorCentro} px em {ondePiorCentro})" : ""), piorCentro == 0);
		}

		int ilhas = ContarIlhas(c);
		if (contarIlhas) Ok($"a tinta da foto esta em {ilhas} pecas -- uma por raio ({_palco.Count})", ilhas == _palco.Count);
		else Nota($"a tinta da foto esta em {ilhas} pecas pra {_palco.Count} raios (nao cobrado: ha enfeite solto)");
	}

	// =====================================================================
	// FAMILIA 4 -- TODO COMPRIMENTO
	// =====================================================================
	/// <summary>
	/// DOZE DISTANCIAS DA BOCA A CABECA, em px. Tortas de proposito: 205 e 249 sao as que mediram 13 e
	/// 25 px de fresta no trem de folhas, 2 e o primeiro pacote, 24 e 44 sao o raio saindo, e nenhuma e
	/// multiplo de 32 -- era nos multiplos exatos do ladrilho que o defeito antigo se escondia.
	/// </summary>
	private static readonly float[] Comprimentos = [2, 7, 13, 24, 44, 77, 131, 205, 249, 337, 480, 610];

	/// <summary>Dezessete graus: nem cardeal nem diagonal. Nenhuma folha do BYOND tinha este rumo.</summary>
	private const float AnguloDaEscada = 17f;

	/// <summary>Os doze como uma ESCADA de raios paralelos, do mais curto em cima ao mais longo embaixo.</summary>
	private void PorOsComprimentos()
	{
		LimparOPalco();
		Vector2 t = Tela, u = Rumo(AnguloDaEscada);

		// O DEGRAU sai da janela: o ultimo raio desce `maior * sen` abaixo da propria boca, e tem que
		// caber. Com 720 de altura da 40 px -- 38 de eixo a eixo, e a faixa da sonda (20) nao alcanca a
		// tinta do vizinho.
		float degrau = MathF.Floor((t.Y - 100f - Comprimentos[^1] * u.Y) / (Comprimentos.Length - 1));
		for (int i = 0; i < Comprimentos.Length; i++)
		{
			var cauda = new Vector2(MathF.Floor(t.X * 0.06f), 40f + degrau * i);
			NovoRaio(ArteDeKi.Beam3, 1f, cauda + u * Comprimentos[i], cauda, Vermelho, $"{Comprimentos[i]:0}");
		}
		Assentar();
	}

	// =====================================================================
	// FAMILIA 4b -- TODAS AS ARTES
	// =====================================================================
	/// <summary>Trinta e tres graus: outro rumo que so existe porque a fita gira.</summary>
	private const float AnguloDasArtes = 33f;

	private void PorTodasAsArtesNaDiagonal()
	{
		LimparOPalco();

		ArteDeKi[] artes = [.. ArteDeProjetil.PermitidasPara(TipoDeProjetil.Beam)];
		const int Colunas = 6;
		int linhas = (artes.Length + Colunas - 1) / Colunas;
		Vector2 t = Tela, u = Rumo(AnguloDasArtes);
		float cw = t.X / Colunas, ch = t.Y / linhas;
		float atras = MeiaCelula * (MathF.Abs(u.X) + MathF.Abs(u.Y));

		// CEM PIXELS DA BOCA A CABECA, pra todas: e o tronco que se quer ver, e ele e o mesmo em todas.
		// O que muda de arte pra arte e a frente da cabeca, e por isso o raio e centrado pelo meio do
		// DESENHO (da mao a ponta), nao pelo meio do tronco.
		const float Tronco = 100f;

		for (int i = 0; i < artes.Length; i++)
		{
			var centro = new Vector2(cw * (i % Colunas) + cw * 0.5f, ch * (i / Colunas) + ch * 0.5f);
			float total = atras + Tronco + ArteDeProjetil.FrenteDaCabeca(artes[i]);
			Vector2 cauda = centro - u * (total * 0.5f) + u * atras;
			NovoRaio(artes[i], 1f, cauda + u * Tronco, cauda, Vermelho, artes[i].ToString());
		}
		Assentar();
	}

	// =====================================================================
	// FAMILIA 5 -- A FOTO DO DONO, REPRODUZIDA
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ELA EXISTE DEPOIS DA 4 ============================
	/// A familia 4 mede ate 610 px -- dezenove tiles. O dono fotografou **um feixe atravessando o
	/// mapa**: corpo no canto de cima e o raio descendo a tela inteira, e no trailer foi a DIAGONAL que
	/// ele chamou de torta. Um feixe de tela inteira nao e "o mesmo teste com outro numero": e onde um
	/// erro de meio pixel por trecho vira meio raio de deriva, e onde uma fita torta sai do proprio eixo.
	///
	/// Entao esta familia repete a foto DELE, nas duas leituras: o feixe vertical descendo e o
	/// DIAGONAL do print (o corpo em cima a direita, o raio descendo pra a esquerda). O comprimento sai
	/// da janela e nao de um numero cravado -- ver <see cref="Tela"/>.
	/// ====================================================================================
	/// </summary>
	private void PorAFotoDoDono()
	{
		LimparOPalco();
		Vector2 t = Tela;

		// O VERTICAL: a boca la em cima, a cabeca embaixo -- o raio DESCE, como no print.
		var caudaV = new Vector2(MathF.Floor(t.X * 0.16f), MathF.Floor(t.Y * 0.08f));
		var cabV = new Vector2(caudaV.X, MathF.Floor(t.Y * 0.93f));

		// O DIAGONAL DO PRINT: a boca no canto superior direito, o feixe descendo pra o sudoeste. O
		// comprimento e o maior que cabe sem encostar no vertical nem sair da tela.
		const float Meio = 0.70710678f;
		var caudaD = new Vector2(MathF.Floor(t.X * 0.94f), MathF.Floor(t.Y * 0.07f));
		float compD = MathF.Floor(MathF.Min((caudaD.X - t.X * 0.36f) / Meio, (t.Y * 0.93f - caudaD.Y) / Meio));
		Vector2 cabD = caudaD + new Vector2(-Meio, Meio) * compD;

		NovoRaio(ArteDeKi.Beam3, 1f, cabV, caudaV, Vermelho,
				 $"A FOTO DO DONO -- vertical descendo, {cabV.Y - caudaV.Y:0} px ({(cabV.Y - caudaV.Y) / 32f:0.0} tiles)");
		NovoRaio(ArteDeKi.Beam3, 1f, cabD, caudaD, Vermelho,
				 $"A FOTO DO DONO -- diagonal do print (sudoeste), {compD:0} px ({compD / 32f:0.0} tiles)");
		Assentar();
	}

	// =====================================================================
	// FAMILIA 5a -- LONGOS, FORA DOS OITO RUMOS
	// =====================================================================
	/// <summary>Os tres angulos que nao sao multiplo de 45 nem de 22,5: aqui nao ha folha que sirva de muleta.</summary>
	private static readonly float[] AngulosTortos = [17f, 33f, 61f];

	/// <summary>
	/// Tres feixes em LEQUE, saindo da beirada esquerda e indo ate onde a tela deixa (37, 30 e 16 tiles
	/// na janela de 1280x720). Em leque e nao em estrela: as bocas ficam um decimo da altura uma abaixo
	/// da outra e os feixes so se afastam, entao a faixa da sonda de um nunca encosta na tinta do outro.
	/// </summary>
	private void PorOsLongosTortos()
	{
		LimparOPalco();
		Vector2 t = Tela;
		float frente = ArteDeProjetil.FrenteDaCabeca(ArteDeKi.Beam3);

		for (int i = 0; i < AngulosTortos.Length; i++)
		{
			Vector2 u = Rumo(AngulosTortos[i]);
			var cauda = new Vector2(MathF.Floor(t.X * 0.06f), MathF.Floor(t.Y * (0.08f + 0.10f * i)));
			float comp = MathF.Floor(MathF.Min((t.X * 0.95f - cauda.X) / u.X, (t.Y * 0.93f - cauda.Y) / u.Y)) - frente;
			NovoRaio(ArteDeKi.Beam3, 1f, cauda + u * comp, cauda, Vermelho,
					 $"{AngulosTortos[i]:0} graus, {comp:0} px ({comp / 32f:0.0} tiles)");
		}
		Assentar();
	}

	// =====================================================================
	// FAMILIA 5b -- OS RUMOS NA MESMA FOTO
	// =====================================================================
	/// <summary>
	/// OS DEZENOVE RUMOS: os dezesseis da rosa (de 22,5 em 22,5 graus) e mais tres que nao sao multiplo
	/// de coisa nenhuma. No sentido da tela -- 0 e leste, 90 e SUL.
	///
	/// O trem de folhas tinha arte pra oito (ou quatro) e nenhuma girava; fora delas cada carimbo
	/// escorregava de lado. A fita nao escolhe direcao -- ela gira --, e e por isso que a pergunta
	/// vale ser feita em rumos que folha nenhuma teve.
	/// </summary>
	private static readonly (float Graus, string Nome)[] Rumos =
	[
		(0f, "leste"), (22.5f, "22,5"), (45f, "sudeste"), (67.5f, "67,5"),
		(90f, "sul"), (112.5f, "112,5"), (135f, "sudoeste"), (157.5f, "157,5"),
		(180f, "oeste"), (202.5f, "202,5"), (225f, "noroeste"), (247.5f, "247,5"),
		(270f, "norte"), (292.5f, "292,5"), (315f, "nordeste"), (337.5f, "337,5"),
		(17f, "17"), (33f, "33"), (61f, "61"),
	];

	private static bool EhCardeal(float graus) => graus % 90f == 0f;

	private const int ColunasDosRumos = 5, LinhasDosRumos = 4;

	/// <summary>
	/// O TAMANHO DO RAIO LONGO DA GRADE, da mao a ponta, tirado da janela -- e torto de proposito: o
	/// `- 3` no fim garante que ele nao e multiplo de 32 em tamanho nenhum de janela.
	/// </summary>
	private float TotalDaGrade() =>
		MathF.Floor(MathF.Min(Tela.X / ColunasDosRumos, Tela.Y / LinhasDosRumos) * 0.78f) - 3f;

	/// <summary>
	/// UM RAIO EM CADA CELULA DE UMA GRADE 5x4, cada um num rumo.
	///
	/// ============================ POR QUE EM CELULAS SEPARADAS, E NAO EM ESTRELA ============================
	/// O desenho obvio seria uma estrela: dezenove feixes saindo do mesmo punho. Ele ficaria bonito e
	/// mediria MENOS: perto da mao os feixes se cruzam, a tinta de um tapa o buraco do vizinho e a
	/// sonda acha o raio inteiro. Uma foto que so pode dar verde nao e uma medida.
	///
	/// Em celulas separadas cada raio esta sozinho com o fundo, e a sonda so pode achar a tinta DELE.
	/// ====================================================================================================
	/// </summary>
	/// <param name="daBocaACabeca">A distancia da boca a cabeca, em px. Zero = o maior que cabe na celula.</param>
	private void PorOsRumos(float daBocaACabeca)
	{
		LimparOPalco();

		Vector2 t = Tela;
		float cw = t.X / ColunasDosRumos, ch = t.Y / LinhasDosRumos;
		float frente = ArteDeProjetil.FrenteDaCabeca(ArteDeKi.Beam3);

		for (int i = 0; i < Rumos.Length; i++)
		{
			Vector2 u = Rumo(Rumos[i].Graus);
			var centro = new Vector2(cw * (i % ColunasDosRumos) + cw * 0.5f, ch * (i / ColunasDosRumos) + ch * 0.5f);
			float atras = MeiaCelula * (MathF.Abs(u.X) + MathF.Abs(u.Y));
			float total = daBocaACabeca > 0f ? atras + daBocaACabeca + frente : TotalDaGrade();

			// CENTRADO NA CELULA pelo meio do DESENHO: assim a celula inteira e a margem, e o mesmo
			// tamanho serve pros dezenove rumos sem um deles vazar pro vizinho.
			Vector2 cauda = centro - u * (total * 0.5f) + u * atras;
			Vector2 cabeca = centro + u * (total * 0.5f - frente);
			NovoRaio(ArteDeKi.Beam3, 1f, cabeca, cauda, Vermelho, Rumos[i].Nome);
		}
		Assentar();
	}

	/// <summary>O que as duas reguas TEM que achar nos dezenove rumos. Ver a familia 5d.</summary>
	private enum Esperado
	{
		/// <summary>Os dezenove inteiros: nem fresta, nem fundo no eixo, e uma ilha de tinta por raio.</summary>
		Inteiro,

		/// <summary>Os dezenove furados e em pedacos: o `Picotado` corta a fita, em qualquer rumo.</summary>
		TudoPicotado,

		/// <summary>
		/// So os QUATRO CARDEAIS no eixo: o `SemGirar` poe a mao no eixo cardeal mais proximo atras da
		/// ponta, que pros quatro e a propria mao -- e pros outros quinze e outro lugar, e o raio deixa
		/// o eixo dele. Mas NENHUM se parte: sao dezenove ilhas, como no raio certo.
		/// </summary>
		SoOsCardeais,
	}

	private void MedirOsRumos(string nome, string titulo, Esperado esperado)
	{
		Chapa? c = Foto(nome);
		if (c == null) return;

		var partes = new List<string>();
		var comFresta = new List<string>();
		var comFundoNoEixo = new List<string>();
		var cardeaisRuins = new List<string>();
		int pior = 0, piorCentro = 0, fora = 0, tortos = 0, tortosReprovados = 0;

		for (int i = 0; i < _palco.Count; i++)
		{
			TiroNoPalco t = _palco[i];
			(Fresta faixa, Fresta centro) = Sondar(c, t);
			fora += centro.Fora;
			partes.Add($"{t.Rotulo} {faixa.Maior}/{centro.Total}");

			if (faixa.Maior > 0) comFresta.Add(t.Rotulo);
			if (centro.Total > 0) comFundoNoEixo.Add(t.Rotulo);
			pior = Math.Max(pior, faixa.Maior);
			piorCentro = Math.Max(piorCentro, centro.Total);

			if (EhCardeal(Rumos[i].Graus))
			{
				if (faixa.Maior > 0 || centro.Total > 0) cardeaisRuins.Add(t.Rotulo);
			}
			else
			{
				tortos++;
				if (faixa.Maior > 0 && centro.Total > 0) tortosReprovados++;
			}
		}

		int ilhas = ContarIlhas(c);
		Nota($"{titulo}: por rumo, fresta na faixa / fundo na linha central, em px -- " + string.Join("   ", partes)
			 + $"   -- {ilhas} ilhas de tinta na foto");
		if (fora > 0) Nota($"ATENCAO: {fora} amostras da linha central cairam FORA da foto, e contam como fundo");

		switch (esperado)
		{
			case Esperado.Inteiro:
				Ok($"{titulo}: nenhum dos {_palco.Count} rumos tem fresta no corpo"
				   + (pior > 0 ? $" (pior: {pior} px; com fresta: {string.Join(", ", comFresta)})" : ""), pior == 0);
				Ok($"{titulo}: nenhum dos {_palco.Count} tem pixel de fundo na LINHA CENTRAL"
				   + (piorCentro > 0 ? $" (pior: {piorCentro} px; furados: {string.Join(", ", comFundoNoEixo)})" : ""),
				   piorCentro == 0);
				Ok($"{titulo}: a tinta esta em {ilhas} pecas -- uma por raio", ilhas == _palco.Count);
				break;

			case Esperado.TudoPicotado:
				Ok($"{titulo}: a sonda da FAIXA reprova os {_palco.Count} rumos ({comFresta.Count} com fresta)",
				   comFresta.Count == _palco.Count);
				Ok($"{titulo}: ...e a da LINHA CENTRAL tambem ({comFundoNoEixo.Count} furados)",
				   comFundoNoEixo.Count == _palco.Count);
				Ok($"{titulo}: ...e a tinta se partiu: {ilhas} pecas pra {_palco.Count} raios", ilhas >= _palco.Count * 2);
				break;

			case Esperado.SoOsCardeais:
				Ok($"{titulo}: as duas sondas reprovam os {tortos} rumos que NAO sao cardeais ({tortosReprovados} reprovados)",
				   tortos > 0 && tortosReprovados == tortos);
				Ok($"{titulo}: ...e os 4 cardeais continuam no eixo -- o cardeal mais proximo deles e o proprio"
				   + (cardeaisRuins.Count > 0 ? $" (reprovados: {string.Join(", ", cardeaisRuins)})" : ""),
				   cardeaisRuins.Count == 0);

				// O RAIO SAIU DO EIXO MAS NAO SE PARTIU, e isso separa este defeito do outro. Com a mao
				// fora de lugar o desenho de producao faz a CURVA ate a ponta (`ProjetilDesenhado.Curva`),
				// e uma curva inteira continua sendo uma ilha so: e o "poder ate dar curva" do dono, na foto.
				Ok($"{titulo}: ...e nenhum se PARTIU: {ilhas} pecas pra {_palco.Count} raios -- torto, curvo, e inteiro",
				   ilhas == _palco.Count);
				break;
		}
	}

	// =====================================================================
	// FAMILIA 5c -- O FIO E O MURO
	// =====================================================================
	/// <summary>
	/// AS DUAS TECNICAS QUE O DM POE NOS EXTREMOS, com a arte e a escala que o jogo manda de verdade, e
	/// entre elas a mesma arte do fio na escala do muro:
	///
	///   **Onda de Ki** -- `Beam3`, `wavemult` 1. O fio.
	///   **o mesmo `Beam3` a 4x** -- pra a ESCALA responder sozinha, sem a arte ajudar.
	///   **Final Flash** -- `Beam - Big Fire`, `wavemult` 4 (`GameServer.Tecnicas.G5.cs`). O muro.
	///
	/// ============================ A PERGUNTA E "A ESCALA ENGROSSA O DESENHO?" ============================
	/// No DM o `wavemult` entra em `A.transform *= wavemult` (`beams.dm:149`) -- ele engorda o SPRITE, e
	/// e por isso que o Final Flash e um muro. Aqui a escala multiplica as medidas do tiro
	/// (`PintorDeKi.Medir`), e a foto e quem diz se multiplicou. O par fio/muro sozinho nao isola a
	/// escala (a `Big Fire` ja e cinco vezes mais grossa na escala 1); o do meio isola.
	///
	/// A outra metade desta familia -- "o passo do trem NAO cresce com a escala" -- morreu com o trem.
	/// ====================================================================================================
	/// </summary>
	private void PorOFioEOMuro()
	{
		LimparOPalco();
		Vector2 t = Tela;
		float y0 = MathF.Floor(t.Y * 0.12f);

		// O COMPRIMENTO SAI DO MURO: a cabeca dele avanca 128 px alem da posicao do tiro, e a ponta tem
		// que caber na foto pra a sonda ir ate ela. O `- 5` deixa o numero torto -- ver `TotalDaGrade`.
		float comp = MathF.Floor(t.Y * 0.96f - y0 - ArteDeProjetil.FrenteDaCabeca(ArteDeKi.BeamBigFire) * 4f) - 5f;

		(ArteDeKi Arte, float Escala, float X, string Nome)[] tres =
		[
			(ArteDeKi.Beam3, 1f, 0.12f, "O FIO (Onda de Ki: Beam3, wavemult 1)"),
			(ArteDeKi.Beam3, 4f, 0.36f, "o mesmo Beam3 a 4x"),
			(ArteDeKi.BeamBigFire, 4f, 0.74f, "O MURO (Final Flash: Big Fire, wavemult 4)"),
		];
		foreach ((ArteDeKi arte, float escala, float x, string rotulo) in tres)
		{
			var cauda = new Vector2(MathF.Floor(t.X * x), y0);
			NovoRaio(arte, escala, cauda + new Vector2(0, comp), cauda, Vermelho, $"{rotulo}, {comp:0} px");
		}
		Assentar();
	}

	private void MedirOFioEOMuro()
	{
		Chapa? c = Foto("5c-o-fio-e-o-muro");
		if (c == null) return;

		foreach (TiroNoPalco t in _palco)
		{
			(Fresta faixa, Fresta centro) = Sondar(c, t);
			Ok($"{t.Rotulo}: inteiro da mao a ponta (fresta {faixa.Maior} px, fundo no eixo {centro.Total} px)",
			   faixa.Maior == 0 && centro.Total == 0);
		}

		// MEDIDO em 84 rodadas: o fio de 22 a 24 px, o mesmo a 4x de 66 a 73 (2,8 a 3,3 vezes) e o muro
		// de 330 a 375. Nao sao 4 vezes porque a largura que se ve inclui o halo, e o alcance dele
		// (`7 + 1,1 raio`) nao quadruplica com o tronco. Uma escala que nao chegasse ao desenho daria 1.
		float fio = LarguraNaFoto(c, _palco[0]), fioA4 = LarguraNaFoto(c, _palco[1]), muro = LarguraNaFoto(c, _palco[2]);
		Nota($"largura medida no meio do feixe: fio {fio:0} px, o mesmo a 4x {fioA4:0} px, muro {muro:0} px");
		Ok($"a ESCALA engrossa o desenho: o Beam3 a 4x tem {fioA4:0} px contra {fio:0} ({fioA4 / MathF.Max(1f, fio):0.0}x)",
		   fio > 0 && fioA4 > fio * 2f);
		Ok($"o MURO e muito mais grosso que o FIO na foto ({muro:0} px contra {fio:0}, {muro / MathF.Max(1f, fio):0.0}x)",
		   fio > 0 && muro > fio * 2f);
	}

	/// <summary>
	/// A LARGURA DESENHADA NO MEIO DO FEIXE, em px de mundo: a corrida de tinta que atravessa o eixo, na
	/// perpendicular. Com o halo -- e a largura que o JOGADOR ve, e e ela que o `wavemult` tem que mexer.
	/// (A largura do corpo sem o halo, contra a tabela, e assunto da familia 6.)
	/// </summary>
	private static float LarguraNaFoto(Chapa c, TiroNoPalco t) =>
		CorridaDeTinta(c, (t.Mao + t.Ponta) * 0.5f, new Vector2(-t.Rumo.Y, t.Rumo.X));

	// =====================================================================
	// FAMILIA 6 -- O DESENHO OBEDECE AS TABELAS DO CORE
	// =====================================================================
	/// <summary>
	/// ============================ DUAS MEDIDAS DO DESENHO SAO REGRA DO SERVIDOR ============================
	/// `ArteDeProjetil.FrenteDaCabeca` e `ArteDeProjetil.MeiaEspessuraDoTronco`. O servidor planta a
	/// cabeca de um raio encostada em quem ela empurra, encontra duas cabecas numa disputa e corta o
	/// tronco na beirada POR ESSES NUMEROS -- e desde 2026-10-07 o desenho os le do mesmo lugar
	/// (`PintorDeKi.Medir`), em vez de serem eles a medida de uma folha.
	///
	/// "Le do mesmo lugar" e uma afirmacao sobre o CODIGO. A do dono foi sobre a tela (2026-09-23:
	/// *"as cabecas ainda estao se sobrepondo as vezes"* -- era o Final Flash, cuja cabeca avanca
	/// 128 px e cobria quem ela empurrava). Entao aqui cada arte que um raio pode vestir e desenhada
	/// deitada no fundo liso e a FOTO responde:
	///
	///   (a) ate onde a tinta SOLIDA vai adiante da posicao do tiro -- tem que ser `FrenteDaCabeca x
	///       escala`;
	///   (b) que meia espessura o tronco MACICO tem, longe das duas bolas -- tem que ser
	///       `MeiaEspessuraDoTronco x Macico x escala`.
	///
	/// ============================ O HALO E APAGADO NESTES NODES, E SO NELES ============================
	/// O halo e luz SOMADA alem da casca: ele nao encosta em ninguem, mas borra a beirada que esta
	/// regua procura. Cada raio daqui tem o `alcance_do_halo` do proprio material escrito em 0,001 px
	/// (e nao zero: o shader divide por ele) -- e so isso: geometria, estilo e medidas sao os de
	/// producao. Ver <see cref="SemHalo"/>.
	///
	/// E TODOS APONTAM PRO LESTE, com o eixo no meio de uma linha de pixels: a regua anda de pixel em
	/// pixel pela grade da propria foto, sem arredondar diagonal nenhuma.
	/// ======================================================================================================
	/// </summary>
	private void PorARegra(int pagina)
	{
		LimparOPalco();

		ArteDeKi[] artes = [.. ArteDeProjetil.PermitidasPara(TipoDeProjetil.Beam)];
		for (int i = pagina * PorPaginaDaRegra, cel = 0; i < artes.Length && cel < PorPaginaDaRegra; i++, cel++)
			RaioDeRegua(Celula(cel, ColunasDaRegra, LinhasDaRegra), artes[i], 1f);
		Assentar();
	}

	private const int ColunasDaRegra = 2, LinhasDaRegra = 4, PorPaginaDaRegra = ColunasDaRegra * LinhasDaRegra;

	/// <summary>Qual pagina da regua esta no palco. Ver o passo 39 do roteiro.</summary>
	private int _paginaDaRegra;

	/// <summary>Quantas fotos de oito sao precisas pra TODA arte que um raio pode vestir passar pela regua.</summary>
	private static int PaginasDaRegra() =>
		(ArteDeProjetil.PermitidasPara(TipoDeProjetil.Beam).Count() + PorPaginaDaRegra - 1) / PorPaginaDaRegra;

	private Rect2 Celula(int i, int colunas, int linhas)
	{
		Vector2 t = Tela;
		float cw = t.X / colunas, ch = t.Y / linhas;
		return new Rect2(cw * (i % colunas), ch * (i / colunas), cw, ch);
	}

	/// <summary>
	/// O Final Flash a 4x -- a escala com que o jogo o atira -- ocupa a metade de cima inteira (216 px
	/// de tronco, 128 de frente); embaixo, quatro artes a 2x e 3x. Juntas, as cinco cobrem tres das
	/// quatro frentes que nao sao o meio tile de todo mundo: 32, 14 e 0 px, multiplicadas pela escala.
	/// </summary>
	private void PorARegraEmOutrasEscalas()
	{
		LimparOPalco();
		Vector2 t = Tela;
		RaioDeRegua(new Rect2(0, 0, t.X, t.Y * 0.5f), ArteDeKi.BeamBigFire, 4f);
		RaioDeRegua(Celula(4, ColunasDaRegra, LinhasDaRegra), ArteDeKi.Beam3, 2f);
		RaioDeRegua(Celula(5, ColunasDaRegra, LinhasDaRegra), ArteDeKi.Kamehameha4, 2f);
		RaioDeRegua(Celula(6, ColunasDaRegra, LinhasDaRegra), ArteDeKi.Dodompa, 3f);
		RaioDeRegua(Celula(7, ColunasDaRegra, LinhasDaRegra), ArteDeKi.Makkankosappo, 2f);
		Assentar();
	}

	/// <summary>
	/// UM RAIO DEITADO NA CELULA, da esquerda pra direita, sem halo e tingido de magenta. A mao fica a
	/// 44 px da beirada esquerda e a PONTA DA REGRA a 48 da direita: o que muda de arte pra arte e onde
	/// a cabeca (a posicao do tiro) cai, e nao onde o desenho acaba.
	/// </summary>
	private TiroNoPalco RaioDeRegua(Rect2 cel, ArteDeKi arte, float escala, float? vestidoCom = null)
	{
		float y = MathF.Floor(cel.Position.Y + cel.Size.Y * 0.5f) + 0.5f;
		float xMao = MathF.Floor(cel.Position.X) + 44f;
		float xPonta = MathF.Floor(cel.End.X) - 48f;
		float frente = ArteDeProjetil.FrenteDaCabeca(arte) * escala;

		string rotulo = escala == 1f ? arte.ToString() : $"{arte} a {escala:0}x";
		TiroNoPalco t = NovoRaio(arte, escala, new Vector2(xPonta - frente, y), new Vector2(xMao + MeiaCelula, y),
								 Magenta, rotulo, vestidoCom);
		SemHalo(t.No);
		return t;
	}

	/// <summary>
	/// APAGA O HALO DESTE NODE, pra a foto mostrar so o corpo. E a unica escrita desta bancada num
	/// material de producao, e ela e num uniform que nao muda a FORMA de nada: o halo e somado por fora
	/// da casca. 0,001 e nao zero -- o shader divide a distancia por este numero.
	/// </summary>
	private static void SemHalo(ProjetilDesenhado no)
	{
		if (no.Material is ShaderMaterial m) m.SetShaderParameter("alcance_do_halo", 0.001f);
	}

	/// <summary>O que a foto disse de um raio deitado: a frente solida e a meia espessura do tronco, em px de mundo.</summary>
	private readonly record struct Medida(float Frente, float Meia, float MeiaMenor, float MeiaMaior, int Estacoes);

	/// <summary>De quanto ATRAS da posicao do tiro a regua da frente comeca a andar. Ali e sempre tronco.</summary>
	private const float RecuoDaRegua = 8f;

	private static Medida MedirNaRegua(Chapa c, TiroNoPalco t)
	{
		float k = c.PxPorMundo;

		// ---- (a) A FRENTE. Parte de dentro do corpo, um pouco atras da posicao do tiro, e anda pro
		//      leste enquanto for solido. Em TRES linhas (a do eixo e as duas vizinhas), ficando com a
		//      que foi mais longe: a geometria e arredondada pro pixel ao desenhar e o eixo pode ter
		//      caido meia linha pro lado. Numa cabeca redonda as tres dao o mesmo; numa seta, so a do
		//      eixo chega na ponta.
		Vector2I p0 = c.Pixel(t.Cabeca - new Vector2(RecuoDaRegua, 0));
		int maisLonge = 0;
		for (int dy = -1; dy <= 1; dy++)
		{
			int n = 0;
			while (c.Solido(p0.X + n, p0.Y + dy)) n++;
			maisLonge = Math.Max(maisLonge, n);
		}
		float frente = maisLonge == 0
			? float.NaN
			: (p0.X + maisLonge - (c.DoMundo * t.Cabeca).X) / k;

		// ---- (b) A MEIA ESPESSURA, no TERCO DO MEIO do desenho: longe da gota das duas bolas, que
		//      desce ate tres raios de bola pra dentro do tronco. Em cada coluna, a corrida de solido
		//      que atravessa o eixo; a media das colunas, porque a casca ondula de proposito (as
		//      barrigas que viajam da mao pra ponta: ate 13% do raio pra cada lado, com media zero).
		float x1 = t.Mao.X + (t.Ponta.X - t.Mao.X) / 3f, x2 = t.Mao.X + (t.Ponta.X - t.Mao.X) * 2f / 3f;
		Vector2I a = c.Pixel(new Vector2(x1, t.Cabeca.Y)), b = c.Pixel(new Vector2(x2, t.Cabeca.Y));
		long soma = 0;
		int estacoes = 0, menor = int.MaxValue, maior = 0;
		for (int x = a.X; x <= b.X; x++)
		{
			int corrida = 0;
			if (c.Solido(x, a.Y))
			{
				int cima = 0, baixo = 0;
				while (c.Solido(x, a.Y - 1 - cima)) cima++;
				while (c.Solido(x, a.Y + 1 + baixo)) baixo++;
				corrida = 1 + cima + baixo;
			}
			soma += corrida;
			estacoes++;
			menor = Math.Min(menor, corrida);
			maior = Math.Max(maior, corrida);
		}
		if (estacoes == 0) return new Medida(frente, float.NaN, float.NaN, float.NaN, 0);

		return new Medida(frente, soma / (float)estacoes / k / 2f, menor / k / 2f, maior / k / 2f, estacoes);
	}

	/// <summary>
	/// A FOLGA DA REGUA DA PONTA, em px de mundo: um pixel e meio.
	///
	/// ============================ MEDIDA, E NAO ESCOLHIDA ============================
	/// O que a regua tem que engolir e fixo e pequeno: meio pixel da grade da foto, e ate meio do
	/// arredondamento dos vertices (`snap_2d_vertices_to_pixel` empurra a fita inteira pro pixel).
	/// Em 84 rodadas, nas 23 artes sem coroa e em quatro escalas, o desvio foi SEMPRE 0 ou +1 px -- e
	/// quase sempre o mesmo pra mesma arte (o `Beam3` e os Kamehamehas 1 a 3 dao +1, a `Big Fire` da
	/// 0), que e a assinatura de arredondamento e nao de ruido. A ponta nao respira com o shader: a
	/// bola da cabeca incha e murcha em volta de um centro que recua junto.
	///
	/// DOIS PIXELS SERIAM FROUXOS DEMAIS PRA UMA DAS LINHAS DA TABELA: o Dodompa manda 14 e o padrao
	/// de todo mundo e 16. Com 2 px de folga um Dodompa desenhado a 16 passaria -- com 1,5 ele reprova,
	/// e a contraprova confere isso.
	/// ================================================================================
	/// </summary>
	private const float FolgaDaRegua = 1.5f;

	/// <summary>
	/// A FOLGA DA PONTA DESTE TIRO, pra tras e pra frente. E a da regua -- e, nas artes de COROA (so o
	/// Static Beam), mais a do estilo: a cabeca dele e um estouro de espinhos que pisca, e o espinho do
	/// eixo tem de 0,83 a 1,35 raios de cabeca (`FeixeDeKi.gdshader`), entao a tinta acaba ate 0,2 raio
	/// antes e ate 0,4 depois da ponta da regra. Medido: de -2 a +7 px em 84 rodadas, com 15 px de raio.
	/// </summary>
	private static (float Atras, float AFrente) FolgaDaPonta(TiroNoPalco t)
	{
		ArteDeKiNoCliente.EstiloDeFeixe e = ArteDeKiNoCliente.EstiloDoFeixe(t.Arte);
		float coroa = e.Cabeca * e.Coroa * t.Escala;
		return (FolgaDaRegua + 0.2f * coroa, FolgaDaRegua + 0.4f * coroa);
	}

	/// <summary>A PONTA MEDIDA ESTA A `adiante` PX DA POSICAO DO TIRO, com a folga deste tiro?</summary>
	private static bool PontaEm(TiroNoPalco t, Medida m, float adiante)
	{
		(float atras, float aFrente) = FolgaDaPonta(t);
		return m.Frente >= adiante - atras && m.Frente <= adiante + aFrente;
	}

	/// <summary>A PONTA ESTA ONDE A TABELA MANDA -- `FrenteDaCabeca x escala` adiante da posicao do tiro?</summary>
	private static bool FrenteNaRegra(TiroNoPalco t, Medida m) =>
		PontaEm(t, m, ArteDeProjetil.FrenteDaCabeca(t.Arte) * t.Escala);

	/// <summary>
	/// ESTE TRONCO TEM BEIRADA LIMPA? So nesses a meia espessura e medida. Os outros tem a casca em
	/// dente (a serrilha do Masenko e do Galick Ho), em lingua (os fiapos do Kamehameha 5) ou cruzada
	/// por enfeite opaco (os aneis da broca, as descargas do Static Beam) -- e ali "a largura do
	/// tronco" nao e um numero, e um desenho.
	/// </summary>
	private static bool TroncoLimpo(ArteDeKiNoCliente.EstiloDeFeixe e) =>
		e.Serrilha == 0f && e.Fiapos == 0f && e.Aneis == 0f && e.Espiral == 0f && e.Eletrico == 0f;

	/// <summary>
	/// O TRONCO TEM A ESPESSURA QUE A TABELA MANDA? A folga e de 1 px, ou 13% do esperado quando isso
	/// for mais.
	///
	/// ============================ DE ONDE SAEM OS DOIS NUMEROS ============================
	/// UM PIXEL e a grade da foto: a meia espessura e metade de uma contagem de pixels. Nos troncos de
	/// ate 13 px a media das colunas ficou entre -0,3 e +0,4 px da tabela em 84 rodadas.
	///
	/// TREZE POR CENTO e o quanto a casca ONDULA de proposito no shader (`0,16 x (ruido - 0,5)` mais
	/// `0,05 x barriga`: ate 8% + 5% pra cada lado). Num raio fino a ondulacao passa dezenas de vezes
	/// pela janela medida e a media a apaga; num raio grosso ela e LONGA (o comprimento dela cresce com
	/// o raio) e a janela ve um pedaco so. Medido: a `Big Fire` (27 px) de -1,6 a +2,0; o Kamehameha 4
	/// a 2x (26 px) de -1,3 a +1,3; o Final Flash a 4x (108 px) de -7,3 a +8,4 -- todos dentro dos 13%,
	/// que e o teto que o proprio desenho se da.
	///
	/// E O TRONCO QUE PULSA (o Boom Wave) pode estar em qualquer ponto entre 45% e 100% da largura
	/// nominal: e o estilo dele. Medido de 1,5 a 3,4 px, com 3 na tabela.
	/// ====================================================================================
	/// </summary>
	private static bool MeiaNaRegra(TiroNoPalco t, Medida m)
	{
		ArteDeKiNoCliente.EstiloDeFeixe e = ArteDeKiNoCliente.EstiloDoFeixe(t.Arte);
		float esperada = MeiaEsperada(t);
		float folga = MathF.Max(1f, esperada * 0.13f);
		return m.Meia >= esperada * (1f - 0.55f * e.Pulsar) - folga && m.Meia <= esperada + folga;
	}

	private static float MeiaEsperada(TiroNoPalco t) =>
		ArteDeProjetil.MeiaEspessuraDoTronco(t.Arte) * ArteDeKiNoCliente.EstiloDoFeixe(t.Arte).Macico * t.Escala;

	private void MedirARegra(string nome)
	{
		Chapa? c = Foto(nome);
		if (c == null) return;

		var pontaForaDoDito = new List<string>();
		int comFrente = 0, reprovadosContraAPosicao = 0;
		foreach (TiroNoPalco t in _palco)
		{
			Medida m = MedirNaRegua(c, t);
			ArteDeKiNoCliente.EstiloDeFeixe e = ArteDeKiNoCliente.EstiloDoFeixe(t.Arte);
			float frente = ArteDeProjetil.FrenteDaCabeca(t.Arte) * t.Escala;

			Ok($"{t.Rotulo}: a ponta SOLIDA cai a {m.Frente:0.0} px da posicao do tiro (a tabela manda {frente:0.0}"
			   + (e.Coroa > 0f ? ", e a coroa pisca em volta" : "") + ")", FrenteNaRegra(t, m));

			if (TroncoLimpo(e))
				Ok($"{t.Rotulo}: o tronco macico tem {m.Meia:0.0} px de meia espessura (a tabela manda {MeiaEsperada(t):0.0}"
				   + (e.Pulsar > 0f ? ", pulsando" : "") + $"; de {m.MeiaMenor:0.0} a {m.MeiaMaior:0.0} em {m.Estacoes} colunas)",
				   MeiaNaRegra(t, m));
			else
				Nota($"{t.Rotulo}: tronco com enfeite na beirada -- a meia espessura nao e medida "
					 + $"(a foto deu {m.Meia:0.0}; o macico da tabela e {MeiaEsperada(t):0.0})");

			// A PONTA QUE O MUNDO USA. `PontaDesenhada` e onde o `World` poe a estrela do choque de dois
			// feixes; ela se apresenta como "a mesma conta que o `_Draw` faz", e quem confere e a foto.
			float dita = t.No.PontaDesenhada.X - t.Cabeca.X;
			if (!PontaEm(t, m, dita)) pontaForaDoDito.Add($"{t.Rotulo} ({m.Frente:0.0} na foto, {dita:0.0} no node)");

			// E A CONTRAPROVA DELA: a ponta de quem esquecesse de somar a frente seria a propria posicao
			// do tiro. Contra ESSA ponta a regua tem que reprovar todo raio cuja frente e maior que a
			// folga -- todos, menos a broca, que e a unica de frente zero.
			if (frente > FolgaDaPonta(t).AFrente)
			{
				comFrente++;
				if (!PontaEm(t, m, 0f)) reprovadosContraAPosicao++;
			}
		}

		Ok($"a `PontaDesenhada` dos {_palco.Count} (onde o mundo poe a estrela do choque) e onde a tinta acaba"
		   + (pontaForaDoDito.Count > 0 ? $" -- FORA: {string.Join(", ", pontaForaDoDito)}" : ""),
		   pontaForaDoDito.Count == 0);
		Ok($"CONTRA-EXEMPLO: contra a POSICAO do tiro (a ponta de quem esquecesse a frente) a mesma regua reprova "
		   + $"os {comFrente} que tem frente ({reprovadosContraAPosicao})", comFrente > 0 && reprovadosContraAPosicao == comFrente);
	}

	/// <summary>
	/// ============================ A CONTRAPROVA: DUAS MANEIRAS DE O DESENHO DESOBEDECER ============================
	/// As pontas e os troncos verdes de cima nao provam que a regua enxerga. Aqui oito raios sao
	/// postos com dois defeitos, e ela tem que reprovar exatamente quem o defeito alcanca:
	///
	///   * `Feixe.AlcanceFixoDeTeste` LIGADO DURANTE O `Vestir` -- o defeito injetavel do Core que faz
	///     toda cabeca ter os 16 px da `Beam3`, seja qual for a arte. E o codigo de antes de 2026-09-23,
	///     o do Final Flash cobrindo quem ele empurrava. O `Vestir` mede o tiro por ele uma vez, e o
	///     campo ja pode ser desligado: aquele node segue desenhando a ponta no lugar errado. As QUATRO
	///     artes de frente PROPRIA tem que reprovar (inclusive o Dodompa, que erra por 2 px e so 2); as
	///     duas de frente comum NAO (16 e 16), e tronco nenhum -- a regua nao pode ficar vermelha de
	///     susto;
	///   * a `Escala` ESCRITA DEPOIS DO `Vestir`, sem vestir de novo: o tiro diz que e 3x e desenha 1x.
	///     Ponta e tronco reprovam juntos; o vizinho, vestido a 3x de verdade, passa.
	///
	/// E a segunda rodada poe os MESMOS oito sem defeito nenhum: todos voltam ao verde.
	/// ==============================================================================================================
	/// </summary>
	private void PorAContraprovaDaRegra(bool comDefeito)
	{
		LimparOPalco();

		Feixe.AlcanceFixoDeTeste = comDefeito;
		try
		{
			for (int i = 0; i < ComAlcanceFixo.Length; i++)
				RaioDeRegua(Celula(i, ColunasDaRegra, LinhasDaRegra), ComAlcanceFixo[i], 1f);
		}
		finally { Feixe.AlcanceFixoDeTeste = false; }

		RaioDeRegua(Celula(6, ColunasDaRegra, LinhasDaRegra), ArteDeKi.Beam3, 3f, vestidoCom: comDefeito ? 1f : 3f);
		RaioDeRegua(Celula(7, ColunasDaRegra, LinhasDaRegra), ArteDeKi.Beam3, 3f);
		Assentar();
	}

	/// <summary>
	/// As seis celulas do alcance fixo: primeiro as QUATRO artes cuja frente nao e o meio tile de todo
	/// mundo (32, 21, 0 e 14 px), depois duas em que e (16) -- e essas nao podem reprovar.
	/// </summary>
	private static readonly ArteDeKi[] ComAlcanceFixo =
	[
		ArteDeKi.BeamBigFire, ArteDeKi.EraserCannon, ArteDeKi.Makkankosappo, ArteDeKi.Dodompa,
		ArteDeKi.Beam3, ArteDeKi.Kamehameha1,
	];

	private const int ComFrentePropria = 4;

	private void MedirAContraprovaDaRegra(string nome, bool comDefeito)
	{
		Chapa? c = Foto(nome);
		if (c == null) return;

		var medidas = _palco.Select(t => (Tiro: t, M: MedirNaRegua(c, t))).ToList();
		string marca = comDefeito ? "[injecao] " : "";
		foreach ((TiroNoPalco t, Medida m) in medidas)
			Nota($"{marca}{t.Rotulo}: ponta a {m.Frente:0.0} px (tabela {ArteDeProjetil.FrenteDaCabeca(t.Arte) * t.Escala:0.0}), "
				 + $"meia espessura {m.Meia:0.0} px (tabela {MeiaEsperada(t):0.0})");

		if (!comDefeito)
		{
			Ok("com os defeitos DESLIGADOS, as oito pontas voltam pra onde a tabela manda",
			   medidas.All(p => FrenteNaRegra(p.Tiro, p.M)));
			Ok("...e os troncos tambem",
			   medidas.Where(p => TroncoLimpo(ArteDeKiNoCliente.EstiloDoFeixe(p.Tiro.Arte))).All(p => MeiaNaRegra(p.Tiro, p.M)));
			return;
		}

		// A ordem e a do `PorAContraprovaDaRegra`: as seis do alcance fixo (quatro de frente propria,
		// duas de frente comum), depois a escala mentida e o controle dela.
		var fixas = medidas.Take(ComAlcanceFixo.Length).ToList();
		Ok("[injecao] com o alcance FIXO a regua REPROVA a ponta do Final Flash, do Eraser Cannon, da broca e do Dodompa",
		   fixas.Take(ComFrentePropria).All(p => !FrenteNaRegra(p.Tiro, p.M)));
		Ok("[injecao] ...e NAO reprova o Beam3 nem o Kamehameha, cuja frente ja era 16 (a regua nao fica vermelha de susto)",
		   fixas.Skip(ComFrentePropria).All(p => FrenteNaRegra(p.Tiro, p.M)));
		Ok("[injecao] ...nem o TRONCO de ninguem: o defeito so mexe na frente",
		   fixas.Where(p => TroncoLimpo(ArteDeKiNoCliente.EstiloDoFeixe(p.Tiro.Arte))).All(p => MeiaNaRegra(p.Tiro, p.M)));

		(TiroNoPalco mentido, Medida doMentido) = medidas[ComAlcanceFixo.Length];
		(TiroNoPalco vizinho, Medida doVizinho) = medidas[ComAlcanceFixo.Length + 1];
		Ok("[injecao] um tiro que DIZ 3x e foi vestido a 1x e reprovado na ponta E no tronco",
		   !FrenteNaRegra(mentido, doMentido) && !MeiaNaRegra(mentido, doMentido));
		Ok("[injecao] ...e o vizinho, vestido a 3x de verdade, passa nas duas",
		   FrenteNaRegra(vizinho, doVizinho) && MeiaNaRegra(vizinho, doVizinho));
	}
}
