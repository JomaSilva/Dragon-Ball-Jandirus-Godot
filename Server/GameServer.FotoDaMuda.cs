using System;
using System.Collections.Generic;
using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DA FOTO DA PAREDE MUDA (`--diagmuda`) -- ver `Client/RoboDeParedeMuda.cs`.
///
/// ============================ POR QUE ESTA FOTO PRECISOU EXISTIR ============================
/// O censo fecha em ZERO nos 40 andares e a `--socoteste` prova, em numero, que a celula dura
/// aguenta 40 socos. As duas ficariam verdes num mundo em que o cliente nunca desenha nada disso --
/// e a queixa do dono e literalmente sobre o que ele VE. Este projeto ja catalogou esse cego (a
/// memoria "a bancada mede INTENCAO"): `uniform` escrito nao e pixel na tela, e "o servidor recusou
/// o soco" nao e "o corpo parou na costura".
///
/// **E ha uma coisa que SO A FOTO responde aqui**: uma parede invisivel nao tem pixel. Nao da pra
/// fotografar a coisa; da pra fotografar a CONSEQUENCIA -- o corpo parado no meio de um chao que
/// continua desenhado atras dele.
/// ==========================================================================================
///
/// ============================ A DIVISAO DE TRABALHO COM O CLIENTE ============================
/// **QUEM SABE O QUE E INVISIVEL E O CLIENTE, e nao este arquivo.** O servidor nao le `.pedacos`:
/// ele nao desenha nada e nao tem como responder "esta celula tem tile?". Quem tem a resposta e o
/// tilemap montado (`World.CamadasDoCenarioDeTeste`) -- ou seja, a MESMA fonte de onde sai o pixel
/// que o jogador olha.
///
/// Entao aqui mora so o que e de servidor: quais celulas BLOQUEIAM, levar o corpo, socar, andar. A
/// escolha final de QUAL celula fotografar e do robo, depois de perguntar ao desenho.
///
/// Tentar responder "invisivel" daqui seria adivinhar pelo `.duro`, e o `.duro` e justamente o que a
/// bancada esta testando -- a prova passaria a depender do que ela quer provar.
/// ============================================================================================
///
/// ============================ NADA AQUI FORJA O DESFECHO ============================
/// O soco e o <see cref="SocarCenario"/> de producao (o mesmo que a tecla manda), o passo e o
/// <see cref="AplicarComando"/> (o mesmo atuador da IA e do jogador), a viagem e o
/// <see cref="MoveToZone"/> (o mesmo da troca de planeta).
/// ====================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// O BP QUE A FOTO PRECISA -- acima do <see cref="Empurrao.ResistenciaPadrao"/>.
	///
	/// Sem isto a foto sairia certa pelo motivo errado: um personagem recem-criado sorteia BP em volta
	/// de 20, e ai a parede fica de pe porque o punho e fraco -- exatamente o mesmo pixel de "a parede
	/// e indestrutivel". A `MartelarComOSoco` ja escreve a licao ("FRACO DEMAIS NAO E FALHA"); aqui ela
	/// vira o contrario: fraco demais tornaria a foto MUDA.
	/// </summary>
	private const double BpDaFotoDaMuda = 50_000_000;

	/// <summary>A chave `--semduro` esta ligada? E a legenda que separa o ANTES do DEPOIS.</summary>
	internal bool SemDuroNaFotoDaMuda => _semDuro;

	/// <summary>
	/// ONDE O CORPO ESTA POSTADO, e pra onde ele olha -- REAFIRMADO A CADA SOCO.
	///
	/// ============================ A LICAO QUE ESTE CAMPO PAGA ============================
	/// A `MartelarComOSoco` ja tinha escrito: *"o robo de soco ANDA e VIRA sozinho, e a direcao do
	/// corpo vem do cliente -- meio segundo depois de nascer colado na parede ele ja esta de lado e
	/// socando o vazio"*. A primeira rodada desta bancada caiu no MESMO buraco, letra por letra:
	/// `pl.Facing = North` era escrito uma vez, o cliente mandava o proprio rumo (South, o padrao) no
	/// tique seguinte, e o soco ia pro chao livre atras do corpo. O log disse "o punho nao acha
	/// cenario nenhum" -- que era verdade, e nao era o palco errado: era a mira girada.
	///
	/// Guardar o posto e reafirma-lo dentro do <see cref="SocarNaFotoDaMuda"/> resolve na raiz, e
	/// deixa o soco continuar sendo o de producao.
	/// =====================================================================================
	/// </summary>
	private (int Cx, int Cy, Facing Olhar) _postoDaMuda;

	/// <summary>Quantas candidatas no maximo -- o robo VISITA cada uma, entao a lista custa caminhada.</summary>
	private const int TetoDeCandidatasDaMuda = 400;

	/// <summary>
	/// AS DIVISORIAS DA ZONA INTEIRA, com o lado por onde da pra soca-las.
	///
	/// So GEOMETRIA -- ver a nota do cabecalho sobre a divisao de trabalho. O robo recebe esta lista e
	/// pergunta ao DESENHO qual delas nao tem tile nenhum.
	///
	/// `Dx/Dy` e o vizinho ANDAVEL de onde o punho alcanca; o corpo fica la e olha pra celula.
	///
	/// ============================ POR QUE ELA VARRE A ZONA E NAO UMA LINHA ============================
	/// A primeira versao recebia a coluna do relatorio do censo ("x=274" no Templo, "y=203" em Arconia)
	/// -- honesto, e fragil de um jeito que custou uma rodada: a linha y=203 de Arconia esta INTEIRA
	/// dentro do lago, e depois que a agua entrou na conta de "andavel" ela ficou sem nenhuma candidata
	/// alcancavel. Numero de relatorio envelhece; a pergunta ("onde ha divisoria invisivel nesta zona?")
	/// nao. A coordenada achada sai no log, e da pra conferir com a lista de colunas do censo.
	/// =================================================================================================
	/// </summary>
	internal List<(int Cx, int Cy, int Dx, int Dy)> CandidatasDaMuda(string zona)
	{
		var saida = new List<(int Cx, int Cy, int Dx, int Dy)>();
		ZoneEntry? e = _catalogo?.Get(zona);
		if (e?.Mapa is not { } mapa) return saida;

		HashSet<long> pintadas = CelulasPintadas(e);

		for (int cy = 0; cy < mapa.Height; cy++)
		for (int cx = 0; cx < mapa.Width; cx++)
		{
			if (!mapa.BlockedCell(cx, cy) || mapa.NaBorda(cx, cy)) continue;

			// ============================ O PRE-FILTRO DO DESENHO, e por que ele NAO fecha a prova ============================
			// Sem ele a lista sai na ordem do mapa e o robo teria que VISITAR cada divisoria pra
			// perguntar ao tilemap se ela desenha -- e em Arconia as duas unicas mudas alcancaveis estao
			// depois de centenas de paredes desenhadas. A rodada anterior morreu de paciencia exatamente
			// ai, com 400 candidatas e nenhuma delas muda.
			//
			// **Isto e uma DICA e nao a resposta.** Quem responde continua sendo o tilemap MONTADO
			// (`World.CelulaDesenhaDeTeste`), e por dois motivos: o arquivo pode nao ser o que o Godot
			// montou (foi assim que os 35 atlas escritos e nunca importados sobreviveram), e o controle
			// do chao sob os pes so existe na tela. Duas fontes independentes concordando e a prova; uma
			// so seria a mesma leitura duas vezes.
			// ================================================================================================================
			if (pintadas.Contains((long)cy * mapa.Width + cx)) continue;

			// ============================ DIVISORIA, E NAO CASCA DE BLOCO ============================
			// Chao livre nos DOIS lados opostos: e a forma que o jogador sente como parede invisivel.
			// A casca de um bloco macico tambem bloqueia e ninguem reclama dela -- ha pedra desenhada
			// do outro lado. E a mesma definicao de "CORTA area andavel" do censo, de proposito: se as
			// duas divergirem, a bancada fotografa uma celula que o relatorio nao conta.
			// =========================================================================================
			// ============================ LIVRE E "DA PRA ANDAR", E NAO SO "NAO E PAREDE" ============================
			// A AGUA entra aqui, e ela custou uma rodada inteira: em Arconia a bancada escolheu uma
			// costura cujos dois lados sao **lago**. Tudo dava certo no papel -- a celula bloqueava, nao
			// desenhava, caia no soco -- e a caminhada rendia **0 px** com as duas copias da colisao
			// dizendo que o caminho estava aberto, porque `BlockedCell` fala de PAREDE e o corpo tambem
			// para na agua (`ZoneCollision.Bloqueia` com o `ModoDeTravessia`).
			//
			// A pergunta certa e a que o movimento faz, e nao uma parecida.
			// ========================================================================================================
			bool le = Anda(mapa, cx - 1, cy), ld = Anda(mapa, cx + 1, cy);
			bool lc = Anda(mapa, cx, cy - 1), lb = Anda(mapa, cx, cy + 1);
			if (!((le && ld) || (lc && lb))) continue;

			// ...e a PROPRIA celula nao pode ser agua: derrubar uma parede sobre um lago abriria um
			// caminho que o corpo continua nao podendo pisar, e a cena C nao teria como se provar.
			if (mapa.EhAgua(cx, cy)) continue;

			// DE QUE LADO SE SOCA. A costura do Templo e uma COLUNA: ela tem livre a leste e a oeste e
			// parede em cima e embaixo -- ficar ao sul dela, que era a unica pose da primeira rodada,
			// nao existe. Foi o que fez a cena do Lookout sair vazia.
			(int dx, int dy) = lb ? (0, 1) : lc ? (0, -1) : le ? (-1, 0) : (1, 0);
			saida.Add((cx, cy, dx, dy));
		}
		// ============================ DO MIOLO PRA FORA, e nao na ordem do arquivo ============================
		// A varredura crua entrega primeiro a linha de menor `y`, e a foto do Templo saia em (2,54) --
		// tecnicamente uma divisoria invisivel, e visualmente a beirada do mapa, que e o unico lugar em
		// que uma parede sem desenho parece natural. A queixa do dono e sobre o MIOLO ("no meio de onde
		// eu ando"), entao a fila comeca pelo centro da zona.
		//
		// E ordenar tambem e o que torna o teto abaixo honesto: cortar 400 de uma lista ordenada por
		// relevancia descarta o que menos importa; cortar 400 da ordem do arquivo descarta o mapa todo
		// depois da primeira linha cheia.
		// ======================================================================================================
		float meioX = mapa.Width / 2f, meioY = mapa.Height / 2f;
		saida.Sort((a, b) =>
			((a.Cx - meioX) * (a.Cx - meioX) + (a.Cy - meioY) * (a.Cy - meioY))
			.CompareTo((b.Cx - meioX) * (b.Cx - meioX) + (b.Cy - meioY) * (b.Cy - meioY)));
		if (saida.Count > TetoDeCandidatasDaMuda) saida.RemoveRange(TetoDeCandidatasDaMuda,
																   saida.Count - TetoDeCandidatasDaMuda);
		return saida;
	}

	/// <summary>
	/// AS CELULAS QUE O `.pedacos` PINTA nesta zona, em qualquer camada. SO PRA BANCADA.
	///
	/// ============================ O SERVIDOR NAO LE `.pedacos` -- E AQUI E EXCECAO ============================
	/// Esta e a unica funcao do servidor que abre a pintura do cenario, e ela existe so pra ORDENAR a
	/// busca da bancada (ver o pre-filtro na <see cref="CandidatasDaMuda"/>). Nada de producao a chama,
	/// o `HashSet` morre com a chamada, e a prova de que a celula nao aparece continua sendo do CLIENTE.
	/// Se um dia isto virar caminho de producao, a nota de memoria do projeto sobre carga de cenario se
	/// aplica inteira: o arquivo de Lookout tem centenas de milhares de celulas.
	/// ==========================================================================================================
	/// </summary>
	private static HashSet<long> CelulasPintadas(ZoneEntry e)
	{
		var pintadas = new HashSet<long>();
		if (e.Pedacos.Length == 0 || !Godot.FileAccess.FileExists(e.Pedacos)) return pintadas;
		if (PedacosDoMapa.Ler(Godot.FileAccess.GetFileAsBytes(e.Pedacos)) is not { } ped) return pintadas;

		for (int c = 0; c < ped.Camadas.Length; c++)
			for (int cy = ped.Cy0; cy < ped.Cy1; cy++)
				for (int cx = ped.Cx0; cx < ped.Cx1; cx++)
				{
					if (!ped.Achar(cx, cy, c, out int ini, out int q)) continue;
					for (int i = 0; i < q; i++)
					{
						CelulaDePedaco cel = ped.Celula(ini, i);
						pintadas.Add((long)cel.Y * e.W + cel.X);
					}
				}
		return pintadas;
	}

	/// <summary>
	/// DA PRA ANDAR NESTA CELULA? -- parede E agua, a MESMA pergunta que o movimento faz.
	///
	/// Perguntar so `BlockedCell` aqui e o erro que esta bancada cometeu e pagou: ver a nota da agua na
	/// <see cref="CandidatasDaMuda"/>.
	/// </summary>
	private static bool Anda(ZoneCollision mapa, int cx, int cy) =>
		!mapa.BlockedCell(cx, cy) && !mapa.EhAgua(cx, cy);

	/// <summary>
	/// LEVA O CORPO PRA OUTRA ZONA -- <see cref="MoveToZone"/>, o mesmo da troca de planeta.
	///
	/// E ele quem manda o `ZoneChanged` que faz o CLIENTE carregar a cena. Sem esse pacote a camera
	/// continuaria na Terra, o tilemap seria o da Terra, e tanto a foto quanto a pergunta "esta celula
	/// tem tile?" responderiam sobre o mapa errado -- com todas as checagens verdes.
	/// </summary>
	internal bool ViajarNaFotoDaMuda(int id, string zona, int cx, int cy)
	{
		if (!_players.ContainsKey(id)) return false;
		if (_catalogo?.Get(zona)?.Mapa == null) return false;

		const int T = ZoneCollision.TileSize;
		MoveToZone(id, new ZoneKey(ZoneKey.KindPremade, zona),
				   new Vec2(cx * T + T / 2f, cy * T + T / 2f - MoveRules.FeetOffsetY));
		return true;
	}

	/// <summary>
	/// POSTA O CORPO no vizinho livre e o vira PRA celula -- e guarda o posto (ver <see cref="_postoDaMuda"/>).
	/// </summary>
	internal bool PostarNaFotoDaMuda(int id, int cx, int cy, int dx, int dy)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;

		const int T = ZoneCollision.TileSize;
		pl.Pos = new Vec2((cx + dx) * T + T / 2f, (cy + dy) * T + T / 2f - MoveRules.FeetOffsetY);

		// OLHA PRA CELULA: o rumo e o INVERSO do deslocamento do posto.
		Facing olhar = dy > 0 ? Facing.North : dy < 0 ? Facing.South : dx > 0 ? Facing.West : Facing.East;
		pl.Facing = olhar;
		_postoDaMuda = (cx + dx, cy + dy, olhar);

		// Ver `BpDaFotoDaMuda`: a foto tem que separar "nao cede" de "punho fraco".
		pl.Ficha.expressedBP = BpDaFotoDaMuda;
		return true;
	}

	/// <summary>O estado da celula, do jeito que o servidor o ve. As tres perguntas separadas.</summary>
	internal (bool Bloqueia, bool Duro, bool Agua) CelulaNaFotoDaMuda(string zona, int cx, int cy)
	{
		ZoneEntry? e = _catalogo?.Get(zona);
		return e?.Mapa is not { } mapa
			? (false, false, false)
			: (mapa.BlockedCell(cx, cy), mapa.Indestrutivel(cx, cy), mapa.EhAgua(cx, cy));
	}

	/// <summary>
	/// O SOCO -- <see cref="SocarCenario"/>, a funcao de producao, com o posto REAFIRMADO antes.
	///
	/// A reafirmacao e o conserto descrito no <see cref="_postoDaMuda"/>, e ela nao afrouxa a prova:
	/// o que se mede e o DESFECHO do soco (a celula cair ou nao), e nao se o corpo consegue ficar
	/// virado. Devolve `false` quando o punho nao acha cenario nenhum na frente -- ali e FALHA de
	/// montagem do palco, e nao resultado.
	/// </summary>
	internal bool SocarNaFotoDaMuda(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;

		const int T = ZoneCollision.TileSize;
		pl.Pos = new Vec2(_postoDaMuda.Cx * T + T / 2f, _postoDaMuda.Cy * T + T / 2f - MoveRules.FeetOffsetY);
		pl.Facing = _postoDaMuda.Olhar;

		// ============================ O BP TAMBEM E REAFIRMADO, E PELA MESMA LICAO ============================
		// `expressedBP` e DERIVADO: o tique do corpo o recalcula da ficha, e entre o `Postar` e o soco ha
		// segundos de espera pela pintura do cenario. Escrever o BP uma vez so deu a rodada em que NADA
		// caiu -- nem a costura, nem a parede normal do contra-exemplo -- e o log leu como "o conserto
		// funcionou demais", que e o pior jeito de errar: o defeito se disfarcando de acerto.
		// ======================================================================================================
		pl.Ficha.expressedBP = BpDaFotoDaMuda;
		return SocarCenario(pl);
	}

	/// <summary>Onde o corpo esta, em pixels -- pra medir QUANTO ele andou na tentativa de furar.</summary>
	internal Vec2 OndeEstouNaFotoDaMuda(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? pl.Pos : Vec2.Zero;

	/// <summary>Em que CELULA o corpo esta -- e o que diz se ele atravessou a costura ou parou nela.</summary>
	internal (int Cx, int Cy) CelulaDoCorpoNaFotoDaMuda(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? CelulaDoPonto(pl.Pos) : (0, 0);

	/// <summary>O rumo que aponta da celula do posto pra celula alvo -- o mesmo `Frente` do combate.</summary>
	internal Vec2 RumoDoPostoNaFotoDaMuda() => MeleeArea.Frente(_postoDaMuda.Olhar);

	/// <summary>
	/// UMA PAREDE QUE **DEVE** CAIR, perto do corpo -- o CONTRA-EXEMPLO.
	///
	/// ============================ POR QUE ELA E OBRIGATORIA ============================
	/// Porque "nada quebrou" e o desfecho de um conserto certo E de um conserto que matou o cenario
	/// destrutivel inteiro, e as duas coisas dao a mesma foto. Sem esta cena, marcar o mapa todo como
	/// duro passaria verde em tudo que esta acima.
	///
	/// A escolha e o espelho da <see cref="CandidatasDaMuda"/>: bloqueia, fora da beirada, com vizinho
	/// livre -- e **NAO** dura. Anda em aneis a partir do corpo, igual a `EncostarNaParede`.
	/// ==================================================================================
	/// </summary>
	internal (bool Achou, int Cx, int Cy, int Dx, int Dy) AcharParedeQueCedeNaMuda(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return (false, 0, 0, 0, 0);
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		if (mapa == null) return (false, 0, 0, 0, 0);

		(int cx0, int cy0) = CelulaDoPonto(pl.Pos);
		for (int r = 1; r < 80; r++)
			for (int dy = -r; dy <= r; dy++)
				for (int dx = -r; dx <= r; dx++)
				{
					if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;   // so a casca do anel
					int cx = cx0 + dx, cy = cy0 + dy;
					if (!mapa.BlockedCell(cx, cy) || mapa.NaBorda(cx, cy) || mapa.Indestrutivel(cx, cy)
						|| mapa.EhAgua(cx, cy))
						continue;
					// `Anda` e nao `BlockedCell`, pela mesma razao da agua na `CandidatasDaMuda`: pisar
					// na celula derrubada e METADE desta cena, e um posto dentro do lago nao anda.
					if (Anda(mapa, cx, cy + 1)) return (true, cx, cy, 0, 1);
					if (Anda(mapa, cx, cy - 1)) return (true, cx, cy, 0, -1);
					if (Anda(mapa, cx - 1, cy)) return (true, cx, cy, -1, 0);
					if (Anda(mapa, cx + 1, cy)) return (true, cx, cy, 1, 0);
				}
		return (false, 0, 0, 0, 0);
	}

	/// <summary>O nome da zona onde o corpo esta -- a legenda precisa dizer em que planeta a foto saiu.</summary>
	internal string ZonaNaFotoDaMuda(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? pl.Zone.Name : "";
}
