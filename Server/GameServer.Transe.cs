using System.Text.Json;
using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// O TRANSE -- o que a meditacao profunda deve ao corpo que ficou la fora. Item 1 do playtest de 2026-10-10.
///
/// ============================ AS QUEIXAS, E O QUE HAVIA POR BAIXO DE CADA UMA ============================
/// **(a) "as vezes o jogador fica preso".** Nao havia gesto de saida: o "Meditate de novo acorda" do original
/// (`Meditate.dm:20-21`) nao foi portado, e a unica porta era um verb no fim da aba Learning do menu P. A
/// saida por vitoria nem sempre existe (o reflexo de quem regenera membro nao morre), quem sobra numa mente
/// depois de um PvP fica sozinho num plano branco, e um save feito no meio do transe devolvia o personagem
/// dentro de uma mente sem corpo pra voltar.
///
/// **(b) "o corpo real toma dano quando o jogador sai".** O corpo largado DIVIDIA a ficha e o corpo com quem
/// estava la dentro, e a regra "nada aqui e real" era so uma foto de cinco campos devolvida na saida. Tudo o
/// que nao cabia na foto vazava: o buff ligado la dentro que continua queimando o corpo (o Kaio-ken primeiro),
/// o arremesso que segue voando no mundo real, o Zanzo Clash que termina com um golpe de verdade, a saida
/// que nao passa pela foto (um admin trazendo, o torneio transportando). E o avesso tambem: o golpe, o
/// nocaute e a MORTE de verdade que o corpo levava la fora eram APAGADOS pela foto.
///
/// **(c) "o corpo do Majin fica de pe".** Duas coisas. A que e defeito vale pra toda raca: a pose do corpo
/// largado saia do `Ficha.med` dividido, e o primeiro passo dentro da mente o desligava. A que e do Majin e a
/// arte do original: o estado "Meditate" do `Majin.dmi` e uma figura de pe, de bracos cruzados e olhos
/// fechados -- a conversao esta certa, e o corpo certo ja e esse.
///
/// ============================ O CONSERTO: O CORPO LARGADO TEM CORPO PROPRIO ============================
/// Na meditacao profunda o corpo que fica la fora ganha uma COPIA da ficha e um corpo so dele, congelados
/// no instante da entrada (<see cref="DarCorpoProprio"/>). O que acontece la dentro nao encosta nele; o que
/// acontece la fora -- o golpe, o nocaute, a morte -- acontece NELE, e e o estado DELE que o dono recebe ao
/// voltar (`RestaurarDaMente`). A pose dele e a dele. A foto continua existindo, como reserva pra quando o
/// corpo some do mundo.
///
/// (O leme da nave grande, que usa o mesmo `LargarOCorpo`, continua dividindo o corpo de proposito: la nao
/// ha "nada e real" -- quem pilota e quem apanha sao a mesma pessoa.)
///
/// E toda saida passou a ser UMA: a foto e a faxina do que foi ligado la dentro
/// (<see cref="LimparOCombateDaMente"/>) rodam tambem quando alguem tira o jogador da mente por fora
/// (<see cref="ArrancarDoTranse"/>, chamado pelo `MoveToZone`), e o save de quem esta em transe leva o
/// endereco e o estado do corpo de verdade (`TranseSalvo`).
/// ======================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--transeteste`): o corpo largado na meditacao profunda volta a DIVIDIR a
	/// ficha e o corpo com o dono -- o estado de antes de 2026-10-10. Lido pelo proprio `LargarOCorpo`.
	/// </summary>
	internal bool BonecoDivideOCorpoDeTeste;

	private static readonly JsonSerializerOptions CopiaDeFicha = new() { IncludeFields = true };

	/// <summary>Este corpo largado tem corpo so dele (o da meditacao), ou divide o do dono (o do leme)?</summary>
	private static bool TemCorpoProprio(ServerPlayer boneco, ServerPlayer dono) =>
		!ReferenceEquals(boneco.Combate, dono.Combate);

	/// <summary>
	/// DA AO CORPO LARGADO UMA FICHA E UM CORPO SO DELE, iguais aos do dono neste instante.
	///
	/// A FICHA VAI POR JSON, o mesmo caminho do savefile: e a copia por valor que o jogo ja faz toda vez que
	/// alguem desloga e volta, entao nao ha lista de campos escrita a mao pra envelhecer. O CORPO e um
	/// `CombatState` novo com os membros copiados um a um.
	///
	/// ELE FICA MEDITANDO: a pose que quem passa ve sai do `med` DESTA ficha (`ServerPlayer.Pose`), e nada
	/// do que o dono faz la dentro -- andar, treinar, cair -- escreve nela.
	///
	/// SEM `NegarMorte` E SEM `AoMorrer`: se um golpe letal acabar com este corpo, ele so fica marcado como
	/// morto. Quem decide se o DONO morre sao as regras do dono (o seguro da Aura of Destruction, o despertar
	/// do bio-androide), no instante em que ele volta -- ver <see cref="DecidirOQueOCorpoSofreu"/>.
	/// </summary>
	private void DarCorpoProprio(ServerPlayer boneco, ServerPlayer dono)
	{
		Fighter copia = JsonSerializer.Deserialize<Fighter>(
			JsonSerializer.Serialize(dono.Ficha, CopiaDeFicha), CopiaDeFicha)!;
		copia.med = true;
		copia.train = false;

		var corpo = new CombatState(copia, TemRabo(dono.Race), EixoDeRegen(dono.Race, copia));
		foreach (BodyPart p in corpo.Corpo.Partes)
		{
			if (dono.Combate.Corpo.Achar(p.Nome) is not { } real) continue;
			p.Decepado = real.Decepado;
			p.Vida = Math.Clamp(real.Vida, 0, p.VidaMax);
		}
		corpo.AoDecepar = (_, caiu) => SoltarPecas(boneco, caiu);
		corpo.SendoArremessado = () => boneco.TiquesDeVoo > 0;
		corpo.SincronizarVida();

		boneco.Ficha = copia;
		boneco.Combate = corpo;
	}

	/// <summary>
	/// O QUE O CORPO SOFREU LA FORA, cobrado pelas regras do DONO. Chamado no fim do `RestaurarDaMente`, com
	/// os membros do corpo largado ja copiados pro dono.
	///
	/// A MORTE E O NOCAUTE NAO SAO COPIADOS COMO BIT: passam pelo `Morrer()`/`Nocautear()` do dono, que e
	/// onde moram o seguro de morte, o prazo do cadaver e a viagem pro Outro Mundo. A pergunta e a mesma que
	/// o resolvedor de golpe faz depois de ferir (`MeleeResolver.AplicarNoMembro`).
	/// </summary>
	private void DecidirOQueOCorpoSofreu(ServerPlayer pl, ServerPlayer boneco, bool entrouMorto)
	{
		Body corpo = pl.Combate.Corpo;
		bool letal = corpo.DeveMorrer() && !corpo.RegeneraDecepado;

		// (v0.3.0) QUEM ENTROU MORTO (o fantasma que mergulhou -- ver `DimensaoMental.PorQueNaoMergulhar`): o `dead` do corpo de
		// fora e o da entrada, e nao uma morte nova. O golpe letal la fora e o "morrer de novo" do fantasma -- curado, a mesa do
		// Enma, 2 s de coma --, e quem o cobra e a volta, depois da viagem (`MorreuDeNovoLaFora`). Fora isso, o nocaute de fora
		// ele sofre como o vivo.
		if (entrouMorto && letal) return;
		if (!entrouMorto && (boneco.Ficha.dead || letal))
		{
			if (pl.Combate.Morrer()) return;
		}
		if ((!pl.Ficha.dead || entrouMorto) && (boneco.Ficha.KO || corpo.DeveNocautear()))
			pl.Combate.Nocautear(
				boneco.Combate.NocauteRestante > 0 ? boneco.Combate.NocauteRestante : MeleeResolver.TetoDoNocaute,
				porVital: boneco.Combate.NocautePorVital || corpo.DeveNocautear());
	}

	/// <summary>
	/// (v0.3.0) O FANTASMA QUE CAIU DE VEZ LA FORA enquanto meditava: entrou morto (`FotoDaMente.Morto`), continua morto, e o
	/// corpo que ficou levou um golpe que o mataria -- o `Morrer()` do corpo de fora, que nao tem gancho de fantasma, so o
	/// deixa ferido. E perguntado ANTES da volta (a foto e o corpo de fora somem no restauro) e cobrado DEPOIS dela, pelo
	/// `Morrer()` do dono: no fantasma de pe e o `AoMorrerDeNovo` (`GameServer.MorrerDeNovoNoAlem`) -- curado, de volta a mesa
	/// do Enma, 2 s em coma e o passe rasgado. Cobrado antes, ele viajaria da mente pra mesa e em seguida seria puxado pro
	/// lugar do corpo.
	///
	/// QUEM E ARRANCADO POR FORA (`ArrancarDoTranse`: um admin, o fim do passe) nao paga aqui: vai pra onde o levam, com as
	/// feridas do corpo de fora.
	/// </summary>
	private bool MorreuDeNovoLaFora(ServerPlayer pl) =>
		pl.Ficha.dead && pl.FotoDaMente is { Morto: true } && pl.BonecoLargado is { } b && TemCorpoProprio(b, pl)
		&& ZoneList(b.Zone.Hash).Contains(b) && b.Combate.Corpo.DeveMorrer() && !b.Combate.Corpo.RegeneraDecepado;

	/// <summary>
	/// A FAXINA DA SAIDA: o que foi ligado, armado ou posto em movimento DENTRO da mente nao segue o corpo
	/// pra fora. Sem ela a foto devolvia os membros e deixava de pe tudo o que continuaria a feri-los.
	///
	///   * o que se move sozinho: o arremesso em curso (`TickDoEmpurrao` o seguia no mundo real), o Zanzo
	///     Clash (que termina com um golpe de verdade no perdedor), a paralisia, a morte adiada de uma
	///     tecnica e as marcas do Godly Display;
	///   * o que foi LIGADO la dentro: os buffs que nao estavam de pe na entrada (os de antes continuam),
	///     o Kaio-ken, a carga de Ki;
	///   * o relogio da luta: a marca de combate, o atordoamento, o combo, a guarda -- e o modo letal volta
	///     a ser o da entrada (o `murderToggle` que o original guarda e devolve, `MindMeditate.dm:375-376`);
	///   * a atividade: o corpo acorda PARADO.
	///
	/// `foto` nula e o transe sem registro (o estado inconsistente): a faxina vale igual, e sem lista de
	/// "o que ja estava ligado" cai tudo.
	/// </summary>
	private void LimparOCombateDaMente(ServerPlayer pl, FotoDaMente? foto)
	{
		pl.TiquesDeVoo = 0;
		pl.ForcaDoVoo = 0;
		SoltarDoEmbate(pl.Id);
		EsquecerParalisia(pl.Id);
		_mortesPendentesG12.RemoveAll(m => m.Vitima == pl.Id);
		pl.GdMarcados.Clear();
		foreach (ServerPlayer o in _players.Values) o.GdMarcados.Remove(pl.Id);

		foreach (string id in BuffsDe(pl.Id).Keys.ToList())
			if (foto == null || !foto.Buffs.Contains(id)) DesligarBuff(pl, id);
		if (_kaiokenAmtG2.ContainsKey(pl.Id) && foto is not { Kaioken: true }) ReverterKaiokenG2(pl, porKO: false);
		if (pl.Carregando) Carregar(pl, false);

		CombatState c = pl.Combate;
		c.EmCombate = 0;
		c.LutandoDeVerdade = 0;
		c.Stun = 0;
		c.Combo = 0;
		c.Bloqueando = false;
		if (foto != null) c.Letal = foto.Letal;
		pl.UltimoAgressor = 0;
		pl.RancorAte = 0;

		pl.Ficha.med = false;
		pl.Ficha.train = false;
	}

	/// <summary>
	/// A SAIDA QUE SEMPRE FUNCIONA -- o botao do HUD, a tecla M dentro da mente e o verb antigo do menu P
	/// chegam todos aqui.
	///
	/// Tres estados, e nenhum deles termina em "preso":
	///   * NA MENTE (com ou sem corpo pra voltar): a saida de sempre. Sem corpo, o `VoltarProCorpo` cai no
	///     destino de emergencia -- e a faxina roda mesmo sem foto;
	///   * FORA DA MENTE, mas ainda "fora do corpo" por um transe que alguem interrompeu pela metade: fecha
	///     o que sobrou (era o estado em que a meditacao profunda parava de funcionar em silencio);
	///   * NENHUM DOS DOIS: diz que nao ha de onde sair.
	///
	/// PELO CANAL DE VERBOS, e nao pelo de habilidades: aquele e barrado pra corpo possuido e pra quem esta
	/// na espera do torneio, e a porta de saida de um lugar "onde nada e real" nao pode depender de o corpo
	/// estar em condicao de usar tecnica.
	/// </summary>
	internal void SairDoTranse(ServerPlayer pl)
	{
		if (NaMente(pl))
		{
			// (2026-10-27, o pedido 124c) A LUTA NA MENTE NAO SE ENCERRA NO MEIO: com a lembranca de pe, nao se acorda. Ver `GameServer.MenteLista.cs`.
			if (LutandoNaMente(pl)) { Avisar(pl, "a luta na mente so acaba quando um dos dois cai -- nao da pra acordar no meio dela."); return; }
			SairDaMente(pl, "voce abre os olhos.");
			return;
		}

		// (2026-10-18) QUEM OBSERVA UMA NAVE DE FORA nao esta em transe, mas tambem esta fora do corpo: volta pela porta dele, que desfaz o
		// olho e a barra do tanque -- o ramo de baixo devolveria o corpo e deixaria o olho registrado. Ver `GameServer.NaveObservar.cs`.
		if (EhOlhoDeFora(pl)) { PararDeObservar(pl, "voce volta pro proprio corpo, na ponte."); return; }

		if (pl.BonecoLargado != null && NaveDoPiloto(pl) == null)
		{
			RestaurarDaMente(pl);
			VoltarProCorpo(pl, "voce volta pro proprio corpo.");
			return;
		}

		Avisar(pl, "voce nao esta em transe.");
	}

	/// <summary>
	/// ALGUEM ESTA TIRANDO ESTE CORPO DA MENTE POR FORA -- um admin trazendo, o torneio transportando, um
	/// selo, uma fusao. Chamado pelo `MoveToZone` ANTES da viagem.
	///
	/// Essas saidas nao passavam por `SairDaMente`: o jogador ia embora com o dano de dentro (a foto nunca
	/// era aplicada), e deixava pra tras um corpo largado e uma foto pendentes -- dali em diante a meditacao
	/// profunda era recusada sem frase nenhuma (`LargarOCorpo`: "ja esta fora do corpo"), e o primeiro nocaute
	/// o puxava de volta pro corpo velho.
	///
	/// O CORPO VAI JUNTO: quem foi levado nao deixa um corpo meditando no lugar de onde saiu.
	/// </summary>
	private void ArrancarDoTranse(ServerPlayer pl)
	{
		ZoneKey mente = pl.Zone;
		DesfazerOOponente(pl, "");
		RestaurarDaMente(pl);

		ServerPlayer? boneco = pl.BonecoLargado;
		pl.BonecoLargado = null;
		if (boneco != null)
		{
			boneco.DonoDoCorpoLargado = 0;
			if (ZoneList(boneco.Zone.Hash).Contains(boneco)) RemoverNpc(boneco);
		}

		Avisar(pl, "voce e arrancado do transe.");
		foreach (ServerPlayer o in ZoneList(mente.Hash))
			if (o != pl && o.Peer != null) Avisar(o, $"a presenca de {pl.Name} se desfaz da sua mente...");
		GD.Print($"[server] {pl.Name} foi tirado da mente por fora: o transe fechou antes da viagem");
	}

	/// <summary>
	/// O SAVE DE QUEM ESTAVA EM TRANSE VIRA O SAVE DO CORPO DE VERDADE, antes de qualquer leitura. Chamado no
	/// login, com o save ainda na mao.
	///
	/// O autosave grava quem esta dentro de uma mente (e o servidor pode cair ali): sem isto o personagem
	/// voltava DENTRO da mente, sem corpo pra onde sair, e com os ferimentos de mentira ja gravados como de
	/// verdade. O bloco `Transe` traz o endereco e o estado do corpo largado no instante da gravacao.
	/// </summary>
	internal static void TirarOSaveDoTranse(CharacterSave c)
	{
		if (c.Transe is not { } t) return;
		c.Transe = null;

		c.Zona = t.Zona;
		c.ZonaTipo = t.ZonaTipo;
		c.ZonaSeed = t.ZonaSeed;
		c.X = t.X;
		c.Y = t.Y;
		c.Membros = t.Membros;
		if (c.Ficha != null)
		{
			c.Ficha.Ki = t.Ki;
			c.Ficha.stamina = t.Vigor;
			c.Ficha.KO = false;
			c.Ficha.dead = t.Morto;
			c.Ficha.med = false;
			c.Ficha.train = false;
		}
	}
}
