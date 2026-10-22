using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Npc;

namespace Jandirus.Server;

/// <summary>
/// O ROTEIRO DO CHEFE: a ficha pronta virando degraus, por DANO e nao por decisao.
///
/// ============================ A TRANSFORMACAO DE CHEFE NAO E DA IA ============================
/// Esta e a parte do pedido do dono que so o original responde: *"o freeza do planeta vegeta nao
/// transforma enquanto o freeza de namek transforma"*. Os dois sao o mesmo mob, pela mesma fabrica,
/// com o mesmo `ai_no_powerup = 1` e o mesmo cerebro (BossEvents.dm:175-190). Nenhuma linha da IA
/// os distingue.
///
/// O que os distingue e (a) o BP ser uma LISTA em vez de um escalar e (b) haver um controlador de
/// evento -- fora do cerebro -- que olha o membro mais ferido e avanca o estagio
/// (BossEvents.dm:588-590). Este arquivo e esse controlador.
///
/// O unico jeito de o Freeza de Vegeta "nao transformar" e a lista dele ter um item so. Nao ha
/// nenhum `if (ehFreezaDeVegeta)` em lugar nenhum -- nem la, nem aqui.
/// ==========================================================================================
///
/// ============================ POR QUE FORA DO `TickDosCorposSemDono` ============================
/// Porque nao e decisao. O cerebro escolhe o que APERTAR; isto e um estagio de evento, e ele
/// acontece mesmo com o corpo nocauteado a caminho (nao acontece -- ver o gate de KO abaixo -- mas
/// a razao de nao acontecer e uma regra do evento, nao uma escolha do bicho).
///
/// Misturar os dois faria a IA precisar saber de roteiro, e o roteiro precisar de um cerebro pra
/// rodar: um chefe parado numa cidade, sem ninguem por perto, pararia de avancar.
/// ==========================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// A FRACAO DE VIDA DO MEMBRO MAIS FERIDO (0..1). Decepado conta como ZERO.
	///
	/// Copia do `bev_worst_limb_frac` (BossEvents.dm:153). Ele mede o membro e nao o HP de propostio:
	/// o HP e a MEDIA dos membros neste port (`Body.Vida`), e uma media nunca desce o bastante pra
	/// disparar um limiar de 25% enquanto o corpo inteiro nao estiver quase destruido. O que o
	/// original quer medir e "onde ele esta mais quebrado", e isso e o pior membro.
	/// </summary>
	private static double PiorMembro(ServerPlayer pl)
	{
		double pior = 1;
		foreach (BodyPart p in pl.Combate.Corpo.Partes)
		{
			double f = p.Decepado ? 0 : p.Fracao;
			if (f < pior) pior = f;
		}
		return pior;
	}

	/// <summary>
	/// O TIQUE DO ROTEIRO. Varre os corpos com ficha de chefe e avanca quem cruzou o gatilho.
	///
	/// Roda no tique de 1 s (nao no de 30 Hz): a fracao do pior membro nao muda de forma
	/// interessante em 33 ms, e a transformacao e um evento com anuncio, cura e troca de sprite --
	/// coisas que ninguem quer conferir trinta vezes por segundo.
	/// </summary>
	private void TickDoRoteiro()
	{
		// LISTA A PARTE, como o `TickDosCorposSemDono` ja faz: avancar um estagio mexe na vida, no
		// BP e na forma, e nada impede que amanha isso mate ou remova alguem do `_players`.
		foreach (ServerPlayer npc in _players.Values.Where(p => p.Papel is { EhChefe: true }).ToList())
		{
			PapelDeNpc papel = npc.Papel!;

			// ============================ QUEM ESTA VENDO ESTE CHEFE ANOTA QUE O VIU ============================
			// *"tem a opcao de enfrentar NPCS BOSS Q VC JA VIU ANTES na sua mente"* -- e este laco e o
			// unico do servidor que ja varre **todo corpo com ficha de chefe que existe agora**,
			// independente de quem o pos no mundo (a cadeia de sagas, o admin, uma bancada). Pendurar a
			// testemunha no monitor da saga deixaria de fora justamente os chefes que o dono invoca pra
			// testar. Ver `AnotarChefesVistos` (`GameServer.Mente.cs`).
			//
			// ANTES dos `continue` de morte e nocaute: ver um chefe caido tambem e te-lo visto -- e o
			// caido e, quase sempre, o instante em que mais gente esta olhando.
			// ================================================================================================
			AnotarChefesVistos(npc);

			if (npc.Ficha.dead) continue;

			// NOCAUTEADO NAO TRANSFORMA -- `!boss2.KO` (BossEvents.dm:588), com o comentario que
			// explica o desenho: *"janela de finalizacao"*. Sem isso o chefe se cura e volta com BP
			// novo exatamente no instante em que o jogador conseguiu derruba-lo, e o nocaute deixa
			// de significar alguma coisa.
			if (npc.Ficha.KO) continue;

			// ============================ O BP PINADO SE REANCORA, TODO SEGUNDO ============================
			// E o `NPCTicker()` do `EventBoss` (BossEvents.dm:189-194) inteiro, e ele existe pela mesma
			// razao la e aqui: *"BP anunciado = BP real"*. O corpo de um chefe passa pelos MESMOS lacos
			// de um jogador -- `TickFichas` chama `Treinar` e `Ficha.Tick` pra todo mundo no `_players`,
			// e o Zenkai paga na hora da derrota --, entao um chefe Saiyajin nocauteado numa luta longa
			// terminaria com um BP que ninguem anunciou.
			//
			// A conta e a do degrau atual, e ela vem do PAPEL: quem nasceu por uma saga carrega o vetor
			// que o marco pinou; quem nasceu pelo admin ou pela bancada cai na ficha do `npcs.json`.
			// ==========================================================================================
			double pinado = papel.BpAtual;
			if (pinado > 0 && Math.Abs(npc.Ficha.BP - pinado) > 0.5)
			{
				npc.Ficha.BP = pinado;
				npc.Ficha.Statify();
				npc.Ficha.PowerLevel();
				npc.SigAtributos = "";
			}

			// (2026-10-20) ...E OS ATRIBUTOS: o dobro do jogador mais forte de agora, e a pericia no maximo -- ver `DarODobroAoChefe`
			DarODobroAoChefe(npc);

			// (2026-10-19) O CELL DO TORNEIO NAO TROCA DE FORMA POR DANO: no x1 ele cai nocauteado, e a volta Super Perfeita e a da morte -- o dono:
			// *"se ele for derrotado por nocaute o jogo vai falar q ele foi morto na luta e volta super perfeito"*. Ver `GameServer.TorneioDoCell.cs`.
			if (CellNoTorneio(npc)) continue;

			double gatilho = papel.GatilhoAtual;
			if (gatilho < 0) continue;                     // ultimo degrau: nada o encerra
			if (PiorMembro(npc) > gatilho) continue;
			// Na mente, a lembranca nao sobe alem da ultima forma que o dono viu.
			if (!LembrancaPodeSubir(npc)) continue;

			AvancarEstagio(npc);
		}
	}

	/// <summary>
	/// SOBE UM DEGRAU: regenera, cura, reancora, troca a forma e PINA o BP novo.
	///
	/// E o `freeza_transform()` (BossEvents.dm:651-676) na ordem dele, e a ordem importa em dois
	/// pontos que sao facilissimos de escrever errado:
	///
	///  * A CURA VEM ANTES DO BP. Pinar primeiro chamaria `Statify` com o corpo ainda em pedacos, e
	///    o `HP` (media dos membros) entraria na normalizacao pela metade.
	///  * O PIOR MEMBRO E EMPURRADO ESTRITAMENTE ACIMA DO PROXIMO LIMIAR (`+0,01`, BossEvents.dm:662).
	///    Sem esse empurrao, uma rajada unica que leve a vida de 0,72 a 0,20 dispararia a
	///    transformacao, curaria pela metade -- e o resultado ainda estaria abaixo do limiar
	///    seguinte, encadeando duas (ou tres) transformacoes no mesmo instante. O chefe pularia
	///    metade da propria ficha num golpe.
	/// </summary>
	private void AvancarEstagio(ServerPlayer npc)
	{
		PapelDeNpc papel = npc.Papel!;
		EstagioDeChefe? saindo = papel.Degrau;
		papel.Estagio++;
		EntrarNoDegrau(npc, Math.Clamp(saindo?.Cura ?? 0.5, 0, 1));
	}

	/// <summary>
	/// ============================ O CHEFE COM FORMA POR VIR NAO MORRE ANTES DA HORA (dono, 2026-10-18) ============================
	/// *"faça com q viloes q tenham transformaçoes q ainda n ativaram nao possam ser mortos antes da hora (menos o cell) pq acabei matando o
	/// freeza na terceira forma antes dele virar a forma final e com isso a saga n acabou e bugou, ao o jogo detectar q o vilao morreu ele deveria
	/// iniciar a cinematica de transformaçao e recuperar toda a vida"*.
	///
	/// O roteiro sobe o degrau pelo membro mais ferido, uma vez por segundo (<see cref="TickDoRoteiro"/>) -- e NAO com o corpo nocauteado (a
	/// janela de finalizacao do DM, `!boss2.KO`). Era nessa janela, ou num golpe so que passava do limiar direto pra morte, que o Freeza morria na
	/// forma 3. Agora o golpe que o mataria e a TRANSFORMACAO: o degrau seguinte com a cena dele (<see cref="AvancarEstagio"/>) e a vida INTEIRA de
	/// volta, de pe (`CombatState.Curar`). So morre quem esta no ultimo degrau -- o fim que a saga espera.
	///
	/// QUEM ENTRA: o chefe com degrau de DANO por vir (`GatilhoAtual >= 0`), menos os moldes de <see cref="ChefesQueMorremAntesDaHora"/> (o Cell,
	/// pelo nome, como o dono pediu). Instalado no `NegarMorte` (`PrepararCombate`) ANTES dos seguros de jogador; o verb de admin de matar
	/// (`ignorarSeguro`) continua matando. DIVERGENCIA DECLARADA: no DM o Freeza morto na forma 3 morria (o `freeza_transform` so roda pelo
	/// limiar, BossEvents.dm:588).
	/// ===========================================================================================================================
	/// </summary>
	private bool NegarMorteDoChefe(ServerPlayer npc)
	{
		// (2026-10-20) O SUPER PERFEITO, ANTES DE TUDO: o golpe que o mataria vira o Kamehameha que explode a Terra -- ver `GameServer.TorneioDoCell.cs`
		if (NegarAMorteDoSuperPerfeito(npc)) return true;
		// (2026-10-20) E O ANDROIDE QUE O CELL CACA: o golpe dele que o mataria o derruba, pra ser absorvido -- ver `NegarAMorteDoAndroideDoCell`
		if (NegarAMorteDoAndroideDoCell(npc)) return true;
		if (ChefeMorreAntesDaHoraDeTeste || npc.Papel is not { EhChefe: true } papel || papel.GatilhoAtual < 0) return false;
		if (ChefesQueMorremAntesDaHora.Contains(papel.Molde.Id)) return false;
		if (!LembrancaPodeSubir(npc)) return false;
		AvancarEstagio(npc);
		npc.Combate.Curar();
		GD.Print($"[server] ROTEIRO: '{npc.Name}' nao morre antes da hora -- o golpe letal virou a transformacao (degrau {papel.Estagio + 1})");
		return true;
	}

	/// <summary>
	/// OS CHEFES QUE MORREM EM QUALQUER DEGRAU: o Cell (o dono: *"menos o cell"*) -- ele tem a propria volta da morte (`RessurgeNoDegrau`, o
	/// Super Perfeito) e os degraus dele sobem tambem por absorcao.
	/// </summary>
	private static readonly HashSet<string> ChefesQueMorremAntesDaHora = new(StringComparer.OrdinalIgnoreCase) { "cell" };

	/// <summary>DEFEITO INJETADO (`--chefesteste`, familia H): o chefe morre em qualquer degrau -- o jogo de antes de 2026-10-18. Falso em jogo, sempre.</summary>
	internal static bool ChefeMorreAntesDaHoraDeTeste;

	/// <summary>
	/// ENTRA NO DEGRAU EM QUE O PAPEL JA ESTA. Separado do <see cref="AvancarEstagio"/> porque ha
	/// **dois** jeitos de um corpo chegar num degrau, e o segundo nasceu com a cadeia de sagas:
	///
	///   * subindo por dano (ou por absorcao) -- <see cref="AvancarEstagio"/>, que cura o que sobrou;
	///   * NASCENDO ja nele -- o Freeza que estava na forma 3 quando o servidor caiu, e o Cell que
	///     volta Super Perfeito. E o `spawn_freeza_namek(s2_form, resume)` (BossEvents.dm:472), que
	///     usa a MESMA fabrica com o degrau salvo.
	///
	/// A cura e o que difere: nascer no degrau 3 nao "recupera 50% do dano" de um corpo que acabou de
	/// ser montado inteiro. Por isso ela e argumento e nao um passo fixo -- e por isso a de nascimento
	/// e zero e nao um numero parecido com meio.
	/// </summary>
	/// <param name="anunciar">
	/// CHEGAR NUM DEGRAU E TRANSFORMAR-SE NELE SAO COISAS DIFERENTES. A frase da ficha ("Freeza
	/// TRANSFORMOU-SE!") pertence a TRANSICAO; um corpo que **nasce** no degrau 3 -- porque o servidor
	/// caiu no meio da luta -- nao se transformou em nada, e anunciar ali faria o mundo ouvir uma
	/// transformacao que ninguem viu. E o proprio original separa os dois textos: o `resume` do
	/// `spawn_freeza_namek` diz *"Freeza AINDA esta em Namek"* (BossEvents.dm:488).
	/// </param>
	private void EntrarNoDegrau(ServerPlayer npc, double cura, bool anunciar = true)
	{
		PapelDeNpc papel = npc.Papel!;
		EstagioDeChefe? novo = papel.Degrau;
		if (novo == null) return;

		double proximoLimiar = papel.GatilhoAtual;   // ja e o do degrau NOVO

		foreach (BodyPart p in npc.Combate.Corpo.Partes)
		{
			// membro decepado VOLTA (`S.RegrowLimb()`): a forma nova e um corpo novo
			if (p.Decepado) npc.Combate.Corpo.Regenerar(p);

			double baseVida = Math.Max(p.Vida, 0);
			p.Vida = Math.Min(p.VidaMax, baseVida + (p.VidaMax - baseVida) * cura);

			if (proximoLimiar >= 0)
				p.Vida = Math.Max(p.Vida, (proximoLimiar + 0.01) * p.VidaMax);
		}
		npc.Combate.SincronizarVida();

		// A FORMA, quando o degrau declara uma. `Entrar` LIBERA e consome a estreia; `AplicarForma`
		// e quem escreve o `ssjBuff` -- os dois sao as mesmas funcoes da tecla C, e e por isso que
		// um chefe transformado sente exatamente o que um jogador transformado sente.
		if (novo.Forma.Length > 0 && Catalogo.Def(novo.Forma) != null)
		{
			string antes = npc.Forma.Atual;
			npc.Forma.Entrar(novo.Forma);
			AplicarForma(npc);
			// (v0.3.0) A ESTREIA DA FORMA NAQUELE CORPO (*"quando boss se transoforma ele tem cinematica igual um player"*): com
			// `estreia: false` a maestria do molde decidia a cena -- e o guardiao Saiyajin, com 60%, virava SSJ CALADO (a cena e
			// dispensada a partir de 50%, `Cinematicas.Degrau`). O degrau de chefe e a primeira vez daquele corpo naquela forma.
			if (anunciar) AnunciarForma(npc, antes, novo.Forma, estreia: !FormaDeChefeSemEstreiaDeTeste);
		}

		// (v0.3.0) A CENA DO DEGRAU -- o Freeza que troca de corpo, o Cell que evolui (*"quando boss se transoforma ele tem
		// cinematica igual um player"*). ANTES da aparencia nova, no mesmo canal ordenado: o cliente guarda o corpo novo ate a
		// virada. So no degrau sem forma (o de forma toca a cena da forma, logo acima) e so quando o degrau e ANUNCIADO -- o
		// nascimento (`anunciar: false`) tem a cena da chegada. Ver `GameServer.ChefeDeSaga.cs`.
		if (anunciar && novo.Forma.Length == 0 && novo.Cena != Cinematicas.CenaDeChefe.Nenhuma) CenaDoChefe(npc, novo.Cena);

		// O CORPO, quando o degrau troca de sprite -- o `boss2.icon = BEV_FREEZA2_ICONS[s2_form]`.
		//
		// ESCREVE NO SLOT 0 E NAO TROCA A LISTA: quem desenha o corpo de um Frost Demon le
		// `FormasDeFrost[0]` (`VisualCatalog.CorpoSprite`), mas a lista inteira e a escada dele --
		// substitui-la por um item so apagaria os outros degraus da ficha.
		if (novo.Corpo.Length > 0)
		{
			if (npc.Visual.FormasDeFrost.Count > 0) npc.Visual.FormasDeFrost[0] = novo.Corpo;
			else npc.Visual.FormasDeFrost.Add(novo.Corpo);
			TrocarAparencias(npc);
		}

		// (v0.3.0) O CORPO DE BIO DO DEGRAU -- o Cell imperfeito, semi-perfeito e perfeito, pela mesma lista de corpos do jogador
		// bio (`BioAndroids.IndiceDoCorpo`). So o desenho: o BP e o pinado.
		if (novo.DegrauDoBio > 0)
		{
			npc.Visual.Corpo = Jandirus.Core.Races.BioAndroids.IndiceDoCorpo(novo.DegrauDoBio);
			TrocarAparencias(npc);
		}

		// O BP PINADO, e a normalizacao junto -- `bev_pin_bp` (BossEvents.dm:230) tambem enche o Ki:
		// *"transformar renova o folego"*.
		//
		// PELO PAPEL, e nao pelo `novo.Bp` direto: quando a saga pinou o vetor no disparo do marco, e
		// ELE que manda -- inclusive nos degraus que so vao ser alcancados quinze minutos depois, no
		// meio da luta. Ler a ficha do disco aqui deixaria o primeiro degrau pinado e os outros quatro
		// livres pra mudar com o servidor, que e o defeito com quatro quintos do tamanho.
		npc.Ficha.BP = papel.BpAtual;
		Jandirus.Core.Npc.SorteioDeNpc.Normalizar(npc.Ficha);
		npc.SigAtributos = "";

		if (anunciar && novo.Anuncio.Length > 0) AnunciarNaZona(npc, novo.Anuncio);

		GD.Print($"[server] ROTEIRO: '{npc.Name}' entrou no degrau {papel.Estagio + 1}/"
			   + $"{papel.Molde.Estagios.Length} | BP {npc.Ficha.BP:N0}"
			   + (novo.Forma.Length > 0 ? $" | forma {novo.Forma}" : ""));
	}

	/// <summary>
	/// O anuncio do degrau, pra zona inteira. Pelo `Falar` do proprio chefe -- que e o canal que ja
	/// existe pra corpo sem dono (`Chat.cs:41` transmite pra zona e nao pede `Peer`) e que ja tem
	/// trava de repeticao por id.
	/// </summary>
	private void AnunciarNaZona(ServerPlayer quem, string texto)
	{
		foreach (ServerPlayer o in ZoneList(quem.Zone.Hash)) Avisar(o, texto);
		EscutaDoRoteiro?.Add((quem.Id, texto));
	}

	/// <summary>
	/// O que o roteiro anunciou -- ligada so pela bancada (`--npcteste`), nula em jogo.
	/// Mesmo desenho da <see cref="EscutaDeAvisos"/>, e pelo mesmo motivo: pacote que saiu no fio
	/// nao volta, e a frase que o jogador ouve e metade do que esta sessao entrega.
	/// </summary>
	internal static List<(int Quem, string Texto)>? EscutaDoRoteiro;
}
