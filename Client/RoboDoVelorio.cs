using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// ============================ O VELORIO -- A BANCADA DE DOIS CORPOS (`--velorio`) ============================
/// *"falta fazer o personagem quando MORRER ir pro OUTRO MUNDO e a AUREOLA aparecer sobre a
/// cabeca"* -- o pedido do dono. Esta bancada mede o pedido inteiro, do funil da morte ate o pixel,
/// e mede com **DOIS CORPOS NA MESMA TELA**.
///
/// ============================ POR QUE DOIS, E NAO UM ============================
/// Porque com um corpo so a metade das perguntas passa verde estando errada:
///
///   * *"morto tem auréola"* sozinho fica VERDE com `TemAureola => true`. O jogo inteiro andaria de
///     auréola e a bancada aplaudiria. So a linha gemea -- **o vivo ao lado, no mesmo quadro, sem
///     nenhuma** -- fecha essa porta.
///   * *"a auréola aparece no Outro Mundo"* fica VERDE com um crivo por LUGAR (`morto && EhOAlem`).
///     E o crivo por lugar e a correcao errada que <see cref="Alem.TemAureola"/> recusa por escrito
///     (ela plantaria o defeito pro dia do `KeepsBody`, o morto que anda entre os vivos). Por isso o
///     corpo de controle **e levado pro alem tambem**: vivo, no meio dos mortos, e sem auréola.
///   * *"o morto vai pro Outro Mundo"* fica VERDE com "todo corpo caido viaja" -- e ai o
///     NOCAUTEADO viajaria junto, que e o contra-exemplo mais provavel deste jogo (nocaute e morte
///     usam a mesma pose, o mesmo corpo no chao, e ate hoje eram indistinguiveis na tela).
/// ================================================================
///
/// ============================ O QUE ELA MEDE QUE AS OUTRAS TRES NAO MEDEM ============================
///   `--alemteste`   e de SERVIDOR: mede a viagem e o byte, e chama a triagem NA MAO. Nunca desenhou
///                   um pixel e nunca passou pelo `if (dead && agora >= relogio)` do tique.
///   `--diagforma`   chama `MostrarAureola(true)` num boneco do proprio processo: prova a FUNCAO,
///                   nao o percurso.
///   `--morte a|b`   fotografa, com dois processos -- e a prova de olho. Nao julga triagem, nem
///                   borda, nem balao, nem as duas interfaces.
///
/// AQUI: um processo so (o `--host` e servidor e cliente ao mesmo tempo), a morte pelo FUNIL de
/// producao, a triagem pelo TIQUE, e a medida no NODE que esta na tela.
/// ================================================================================
///
/// ============================ AS DEZ FAMILIAS, E COMO CADA UMA REPROVA ============================
///   1. O CONTROLE       -- dois corpos vivos, zero auréolas. Reprova se a auréola for ligada por
///                          qualquer coisa que nao seja a morte (ou se nascer acesa).
///   2. O NOCAUTE        -- KO nao arma relogio, e nem com o relogio vencido a forca ele viaja.
///                          Reprova se alguem trocar o `if (pl.Ficha.dead && ...)` do `TickCombate`
///                          por `Deitado`, ou se `Nocautear` passar a escrever `dead`.
///   3. O CADAVER        -- morto no chao dos vivos, **sem auréola** (o bug que o dono fotografou),
///                          e o vivo ao lado tambem sem. Reprova se `TemAureola` voltar a ser
///                          `=> morto`, ou se `MorteJaViajou` deixar de ser rearmado na morte.
///   4. A VIAGEM         -- o tique leva pro Outro Mundo e **ai** a auréola acende, no fio e no
///                          desenho. Reprova se a triagem parar de ser chamada, se o `IrProAlem`
///                          nao acender o bit, ou se o `S2C.Aureola` nao chegar ao `MostrarAureola`.
///   5. AS DUAS CABECAS  -- morto COM e vivo SEM, lado a lado **dentro** do alem. Reprova em
///                          `TemAureola => true` e em `TemAureola => EhOAlem(zona)`.
///   6. A VOLTA          -- reviver apaga a auréola sem uma linha propria; e quem morre DENTRO do
///                          alem nao volta sozinho: fica de pe, com auréola e relogio trancado, ate
///                          PAGAR O ENMA (o caminho de producao da volta, 2026-09-04). Reprova se a
///                          auréola virar um campo que o revive tem que lembrar, ou se o
///                          `PassoDaMorte` voltar a chamar `Renascer` no alem.
///   7. QUEM NAO VAI     -- cidadao, reflexo e boneco, um por linha, com o corpo nomeado. Reprova se
///                          o `else` largo voltar ("quem nao e NPC renasce"): o reflexo apareceria
///                          de pe na mesa do Enma.
///   8. AS BORDAS        -- mente, ponte e Sala do Tempo. Reprova se a morte na mente virar morte de
///                          verdade, se a viagem nao souber sair de uma zona dinamica, ou se a
///                          tranca da Sala deixar de ser aberta pela morte.
///
///   E MAIS DUAS, que sao do DESENHO e nao da morte:
///   9. O BALAO          -- a fala continua chegando na cabeca do morto, e as duas coisas cabem la
///                          em cima ao mesmo tempo (medido em PIXEL ACESO, nao na conta de cabeca).
///                          Reprova se alguem mexer no `BalaoDeFala.AlturaBase` ou trocar a folha da
///                          auréola por uma com o desenho mais alto.
///  10. AS INTERFACES    -- `INaoSomeComOCorpo` (nao declarada: a auréola SOME com o corpo) e
///                          `ISobeComOCorpo` (nao declarada: ela sobe de graca, no colo do pai).
///                          Reprova se alguem "consertar" a auréola declarando uma das duas.
/// ==============================================================================================
///
/// ============================ A TABELA DE MUTANTES -- CADA FAMILIA COM O DEFEITO NA FRENTE ============================
/// "Como reprova" escrito no comentario e uma promessa. Estas doze foram INJETADAS no codigo de
/// producao, compiladas e rodadas, uma de cada vez (placar limpo: **70 passaram, 0 falharam**):
///
///   defeito injetado                                                              | reprovam
///   ------------------------------------------------------------------------------|---------
///   `TemAureola => true`                                                           |   12
///   `TemAureola => morto` (o bug que o dono fotografou: cadaver com auréola)        |    4
///   `MorteJaViajou = EhOAlem(zona)` no funil (o crivo por LUGAR)                    |    2
///   `IrProAlem` nao acende o bit (`MorteJaViajou = false`)                          |    9
///   o tique tria por `pl.Deitado` no lugar de `pl.Ficha.dead` (o KO viaja)          |   16
///   a triagem volta ao `else` largo (`Renascer` no terceiro grupo)                  |    3
///   `IrProAlem` nao chama `AMorteSaiDaSala` (a tranca da Sala nao abre)             |    1
///   `BordasDeQuemEstaFora` sem `|| pl.Ficha.dead` (morrer na mente mata de verdade) |    1
///   `CharacterVisual` declara `INaoSomeComOCorpo`                                   |    2
///   `BalaoDeFala.Deslocamento` recebe a posicao CRUA (perde a altura propria)       |    2
///   `VestirCorpoInteiro` nao veste a auréola de quem nasce na tela                  |    1
///   `World.AoFalar` cala quem tem auréola (morto nao fala)                          |    2
///   `LocalPlayer` volta a deitar o corpo por `Imobilizado` (o morto se ve caido)    |    3
///
/// DUAS INJECOES NAO FORAM PEGAS NA PRIMEIRA TENTATIVA, e as duas ensinaram algo:
///   * trocar so o GATILHO do `TickDasAureolas` (e nao o `PacoteDeAureola`) nao muda o que sai no
///     fio -- os dois calculam a regra por conta propria, e o pacote continuou saindo certo;
///   * `VestirCorpoInteiro` estava descoberto nas TRES bancadas (esta, a `--alemteste` e a
///     `--morte`): todas olhavam corpos que ja existiam na tela quando a auréola acendeu. Foi por
///     isso que nasceu o TERCEIRO corpo desta bancada -- o que chega ja morto, de longe.
/// ================================================================================================================
///
/// COMO RODAR (uma janela so, o servidor sobe junto; ver `testar-velorio.bat`):
///     Godot --headless --path . --host --rede 7976 --velorio --raca Saiyan --nome Defunto
///           --conta bancada_velorio
///
/// ============================ ELA MATA O HOST DE VERDADE, E DESMONTA TUDO ============================
/// E o unico jeito: a triagem so leva pro Outro Mundo quem tem `Peer` (<see cref="Jandirus.Core.Npc.Gente"/>),
/// e corpo forjado nao tem. O host morre cinco vezes aqui dentro e volta vivo no fim; os corpos de
/// bancada saem do mundo pelo <see cref="GameServer.DesmontarOVelorioDeTeste"/>.
/// ================================================================================================
/// </summary>
public partial class RoboDoVelorio : Node
{
	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	private bool _acabou;
	private bool _pagouOEnma;
	private int _passo;
	private double _t, _espera;

	/// <summary>
	/// ESPERAR PRA SEMPRE NAO E REPROVAR. Toda espera desta bancada e por uma CONDICAO (a zona
	/// trocou, o corpo remoto nasceu), e uma condicao que nunca chega deixaria a janela parada em vez
	/// de dar placar -- pior que bancada nenhuma, porque num script ela pendura a fila inteira.
	/// </summary>
	private const double EsperaMaxima = 20.0;

	/// <summary>O corpo de CONTROLE: vivo do primeiro ao ultimo quadro. Ver o cabecalho.</summary>
	private int _oVivo;
	private int _oCidadao, _oReflexo, _oBoneco;

	/// <summary>O terceiro corpo: nasce longe, ja morto e ja viajado. Ver o passo 7.</summary>
	private int _oDeLonge;

	/// <summary>A caixa da auréola com os pes no chao -- a referencia da familia da subida.</summary>
	private Rect2? _caixaNoChao;

	private int _ok;

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (ok) _ok++;
		else _falhas.Add(oque);
	}

	private void Nota(string oque) => _passos.Add("  --     " + oque);

	private void Passar() { _passo++; _t = 0; _espera = 0; }

	// =====================================================================
	// AS DUAS PERGUNTAS QUE ESTA BANCADA FAZ O TEMPO TODO
	// =====================================================================
	/// <summary>
	/// ESTE CORPO TEM AUREOLA **NO FIO**? E o conjunto que o `S2C.Aureola` escreve no cliente.
	///
	/// Ela e a metade que sobrevive a um corpo que ainda nao nasceu na tela (o pacote e reliable e
	/// chega quando o servidor quer, inclusive antes do boneco existir).
	/// </summary>
	private static bool NoFio(int id) => GameClient.Instance?.ComAureola.Contains(id) == true;

	/// <summary>
	/// ESTE CORPO TEM AUREOLA **NO DESENHO**? E o `Visible` da camada, que e o que o jogador ve.
	///
	/// AS DUAS PERGUNTAS SAO COBRADAS SEMPRE JUNTAS. O fio sozinho ja passou verde com o desenho
	/// errado neste projeto (a familia do "uniform escrito != pixel desenhado"), e o desenho sozinho
	/// nao prova que a informacao veio do servidor.
	/// </summary>
	private bool NoDesenho(Node2D? corpo) =>
		corpo?.GetNodeOrNull<CharacterVisual>("Visual")?.AureolaVisivelDeTeste == true;

	private Node2D? Remoto(int id) => World.Instancia?.CorpoDeTeste(id);

	private static GameServer? Srv => GameServer.Instance;

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_acabou) return;

		if (GameClient.Instance is not { Connected: true } cli
		 || World.Instancia is not { } mundo
		 || Srv is not { } srv
		 || GetTree().Root.FindChild("LocalPlayer", true, false) is not Node2D eu
		 || eu.GetNodeOrNull<CharacterVisual>("Visual") == null)
		{
			_espera += delta;
			if (_espera > EsperaMaxima)
			{
				Conferir(false, $"em {EsperaMaxima:0}s o mundo subiu com o meu corpo dentro "
							  + "(e o servidor esta neste processo -- a bancada exige `--host`)");
				Fechar();
			}
			return;
		}

		_espera = 0;
		_t += delta;

		switch (_passo)
		{
			// -------------------------------------------------------------
			// 1) O CONTROLE -- dois corpos vivos, zero auréolas
			// -------------------------------------------------------------
			case 0:
				GD.Print("[velorio] ================ A MORTE, O OUTRO MUNDO E A AUREOLA, COM DOIS CORPOS ================");
				_oVivo = srv.ForjarNoVelorioDeTeste("vivo", cli.LocalId);
				Conferir(_oVivo != 0, "o corpo de CONTROLE nasceu ao meu lado (`velorio: o vivo`)");
				if (_oVivo == 0) { Fechar(); return; }
				Passar();
				break;

			case 1:
			{
				// O corpo remoto nasce pelo SNAPSHOT, que e por tique -- ele nao existe no quadro
				// seguinte ao nascimento no servidor.
				if (Remoto(_oVivo) is not { } vivo)
				{
					if (_t > EsperaMaxima)
					{ Conferir(false, "o corpo de controle apareceu na minha tela (snapshot)"); Fechar(); }
					return;
				}

				GD.Print("[velorio] -- 1) o controle: dois corpos vivos --");
				Conferir(!NoFio(cli.LocalId) && !NoDesenho(eu),
						 "VIVO eu nao tenho auréola -- nem no fio, nem no desenho");
				Conferir(!NoFio(_oVivo) && !NoDesenho(vivo),
						 "...e o corpo ao lado (`velorio: o vivo`) tambem nao -- a linha gemea, sem a "
					   + "qual `TemAureola => true` passaria verde");

				GameServer.FotoDoVelorio f = srv.FotoDoVelorioDeTeste(cli.LocalId);
				Conferir(f.Existe && !f.Morto && !f.KO && !f.Deitado,
						 "e o servidor concorda: eu estou vivo, de pe e inteiro");
				Passar();
				break;
			}

			// -------------------------------------------------------------
			// 2) O NOCAUTE -- o contra-exemplo
			// -------------------------------------------------------------
			case 2:
			{
				GD.Print("[velorio] -- 2) o nocaute NAO e a morte --");
				Conferir(srv.NocautearNoVelorioDeTeste(cli.LocalId, 3.0), "fui NOCAUTEADO (nao morto)");

				GameServer.FotoDoVelorio f = srv.FotoDoVelorioDeTeste(cli.LocalId);
				Conferir(f.KO && !f.Morto, "o servidor sabe a diferenca: KO ligado, `dead` desligado");
				Conferir(f.Deitado, "...e o corpo esta no chao -- que e onde nocaute e morte se PARECEM");
				Conferir(!NoFio(cli.LocalId) && !NoDesenho(eu),
						 "NOCAUTEADO nao tem auréola: e ela que separa os dois na tela");

				// A FORCA BRUTA: relogio vencido no corpo de um nocauteado. Quem recusa e o
				// `if (pl.Ficha.dead && agora >= pl.RelogioDaMorte)` do `TickCombate`, e essa e a
				// unica linha do jogo que impede o nocauteado de subir pro Outro Mundo.
				srv.VencerORelogioDoVelorioDeTeste(cli.LocalId);
				Passar();
				break;
			}

			case 3:
			{
				if (_t < 0.4) return;   // uns doze tiques do servidor: tempo de sobra pra viajar
				GameServer.FotoDoVelorio f = srv.FotoDoVelorioDeTeste(cli.LocalId);
				Conferir(!Alem.EhOAlem(f.Zona),
						 $"com o relogio VENCIDO A FORCA o nocauteado continua onde estava ({f.Zona}) "
					   + "-- o `dead` do tique e quem o segura");
				Conferir(!NoFio(cli.LocalId), "...e continua sem auréola");
				Passar();
				break;
			}

			// -------------------------------------------------------------
			// 3) O CADAVER -- morto no chao dos vivos, e SEM auréola
			// -------------------------------------------------------------
			case 4:
			{
				GD.Print("[velorio] -- 3) o cadaver --");

				// A BORDA DA SALA DO TEMPO ENTRA AQUI DE CARONA: marcar o preso ANTES desta morte
				// custa zero viagem, e a resposta e cobrada la em cima, na chegada ao alem.
				Conferir(srv.PrenderNaSalaNoVelorioDeTeste(cli.LocalId),
						 "BORDA: entro nesta morte marcado como PRESO na Sala do Tempo");

				Conferir(srv.MatarNoVelorioDeTeste(cli.LocalId),
						 "MORRI pelo funil de producao (`CombatState.Morrer`, o mesmo do soco letal)");

				GameServer.FotoDoVelorio f = srv.FotoDoVelorioDeTeste(cli.LocalId);
				Conferir(f.Morto, "o servidor me da por morto");
				Conferir(f.FaltamMs > 0 && f.FaltamMs <= Alem.MsNoChao,
						 $"...e o gancho `AoMorrer` armou o relogio do CADAVER sozinho "
					   + $"({f.FaltamMs} ms, teto {Alem.MsNoChao})");
				Conferir(!Alem.EhOAlem(f.Zona), $"...e eu ainda estou no mundo dos vivos ({f.Zona})");
				Conferir(f.Deitado && !f.DePe, "...caido, e nao de pe");
				Passar();
				break;
			}

			case 5:
			{
				if (_t < 0.3) return;   // deixa o `TickDasAureolas` (5 Hz) rodar mais de uma vez
				Conferir(!NoFio(cli.LocalId) && !NoDesenho(eu),
						 "O CADAVER NAO TEM AUREOLA -- o bug que o dono fotografou: *\"o corpo q fica "
					   + "no MAPA DOS VIVOS deveria ser o EXATO CORPO DELE QUANDO MORRE\"*");
				Conferir(Remoto(_oVivo) is { } v && !NoFio(_oVivo) && !NoDesenho(v),
						 "...e o vivo ao lado continua sem -- as duas cabecas no mesmo quadro");
				Nota("neste instante ha DOIS corpos caidos possiveis no jogo (o KO e o cadaver) e "
				   + "NENHUM dos dois desenha auréola: ela e da viagem, e nao do tombo");

				srv.VencerORelogioDoVelorioDeTeste(cli.LocalId);
				Passar();
				break;
			}

			// -------------------------------------------------------------
			// 4) A VIAGEM -- e a auréola acende NA CHEGADA
			// -------------------------------------------------------------
			case 6:
			{
				GD.Print("[velorio] -- 4) a viagem --");
				GameServer.FotoDoVelorio f = srv.FotoDoVelorioDeTeste(cli.LocalId);
				if (!Alem.EhOAlem(f.Zona))
				{
					if (_t > EsperaMaxima)
					{
						Conferir(false, "vencido o prazo do cadaver, o TIQUE me levou pro Outro Mundo "
									  + $"(estou em {f.Zona} -- a triagem nao rodou)");
						Fechar();
					}
					return;
				}

				Conferir(true, $"A MORTE LEVA PRO OUTRO MUNDO -- o pedido do dono ({f.Zona})");
				Conferir(f.Morto && f.DePe && !f.Deitado,
						 "...morto, mas DE PE (o `Un_KO()` do `Death.dm:89`)");
				Conferir(f.FaltamMs > Alem.MsNoChao,
						 $"...e o relogio rearmou pro prazo do ALEM ({f.FaltamMs} ms) -- sem isto o "
					   + "Outro Mundo duraria um quadro");
				Conferir(!f.PresoNaSala,
						 "BORDA (Sala do Tempo): cheguei ao alem JA SOLTO -- a tranca abre no "
					   + "`IrProAlem`, e nao no renascimento (senao a `SalaSessao` contaria o meu "
					   + "minuto de morto numa sala de outro planeta)");
				Passar();
				break;
			}

			case 7:
			{
				// A ZONA TROCOU DO LADO DE CA TAMBEM: o cliente descarrega a Terra e carrega o z6.
				// Enquanto isso o meu corpo local e refeito, e o `eu` deste quadro ja e o novo.
				if (!Alem.EhOAlem(cli.Zone.Name))
				{
					if (_t > EsperaMaxima)
					{ Conferir(false, "o CLIENTE tambem trocou de zona (o `ZoneChanged` chegou)"); Fechar(); }
					return;
				}
				if (_t < 0.4) return;

				Conferir(true, $"o cliente carregou o Outro Mundo ({cli.Zone.Name})");
				Conferir(NoFio(cli.LocalId),
						 "A AUREOLA ACENDEU NO FIO na chegada (`overlayList += 'Halo.dmi'`, "
					   + "`Death.dm:106-108`)");
				Conferir(NoDesenho(eu),
						 "...E VIROU PIXEL: a camada esta na tela, sobre a minha cabeca -- o pedido "
					   + "do dono, no desenho");

				// ============================ O TERCEIRO CORPO: UM MORTO QUE CHEGA DEPOIS ============================
				// Ele nasce LONGE (na Terra), ja morto e ja viajado, e so ENTRA no meu campo de visao
				// no passo seguinte. E o unico jeito de exercitar a linha que veste a auréola em quem
				// **nasce** na minha tela (`World.VestirCorpoInteiro`): o pacote e reliable e pode ter
				// chegado antes de o boneco existir.
				//
				// MEDIDO: apagando aquela linha, esta bancada, a `--alemteste` e a `--morte` ficavam
				// TODAS as tres verdes. O `--morte` olha um corpo que morre na frente dele -- ali o
				// boneco ja existia e quem acende e o `AoMudarAureola`.
				// ================================================================================================
				_oDeLonge = srv.ForjarNoVelorioDeTeste("morto de longe", _oVivo);
				Conferir(_oDeLonge != 0 && srv.MarcarMortoJaViajadoNoVelorioDeTeste(_oDeLonge),
						 "nasceu LONGE um terceiro corpo, morto e ja viajado (auréola acesa fora da "
					   + "minha vista)");

				srv.LevarProAlemNoVelorioDeTeste(_oVivo);
				srv.LevarProAlemNoVelorioDeTeste(_oDeLonge);
				Passar();
				break;
			}

			// -------------------------------------------------------------
			// 5) AS DUAS CABECAS -- dentro do alem, um morto e um vivo
			// -------------------------------------------------------------
			case 8:
			{
				if (Remoto(_oVivo) is not { } vivo)
				{
					if (_t > EsperaMaxima)
					{ Conferir(false, "o corpo de controle chegou ao Outro Mundo comigo"); Fechar(); }
					return;
				}
				if (_t < 0.3) return;

				GD.Print("[velorio] -- 5) as duas cabecas, no Outro Mundo --");
				Conferir(NoFio(cli.LocalId) && NoDesenho(eu), "EU: morto e com auréola");
				Conferir(!NoFio(_oVivo) && !NoDesenho(vivo),
						 "ELE: VIVO no meio dos mortos, sem auréola nenhuma -- e esta linha e a que "
					   + "mata o crivo por LUGAR (`morto && EhOAlem`), que apagaria a auréola do "
					   + "`KeepsBody` no dia em que ele for portado");
				Conferir(srv.FotoDoVelorioDeTeste(_oVivo) is { Existe: true, Morto: false } fv
						 && Alem.EhOAlem(fv.Zona),
						 "...e o servidor confirma que ele esta mesmo aqui dentro, vivo");

				// O TERCEIRO CORPO -- ver o passo anterior. Ele NASCEU na minha tela ja com auréola.
				Conferir(Remoto(_oDeLonge) is { } longe && NoFio(_oDeLonge) && NoDesenho(longe),
						 "e o corpo que chegou DEPOIS, ja morto, nasceu na minha tela COM a auréola "
					   + "-- quem veste quem nasce e o `VestirCorpoInteiro`, e ele le o conjunto que "
					   + "o fio ja tinha entregado (o pacote nao volta a sair so porque um boneco "
					   + "apareceu)");

				// ============================ E EU ME VEJO COMO OS OUTROS ME VEEM ============================
				// **ESTA FAMILIA NASCEU DE UM DEFEITO QUE A BANCADA ACHOU NA PRIMEIRA RODADA.** A caixa
				// de pixel da auréola veio 4x10 px com a base ABAIXO da origem do corpo -- que e o
				// estado `ko` da folha, o desenho da auréola AO LADO da cabeca de um corpo DEITADO. O
				// servidor dizia `DePe`, todas as outras telas desenhavam de pe, e a unica tela que
				// desenhava o morto no chao era a DELE PROPRIO: o `SheetState.Imobilizado` (`KO ||
				// Morto`) mandava no desenho, e ele nao sabe que a morte virou um percurso.
				//
				// Sem esta linha, o pedido do dono quebra justamente pra quem morreu: o jogador chega
				// ao Outro Mundo e se ve caido, com a auréola de lado. Ver `LocalPlayer._deitado`.
				// ==========================================================================================
				GameServer.FotoDoVelorio meu = srv.FotoDoVelorioDeTeste(cli.LocalId);
				CharacterVisual? vis = eu.GetNodeOrNull<CharacterVisual>("Visual");
				Conferir(meu.DePe && !meu.Deitado, "o servidor me da como morto DE PE aqui dentro");
				Conferir(vis != null && Mathf.IsZeroApprox(vis.RotationDegrees),
						 $"...e a MINHA tela concorda: o meu corpo nao esta girado no chao "
					   + $"({vis?.RotationDegrees:0.#} graus) -- as duas telas contando a mesma "
					   + "historia sobre o mesmo corpo");
				Conferir(vis?.CaixaDaAureolaDeTeste is { } cx && cx.Position.Y + cx.Size.Y <= 0.5f,
						 "...e a auréola esta SOBRE a cabeca, e nao ao lado dela (o estado `ko` da "
					   + $"folha desenha a auréola de um corpo caido, e ele so vale pro cadaver) "
					   + $"[pose do corpo: {vis?.AnimacaoDeTeste}]");
				Passar();
				break;
			}

			// -------------------------------------------------------------
			// 9) O BALAO -- a fala sobre a cabeca de quem ja tem uma auréola la
			// -------------------------------------------------------------
			case 9:
			{
				GD.Print("[velorio] -- 9) o balao de fala sobre o morto --");
				if (eu.GetNodeOrNull<BalaoDeFala>("Balao") is not { } balao)
				{ Conferir(false, "o meu corpo tem o filho `Balao`"); Passar(); return; }

				// PELO CAMINHO DO EVENTO (`World.AoFalar`), e nao pelo `Dizer` na mao: o portao de
				// canal e a busca do corpo pelo NOME sao regras, e sao elas que um morto poderia
				// perder sem ninguem notar (basta um `if (dead) return` numa camada acima).
				World.Instancia?.AoFalar(Protocol.Fala.Diz, cli.LocalName, "estou morto, e falo");

				Conferir(balao.Visible && balao.TextoDeTeste == "estou morto, e falo",
						 $"MORTO eu continuo falando, e a fala chega no balao (\"{balao.TextoDeTeste}\")");
				Conferir(NoDesenho(eu), "...e a auréola continua acesa enquanto ele fala");

				ACaixaDosDois(eu, balao);
				Passar();
				break;
			}

			// -------------------------------------------------------------
			// 10) AS DUAS INTERFACES
			// -------------------------------------------------------------
			case 10:
			{
				GD.Print("[velorio] -- 10) `INaoSomeComOCorpo` e `ISobeComOCorpo` --");
				ANaoDeclaracaoDoSumico(eu);
				Passar();
				break;
			}

			// A DURACAO DA ESQUIVA E PRIVADA (`EsquivaZanzoken.Duracao`), e copia-la aqui seria a
			// segunda casa de um numero que ja tem dono. A espera e pela CONDICAO -- o corpo voltou --,
			// que e o que a linha afirma de qualquer jeito.
			case 11:
			{
				bool voltou = eu.GetNodeOrNull<CharacterVisual>("Visual") is { } v && v.IsVisibleInTree();
				if (!voltou && _t < 3.0) return;
				Conferir(voltou && NoDesenho(eu),
						 "...e passada a esquiva o corpo volta E a auréola volta com ele (o "
					   + "`_ExitTree` devolve o `Visible` de cada filho que ele guardou)");
				Passar();
				break;
			}

			case 12:
				if (!ANaoDeclaracaoDaSubida(eu, cli))
				{
					if (_t > 6.0)
					{
						Conferir(false, "o meu desenho subiu com a altitude injetada (sem isso a "
									  + "familia da subida nao tem o que medir)");
						Passar();
					}
					return;
				}
				Passar();
				break;

			// -------------------------------------------------------------
			// 6) A VOLTA -- a auréola some sozinha, e ninguem fica preso
			// -------------------------------------------------------------
			case 13:
			{
				GD.Print("[velorio] -- 6) a volta --");
				Conferir(srv.ReviverNoVelorioDeTeste(cli.LocalId), "REVIVI, e no lugar (ninguem me moveu)");
				Passar();
				break;
			}

			case 14:
			{
				if (_t < 0.3) return;
				GameServer.FotoDoVelorio f = srv.FotoDoVelorioDeTeste(cli.LocalId);
				Conferir(!NoFio(cli.LocalId) && !NoDesenho(eu),
						 "A AUREOLA SUMIU SOZINHA -- nenhum caminho de revive tem uma linha pra ela: "
					   + "ela e uma conjuncao com `dead`, e apagar a morte a apaga");
				Conferir(Alem.EhOAlem(f.Zona) && !f.Morto,
						 $"...e eu continuo NO ALEM, vivo ({f.Zona}) -- a segunda prova de que a "
					   + "auréola nao e do lugar");

				// E AGORA A MORTE DENTRO DO ALEM: o cadaver dela (fase seguinte) e o que separa QUANDO
				// de ONDE, e o que vem depois do cadaver ja nao e uma volta -- o `PassoDaMorte` so
				// tranca o relogio de quem ja esta la; a saida e paga (o Enma, na fase 6b).
				Conferir(srv.MatarNoVelorioDeTeste(cli.LocalId), "morro de novo, agora DENTRO do alem");
				Passar();
				break;
			}

			// -------------------------------------------------------------
			// O CADAVER QUE MORREU **DENTRO** DO ALEM -- o caso que separa QUANDO de ONDE
			// -------------------------------------------------------------
			case 15:
			{
				if (_t < 0.4) return;   // duas voltas do `TickDasAureolas` (5 Hz)

				// ============================ ESTA LINHA E A UNICA QUE PEGA O CRIVO POR LUGAR ============================
				// Um `TemAureola => morto && EhOAlem(zona)` -- a correcao obvia, e a errada -- passa
				// verde em TODAS as outras linhas desta bancada. So aqui ele reprova: este corpo esta
				// morto, esta no Outro Mundo, e AINDA NAO VIAJOU (a viagem dele so vai acontecer daqui
				// a 15 s, e vai ser um renascimento). Ele e um cadaver, e cadaver nao tem auréola --
				// esteja onde estiver.
				//
				// E e o mesmo defeito que apagaria a auréola do `KeepsBody` (o morto que anda entre os
				// vivos, `OtherworldRankSkills.dm:195-202`) no dia em que ele for portado -- e ninguem
				// ligaria uma coisa a outra.
				// ========================================================================================================
				Conferir(!NoFio(cli.LocalId) && !NoDesenho(eu),
						 "o CADAVER de quem morre DENTRO do alem tambem nao tem auréola -- a pergunta "
					   + "e QUANDO (ja viajou?) e nao ONDE (esta no alem?)");

				srv.VencerORelogioDoVelorioDeTeste(cli.LocalId);
				Passar();
				break;
			}

			case 16:
			{
				GameServer.FotoDoVelorio f = srv.FotoDoVelorioDeTeste(cli.LocalId);

				// ---- 6a) O RELOGIO VENCEU DENTRO DO ALEM, E NADA ME LEVA ----
				// Ate 2026-09-04 esta fase esperava o `Renascer` me devolver ao berco ("ninguem fica
				// preso"). O dono pediu o contrario -- *"voce teria que ficar morto ate alguem te
				// reviver com as esferas, ou juntar 1 milhao de zeni e pagar o Enma Daioh"* --, e a
				// prova virou o AVESSO: com o relogio vencido eu continuo morto e la, de pe, com a
				// auréola acesa e o relogio trancado. O que me tira e o Enma, logo abaixo.
				if (!_pagouOEnma)
				{
					if (_t < 0.6) return;   // um tique da triagem + duas voltas do `TickDasAureolas`
					Conferir(Alem.EhOAlem(f.Zona) && f.Morto,
							 $"NINGUEM VOLTA SOZINHO: com o relogio vencido DENTRO do alem eu continuo "
						   + $"morto e la ({f.Zona}) -- o `PassoDaMorte` nao chama mais `Renascer`");
					Conferir(f.FaltamMs > 1_000_000,
							 "...e o relogio foi TRANCADO (o funil nao reexamina quem ja esta la)");
					Conferir(f.DePe && !f.Deitado,
							 "...e eu estou DE PE: o cadaver dos 15 s acabou, e morto no Outro Mundo anda");
					Conferir(NoFio(cli.LocalId) && NoDesenho(eu),
							 "...COM a auréola: a etapa de cadaver passou (`MorteJaViajou` acende no "
						   + "`PassoDaMorte` de quem ja esta no alem) -- de pe e sem auréola seria o "
						   + "par que nunca deve existir junto");
					Conferir(srv.PagarOEnmaNoVelorioDeTeste(cli.LocalId),
							 "PAGUEI O ENMA na mesa dele (1.000.000 de zeni) -- o caminho de producao "
						   + "da volta, `EnmaReviverPorZeni`");
					_pagouOEnma = true;
					_t = 0;
					return;
				}

				// ---- 6b) A VOLTA PAGA ----
				// O `Renascer` rodou DENTRO do meu `_Process` (o helper e sincrono), e o bit da auréola
				// so sai no `TickDasAureolas` seguinte (5 Hz): a foto do servidor diz "vivo, no berco"
				// antes de o fio dizer "sem auréola". Tres tiques, e ai as perguntas sao ESTRITAS.
				if (_t < 0.6) return;
				if (Alem.EhOAlem(f.Zona) || f.Morto)
				{
					if (_t > EsperaMaxima)
					{
						Conferir(false, $"A VOLTA PAGA nao me devolveu: estou {(f.Morto ? "morto" : "vivo")} em {f.Zona}");
						Fechar();
					}
					return;
				}

				Conferir(true, $"A VOLTA E PAGA -- o Enma me devolve vivo ao berco ({f.Zona})");
				Conferir(!NoFio(cli.LocalId), "...sem auréola");
				Conferir(f.FaltamMs <= 0, "...e com o relogio da morte zerado (o funil nao reexamina "
										+ "um corpo vivo)");
				Conferir(f.DebuffDoEnma,
						 "...e com o debuff do Enma no corpo (`zeni_revive_debuff_until`: 25% do BP por uma hora)");
				Passar();
				break;
			}

			// -------------------------------------------------------------
			// 7) QUEM NAO VAI PRO ALEM -- um corpo nomeado por linha
			// -------------------------------------------------------------
			case 17:
			{
				if (_t < 0.5) return;   // o cliente acabou de trocar de zona: deixa o mundo assentar
				GD.Print("[velorio] -- 7) os corpos que NAO vao --");

				_oCidadao = srv.ForjarNoVelorioDeTeste("cidadao", cli.LocalId);
				_oReflexo = srv.ForjarNoVelorioDeTeste("reflexo", cli.LocalId);
				_oBoneco = srv.ForjarNoVelorioDeTeste("boneco", cli.LocalId);
				Conferir(_oCidadao != 0 && _oReflexo != 0 && _oBoneco != 0,
						 "nasceram os tres corpos da triagem (cidadao, reflexo, boneco largado)");

				foreach (int id in new[] { _oCidadao, _oReflexo, _oBoneco })
				{
					srv.MatarNoVelorioDeTeste(id);
					srv.VencerORelogioDoVelorioDeTeste(id);
				}
				Passar();
				break;
			}

			case 18:
			{
				if (_t < 0.5) return;   // o tique tem que rodar a triagem E a remocao dos NPCs

				GameServer.FotoDoVelorio cid = srv.FotoDoVelorioDeTeste(_oCidadao);
				Conferir(!cid.Existe || cid.SaindoDoMundo,
						 "o CIDADAO (NPC do mundo) SAIU do mundo -- nao renasceu na Terra nem "
					   + "apareceu no Outro Mundo (as duas alternativas o deixariam no `_players`); "
					   + "quem repoe habitante e a manutencao do povoamento");
				Conferir(Remoto(_oCidadao) == null, "...e o boneco dele sumiu da minha tela");

				GameServer.FotoDoVelorio refl = srv.FotoDoVelorioDeTeste(_oReflexo);
				Conferir(refl.Existe && !Alem.EhOAlem(refl.Zona) && refl.Morto,
						 $"o REFLEXO DA MENTE (sem dono, sem papel) nao viaja e nao renasce vivo "
					   + $"({refl.Zona}) -- era o `else` largo que o poria de pe na mesa do Enma");
				Conferir(refl.FaltamMs > 1_000_000,
						 "...e o relogio dele foi desarmado pra sempre (o funil nao o reexamina "
					   + "a cada tique)");

				GameServer.FotoDoVelorio bon = srv.FotoDoVelorioDeTeste(_oBoneco);
				Conferir(bon.Existe && !Alem.EhOAlem(bon.Zona) && bon.FaltamMs > 1_000_000,
						 "o BONECO DO CORPO LARGADO nao vai, **mesmo carregando o `Peer` do dono** -- "
					   + "e o unico corpo que passa nas duas primeiras pernas do crivo, e quem o "
					   + "recusa e a terceira (`DonoDoCorpoLargado`)");
				Conferir(!NoFio(_oReflexo) && !NoFio(_oBoneco),
						 "...e nenhum dos dois acendeu auréola: quem nao viaja nao ganha uma");
				Passar();
				break;
			}

			// -------------------------------------------------------------
			// 8) AS BORDAS -- a mente e a ponte (a Sala ja foi medida na chegada)
			// -------------------------------------------------------------
			case 19:
				GD.Print("[velorio] -- 8) as bordas --");
				Conferir(srv.MergulharNaMenteNoVelorioDeTeste(cli.LocalId),
						 "BORDA (mente): mergulhei em transe e larguei o corpo pra tras");
				Conferir(srv.MatarNoVelorioDeTeste(cli.LocalId), "...e morri LA DENTRO");
				Passar();
				break;

			case 20:
			{
				GameServer.FotoDoVelorio f = srv.FotoDoVelorioDeTeste(cli.LocalId);
				if (f.NaMente || f.Morto)
				{
					if (_t > EsperaMaxima)
					{
						Conferir(false, "morte na mente NAO e morte: o `BordasDeQuemEstaFora` me "
									  + $"acorda no corpo (estou {(f.Morto ? "morto" : "vivo")} em {f.Zona})");
						Fechar();
					}
					return;
				}

				Conferir(!f.Morto && !f.NaMente && !Alem.EhOAlem(f.Zona),
						 $"BORDA (mente): morrer no transe me devolve VIVO ao corpo ({f.Zona}) -- o "
					   + "*\"morte MENTAL nao e real\"* do `MindMeditate.dm:448`, sem um `if` novo "
					   + "no funil da morte");
				Conferir(!NoFio(cli.LocalId),
						 "...e a auréola nunca chegou a acender (o prazo dos 15 s nem venceu)");

				Conferir(srv.PorNaPonteNoVelorioDeTeste(cli.LocalId, 9_401),
						 "BORDA (ponte): fui posto numa ZONA DINAMICA (o interior de uma nave)");
				Passar();
				break;
			}

			case 21:
				if (_t < 0.4) return;
				Conferir(srv.MatarNoVelorioDeTeste(cli.LocalId), "...e morri la dentro");
				srv.VencerORelogioDoVelorioDeTeste(cli.LocalId);
				Passar();
				break;

			case 22:
			{
				GameServer.FotoDoVelorio f = srv.FotoDoVelorioDeTeste(cli.LocalId);
				if (!Alem.EhOAlem(f.Zona))
				{
					if (_t > EsperaMaxima)
					{
						Conferir(false, "BORDA (ponte): a viagem sabe sair de uma zona dinamica "
									  + $"(estou em {f.Zona})");
						Fechar();
					}
					return;
				}

				Conferir(true, "BORDA (ponte): morrer numa zona dinamica leva pro Outro Mundo igual "
							 + "-- o `MoveToZone` nao pergunta de onde o corpo vem");
				// A VOLTA E PAGA (2026-09-04): vencer o relogio de novo so o trancaria (fase 6a); quem
				// me tira do alem e o Enma, e a pergunta desta borda e ONDE ele me poe.
				Conferir(srv.PagarOEnmaNoVelorioDeTeste(cli.LocalId),
						 "...e de la eu pago o Enma (a unica volta que nao depende de um vivo ao lado)");
				Passar();
				break;
			}

			case 23:
			{
				GameServer.FotoDoVelorio f = srv.FotoDoVelorioDeTeste(cli.LocalId);
				if (f.Morto || Alem.EhOAlem(f.Zona))
				{
					if (_t > EsperaMaxima)
					{ Conferir(false, "BORDA (ponte): e a volta paga me tira de la"); Fechar(); }
					return;
				}

				Conferir(!f.NaPonte,
						 $"BORDA (ponte): a volta paga me poe no BERCO ({f.Zona}) e nao dentro da nave -- o "
					   + "`DestinoDe` nao devolve ninguem pra uma zona que pode ter deixado de existir");
				Passar();
				break;
			}

			default:
				Fechar();
				break;
		}
	}

	// =====================================================================
	// 9) A CAIXA DOS DOIS -- em pixel aceso, e nao na conta de cabeca
	// =====================================================================
	/// <summary>
	/// ============================ O BALAO E A AUREOLA CABEM OS DOIS ============================
	/// **Esta era a unica afirmacao desta feature sem prova automatica.** A entrega anterior fechou a
	/// conta na mao: *"o sprite e 32x32 `Centered`, o topo do tile e -16, o balao senta em -26, sobram
	/// 10 px"*. A conta esta certa e envelhece calada -- ela quebra se alguem mexer no
	/// <see cref="BalaoDeFala.AlturaBase"/> (cinco pixels a menos e as duas coisas se encostam) ou
	/// trocar a folha da auréola por uma com o desenho mais alto no tile.
	///
	/// Aqui a caixa da auréola e o RETANGULO DE PIXEL ACESO do quadro que esta na tela (ver
	/// <see cref="CharacterVisual.CaixaDaAureolaDeTeste"/>), e nao o tile inteiro: e o unico numero
	/// que responde por "o desenho invade o balao?".
	/// =====================================================================================
	/// </summary>
	private void ACaixaDosDois(Node2D eu, BalaoDeFala balao)
	{
		if (eu.GetNodeOrNull<CharacterVisual>("Visual") is not { } vis
		 || vis.CaixaDaAureolaDeTeste is not { } caixa)
		{ Conferir(false, "deu pra medir a caixa de pixel aceso da auréola"); return; }

		_caixaNoChao = caixa;   // a referencia da familia da subida, medida com os pes no chao

		// EM ESPACO DO CORPO: a caixa sai em coordenadas do `Visual`, e o balao e irmao dele.
		float topoDaAureola = vis.Position.Y + caixa.Position.Y;
		float baseDaAureola = topoDaAureola + caixa.Size.Y;
		float baseDoBalao = balao.Position.Y;   // ele cresce pra CIMA a partir daqui

		Conferir(caixa.Size.X > 0 && caixa.Size.Y > 0 && caixa.Size.X <= 32 && caixa.Size.Y <= 32,
				 $"a auréola desenha dentro do tile de 32 px (caixa {caixa.Size.X:0}x{caixa.Size.Y:0} px)");
		Conferir(baseDaAureola <= 0.5f,
				 $"...e ela desenha SOBRE A CABECA e nao no meio do corpo (a base dela fica em "
			   + $"{baseDaAureola:0.#} px, acima da origem do corpo)");
		Conferir(baseDoBalao < topoDaAureola,
				 $"o BALAO comeca acima do topo da auréola -- folga de "
			   + $"{topoDaAureola - baseDoBalao:0.#} px (balao em {baseDoBalao:0.#}, auréola em "
			   + $"{topoDaAureola:0.#}); as duas coisas cabem sobre a mesma cabeca");
	}

	// =====================================================================
	// 10) AS DUAS INTERFACES QUE A AUREOLA **NAO** DECLARA
	// =====================================================================
	/// <summary>
	/// ============================ `INaoSomeComOCorpo` -- NAO, E ISSO E A DECISAO ============================
	/// A auréola e **pixel do corpo**, nao rotulo sobre ele: no DM ela e `overlays` do mob
	/// (`OverlayMobHandlers.dm:17-18`) e o `flick('Zanzoken.dmi')` trocava o `icon` inteiro, levando-a
	/// junto. Auréola pairando sozinha sobre tres listras seria o pior pixel da tela.
	///
	/// A PROVA E O CONTRASTE, e por isso ela e uma linha so com DOIS sujeitos: no MESMO quadro, a
	/// auréola sai da tela e o balao FICA -- e o balao declara a interface. Sem o contraste, "a
	/// auréola sumiu" tambem ficaria verde se o efeito tivesse apagado a tela inteira.
	///
	/// COMO REPROVA: escreva `INaoSomeComOCorpo` no `CharacterVisual` (o "conserto" plausivel, que
	/// alguem faz achando que auréola e rotulo) e esta linha fica vermelha.
	/// ====================================================================================================
	/// </summary>
	private void ANaoDeclaracaoDoSumico(Node2D eu)
	{
		if (eu.GetNodeOrNull<CharacterVisual>("Visual") is not { } vis) return;
		if (eu.GetNodeOrNull<BalaoDeFala>("Balao") is not { } balao) return;

		Conferir(vis is not INaoSomeComOCorpo,
				 "a auréola mora no `CharacterVisual`, que NAO declara `INaoSomeComOCorpo` -- ela e "
			   + "pixel do corpo, e some com ele");
		Conferir(balao is INaoSomeComOCorpo,
				 "...e o balao declara (rotulo nao e corpo) -- os dois sujeitos do contraste");

		EsquivaZanzoken.Trocar(eu);

		Conferir(!vis.IsVisibleInTree(),
				 "ESQUIVA: o corpo inteiro sai da tela, e a auréola vai junto no colo dele");
		Conferir(vis.AureolaVisivelDeTeste,
				 "...sem que ninguem apague a CAMADA (ela nao decidiu sumir: o pai e que sumiu)");
		Conferir(balao.IsVisibleInTree(),
				 "...e o balao continua na tela no MESMO quadro -- o contraste que faz a linha de "
			   + "cima significar alguma coisa");
	}

	/// <summary>
	/// ============================ `ISobeComOCorpo` -- NAO, E ISSO NAO E "fica no chao" ============================
	/// Aquela interface e pro node com ALTURA PROPRIA em repouso, que precisa SOMAR o deslocamento do
	/// voo em vez de receber a posicao crua. A auréola nao tem altura propria -- o "sobre a cabeca"
	/// esta nos PIXELS do sprite -- e, sendo camada do `Visual`, ela nem chega ao `SubirComOVoo`, que
	/// percorre os filhos do CORPO. Ela sobe de graca no colo do pai, como o cabelo.
	///
	/// A MEDIDA E O ACORDO, e nao a altura: o servidor reafirma "altitude 0" trinta vezes por segundo
	/// e o desenho ja comeca a descer no quadro seguinte. O que tem que ser verdade em qualquer ponto
	/// da subida e que o VISUAL recebeu o empurrao cru e o BALAO recebeu o empurrao SOMADO a altura
	/// dele -- e que a auréola nao recebeu empurrao nenhum, porque ela viaja no pai.
	///
	/// COMO REPROVA: declare `ISobeComOCorpo` na auréola e some o deslocamento nela -- a caixa dela
	/// desce (ou sobe) o dobro e a primeira linha cai. Escreva `Position = deslocamento` cru no balao
	/// (o defeito historico) e a segunda cai.
	/// ========================================================================================================
	/// </summary>
	private bool ANaoDeclaracaoDaSubida(Node2D eu, GameClient cli)
	{
		if (eu.GetNodeOrNull<CharacterVisual>("Visual") is not { } vis) return false;
		if (eu.GetNodeOrNull<BalaoDeFala>("Balao") is not { } balao) return false;

		// A ALTITUDE INJETADA PELO CAMINHO DO SNAPSHOT -- o unico por onde o corpo local sobe.
		World.Instancia?.AoReceberSnapshot([new EntityState
		{
			Id = cli.LocalId,
			Facing = (byte)Facing.South,
			Pose = Protocol.Pose.Normal,
			Voando = true,
			Altitude = 200f,
		}]);

		float empurrao = vis.Position.Y;
		if (empurrao > -1f || vis.CaixaDaAureolaDeTeste is not { } noAr) return false;   // ainda no chao
		if (_caixaNoChao is not { } noChao)
		{ Conferir(false, "a caixa da auréola foi medida NO CHAO antes de subir"); return true; }

		Conferir(Mathf.Abs(balao.Position.Y - (BalaoDeFala.AlturaBase + empurrao)) < 0.01f,
				 $"VOANDO: o balao SOMOU o empurrao ({empurrao:0.#} px) a altura propria dele "
			   + $"({BalaoDeFala.AlturaBase:0.#}) -- e o `ISobeComOCorpo` que ele declara");

		// A CAIXA DA AUREOLA E MEDIDA NA REGUA DO PAI. Se ela recebesse o empurrao (como receberia
		// declarando `ISobeComOCorpo`), este numero mudaria -- e no mundo ela teria subido DUAS vezes,
		// descolando da cabeca exatamente no ar, que e onde ninguem procuraria o defeito.
		Conferir(Mathf.IsEqualApprox(noAr.Position.Y, noChao.Position.Y),
				 $"...e a auréola nao recebeu empurrao NENHUM: a caixa dela e a mesma no chao e no ar "
			   + $"({noChao.Position.Y:0.##} px do `Visual`) -- quem subiu foi o pai, e ela foi no colo");
		Conferir(vis.GetParent() == eu,
				 "...porque ela e neta do corpo, e o `SubirComOVoo` so percorre os FILHOS dele "
			   + "(por isso a auréola nao precisa -- nem pode -- declarar `ISobeComOCorpo`)");
		return true;
	}

	// =====================================================================
	private void Fechar()
	{
		_acabou = true;

		// DESMONTAR ANTES DO PLACAR: se a bancada abortar no meio, os corpos de bancada nao podem
		// ficar no mundo -- e o `Peer` emprestado ao boneco tem que voltar pro dono.
		Srv?.DesmontarOVelorioDeTeste();

		foreach (string p in _passos) GD.Print("[velorio] " + p);
		GD.Print($"[velorio] ================ {_ok} passaram, {_falhas.Count} falharam ================");
		foreach (string f in _falhas) GD.PrintErr("[velorio]   FALHA: " + f);

		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
