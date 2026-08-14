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

			double gatilho = papel.GatilhoAtual;
			if (gatilho < 0) continue;                     // ultimo degrau: nada o encerra
			if (PiorMembro(npc) > gatilho) continue;

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
			if (anunciar) AnunciarForma(npc, antes, novo.Forma, estreia: false);
		}

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
