using System.Text.Json;
using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Economia;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// UMA POSSE DO NEO TUFFLE EM CURSO: quem entrou (o Tuffle), em quem (o hospede), o corpo do Tuffle guardado enquanto ele esta dentro do
/// outro, e o que o mundo ve no lugar dele (o nome, a raca, o genero e a cara do hospede). Mora em `GameServer._posses`, pelos dois ids.
/// </summary>
public sealed class PosseDoTuffle
{
	public ServerPlayer Tuffle = null!, Hospede = null!;
	public Fighter FichaDoTuffle = null!;
	public CombatState CombateDoTuffle = null!;
	public DisfarceG12 Corpo = null!;

	/// <summary>
	/// (2026-10-28, o pedido 144) O RESTO DO CORPO DO TUFFLE, guardado enquanto ele usa o do hospede: a raca e a classe (as passivas de raca,
	/// a Ascensao e a escada de formas perguntam por elas), o livro de tecnicas e o estado das formas. Voltam na expulsao.
	/// </summary>
	public string RacaDoTuffle = "", ClasseDoTuffle = "";
	public Jandirus.Core.Skills.SkillBook LivroDoTuffle = null!;
	public Jandirus.Core.Forms.EstadoDeForma FormaDoTuffle = null!;

	/// <summary>
	/// (2026-10-29, o pedido 159) O HOSPEDEIRO PERMANENTE: o hospede MORREU na entrada (e joga do Outro Mundo), e o Tuffle anda numa COPIA
	/// do corpo dele. A posse mora so pelo id do Tuffle -- nao ha corpo de verdade pra esconder nem pra arrastar. Ver `HospedarDeVez`.
	/// (2026-10-30, o pedido 180) E ela atravessa o logout: o save leva a copia (`CharacterSave.Hospedeiro`) e o login a devolve.
	/// </summary>
	public bool Permanente;
}

/// <summary>
/// ============================ O NEO TUFFLE (2026-10-18) ============================
/// O pedido do dono: a maquina dos Tsufurujins cria um NEO TUFFLE (em vez de androide/bio) que POSSUI jogadores nocauteados, e o cabelo do
/// possuido fica branco-acinzentado. E as tres respostas dele: quem vira e o PROPRIO Tsufurujin (como no Android Lab); a posse e "entra no
/// corpo" (como o Baby no Vegeta: o Tuffle dirige o corpo do nocauteado com o poder dele, e o dono so assiste); e ela acaba quando o corpo
/// possuido cai (o Tuffle e expulso e volta pro proprio corpo). O DU nao tem nada disto alem de uma roupa (`TuffleTux.dmi`): e sistema novo.
///
/// ============================ A MAQUINA ============================
/// O mainframe de androides ganha o terceiro laboratorio, o Neo Tuffle Lab (`Obra.Lab = 3`), que so um Tsufurujin instala, com a mesma
/// tecnologia dos outros dois (`TechDoLaboratorio`). Dentro dele o Tsufurujin vira Neo Tuffle (`Fighter.NeoTuffle`, no disco), pagando como
/// a conversao em androide de absorcao: um milhao de zeni ou o dobro em recursos. O poder nao muda -- o poder dele passa a ser o dos outros.
/// (2026-10-20) O CORPO MUDA: no proprio corpo o Neo Tuffle anda na folha do Baby (`Bebi 1 Alt`), careca e sem os olhos de gente, com a roupa
/// da criacao por cima -- o pedido do dono, *"ao inves de usar o sprite normal do personagem q se transformou em neotuffle"*, e a escolha dele
/// sobre a roupa. Ver `Appearance.CorpoDoTuffle`.
///
/// ============================ A POSSE: O TUFFLE ANDA NO CORPO DO OUTRO ============================
/// O verb "Possuir um nocauteado" (aba Other) pega o jogador nocauteado mais perto, ao alcance de uso. O cliente so dirige o proprio id
/// (`GameClient.LocalId` nasce no login e nao muda), entao quem anda no mundo e o corpo do TUFFLE, vestindo o do hospede:
///   * a ficha e o corpo de luta sao uma COPIA dos do hospede (`CorpoEmprestado`, o mesmo jeito do corpo do transe: a ficha por JSON, os
///     membros um a um) -- o poder, os stats, a raca da ficha e os membros que faltam. O Tuffle LEVANTA o corpo: a vida volta e o Ki
///     enche (o membro perdido continua perdido);
///   * o mundo ve o nome, a raca, o genero e a cara do hospede (`CorpoDaPosse`, pelo mesmo funil do disfarce -- `DisfarceDe`), com o cabelo
///     branco-acinzentado (o bit 1 do segundo byte de marcas do `PacoteDeAparencia`);
///   * o corpo de verdade do hospede some (`EstaOculto`), sai da grade, ninguem o alcanca (`CombatState.Possuido`) nem o sente
///     (`HospedeEscondido`) e anda com o Tuffle
///     (<see cref="TickDasPosses"/>); o dono dele nao mexe em nada (o portao do `Handle`) e o cliente dele segue o Tuffle (`S2C.Possuido`):
///     ele so assiste.
///   * (2026-10-28, o pedido 144) O CORPO E O DO HOSPEDE POR INTEIRO: *"poder usar todas as skills e formas desse corpo"* e *"ele n vai ter
///     ascencion bonus nem nada, ele vai ter literal tudo do corpo do hospedeiro, formas etc, e se a raça do hospedeiro n tiver ascencion
///     ele tb n vai ter"*. Alem da ficha, o Tuffle passa a ter a RACA e a CLASSE do hospede (as passivas de raca, a Ascensao -- que so
///     sobe raca sem escada de formas -- e a escada que a tecla C sobe), e uma COPIA do livro de tecnicas e do estado das formas dele (o
///     que o Tuffle treinar nelas fica na copia, como o poder). Os dele voltam na expulsao (`PosseDoTuffle`). A MOCHILA continua dele.
///     Ate aqui a mente era do Tuffle: as tecnicas, a raca e as formas eram as dele, e a Ascensao do Tsufurujin subia no poder do outro.
///
/// ============================ O FIM: O CORPO POSSUIDO CAI -- OU O TUFFLE SAI ============================
/// (2026-10-28, o pedido 144) O Tuffle sai quando quiser: o verb "Sair do corpo" (aba Other, so pra quem esta dentro de outro corpo --
/// `Fatos.DentroDeOutroCorpo`) e a mesma expulsao, e o hospede fica caido onde estava.
/// Nocauteado ou morto o corpo emprestado, o Tuffle e expulso (<see cref="Expulsar"/>): ele volta pro proprio corpo -- como estava quando
/// entrou -- um tile ao lado, e o hospede volta a ser corpo onde o emprestado caiu, com o que ele sofreu la (os membros, o nocaute, a
/// morte). Sair do jogo (qualquer um dos dois) tambem expulsa, ANTES do save (o `Drop`). E o Tuffle nao vai pro disco enquanto esta dentro
/// do outro (`Persistir`): a ficha que ele carrega e a copia do corpo alheio -- o mesmo argumento da fusao.
/// ==================================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O laboratorio do Neo Tuffle no mainframe (`Obra.Lab`): 1 e o Android Lab, 2 o Bio-Android Lab.</summary>
	private const int LabDoNeoTuffle = 3;

	/// <summary>A raca que instala o laboratorio e vira Neo Tuffle (o Tsufurujin -- `NomeDaRaca`).</summary>
	private const string RacaDoTuffle = "Tsujin";

	/// <summary>A conversao, em zeni: a do androide de absorcao. Em recursos, o dobro (`CobrarOuExplicar`).</summary>
	private const double CustoNeoTuffle = 1_000_000;

	/// <summary>As posses em curso, pelos DOIS ids (o do Tuffle e o do hospede).</summary>
	private readonly Dictionary<int, PosseDoTuffle> _posses = [];

	/// <summary>DEFEITO INJETADO (`--tuffleteste`): o corpo possuido cai e o Tuffle continua dentro. Lido pelo <see cref="TickDasPosses"/>. Falso em jogo, sempre.</summary>
	internal static bool PosseNaoAcabaNoNocauteDeTeste;

	/// <summary>DEFEITO INJETADO (`--tuffleteste`): o corpo de verdade do hospede fica a vista -- o mundo ve dois dele. Lido pelo `EstaOculto`. Falso em jogo, sempre.</summary>
	internal static bool HospedeAVistaDeTeste;

	/// <summary>DEFEITO INJETADO (`--tuffleteste`): o dono do corpo possuido continua mexendo nele. Lido pelo portao do `Handle`. Falso em jogo, sempre.</summary>
	internal static bool HospedeComAsRedeasDeTeste;

	/// <summary>DEFEITO INJETADO (`--tuffleteste` e `--diagtuffle`): o pacote de aparencia sem o cabelo branco-acinzentado. Falso em jogo, sempre.</summary>
	internal static bool TuffleSemCabeloBrancoDeTeste;

	/// <summary>DEFEITO INJETADO (`--tuffleteste`): o Tsufurujin comum possui como o Neo Tuffle -- a maquina nao serve pra nada. Falso em jogo, sempre.</summary>
	internal static bool PosseSemAMaquinaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (`--tuffleteste` e `--diagtuffle`): o pacote de aparencia sem a marca do corpo do Neo Tuffle -- ele anda com a cara da
	/// criacao, cabelo e roupa, como antes de 2026-10-20. Lido pelo `PacoteDeAparencia`. Falso em jogo, sempre.
	/// </summary>
	internal static bool TuffleSemCorpoDoBabyDeTeste;

	// =====================================================================
	// A MAQUINA
	// =====================================================================
	/// <summary>
	/// VIRAR NEO TUFFLE, dentro do Neo Tuffle Lab: o Tsufurujin com a tecnologia e o dinheiro. O poder nao muda; o CORPO sim (2026-10-20, o dono):
	/// ele passa a andar na folha do Baby, careca, com a roupa da criacao por cima (`Appearance.CorpoDoTuffle`) -- e a zona ve na hora.
	/// </summary>
	private void VirarNeoTuffle(ServerPlayer pl, Moeda moeda)
	{
		if (ObraPerto(pl) is not { Aparafusada: true, Lab: LabDoNeoTuffle })
		{ Avisar(pl, "você precisa estar dentro de um Neo Tuffle Lab."); return; }
		if (pl.Race != RacaDoTuffle) { Avisar(pl, "essa máquina foi feita pro corpo de um Tsufurujin."); return; }
		if (pl.Ficha.NeoTuffle) { Avisar(pl, "você já é um Neo Tuffle."); return; }
		if (pl.Ficha.techskill < TechDoLaboratorio)
		{
			Avisar(pl, $"isso pede {TechDoLaboratorio:0} de tecnologia -- você tem {pl.Ficha.techskill:0}.");
			return;
		}
		if (!CobrarOuExplicar(pl, CustoNeoTuffle, moeda, "a conversão")) return;

		pl.Ficha.NeoTuffle = true;
		pl.SigAtributos = "";
		// O CORPO NOVO APARECE AGORA, pra ele e pra quem esta perto -- o pacote de aparencia leva a marca (`PacoteDeAparencia`, o bit 2 do
		// segundo byte de marcas). Sem esta linha a folha do Baby so chegaria na proxima troca de zona.
		ReapresentarAparencia(pl);
		GD.Print($"[server] {pl.Name} virou Neo Tuffle");
		Avisar(pl, "a máquina se fecha sobre você. Quando abre, você sente que caberia dentro de outro corpo -- e o dirigiria por dentro. "
				   + "(aba Other: \"Possuir um nocauteado\")");
		Persistir(pl);
	}

	// =====================================================================
	// A POSSE
	// =====================================================================
	/// <summary>O verb da aba Other. Mora na cadeia do `UsarHabilidade`, ao lado dos filhos.</summary>
	private bool UsarVerboDoTuffle(ServerPlayer pl, string id)
	{
		switch (id)
		{
			case "tuffle_possuir": Possuir(pl); return true;
			case "tuffle_sair": SairDoCorpo(pl); return true;   // (2026-10-28) o pedido 144
			case "tuffle_hospedar": HospedarDeVez(pl); return true;   // (2026-10-29) o pedido 159
			default: return false;
		}
	}

	/// <summary>
	/// (2026-10-28, o pedido 144) SAIR DO CORPO QUANDO QUISER: *"permita o neotuffle ter uma opçao de sair do corpo de quem ele invadiu quando
	/// quiser"*. E a mesma expulsao do corpo que cai (<see cref="Expulsar"/>): o Tuffle volta pro proprio corpo um tile ao lado, e o hospede
	/// fica onde estava, caido -- quem sai de um corpo o deixa no chao, como no logout.
	/// </summary>
	private void SairDoCorpo(ServerPlayer a)
	{
		if (PosseDoTuffleDe(a.Id) is not { } p || p.Tuffle != a) { Avisar(a, "você não está dentro de outro corpo."); return; }
		// (2026-10-29, o pedido 159) do hospedeiro permanente a copia fica no chao, morta -- ver `DeixarOHospedeiro`
		if (p.Permanente) { DeixarOHospedeiro(p, $"você sai da cópia de {p.Corpo.Nome}, e ela cai morta no chão.", morreJunto: false); return; }
		Expulsar(p, $"você sai do corpo de {p.Corpo.Nome}.");
	}

	/// <summary>A posse em que este corpo esta (como Tuffle ou como hospede), ou nula.</summary>
	private PosseDoTuffle? PosseDoTuffleDe(int id) => _posses.TryGetValue(id, out PosseDoTuffle? p) ? p : null;

	/// <summary>Este id e o corpo de verdade de alguem possuido agora? E o que esconde o corpo (`EstaOculto`).</summary>
	private bool EhHospedeDeTuffle(int id) => !HospedeAVistaDeTeste && _posses.TryGetValue(id, out PosseDoTuffle? p) && p.Hospede.Id == id;

	/// <summary>
	/// O MESMO ESCONDERIJO PRA QUEM PROCURA ENERGIA: o Sense, a telepatia e o Observe (`AchoAEnergia`) e o scouter (o scan dos `Sentidos`) nao
	/// acham o corpo de verdade do hospede -- a energia dele anda dentro do Tuffle, e ele apareceria duas vezes.
	/// </summary>
	private static bool HospedeEscondido(ServerPlayer o) => o.PossuidoPor != 0 && !HospedeAVistaDeTeste;

	/// <summary>
	/// O QUE O MUNDO VE NO LUGAR DO CORPO DE QUEM JOGA: o corpo possuido (o Tuffle andando como o hospede) ou o disfarce da imitacao. Um funil
	/// so pros dois -- o nome, a cara, a raca e o genero do `PacoteDeAparencia` perguntam aqui.
	/// </summary>
	private static DisfarceG12? DisfarceDe(ServerPlayer p) => p.CorpoDaPosse ?? p.Disfarce;

	/// <summary>
	/// POSSUIR: o Neo Tuffle, de pe no proprio corpo, entra no jogador nocauteado mais perto. Ver o cabecalho deste arquivo pro que muda.
	/// </summary>
	private void Possuir(ServerPlayer a)
	{
		if (PosseDoTuffleDe(a.Id) != null) { Avisar(a, "você já está dentro de outro corpo."); return; }
		if (!a.Ficha.NeoTuffle && !(PosseSemAMaquinaDeTeste && a.Race == RacaDoTuffle)) { Avisar(a, "você não é um Neo Tuffle."); return; }
		if (a.Ficha.KO || a.Ficha.dead) { Avisar(a, "caído, você não entra em corpo nenhum."); return; }
		if (EstaFundido(a.Id) || a.BonecoLargado != null || EhOlhoDeFora(a))
		{ Avisar(a, "agora não: você não está inteiro no próprio corpo."); return; }
		if (HospedeAoAlcance(a) is not { } b) { Avisar(a, "não há nenhum jogador nocauteado ao seu alcance."); return; }

		(Fighter emprestada, CombatState corpo) = CorpoEmprestado(a, b);
		var posse = new PosseDoTuffle
		{
			Tuffle = a, Hospede = b, FichaDoTuffle = a.Ficha, CombateDoTuffle = a.Combate,
			Corpo = new DisfarceG12 { Nome = NomeVisivel(b), Raca = b.Race, Genero = b.Genero, Visual = VisualVisivel(b).Copiar() },
		};
		a.Ficha = emprestada;
		a.Combate = corpo;
		a.CorpoDaPosse = posse.Corpo;
		a.Facing = b.Facing;
		CravarPosicao(a, b.Pos);
		b.PossuidoPor = a.Id;
		b.Combate.Possuido = true;
		_posses[a.Id] = posse;
		_posses[b.Id] = posse;

		// (2026-10-28, o pedido 144) O RESTO DO CORPO DO HOSPEDE: a raca e a classe dele, e uma copia do livro e das formas -- ver o cabecalho.
		// Os do Tuffle ficam guardados na posse e voltam na expulsao. Os `AplicarPoderes`/`AplicarEfeitos` logo abaixo ja leem o livro novo:
		// os efeitos das tecnicas DO HOSPEDE sobre a ficha dele, e as arvores pela raca dele.
		posse.RacaDoTuffle = a.Race;
		posse.ClasseDoTuffle = a.Class;
		posse.LivroDoTuffle = a.Livro;
		posse.FormaDoTuffle = a.Forma;
		if (!TuffleComAMenteDeTeste)
		{
			a.Race = b.Race;
			a.Class = b.Class;
			a.Livro = CopiaDoLivro(b.Livro);
			a.Forma = CopiaDasFormas(b.Forma);
		}

		// (A ASCENSAO e a do corpo sem linha nenhuma aqui: a ficha copiada traz o bonus do hospede, e o tique que a reafirma -- `AfirmarAAscensao`
		// -- pergunta pela raca, que agora e a dele. Com a raca do Tuffle, o tique punha a Ascensao do Tsufurujin no poder do outro.)
		a.Ficha.Statify();
		a.SigAtributos = "";
		AplicarPoderes(a);
		AplicarEfeitos(a);
		MandarSkills(a, forcar: true);
		MandarFicha(a);
		MandarAtributos(a);
		TrocarAparencias(a);
		MandarPossuido(b, a.Id);

		Avisar(a, $"você entra no corpo de {posse.Corpo.Nome}! Ele é seu até cair.");
		Avisar(b, "um Neo Tuffle entrou no seu corpo! Você só assiste -- ele sai quando o seu corpo cair.");
		// (O emote sai com o nome DO TUFFLE -- o `Falar` escreve o de quem joga --, e e a unica pista de quem esta la dentro: quem estava perto viu.)
		Falar(a, Protocol.Fala.Emote, $"entra no corpo de {posse.Corpo.Nome}, que se levanta com o cabelo branco-acinzentado.");
		GD.Print($"[server] NEO TUFFLE: {a.Name} entrou no corpo de {b.Name}");
	}

	/// <summary>
	/// (2026-10-31, o pedido 201) O PODER DA COPIA COMECA EM 10% do BP que o corpo tinha (era 90%, o pedido 159) -- o dono: *"vc começa com
	/// 10% do bp original desse corpo, enquanto vc vai masterizando o uso desse corpo mais % do poder original vc consegue liberar , ate
	/// 100%, vc n consegue fazer esse corpo ganhar mais poder do q quando ele foi roubado"*.
	/// </summary>
	internal const double FracaoInicialDoHospedeiro = 0.10;

	/// <summary>
	/// (o pedido 201) O DOMINIO DO CORPO sobe no ritmo da maestria da escada de formas (`Catalogo.MaestriaPorSegundo`: 100% em 3 h dentro
	/// dele) -- e no DOBRO lutando. Escolha minha, a confirmar com o dono.
	/// </summary>
	internal const double DominioLutando = 2;

	/// <summary>
	/// (2026-10-31, o adendo 201d) O CORPO DOMINADO POR INTEIRO RENDE 110% do BP que tinha -- o dono: *"ao domionar completamente o corpo
	/// atual, ele pode o poder do corpo dominado dele é 110% do poder do corpo original, ou seja, ao dominar o corpo ele acessa mais
	/// poder do q o corpo original tinha (uma forma de compensar invadir corpos)"*. E o fim da LINHA RETA que sai dos 10%: o dono
	/// escolheu *"Linha reta até 110%"* -- com 90% de dominio o corpo ja rende o que tinha, e nao ha salto no fim.
	/// </summary>
	internal const double FracaoNoDominioCompleto = 1.10;

	/// <summary>(o pedido 201) Quanto do BP roubado a copia rende com este dominio (0-100): de 10% a 110%, em linha reta (o adendo
	/// 201d, a escolha do dono).</summary>
	internal static double FracaoDoHospedeiro(double dominio) =>
		FracaoInicialDoHospedeiro + (FracaoNoDominioCompleto - FracaoInicialDoHospedeiro) * Math.Clamp(dominio, 0, 100) / 100;

	/// <summary>(o pedido 201) O BP da copia agora: o roubado vezes a fracao do dominio -- nunca acima de 110% do roubado (o dominio
	/// completo, o adendo 201d).</summary>
	internal static double BpDoHospedeiro(Fighter copia) => copia.tuffle_bp_roubado * FracaoDoHospedeiro(copia.tuffle_dominio);

	/// <summary>
	/// (2026-10-29, o pedido 159) O HOSPEDEIRO PERMANENTE -- o dono: *"ao entrar no corpo de alguem desmaiado, essa pessoa vai morrer e ir
	/// pro outro mundo normalmente, porem o neotuffle vai ficar controlando uma copia do corpo dessa pessoa com 90% do bp original dela
	/// [(2026-10-31, o pedido 201) agora 10%, e o dominio do corpo libera ate 110%, em linha reta -- o adendo 201d]
	/// (mantendo o cabelo branco etc) ate ele morrer nesse corpo ou querer sair por vontade propria"*; *"ele n consegue entrar em um
	/// hospedeiro ja estando em um, ele tem q estar sem hospedeiros pra isso"*. As portas sao as do <see cref="Possuir"/>; o corpo e o
	/// <see cref="CorpoEmprestado"/> (com 90% do BP), a raca, a classe e a copia do livro e das formas; e o hospede MORRE pela porta unica
	/// (`Combate.Morrer`, o Outro Mundo de sempre). A morte da copia leva o Tuffle junto, e sair dela a deixa morta no chao --
	/// <see cref="DeixarOHospedeiro"/>.
	/// </summary>
	/// <param name="alvoDoPalco">(2026-10-31) so o palco do robo `--diagtuffle` (a cena N7 do pedido 201): o corpo caido que ele tomou,
	/// sem dono na tela -- o `HospedeAoAlcance` so aceita jogador conectado. Nulo em jogo: o verbo pergunta ao alcance, como sempre.</param>
	private void HospedarDeVez(ServerPlayer a, ServerPlayer? alvoDoPalco = null)
	{
		if (PosseDoTuffleDe(a.Id) != null) { Avisar(a, "você já está dentro de outro corpo: saia dele antes de tomar outro."); return; }
		if (!a.Ficha.NeoTuffle) { Avisar(a, "você não é um Neo Tuffle."); return; }
		if (a.Ficha.KO || a.Ficha.dead) { Avisar(a, "caído, você não entra em corpo nenhum."); return; }
		if (EstaFundido(a.Id) || a.BonecoLargado != null || EhOlhoDeFora(a))
		{ Avisar(a, "agora não: você não está inteiro no próprio corpo."); return; }
		if ((alvoDoPalco ?? HospedeAoAlcance(a)) is not { } b) { Avisar(a, "não há nenhum jogador nocauteado ao seu alcance."); return; }

		// (2026-10-30, o pedido 180) OS BUFFS ACESOS SAO DO CORPO QUE OS ACENDEU: apagam aqui, na ficha DELE, antes da troca. A posse vai pro
		// disco (`SaveDoHospedeiro`) e o registro dos buffs e por id: com um buff dele de pe, o save o tiraria da copia, que nunca o teve, e o
		// numero dele ficaria preso na ficha do Tuffle pra sempre (o Kaio-ken preso de 2026-10-16, ver `FichaSemOsBuffs`).
		DerrubarBuffs(a);

		(Fighter copia, CombatState corpo) = CorpoEmprestado(a, b);
		// (2026-10-31, o pedido 201) A COPIA GUARDA O BP DO ROUBO (o teto) e comeca com 10% dele: o dominio libera o resto.
		copia.tuffle_bp_roubado = b.Ficha.BP;
		copia.tuffle_dominio = 0;
		copia.BP = BpDoHospedeiro(copia);
		var posse = new PosseDoTuffle
		{
			Tuffle = a, Hospede = b, FichaDoTuffle = a.Ficha, CombateDoTuffle = a.Combate, Permanente = true,
			Corpo = new DisfarceG12 { Nome = NomeVisivel(b), Raca = b.Race, Genero = b.Genero, Visual = VisualVisivel(b).Copiar() },
			RacaDoTuffle = a.Race, ClasseDoTuffle = a.Class, LivroDoTuffle = a.Livro, FormaDoTuffle = a.Forma,
		};
		a.Ficha = copia;
		a.Combate = corpo;
		a.CorpoDaPosse = posse.Corpo;
		a.Facing = b.Facing;
		a.Race = b.Race;
		a.Class = b.Class;
		a.Livro = CopiaDoLivro(b.Livro);
		a.Forma = CopiaDasFormas(b.Forma);
		CravarPosicao(a, b.Pos);
		_posses[a.Id] = posse;

		a.Ficha.Statify();
		a.SigAtributos = "";
		AplicarPoderes(a);
		AplicarEfeitos(a);
		MandarSkills(a, forcar: true);
		MandarFicha(a);
		MandarAtributos(a);
		TrocarAparencias(a);

		// O HOSPEDE MORRE, e pela porta (o relogio da morte, a aureola, o cadaver e o funil de derrota) -- como o Revive que cobra a vida
		Avisar(b, "um Neo Tuffle tomou o seu corpo de vez: você morre, e ele fica com uma cópia de você.");
		b.Combate.Morrer(ignorarSeguro: true);

		Avisar(a, $"você toma o corpo de {posse.Corpo.Nome} de vez: ele morre, e você fica com uma cópia dele -- que começa com "
				  + $"{FracaoInicialDoHospedeiro * 100:0}% do poder dele. Dominando o corpo, você libera o resto, e dominado por inteiro ele rende "
				  + $"{FracaoNoDominioCompleto * 100:0}% (mais do que ele tinha) -- até morrer nela (e você morre junto) ou sair dela.");
		Falar(a, Protocol.Fala.Emote, $"toma o corpo de {posse.Corpo.Nome}, que se levanta com o cabelo branco-acinzentado.");
		GD.Print($"[server] NEO TUFFLE: {a.Name} tomou de vez o corpo de {b.Name} (hospedeiro permanente)");
	}

	/// <summary>
	/// (2026-10-29, o pedido 159) O FIM DO HOSPEDEIRO PERMANENTE -- saindo (pelo verb; o logout guarda a posse, o pedido 180) ou morrendo nele. A COPIA CAI MORTA no
	/// chao, com a cara do hospede (*"Quando ele sai o corpo do hospedeiro copiado cai morto no chao"*: o `DeixarOCadaver` de um corpo de
	/// passagem com a aparencia da posse), e o Tuffle volta pro proprio corpo -- vivo, um tile ao lado; ou, se a copia morreu, MORTO com ela
	/// (*"Se o corpo do neotuffle hospedeiro morrer o neotuffle morre junto"*: a porta unica, `SumiuAbsorvido` pra nao deixar um segundo
	/// corpo -- o dele nao estava no mundo).
	/// </summary>
	private void DeixarOHospedeiro(PosseDoTuffle posse, string praOTuffle, bool morreJunto)
	{
		ServerPlayer a = posse.Tuffle;
		_posses.Remove(a.Id);
		// (2026-10-30, o pedido 180) ...e os que ele acendeu na copia apagam NELA, antes da volta: desfeitos depois, cairiam na ficha dele
		DerrubarBuffs(a);

		// ---- a copia cai morta onde estava ----
		DeixarOCadaver(new ServerPlayer
		{
			Id = -1, Peer = null, Name = posse.Corpo.Nome, Race = posse.Corpo.Raca, Genero = posse.Corpo.Genero,
			Zone = a.Zone, Pos = a.Pos, Altitude = a.Altitude, Facing = a.Facing, Planeta = a.Planeta,
			Visual = posse.Corpo.Visual.Copiar(), Ficha = a.Ficha, Combate = a.Combate,
		});
		Vec2 onde = a.Pos;
		ZoneKey zona = a.Zone;

		// ---- o Tuffle volta pro proprio corpo ----
		a.Ficha = posse.FichaDoTuffle;
		a.Combate = posse.CombateDoTuffle;
		a.CorpoDaPosse = null;
		a.Race = posse.RacaDoTuffle;
		a.Class = posse.ClasseDoTuffle;
		a.Livro = posse.LivroDoTuffle;
		a.Forma = posse.FormaDoTuffle;
		a.Ficha.Statify();
		a.SigAtributos = "";
		if (!morreJunto)
		{
			Vec2 ao = onde + new Vec2(ZoneCollision.TileSize, 0);
			CravarPosicao(a, MapaDaZonaOuCatalogo(zona)?.PontoLivrePerto(ao) ?? ao);
		}

		AplicarPoderes(a);
		AplicarEfeitos(a);
		MandarSkills(a, forcar: true);
		MandarFicha(a);
		MandarAtributos(a);
		TrocarAparencias(a);
		Avisar(a, praOTuffle);
		if (morreJunto)
		{
			a.SumiuAbsorvido = true;
			a.Combate.Morrer(ignorarSeguro: true);
		}
		GD.Print($"[server] NEO TUFFLE: {a.Name} deixou a copia de {posse.Corpo.Nome}{(morreJunto ? " (morreu nela, e morre junto)" : "")}");
	}


	/// <summary>
	/// O NOCAUTEADO QUE PODE SER POSSUIDO, o mais perto ao alcance de uso: um jogador (o NPC nao -- o pedido e de jogadores), vivo, no proprio
	/// corpo (sem fusao, sem transe, sem olho de nave) e que ninguem possui.
	/// </summary>
	private ServerPlayer? HospedeAoAlcance(ServerPlayer a) =>
		_players.Values
			.Where(o => o != a && EhJogador(o) && o.Zone.Equals(a.Zone) && o.Ficha.KO && !o.Ficha.dead && !EhCadaver(o)
						&& PosseDoTuffleDe(o.Id) == null && !EstaFundido(o.Id) && o.BonecoLargado == null && !EhOlhoDeFora(o)
						&& Vec2.Distance(o.Pos, a.Pos) <= AlcanceDeUso)
			.OrderBy(o => Vec2.Distance(o.Pos, a.Pos))
			.FirstOrDefault();

	/// <summary>
	/// O CORPO EMPRESTADO: uma copia da ficha do hospede (por JSON, o caminho do savefile -- como o corpo do transe, `DarCorpoProprio`) e um
	/// corpo de luta novo com os membros dele um a um. O Tuffle o levanta: o nocaute sai, a vida volta e o Ki enche; o membro perdido continua
	/// perdido. Os ganchos sao os do Tuffle (as pecas caem dele, o arremesso e o dele, a cena e a dele) -- e NAO ha `AoMorrer` nem
	/// `NegarMorte`: a morte deste corpo e a do hospede, cobrada pelas regras dele na expulsao.
	/// </summary>
	private (Fighter, CombatState) CorpoEmprestado(ServerPlayer tuffle, ServerPlayer hospede)
	{
		Fighter copia = JsonSerializer.Deserialize<Fighter>(JsonSerializer.Serialize(hospede.Ficha, CopiaDeFicha), CopiaDeFicha)!;
		copia.KO = false;
		copia.train = false;
		copia.med = false;

		var corpo = new CombatState(copia, TemRabo(hospede.Race), EixoDeRegen(hospede.Race, copia));
		foreach (BodyPart p in corpo.Corpo.Partes)
		{
			if (hospede.Combate.Corpo.Achar(p.Nome) is not { } real) continue;
			p.Decepado = real.Decepado;
			p.Vida = real.Decepado ? 0 : p.VidaMax;
		}
		LigarAoTuffle(corpo, tuffle);
		corpo.SincronizarVida();
		copia.Statify();
		copia.Ki = copia.MaxKi;
		return (copia, corpo);
	}

	/// <summary>Os ganchos do corpo emprestado, que sao os do Tuffle -- ver <see cref="CorpoEmprestado"/>. O login os poe tambem na copia que volta do disco.</summary>
	private void LigarAoTuffle(CombatState corpo, ServerPlayer tuffle)
	{
		corpo.AoDecepar = (_, caiu) => SoltarPecas(tuffle, caiu);
		corpo.SendoArremessado = () => tuffle.TiquesDeVoo > 0;
		corpo.EmCinematica = () => EmCena(tuffle);
		corpo.RitmoDaRecarga = () => RitmoDaRecargaDe(tuffle);
	}

	/// <summary>(2026-10-30, o pedido 180) A posse de hospedeiro permanente que este jogador dirige, ou nula.</summary>
	private PosseDoTuffle? HospedeiroPermanenteDe(ServerPlayer pl) =>
		PosseDoTuffleDe(pl.Id) is { Permanente: true } p && p.Tuffle == pl ? p : null;

	/// <summary>
	/// (2026-10-30, o pedido 180) O SAVE DE QUEM ESTA NO HOSPEDEIRO PERMANENTE -- o dono: *"guarda a posse"*. O corpo DELE vai pelo caminho de
	/// sempre (`AccountStore.DeJogador`, com a ficha, os membros, a raca, a classe, o livro e as formas que a posse guarda: trocados no corpo so
	/// pra leitura, e devolvidos), e a copia vai por cima (`CharacterSave.Hospedeiro`). Os buffs acesos sao todos da copia (a entrada apagou os
	/// dele, `HospedarDeVez`): saem da ficha dela, como saem da de todo mundo (`FichaSemOsBuffs`).
	/// </summary>
	private CharacterSave SaveDoHospedeiro(ServerPlayer a, PosseDoTuffle posse)
	{
		var copia = new HospedeiroSalvo
		{
			Corpo = posse.Corpo, Raca = a.Race, Classe = a.Class, ContaDoHospede = posse.Hospede.Conta,
			Ficha = FichaSemOsBuffs(a), Membros = FotografarCorpo(a.Combate),
			Skills = [.. a.Livro.Aprendidas], SkillsEnsinadas = [.. a.Livro.Ensinadas], SkillsCompradas = [.. a.Livro.Compradas],
			SkillsEscolhas = new Dictionary<string, int>(a.Livro.Escolhas),
			SkillsGanhos = a.Livro.GanhosNaCompra.ToDictionary(kv => kv.Key, kv => new Dictionary<string, double>(kv.Value)),
			MarcosTotais = a.Livro.MarcosTotais, MarcosLivres = a.Livro.MarcosLivres, MarcosPorBp = a.Livro.MarcosPorBp,
			Limiares = a.Forma.Limiares, FormasDespertadas = [.. a.Forma.Liberadas], FormasEstreadas = [.. a.Forma.EstreiaVista],
			PortasCortadas = [.. a.Forma.PortasCortadas], Maestrias = a.Forma.Maestria.ParaSave(),
			GradesLigados = a.Forma.GradesLigados, CPelasDivinas = a.Forma.CPelasDivinas, CPeloMistico = a.Forma.CPeloMistico,
		};

		(Fighter ficha, CombatState combate, DisfarceG12? visto) = (a.Ficha, a.Combate, a.CorpoDaPosse);
		(string raca, string classe) = (a.Race, a.Class);
		(Jandirus.Core.Skills.SkillBook livro, Jandirus.Core.Forms.EstadoDeForma forma) = (a.Livro, a.Forma);
		a.Ficha = posse.FichaDoTuffle;
		a.Combate = posse.CombateDoTuffle;
		a.CorpoDaPosse = null;
		a.Race = posse.RacaDoTuffle;
		a.Class = posse.ClasseDoTuffle;
		a.Livro = posse.LivroDoTuffle;
		a.Forma = posse.FormaDoTuffle;
		try
		{
			a.Ficha.Class = a.Class;
			CharacterSave salvo = AccountStore.DeJogador(a, NowMs());
			salvo.Hospedeiro = copia;
			return salvo;
		}
		finally
		{
			(a.Ficha, a.Combate, a.CorpoDaPosse) = (ficha, combate, visto);
			(a.Race, a.Class) = (raca, classe);
			(a.Livro, a.Forma) = (livro, forma);
		}
	}

	/// <summary>
	/// (2026-10-30, o pedido 180) A VOLTA PRA DENTRO DO HOSPEDEIRO PERMANENTE, no login: o `Entrar` montou o corpo DELE pelo caminho de sempre,
	/// e aqui ele volta pra dentro da copia que o save guardou (<see cref="SaveDoHospedeiro"/>) -- a ficha, os membros, a raca, a classe, o
	/// livro e as formas dela, e a cara do hospede pro mundo. E o fim do <see cref="HospedarDeVez"/> sem a morte (o hospede ja morreu) e sem
	/// os pacotes: o `JoinAccepted` ainda nao saiu, e a ficha, as tecnicas e a aparencia que o login manda adiante ja sao as da copia.
	/// </summary>
	private void RetomarOHospedeiro(ServerPlayer a, HospedeiroSalvo h)
	{
		Fighter copia = h.Ficha;
		copia.raboPresoMod = 1;   // o aperto no rabo nao atravessa o logout (o `PrepararCombate`)
		// (2026-10-31, o pedido 201) A COPIA GRAVADA ANTES DO PEDIDO 201 nao tem o BP do roubo: ela rendia 90% dele (o pedido 159). O roubado e o
		// BP dela sobre 0,9, e o dominio e o que da os mesmos 90% -- ninguem perde poder no login por causa da regra nova.
		if (copia.tuffle_bp_roubado <= 0)
		{
			copia.tuffle_bp_roubado = copia.BP / 0.9;
			// (o adendo 201d) na linha reta de 10% a 110%, o dominio que da os 90% e 80%
			copia.tuffle_dominio = (0.9 - FracaoInicialDoHospedeiro) / (FracaoNoDominioCompleto - FracaoInicialDoHospedeiro) * 100;
		}
		var corpo = new CombatState(copia, TemRabo(h.Raca), EixoDeRegen(h.Raca, copia));
		foreach (BodyPart p in corpo.Corpo.Partes)
			if (h.Membros.TryGetValue(p.Nome, out double[]? v) && v.Length >= 4)
			{
				p.Vida = Math.Clamp(v[0], 0, p.VidaMax);
				p.Decepado = v[1] > 0.5;
				p.Perdido = v[2] > 0.5;
				p.FeridaAberta = Math.Max(0, v[3]);
			}
		LigarAoTuffle(corpo, a);

		var livro = new Jandirus.Core.Skills.SkillBook();
		livro.Carregar(h.Skills);
		livro.CarregarEnsinadas(h.SkillsEnsinadas);
		livro.CarregarCompradas(h.SkillsCompradas);
		livro.CarregarEscolhas(h.SkillsEscolhas);
		livro.CarregarGanhos(h.SkillsGanhos);
		livro.MarcosTotais = h.MarcosTotais;
		livro.MarcosLivres = h.MarcosLivres;
		livro.MarcosPorBp = h.MarcosPorBp;
		var formas = new Jandirus.Core.Forms.EstadoDeForma
		{
			Limiares = h.Limiares, Liberadas = [.. h.FormasDespertadas], EstreiaVista = [.. h.FormasEstreadas], PortasCortadas = [.. h.PortasCortadas],
			GradesLigados = h.GradesLigados, CPelasDivinas = h.CPelasDivinas, CPeloMistico = h.CPeloMistico,
		};
		formas.Maestria.DoSave(h.Maestrias);

		// o hospede de verdade ja morreu e joga do Outro Mundo: o que a posse ainda pergunta dele e a conta (a tech dele, `EDaContaDona`)
		_posses[a.Id] = new PosseDoTuffle
		{
			Tuffle = a, Hospede = new ServerPlayer { Id = -1, Peer = null, Name = h.Corpo.Nome, Conta = h.ContaDoHospede },
			FichaDoTuffle = a.Ficha, CombateDoTuffle = a.Combate, Corpo = h.Corpo, Permanente = true,
			RacaDoTuffle = a.Race, ClasseDoTuffle = a.Class, LivroDoTuffle = a.Livro, FormaDoTuffle = a.Forma,
		};
		a.Ficha = copia;
		a.Combate = corpo;
		a.CorpoDaPosse = h.Corpo;
		a.Race = h.Raca;
		a.Class = h.Classe;
		a.Livro = livro;
		a.Forma = formas;
		corpo.SincronizarVida();
		a.Ficha.Statify();
		a.SigAtributos = "";
		AplicarPoderes(a);
		AplicarEfeitos(a);
		GD.Print($"[server] NEO TUFFLE: {a.Name} voltou dentro da copia de {h.Corpo.Nome} (hospedeiro permanente)");
	}

	/// <summary>
	/// (2026-10-28, o pedido 144) AS TECNICAS DO HOSPEDE, COPIADAS -- pelo mesmo caminho do login (`PrepararSkills`, que monta o livro do que o
	/// save guarda): as aprendidas, as ensinadas, as compradas, as escolhas, o que a compra somou e os marcos. O que o Tuffle fizer com elas
	/// (comprar, esquecer, escolher) fica na copia, como o poder na ficha copiada. As arvores destravadas se recontam no `AplicarEfeitos`.
	/// </summary>
	private static Jandirus.Core.Skills.SkillBook CopiaDoLivro(Jandirus.Core.Skills.SkillBook o)
	{
		var l = new Jandirus.Core.Skills.SkillBook();
		l.Carregar([.. o.Aprendidas]);
		l.CarregarEnsinadas([.. o.Ensinadas]);
		l.CarregarCompradas([.. o.Compradas]);
		l.CarregarEscolhas(new Dictionary<string, int>(o.Escolhas));
		l.CarregarGanhos(o.GanhosNaCompra.ToDictionary(kv => kv.Key, kv => new Dictionary<string, double>(kv.Value)));
		l.MarcosTotais = o.MarcosTotais;
		l.MarcosLivres = o.MarcosLivres;
		l.MarcosPorBp = o.MarcosPorBp;
		return l;
	}

	/// <summary>
	/// (2026-10-28, o pedido 144) AS FORMAS DO HOSPEDE, COPIADAS -- o que o save guardaria delas (`RestaurarFormaEDisciplina`): as despertadas,
	/// as ja estreadas, as portas cortadas, a maestria de cada uma, os limiares sorteados no nascimento dele e as duas preferencias do C. Na
	/// base: o corpo caido nao esta em forma nenhuma. A maestria que o Tuffle treinar nelas fica na copia.
	/// </summary>
	private static Jandirus.Core.Forms.EstadoDeForma CopiaDasFormas(Jandirus.Core.Forms.EstadoDeForma o)
	{
		var c = new Jandirus.Core.Forms.EstadoDeForma
		{
			Limiares = o.Limiares,
			Liberadas = [.. o.Liberadas],
			EstreiaVista = [.. o.EstreiaVista],
			PortasCortadas = [.. o.PortasCortadas],
			GradesLigados = o.GradesLigados,
			CPelasDivinas = o.CPelasDivinas,
		};
		c.Maestria.DoSave(o.Maestria.ParaSave());
		return c;
	}

	/// <summary>
	/// DEFEITO INJETADO (`--tuffleteste`, o pedido 144): a mente do Tuffle dentro do corpo -- a raca, as tecnicas e as formas continuam as dele,
	/// e a Ascensao do Tsufurujin sobe no poder do outro (o jogo de antes). Lido pelo `Possuir`. Falso em jogo, sempre.
	/// </summary>
	internal static bool TuffleComAMenteDeTeste;

	/// <summary>
	/// O TIQUE DAS POSSES, logo depois das naves: o corpo emprestado que caiu expulsa o Tuffle, e o corpo de verdade do hospede anda com ele
	/// -- e de la que o dono assiste (a zona do Tuffle e a que chega no cliente dele).
	/// </summary>
	private void TickDasPosses()
	{
		if (_posses.Count == 0) return;
		List<PosseDoTuffle> posses = [];
		foreach ((int id, PosseDoTuffle p) in _posses)
			if (id == p.Tuffle.Id) posses.Add(p);

		foreach (PosseDoTuffle p in posses)
		{
			ServerPlayer a = p.Tuffle, b = p.Hospede;
			// (2026-10-29, o pedido 159) O HOSPEDEIRO PERMANENTE: o nocaute nao expulsa (a copia e dele agora), e a MORTE dela leva o Tuffle junto.
			// Nao ha corpo de verdade pra arrastar: o hospede joga do Outro Mundo.
			if (p.Permanente)
			{
				if (a.Ficha.dead || (a.Combate.Corpo.DeveMorrer() && !a.Combate.Corpo.RegeneraDecepado))
					DeixarOHospedeiro(p, $"a cópia de {p.Corpo.Nome} morre -- e você morre junto com ela.", morreJunto: true);
				else DominarOHospedeiro(a, p);   // (2026-10-31, o pedido 201)
				continue;
			}
			if (!PosseNaoAcabaNoNocauteDeTeste && (a.Ficha.KO || a.Ficha.dead))
			{
				Expulsar(p, $"o corpo de {p.Corpo.Nome} caiu -- e você é expulso dele, de volta ao seu.");
				continue;
			}
			if (!b.Zone.Equals(a.Zone)) MoveToZone(b.Id, a.Zone, a.Pos);
			else b.Pos = a.Pos;
		}
	}

	/// <summary>
	/// (2026-10-31, o pedido 201) O DOMINIO DO CORPO ROUBADO, a cada tique dentro dele: sobe com o tempo (o dobro lutando), e o BP da copia
	/// segue a conta -- de 10% do roubado (dominio 0) a 110% (dominio completo), em linha reta (o adendo 201d) -- e nunca acima
	/// disso. E aqui que o TETO vale: um ganho que escapasse
	/// por outra porta (o treino ja nao rende, `Fighter.GanhoDeAtividade`; o Zenkai, um desejo) volta pra conta neste mesmo tique. A cada
	/// 10% de dominio, o Tuffle e avisado.
	/// </summary>
	private void DominarOHospedeiro(ServerPlayer a, PosseDoTuffle p)
	{
		Fighter f = a.Ficha;
		if (f.tuffle_bp_roubado <= 0) return;
		int decimoAntes = (int)(f.tuffle_dominio / 10);
		if (f.tuffle_dominio < 100)
		{
			double ritmo = Jandirus.Core.Forms.Catalogo.MaestriaPorSegundo * (a.Combate is { EmCombate: > 0 } ? DominioLutando : 1);
			f.tuffle_dominio = Math.Min(100, f.tuffle_dominio + ritmo * Protocol.TickSeconds);
		}
		// O BP SEGUE A CONTA: pra cima quando a diferenca passa de 0,1% (o Statify nao precisa rodar trinta vezes por segundo por um passo
		// invisivel) -- e no dominio completo, o resto que falta: na linha reta do adendo 201d o ultimo passo nao chega aos 0,1%, e o
		// corpo parava um fio abaixo dos 110% (o robo `--diagtuffle` viu 54.999.654 de 55.000.000); pra baixo -- o teto --, sempre.
		double alvo = BpDoHospedeiro(f);
		if (f.BP > alvo + 1e-6 || alvo - f.BP > alvo * 0.001 || (f.tuffle_dominio >= 100 && f.BP < alvo))
		{
			f.BP = alvo;
			f.Statify();
			a.SigAtributos = "";
		}
		int decimoAgora = (int)(f.tuffle_dominio / 10);
		if (decimoAgora > decimoAntes)
			Avisar(a, decimoAgora >= 10
				? $"você domina o corpo de {p.Corpo.Nome} por inteiro: ele rende {FracaoNoDominioCompleto * 100:0}% do poder que tinha -- mais "
				  + "do que o dono dele -- e não passa disso."
				: $"você domina {decimoAgora * 10}% do corpo de {p.Corpo.Nome}: ele já rende {FracaoDoHospedeiro(f.tuffle_dominio) * 100:0}% do poder que tinha.");
	}

	/// <summary>
	/// A EXPULSAO: o Tuffle volta pro proprio corpo, como estava quando entrou, um tile ao lado; o hospede volta a ser corpo onde o emprestado
	/// estava, com o que ele sofreu la -- os membros e, pelas regras DELE, a morte (o `Morrer` do corpo dele, com o seguro e o Outro Mundo) ou o
	/// nocaute. Quem sai de um corpo o deixa caido: o hospede termina nocauteado mesmo quando a saida e por logout.
	/// </summary>
	private void Expulsar(PosseDoTuffle posse, string praOTuffle)
	{
		ServerPlayer a = posse.Tuffle, b = posse.Hospede;
		_posses.Remove(a.Id);
		_posses.Remove(b.Id);

		Fighter emprestada = a.Ficha;
		CombatState corpo = a.Combate;
		bool morreu = emprestada.dead || (corpo.Corpo.DeveMorrer() && !corpo.Corpo.RegeneraDecepado);
		ZoneKey zona = a.Zone;
		Vec2 onde = a.Pos;
		Facing caiuPara = a.DirecaoDeitado;

		// ---- o Tuffle volta pro proprio corpo ----
		a.Ficha = posse.FichaDoTuffle;
		a.Combate = posse.CombateDoTuffle;
		a.CorpoDaPosse = null;
		// (2026-10-28, o pedido 144) e a raca, a classe, o livro e as formas dele -- as copias do hospede ficam pra tras, com a ficha copiada
		a.Race = posse.RacaDoTuffle;
		a.Class = posse.ClasseDoTuffle;
		a.Livro = posse.LivroDoTuffle;
		a.Forma = posse.FormaDoTuffle;
		a.Ficha.Statify();
		a.SigAtributos = "";

		// ---- o hospede volta a ser corpo, onde o emprestado estava ----
		b.PossuidoPor = 0;
		b.Combate.Possuido = false;
		if (b.Zone.Equals(zona)) b.Pos = onde;
		else MoveToZone(b.Id, zona, onde);
		b.Facing = a.Facing;
		// DEITADO PRO MESMO RUMO em que o corpo emprestado caiu -- e o mesmo corpo, e quem viu a queda nao pode ve-lo virar no chao.
		b.CongelarDirecaoDeitada(caiuPara);
		foreach (BodyPart p in corpo.Corpo.Partes)
		{
			if (b.Combate.Corpo.Achar(p.Nome) is not { } real) continue;
			real.Decepado = p.Decepado;
			real.Vida = Math.Clamp(p.Vida, 0, real.VidaMax);
		}
		b.Combate.SincronizarVida();
		if (!morreu || !b.Combate.Morrer())
			b.Combate.Nocautear(corpo.NocauteRestante > 0 ? corpo.NocauteRestante : MeleeResolver.TetoDoNocaute, porVital: corpo.NocautePorVital);

		// ---- o Tuffle sai do lado ----
		Vec2 ao = onde + new Vec2(ZoneCollision.TileSize, 0);
		CravarPosicao(a, MapaDaZonaOuCatalogo(zona)?.PontoLivrePerto(ao) ?? ao);

		AplicarPoderes(a);
		AplicarEfeitos(a);
		MandarSkills(a, forcar: true);
		MandarFicha(a);
		MandarAtributos(a);
		TrocarAparencias(a);
		TrocarAparencias(b);
		MandarPossuido(b, 0);

		Avisar(a, praOTuffle);
		Avisar(b, "o Neo Tuffle saiu do seu corpo.");
		GD.Print($"[server] NEO TUFFLE: {a.Name} saiu do corpo de {b.Name}{(morreu ? " (o corpo morreu)" : "")}");
	}

	/// <summary>
	/// QUEM SAI DO JOGO NO MEIO DE UMA POSSE a desfaz ANTES do save -- seja o Tuffle (que carrega a copia do corpo alheio) ou o hospede (que
	/// esta escondido andando com ele). E o `SoltarDaFusao` desta mecanica: ver o `Drop`. (2026-10-30) Menos a do hospedeiro permanente, que fica.
	/// </summary>
	private void SoltarDaPosse(ServerPlayer pl)
	{
		if (PosseDoTuffleDe(pl.Id) is not { } p) return;
		// (2026-10-29, o pedido 159) do hospedeiro permanente, sair do jogo era sair dele: a copia ficava no chao, morta. (2026-10-30, o pedido
		// 180) O dono: *"guarda a posse"*. Ela fica de pe pro `Persistir` logo abaixo no `Drop`, que grava o corpo dele E a copia
		// (`SaveDoHospedeiro`); o `DespedirCorpo` a tira do mapa, e o login o poe de volta dentro dela (`RetomarOHospedeiro`).
		if (p.Permanente) return;
		Expulsar(p, p.Tuffle == pl ? "você sai do corpo de " + p.Corpo.Nome + "." : $"o dono do corpo de {p.Corpo.Nome} saiu do jogo -- e você é expulso.");
	}

	/// <summary>O cliente do hospede passa a seguir o corpo do possessor (ou volta ao proprio, com 0). Ver `Protocol.S2C.Possuido`.</summary>
	private static void MandarPossuido(ServerPlayer hospede, int possessor)
	{
		if (hospede.Peer == null) return;
		var w = Protocol.Begin(Protocol.S2C.Possuido);
		w.Put(possessor);
		hospede.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
