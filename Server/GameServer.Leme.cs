using Jandirus.Core.Items;
using Jandirus.Core.Tech;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// (2026-10-19) O CASCO ISOLA QUEM PILOTA. O dono: *"planetas com gravidade quando pilota a nave o personagem piloto sofre os efeitos
/// da gravidade, oq n deveria acontecer, e tb ta dando pra usar after image enquanto pilota a nave, faça com q nenhum efeito externo
/// afete o personagem enquanto esta pilotando a nave e n da pra usar nenhuma skill ou poder enquanto esta pilotando"*. Perguntado no
/// mesmo dia, ele escolheu: os golpes de outros NAO pegam em quem pilota; as tecnicas acesas CAEM ao embarcar, e a forma fica (sem subir
/// nem descer ao leme); e na nave grande o corpo da ponte -- que divide a vida e o estado de combate com quem esta ao leme -- fica
/// blindado junto.
///
/// "AO LEME" E O `PilotoId` DE UMA NAVE (`EstaPilotando`): o pod, o foguete e a Capital Ship. A pergunta de todos os pontos abaixo e
/// <see cref="CascoIsola"/>, e nada e guardado: desembarcar, voltar a ponte e a nave explodir derrubam tudo no mesmo gesto.
///
/// ============================ DE FORA PRA DENTRO: O MUNDO NAO ALCANCA O PILOTO ============================
///   * a GRAVIDADE -- a do chao (`AplicarGravidade`) e a do campo da maquina (`LerOsCampos`). Ao leme o corpo sente a do espaco
///     (`Leme.GravidadeAoLeme`), e com ela vai TUDO o que a gravidade faz, porque tudo le a mesma `Planetgrav + gravmult`: o
///     esmagamento (dano, folego, lentidao, a prisao no chao), o ganho de treino, a maestria e o poder expresso (`gravFelt`). A
///     gravidade so e reescrita na troca de zona, entao os portoes que trocam o piloto de estado sem viagem (embarcar, desembarcar, a
///     nave grande que explode) a reaplicam na hora (<see cref="TomarOLeme"/>, <see cref="LargarOLeme"/>);
///   * a LUA CHEIA e o RAIO da tempestade -- o casco e teto (`SobTeto`): o Saiyajin nao vira Oozaru pilotando;
///   * o AR DIVINO (`EmAtmosferaDivina`) e a cura da torre do Campo Bio (`TickDoCampoBio`);
///   * GOLPE NENHUM -- quem pilota e `Intocavel` (o gancho `CombatState.AoLeme`): a busca do soco, o varredor do projetil, o cone das
///     tecnicas, o agarrao, a absorcao e o funil unico do dano (`CombatState.Ferir`), que leva junto o dano em area e o veneno que ja
///     estava no corpo.
///   (O VACUO e a ESTRELA ja poupavam quem pilota -- `Vacuo.RespiraNoVacuo` e `GameServer.Sol.cs`.)
/// ======================================================================================================
///
/// ============================ DE DENTRO PRA FORA: O PILOTO NAO USA PODER ============================
///   * o DESPACHO engole os comandos de corpo de quem esta ao leme (`ComandoDeCorpo`: soco, guarda, carga, habilidade, transformacao,
///     Zanzoken, atividade) -- o mesmo portao do olho de fora e de quem perdeu as redeas --, e o jogador ouve por que
///     (<see cref="RecusarComandoAoLeme"/>). E por ali que passava o After Image: o duplo clique, a Zanzoken Afterimage e a investida;
///   * os poderes que viajam pelo canal de verbos tambem (<see cref="VerbosDePoder"/>);
///   * ao EMBARCAR caem as tecnicas acesas (<see cref="ApagarAsTecnicasAoLeme"/>).
/// ==================================================================================================
///
/// DESIGN NOVO DO DONO, contra o DM: la o pod copia a posicao do piloto (`PlanetTech.dm:140-144`), e o piloto e um corpo comum no mapa.
/// </summary>
public partial class GameServer
{
	// ============================ OS DEFEITOS INJETADOS (bancada `--lemeteste`) -- falsos em jogo, sempre ============================
	/// <summary>DEFEITO INJETADO (`--lemeteste`): ao leme o corpo volta a sentir a gravidade do chao -- o jogo de antes. Lido pelo <see cref="GravidadeSentida"/>.</summary>
	internal static bool LemeSenteAGravidadeDeTeste;

	/// <summary>DEFEITO INJETADO (`--lemeteste`): o campo da maquina de gravidade alcanca quem esta ao leme. Lido pelo `LerOsCampos`.</summary>
	internal static bool LemeNoCampoDeTeste;

	/// <summary>DEFEITO INJETADO (`--lemeteste`): o casco nao e teto -- a lua cheia pega o Saiyajin ao leme. Lido pelo `SobTeto`.</summary>
	internal static bool LemeSemTetoDeTeste;

	/// <summary>DEFEITO INJETADO (`--lemeteste`): ao leme se respira o ar divino de fora. Lido pelo `EmAtmosferaDivina`.</summary>
	internal static bool LemeRespiraOArDeTeste;

	/// <summary>DEFEITO INJETADO (`--lemeteste`): a torre do Campo Bio cura quem esta ao leme. Lido pelo `TickDoCampoBio`.</summary>
	internal static bool LemeNoCampoBioDeTeste;

	/// <summary>DEFEITO INJETADO (`--lemeteste`): ao leme se apanha -- o casco nao blinda. Lido pelo gancho `CombatState.AoLeme` (`PrepararCombate`).</summary>
	internal static bool LemeApanhaDeTeste;

	/// <summary>DEFEITO INJETADO (`--lemeteste`): ao leme os comandos de corpo e os verbos de poder passam. Lido pelo <see cref="RecusarAoLeme"/>.</summary>
	internal static bool LemeUsaPoderDeTeste;

	/// <summary>DEFEITO INJETADO (`--lemeteste`): embarcar nao apaga as tecnicas acesas. Lido pelo <see cref="ApagarAsTecnicasAoLeme"/>.</summary>
	internal static bool LemeLevaAsTecnicasDeTeste;
	// ================================================================================================================================

	/// <summary>O CASCO ISOLA ESTE CORPO? Ele esta ao leme de alguma nave -- o pod, o foguete ou a Capital Ship.</summary>
	private bool CascoIsola(ServerPlayer pl) => EstaPilotando(pl.Id);

	/// <summary>A GRAVIDADE DO CHAO QUE ESTE CORPO SENTE: a do casco ao leme, a do chao fora dele. Lida pelo `AplicarGravidade`.</summary>
	private double GravidadeSentida(ServerPlayer pl, double doChao) =>
		!LemeSenteAGravidadeDeTeste && CascoIsola(pl) ? Leme.GravidadeAoLeme : doChao;

	/// <summary>
	/// OS PODERES QUE VIAJAM PELO CANAL DE VERBOS, e nao como comando de corpo: a tecla de uma forma, o gatilho da arma de fogo, a escolha
	/// do Materializar (que cai no `UsarHabilidade` por dentro), a energia doada a Genkidama e as ordens das copias do Split Form.
	/// Desfazer as copias e recusar a Genkidama ficam de fora -- sao desligar. Um poder novo pelo canal de verbos entra aqui.
	/// </summary>
	private static readonly HashSet<string> VerbosDePoder = new(StringComparer.Ordinal)
	{
		"forma", ArmasDeFogo.VerboAtirar, Materializacao.VerboDeCanal,
		"genkidama_doar", "splitform_seguir", "splitform_parar", "splitform_alvo", "splitform_perto",
	};

	/// <summary>Este verbo e um poder? Ver <see cref="VerbosDePoder"/>. (O `Verbo` pergunta antes de tudo.)</summary>
	private static bool VerboDePoder(string cmd) => VerbosDePoder.Contains(cmd);

	/// <summary>Quando o proximo aviso de "ao leme nao se usa poder" pode sair (ms de relogio do servidor).</summary>
	private sealed class AvisoDoLeme
	{
		public long Ate;
	}

	/// <summary>
	/// O RELOGIO DO AVISO, POR CORPO (o objeto), e nao por id -- a tabela fraca do `_ancorasDeAtividade` (`GameServer.Atividade.cs`): o
	/// registro morre com o corpo, sem faxina no logout, e um id reusado nao herda o relogio de ninguem.
	/// </summary>
	private readonly System.Runtime.CompilerServices.ConditionalWeakTable<ServerPlayer, AvisoDoLeme> _avisosDoLeme = new();

	/// <summary>
	/// AO LEME NAO SE USA PODER: devolve se recusou, e diz por que -- uma vez a cada `Leme.SegundosEntreAvisos`, porque os pacotes de
	/// corpo chegam em rajada.
	/// </summary>
	private bool RecusarAoLeme(ServerPlayer pl)
	{
		if (LemeUsaPoderDeTeste || NaveDoPiloto(pl) is not { } nave) return false;

		AvisoDoLeme aviso = _avisosDoLeme.GetOrCreateValue(pl);
		long agora = NowMs();
		if (agora >= aviso.Ate)
		{
			aviso.Ate = agora + (long)(Leme.SegundosEntreAvisos * 1000);
			Avisar(pl, Leme.NaoSeUsaPoder(nave.Tipo));
		}
		return true;
	}

	/// <summary>
	/// O PORTAO DO DESPACHO PRA QUEM ESTA AO LEME (o `Handle`): devolve se o pacote foi engolido. O gesto de DESLIGAR -- soltar a guarda,
	/// soltar a carga, parar a atividade -- passa: o embarque ja desligou tudo (<see cref="ApagarAsTecnicasAoLeme"/>), e engolir a tecla
	/// solta so faria o cliente e o servidor discordarem sobre ela (e o jogador ouviria uma recusa por ter soltado uma tecla).
	/// </summary>
	/// <param name="r">O leitor do proprio pacote, com o opcode ja lido. So se ESPIA o byte seguinte: o `switch` do `Handle` le de novo.</param>
	private bool RecusarComandoAoLeme(ServerPlayer pl, Protocol.C2S id, NetDataReader r) =>
		CascoIsola(pl) && !SoDesliga(id, r) && RecusarAoLeme(pl);

	/// <summary>O pacote e o gesto de DESLIGAR alguma coisa? A guarda e a carga baixadas, a atividade parada.</summary>
	private static bool SoDesliga(Protocol.C2S id, NetDataReader r) => r.AvailableBytes > 0 && id switch
	{
		Protocol.C2S.Guard or Protocol.C2S.Carregar => !r.PeekBool(),
		Protocol.C2S.Activity => r.PeekByte() == (byte)Protocol.Activity.Parado,
		_ => false,
	};

	/// <summary>
	/// ASSUMIR O LEME -- o pod no `UsarNave`, a Capital Ship no `PilotarDaPonte`, ja com o `PilotoId` escrito: as tecnicas acesas caem, e
	/// a gravidade passa a ser a do casco.
	/// </summary>
	private void TomarOLeme(ServerPlayer pl)
	{
		ApagarAsTecnicasAoLeme(pl);
		AplicarGravidade(pl);
	}

	/// <summary>
	/// LARGAR O LEME SEM TROCAR DE ZONA -- desembarcar do pod, a nave grande que explode, ja com o `PilotoId` zerado: o chao volta a
	/// pesar. (Quem volta a ponte troca de zona, e o `MoveToZone` ja reaplica.)
	/// </summary>
	private void LargarOLeme(ServerPlayer pl) => AplicarGravidade(pl);

	/// <summary>
	/// AS TECNICAS ACESAS CAEM AO EMBARCAR (o dono, 2026-10-19). O Kaio-ken (pela porta dele, que tambem esquece o multiplicador), os outros
	/// buffs, as miragens do After Image, a carga, a guarda, o treino e a meditacao -- e o que prenderia a NAVE: o raio na mao (enraiza o
	/// corpo e trava a altura), a postura de absorver ki (finca no chao) e o agarrao (o outro no colo). Os tres ultimos so se desligam pela
	/// tecla de habilidade, que o leme engole: deixados acesos, o piloto ficaria preso neles ate desembarcar. A FORMA FICA.
	///
	/// As miragens nao saem a mao: o relogio delas vence AGORA, e o proprio tique do efeito (`TickG14`) as tira e devolve a deflexao -- a
	/// regra do `Removed` mora num lugar so.
	///
	/// O QUE FICOU DE FORA, de proposito: o Sleep do Majin (o primeiro passo da nave o acorda -- "sair do lugar" e o que acorda) e as copias
	/// do Split Form (corpos a parte, que seguem a ultima ordem; dar ordem nova ao leme e recusado, `VerbosDePoder`).
	/// </summary>
	private void ApagarAsTecnicasAoLeme(ServerPlayer pl)
	{
		if (LemeLevaAsTecnicasDeTeste) return;
		bool apagou = false;

		if (TemBuff(pl, BuffKaiokenG2)) { ReverterKaiokenG2(pl, porKO: false); apagou = true; }
		if (BuffsDe(pl.Id).Count > 0) { DerrubarBuffs(pl); apagou = true; }
		if (_miragensG14.TryGetValue(pl.Id, out MiragensDePeG14? miragens)) { miragens.Ate = 0; miragens.ProximaEm = long.MaxValue; apagou = true; }
		if (_canais.TryGetValue(pl.Id, out CanalDeKi? canal)) { FecharCanal(pl.Id, canal, null); apagou = true; }
		if (pl.Carregando) { Carregar(pl, false); apagou = true; }
		if (pl.Combate is { Bloqueando: true } guarda) { guarda.Guardar(false); apagou = true; }
		if (pl.Ficha.train || pl.Ficha.med) { DefinirAtividade(pl, Protocol.Activity.Parado); apagou = true; }
		if (pl.Ficha.ki_absorb_stance) { AlternarPosturaDeKi(pl); apagou = true; }
		if (pl.AgarrandoId != 0) { Soltar(pl, MotivoDaSoltura.Tecla); apagou = true; }

		if (apagou) Avisar(pl, Leme.AsTecnicasSeApagam);
	}
}
