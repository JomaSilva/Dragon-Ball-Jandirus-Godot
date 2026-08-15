using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// ============================ AS JANELAS DA BANCADA QUE FOTOGRAFA O BORRAO (`--diagborrao`) ============================
/// A `--borraoteste` mede os dois relatos do dono em NUMERO -- 41 provas, sete defeitos injetados -- e
/// fecha verde. **E ela fecharia verde com o borrao nunca desenhado na tela.** Tudo o que ela ve e
/// estado de servidor: o `w.Length` do pacote, o `_saltosAnunciados`, a posicao do corpo. Entre esse
/// pacote e o pixel ha o fio, o `GameClient`, o `World.AoPiscar`, a escolha da origem no `LocalPlayer`
/// (`OrigemDoSalto`) e o `RastroDeCorrida` segurando o pedido ate a posicao de CHEGADA existir.
///
/// O relato do dono e sobre o que ele VE -- *"npcs quando usam DASH n ficam com o EFEITO DE BLUR igual
/// os jogadores"* --, e essa metade so o olho fecha. Este arquivo e o que a `--diagborrao` precisa do
/// lado do servidor pra montar a cena; quem mede o pixel e o `Client/RoboDeBorrao.cs`.
/// ========================================================================================================================
///
/// ============================ O QUE ESTAS JANELAS **NAO** FAZEM ============================
/// Nao desenham nada, nao anunciam salto nenhum e nao escrevem `SaiuDe`. O salto sai do
/// <see cref="Atacar"/> -- literalmente o que o `case C2S.Action` chama quando o jogador aperta
/// SHIFT+ESPACO -- e a posse sai do <see cref="AssumirOCorpo"/>, a porta unica da fera do Oozaru e da
/// furia lendaria. O que elas escrevem e so o PALCO: onde os corpos nascem e pra onde eles olham.
/// ======================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// Faixa de ids propria -- longe do `_nextId` e das outras bancadas (91.200 da pose, 91.300 do alem,
	/// 91.700 da `--doiscorposteste`, 91.900 da `--diagcorpos`).
	/// </summary>
	private const int IdBaseDaFotoDoBorrao = 92_100;

	private readonly List<int> _fbNascidos = [];

	/// <summary>
	/// ============================ TRES FAIXAS PARALELAS, TODAS CAMINHAVEIS ============================
	/// O <see cref="AcharPalco"/> acha UMA reta. Esta cena precisa de tres lado a lado -- a do jogador, a
	/// do NPC e a de quem NAO arranca (o contra-exemplo) -- porque o pedido do dono e uma COMPARACAO, e
	/// duas faixas fotografadas em momentos diferentes nao respondem "no mesmo quadro".
	///
	/// **E CAMINHAVEL QUER DIZER `ModoDeTravessia.APe`, e nao "sem parede".** A agua barra quem anda
	/// (`ClasseDeAgua.Bloqueia`) e barra a investida pelo `PathOccupied` do <see cref="Aproximar"/>. A
	/// familia F da `--borraoteste` nasceu medindo "0 arranques, 0 px andados, Ki intacto" nas quatro
	/// distancias por causa exatamente disso: um corredor sem parede dentro de um lago. A bancada estava
	/// medindo o mapa.
	/// ==============================================================================================
	/// </summary>
	/// <param name="tiles">Comprimento de cada faixa, em tiles.</param>
	/// <param name="faixa">Afastamento entre as faixas, em pixels (perpendicular ao rumo).</param>
	internal (bool Achou, Vec2 Origem, Facing Rumo) PalcoDoBorrao(int idDono, int tiles, float faixa)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? pl)) return (false, Vec2.Zero, Facing.East);
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		if (mapa == null) return (false, Vec2.Zero, Facing.East);

		const float T = ZoneCollision.TileSize;

		// LESTE E OESTE PRIMEIRO, e e escolha de ENQUADRAMENTO: a janela e mais larga do que alta
		// (1600x900), e um salto de 300 px pelo eixo Y sairia por cima do quadro com a camera centrada
		// no corpo. A regra medida e a mesma nos quatro rumos -- a familia F da `--borraoteste` mede o
		// numero, e ela nao escolhe eixo.
		foreach (Facing f in (Facing[])[Facing.East, Facing.West, Facing.South, Facing.North])
		{
			Vec2 dir = MeleeArea.Frente(f);
			var lado = new Vec2(-dir.Y, dir.X);   // perpendicular unitaria: a separacao entre as faixas

			for (int raio = 4; raio <= 60; raio += 2)
				for (int ax = -raio; ax <= raio; ax += 2)
					for (int ay = -raio; ay <= raio; ay += 2)
					{
						if (Math.Abs(ax) != raio && Math.Abs(ay) != raio) continue;   // so a borda do anel
						var origem = new Vec2(pl.Pos.X + ax * T, pl.Pos.Y + ay * T);

						bool livre = true;
						// AS TRES FAIXAS DE UMA VEZ, e a amostragem e de MEIO TILE como a do `AcharPalco`:
						// de tile em tile a caixa dos pes passa raspando na quina de um lago e o corpo
						// trava no meio da cena, com a foto ja tirada.
						foreach (float off in (float[])[-faixa, 0f, faixa])
						{
							Vec2 comeco = origem + lado * off;
							for (int k = 0; k <= tiles * 2 && livre; k++)
								if (MoveRules.Occupied(mapa, comeco + dir * (k * T * 0.5f), ModoDeTravessia.APe))
									livre = false;
							if (!livre) break;
						}
						if (livre) return (true, origem, f);
					}
		}
		return (false, Vec2.Zero, Facing.East);
	}

	/// <summary>
	/// PARA ONDE UM CORPO DA CENA VAI. **E encenacao, e nao medida.**
	///
	/// O corpo do jogador e a CAMERA (o `Camera2D` e filho do `LocalPlayer`), entao o palco tem que
	/// nascer em volta dele -- forjar a cena onde ela cabe e fotografar de onde o berco calhou de
	/// largar o host dariam uma foto do nada. E entre uma cena e a seguinte todo mundo volta pra marca,
	/// senao a segunda mediria as sobras da primeira (depois de um salto os dois corpos ficam a 32 px, e
	/// dali nao ha investida nenhuma). O que estas linhas escrevem e o LUGAR; o salto, que e o que se
	/// mede, continua saindo do <see cref="Atacar"/>.
	///
	/// A CORRECAO VAI JUNTO (e o `Peer` nulo do NPC a ignora) porque sem ela o proximo pacote de input
	/// do cliente parte da posicao velha e o servidor puxa o corpo de volta -- a mesma armadilha que o
	/// `Aproximar` e o `Zanzoken` ja tratam, e pelo mesmo par de campos (`SeqDoTeleporte` +
	/// `CorrecaoEsperadaAte`).
	/// </summary>
	internal void PorNoPontoNaFotoDoBorrao(int id, Vec2 onde, Facing olhar)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		long agora = NowMs();
		pl.Pos = onde;
		pl.Facing = olhar;
		pl.LastInputMs = agora;
		pl.CorrecaoEsperadaAte = agora + 500;
		pl.SeqDoTeleporte = pl.SeqInput;
		pl.OrcamentoPx = 0;

		var w = Protocol.Begin(Protocol.S2C.Correction);
		w.Put(pl.SeqInput);
		w.PutVec(pl.Pos);
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// UM CORPO DA CENA -- e ele reusa o <see cref="ForjarCorpoDeFoto"/> da bancada da pose, como faz a
	/// `--diagcorpos`. Aquele metodo ja resolve as tres coisas que uma foto precisa e que sao faceis de
	/// esquecer: `PowerLevel` (senao o poder expresso nasce zero), `AparenciaDeNpc` (senao a foto sai com
	/// um marcador flutuando) e `TrocarAparencias` (senao sai um tufo de cabelo sem corpo).
	///
	/// **E ELE NASCE SEM A AFTERIMAGE, de proposito.** O borrao nao e tecnica -- e o corpo ter passado
	/// por ali --, e a cena inteira desta bancada e sobre corpos que NAO sabem a skill. Um vulto opaco
	/// parado na origem entraria na conta de pixel como se fosse rastro.
	/// </summary>
	internal int ForjarParaAFotoDoBorrao(int idDono, Vec2 desloc, string nome, double bp)
	{
		int id = ForjarCorpoDeFoto(idDono, desloc, nome, bp, comEscada: false);
		if (id != 0) _fbNascidos.Add(id);
		return id;
	}

	/// <summary>
	/// O SALTO DESTE CORPO, do jeito que ele foi no INSTANTE em que aconteceu -- e nao do jeito que a
	/// posicao de agora sugere. Ver o carimbo `_saltoDeCadaCorpo` em <see cref="AnunciarZanzo"/>: pra um
	/// corpo com cliente do outro lado, a posicao de dois quadros depois ja veio reconciliada, e a
	/// bancada media 257 px num salto de 268.
	/// </summary>
	internal (bool Houve, Vec2 De, Vec2 Para, float Quanto) SaltoAnunciadoNaFotoDoBorrao(int id)
		=> _saltoDeCadaCorpo.TryGetValue(id, out (Vec2 De, Vec2 Para) s)
			? (true, s.De, s.Para, (s.Para - s.De).Length)
			: (false, Vec2.Zero, Vec2.Zero, 0f);

	/// <summary>ONDE ESTE CORPO ESTA e pra onde ele olha -- o que a legenda da foto carrega.</summary>
	internal (Vec2 Pos, Vec2 SaiuDe, long DashLivreEm, bool Possuido) EstadoNaFotoDoBorrao(int id)
	{
		ServerPlayer? pl = CorpoDaFotoDeCorpos(id);
		return pl == null ? (Vec2.Zero, Vec2.Zero, 0, false)
						  : (pl.Pos, pl.SaiuDe, pl.DashLivreEm, pl.CerebroDaPosse != null);
	}

	/// <summary>PRA ONDE ESTE CORPO OLHA. Sem isto o cone do arranque procura alvo pro lado errado.</summary>
	internal void ApontarNaFotoDoBorrao(int id, Facing olhar)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Facing = olhar;
	}

	/// <summary>
	/// MARCA UM ALVO -- o <see cref="Mirar"/>, o mesmo `case C2S.Alvo` do duplo clique do jogador.
	///
	/// **E ele e o que estica o arranque**, e nao ser NPC: sem marca o pesado busca 160 px, com marca
	/// busca 480 (`AlcanceDoDashMarcado`). A IA marca todo tique pelo `Cerebro`; o jogador marca com o
	/// mouse. Essa e a assimetria inteira do relato 2, e a familia E da `--borraoteste` a injeta.
	/// </summary>
	internal void MarcarNaFotoDoBorrao(int quem, int alvo)
	{
		if (_players.TryGetValue(quem, out ServerPlayer? pl)) Mirar(pl, alvo);
	}

	/// <summary>
	/// **O SALTO, PELO FUNIL DE PRODUCAO.** <see cref="Atacar"/> com o golpe PESADO e literalmente o que
	/// o `case C2S.Action` executa quando chega o pacote de SHIFT+ESPACO -- mesma virada pro marcado,
	/// mesmo <see cref="Aproximar"/>, mesmo `AnunciarZanzo`, mesmo soco no fim.
	///
	/// Devolve quanto o corpo ANDOU. Zero quer dizer que a investida nao aconteceu (sem alvo, sem Ki, em
	/// recarga ou parede no caminho) -- e sem deslocamento nao ha trajeto que borrar, o que faz desta
	/// linha o CONTRA-EXEMPLO quando ela e zero de proposito.
	/// </summary>
	internal float SaltarNaFotoDoBorrao(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		pl.Combate.Recarga = 0;
		pl.Combate.Stun = 0;
		pl.AtaqueAte = 0;
		pl.DashLivreEm = 0;
		pl.Ficha.Ki = pl.Ficha.MaxKi;

		Vec2 partiu = pl.Pos;
		Atacar(pl, Protocol.Golpe.Pesado);
		return (pl.Pos - partiu).Length;
	}

	/// <summary>
	/// ============================ A FERA: O CORPO PASSA A SER DIRIGIDO DE FORA ============================
	/// <see cref="AssumirOCorpo"/> e a porta UNICA das duas possessoes do jogo -- a fera do Oozaru e a
	/// furia lendaria. Chamada no corpo do JOGADOR, ela e o unico jeito de exercitar o ramo local do
	/// `World.AoPiscar` com a origem vindo do SERVIDOR: com as redeas, o corpo local usa o `_deOndeSai`
	/// que ele mesmo guardou no `LerAcoes`; **sem as redeas, o `LerAcoes` nao roda** e esse campo ficaria
	/// parado no ultimo soco que o dono deu, possivelmente noutra zona.
	///
	/// Nao e hipotese: e o defeito que o dono ja relatou uma vez neste mesmo campo -- *"o efeito do
	/// zanzoken acontece no ultimo local q usei o shift+espaco"*. Ver `LocalPlayer.OrigemDoSalto`.
	/// ====================================================================================================
	/// </summary>
	internal void PossuirNaFotoDoBorrao(int id, bool possuir)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		if (possuir) AssumirOCorpo(pl, new Cerebro());
		else DevolverAsRedeas(pl);
	}

	/// <summary>
	/// ============================ O PORTAO ANTIGO DO BORRAO, LIGAVEL DA BANCADA DE FOTO ============================
	/// Liga o <see cref="_borraoSoComSkill"/> -- o `if (zanzo)` que existia no lugar do `if (investiu)`, e
	/// que era o relato 1 do dono inteiro. **Falso em jogo, sempre.**
	///
	/// A `--borraoteste` ja o injeta e mede o pacote sumir. Esta porta existe pra a `--diagborrao` fazer o
	/// mesmo do lado do PIXEL: uma bancada de foto que so foi vista com o rastro na tela nao sabe
	/// distinguir "o rastro esta la" de "esta medicao acha rastro em qualquer coisa". E a foto que sai
	/// dela e o ANTES do dono -- o corpo aparecendo no destino sem nada explicando o trajeto.
	/// ==========================================================================================================
	/// </summary>
	internal void PortaoAntigoNaFotoDoBorrao(bool ligar) => _borraoSoComSkill = ligar;

	/// <summary>Tira do mundo os corpos que esta bancada forjou. Ela nao poe mais nada la.</summary>
	internal void LimparAFotoDoBorrao()
	{
		foreach (int id in _fbNascidos)
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) continue;
			_npcsPraTirar.Remove(pl);
			_players.Remove(pl.Id);
			ZoneList(pl.Zone.Hash).Remove(pl);
		}
		_fbNascidos.Clear();
	}
}
