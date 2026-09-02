using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// O ZANZOKEN COMO DESLOCAMENTO -- piscar pra um ponto do chao, deixando a miragem pra tras.
///
/// ============================ DE ONDE VEM ============================
/// E o `Zanzoken_Dodge` do original (`misc.dm:66-68`, nivel 3 da Afterimage Technique): "use the
/// skill to immediately teleport to a free tile around you". O dono pediu a versao com mira --
/// "faça com quem tenha after image ao dar double click no chao ele de o teleporte e deixando a
/// miragem a onde estava" -- que e a mesma mecanica com destino escolhido em vez de sorteado.
/// =====================================================================
///
/// TUDO QUE IMPORTA E DECIDIDO AQUI. O cliente manda um PONTO e mais nada: se ele decidisse, a
/// tecnica seria teleporte livre pra qualquer cliente modificado -- que e a definicao de
/// atravessar parede de graca.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// ALCANCE DA PISCADA, em pixels. Seis tiles.
	///
	/// O original teleporta pra um tile LIVRE em volta -- alcance de 1. Aqui vale mais porque o
	/// port tem movimento livre e mira do mouse: um tile de distancia num jogo sem grade nao e
	/// escapar de nada, e o gesto (duplo clique num ponto) so faz sentido se der pra apontar
	/// pra algum lugar que valha a pena.
	/// </summary>
	private const float AlcanceDoZanzo = 192f;

	/// <summary>Fracao do Ki maximo por piscada. Metade do que custa uma investida de soco.</summary>
	private const double CustoZanzoKi = 0.025;

	/// <summary>Espera entre duas piscadas, em ms. Sem ela a tecnica vira voo.</summary>
	private const long RecargaZanzoMs = 900;

	/// <summary>
	/// O jogador pediu pra piscar ate um ponto.
	///
	/// AS RECUSAS SAO MUDAS por escolha: elas acontecem no meio de uma luta, e encher o chat de
	/// "sem Ki" / "muito longe" a cada duplo clique tira a atencao de onde ela precisa estar. A
	/// unica que FALA e a de nao ter a skill -- essa e a que o jogador nao tem como deduzir.
	/// </summary>
	private void Zanzoken(ServerPlayer pl, Vec2 destino)
	{
		// O FIO PSIQUICO DO HERAN (lote G11) usa o MESMO clique no chao: com o toggle ligado, o duplo
		// clique arma um fio de paralisia aos pes em vez de piscar. Ver `FioPsiquicoNoCliqueG11`.
		if (FioPsiquicoNoCliqueG11(pl, destino)) return;

		if (pl.Livro?.Sabe(PathDoZanzoken) != true)
		{
			Avisar(pl, "você não sabe se mover assim -- é o que a Afterimage Technique ensina.");
			return;
		}

		if (pl.Ficha.KO || pl.Ficha.dead) return;

		long agora = NowMs();
		if (agora < pl.ZanzoLivreEm) return;

		Vec2 de = pl.Pos;
		Vec2 d = destino - de;
		float dist = d.Length;
		if (dist < 8f) return;   // clicou nos proprios pes

		// LONGE DEMAIS ENCURTA, nao recusa. Recusar por meio pixel obrigaria o jogador a medir
		// distancia com o mouse no meio de uma briga; encurtar entrega o gesto que ele fez, ate
		// onde a tecnica alcanca.
		if (dist > AlcanceDoZanzo) destino = de + d.Normalized() * AlcanceDoZanzo;

		double custo = pl.Ficha.MaxKi * CustoZanzoKi;
		if (pl.Ficha.Ki < custo) return;

		// PAREDE MANDA MAIS QUE A TECNICA -- a mesma regra da investida do soco. Sem isto o
		// Zanzoken vira a forma barata de entrar em qualquer casa fechada.
		ZoneCollision? mapa = _catalogo?.Get(pl.Zone)?.Mapa;
		if (mapa != null && (MoveRules.Occupied(mapa, destino) || MoveRules.PathOccupied(mapa, de, destino)))
			return;

		pl.Ficha.Ki -= custo;
		pl.Pos = destino;
		pl.Facing = MoveRules.FacingFrom(d, pl.Facing);
		pl.ZanzoLivreEm = agora + RecargaZanzoMs;

		// O MESMO CUIDADO DA INVESTIDA: os pacotes que o cliente ja mandou falam da posicao velha,
		// e sem o carimbo de sequencia o servidor os trataria como cliente errado e puxaria o
		// corpo de volta (ver `GameServer.Input`).
		pl.LastInputMs = agora;
		pl.CorrecaoEsperadaAte = agora + 500;
		pl.SeqDoTeleporte = pl.SeqInput;
		pl.OrcamentoPx = 0;

		var corr = Protocol.Begin(Protocol.S2C.Correction);
		corr.Put(pl.SeqInput);
		corr.PutVec(pl.Pos);
		pl.Peer?.Send(corr, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

		AnunciarZanzo(pl, de);
	}

	/// <summary>
	/// Conta a piscada pra ZONA INTEIRA, com o ponto de PARTIDA.
	///
	/// A origem viaja no pacote porque e onde a miragem nasce, e quando ele chega o corpo ja esta
	/// no destino -- quem recebesse so o id desenharia o vulto em cima do jogador, que e o oposto
	/// de "ficou pra tras". Vale pra quem piscou tambem: e o mesmo pacote, e assim as duas pontas
	/// desenham a mesma coisa no mesmo lugar.
	///
	/// Canal NAO confiavel: perder um vulto custa meio segundo de efeito, e a piscada e uma coisa
	/// de combate, onde o trafego ja e o que mais aperta.
	///
	/// ============================ ESTE PACOTE VIROU "O CORPO SALTOU", NAO "O CORPO PISCOU" ============================
	/// O dono: *"npcs quando usam DASH n ficam com o EFEITO DE BLUR igual os jogadores"*.
	///
	/// O borrao (`Client/RastroDeCorrida.cs`) nunca foi um efeito de dash: e o rastro de CORRER, e o
	/// jogador so o via no arranque por COINCIDENCIA -- a mesma tecla SHIFT que faz o golpe ser Pesado
	/// (`LocalPlayer:957`) tambem liga o `_correndo` (`LocalPlayer:635`), que liga o rastro. Quem nao
	/// segura SHIFT (o soco leve, que TAMBEM investe) nunca borrou; e o NPC nunca borrou porque o
	/// cerebro so escreve `Comando.Correndo` na FUGA (`Cerebro:1831`) -- medido: 0 em 1000 comandos de
	/// perseguicao com o temperamento de fabrica.
	///
	/// Pendurar o borrao no INPUT foi o erro, e consertar isso no cliente exigiria um `if` de tipo de
	/// corpo em cada ponta. Entao ele desceu pro FUNIL DO MOVIMENTO: **quem sabe que houve deslocamento
	/// e o `Aproximar`, e ele ja anunciava por aqui.** Agora o anuncio sai de TODA investida que moveu
	/// o corpo -- jogador, NPC e corpo possuido pelo mesmo caminho, sem `if` nenhum -- e o
	/// <paramref name="vulto"/> diz se, POR CIMA do borrao, tambem nasce a miragem.
	///
	/// CUSTO: **um byte** (o `vulto`). O pacote vai de 13 pra 14 bytes -- opcode(1) + id(4) + Vec2(8).
	/// O que cresce de verdade e a FREQUENCIA: antes so quem sabia a Afterimage anunciava, agora
	/// qualquer corpo que arranque anuncia. Teto por corpo: 2 pacotes/s (`RecargaDashMs = 500`).
	/// ==============================================================================================================
	/// </summary>
	/// <param name="vulto">
	/// Nasce a IMAGEM REMANESCENTE junto? So quem sabe a Afterimage deixa vulto -- o borrao do
	/// deslocamento nao pede skill nenhuma, porque ele nao e tecnica: e o corpo tendo passado por ali.
	/// </param>
	private void AnunciarZanzo(ServerPlayer pl, Vec2 de, bool vulto = true)
	{
		// QUEM ESTA INVISIVEL NAO DEIXA VULTO.
		//
		// A miragem e uma FOTO do corpo, opaca, parada num ponto: um jogador escondido que piscasse
		// (ou investisse) entregava a propria posicao com ela. O sigilo do corpo nao pode depender
		// de o jogador lembrar de nao usar a tecnica.
		//
		// O corte e aqui e nao no cliente: mandar o pacote e depois pedir pra ele nao desenhar seria
		// entregar a posicao pra qualquer cliente modificado, que e a mesma regra do BP escondido.
		if (EstaOculto(pl.Id)) return;

		var w = Protocol.Begin(Protocol.S2C.Zanzo);
		w.Put(pl.Id);
		w.PutVec(de);
		w.Put(vulto);

		// ============================ O CARIMBO QUE A BANCADA LE (`--borraoteste`) ============================
		// Duas linhas, e elas medem o que REALMENTE saiu: o `w.Length` e o tamanho do pacote montado,
		// nao a soma dos `sizeof` que eu acharia que ele tem.
		//
		// SAO NECESSARIAS PORQUE O DESTINO DAQUI E UM `Peer`, e um NPC nao tem nenhum. Sem carimbo, a
		// unica coisa que a bancada poderia conferir seria uma COPIA da condicao do `Atacar` -- ou
		// seja, ela ficaria verde afirmando o que ela mesma escreveu. E o cego que este projeto ja
		// pagou ("uniform escrito != pixel desenhado"): aqui o `w` e o pixel.
		// ====================================================================================================
		_saltosAnunciados++;
		_ultimoSalto = (pl.Id, de, vulto, w.Length);

		// ============================ E O SALTO DE CADA CORPO, PORQUE UM QUADRO DEPOIS ELE JA NAO E EXATO ============================
		// A bancada de FOTO (`--diagborrao`) precisa do tamanho do salto do JOGADOR, e ela nao tem como
		// ler isso do estado: o corpo de quem tem cliente e RECONCILIADO nos quadros seguintes -- o
		// cliente ainda esta mandando input da posicao velha quando a `Correction` sai, e o servidor
		// acomoda alguns pixels disso dentro do `OrcamentoPx`.
		//
		// Medido: um salto de 268 px lido dois quadros depois dava 257. O numero nao estava errado, a
		// LEITURA estava tarde -- e a bancada acusava "o NPC salta mais que o jogador", que e o relato do
		// dono nascendo de um artefato de medicao. O corpo sem cliente nao sofre disso, e era so por isso
		// que os dois discordavam.
		//
		// Aqui, `pl.Pos` ainda e o destino que o `Aproximar` acabou de escrever. Uma escrita de duas
		// posicoes por anuncio, com teto de 2 anuncios/s por corpo.
		// ==========================================================================================================================
		_saltoDeCadaCorpo[pl.Id] = (de, pl.Pos);

		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			o.Peer?.Send(w, Protocol.ChannelState, DeliveryMethod.Unreliable);
	}

	/// <summary>Quantos anuncios de salto sairam. Zerado e lido pela bancada `--borraoteste`.</summary>
	private int _saltosAnunciados;

	/// <summary>O ULTIMO anuncio de salto, como ele foi montado. Ver o carimbo em `AnunciarZanzo`.</summary>
	private (int Quem, Vec2 De, bool Vulto, int Bytes)? _ultimoSalto;

	/// <summary>
	/// DE ONDE ATE ONDE cada corpo saltou da ultima vez -- lido pela bancada `--diagborrao`. Ver o
	/// carimbo em <see cref="AnunciarZanzo"/>: um quadro depois esta distancia ja nao e exata pra quem
	/// tem cliente do outro lado.
	/// </summary>
	private readonly Dictionary<int, (Vec2 De, Vec2 Para)> _saltoDeCadaCorpo = [];
}
