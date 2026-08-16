using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS JANELAS DA FOTO DA COLISAO DE KI (`--diagembateki`, `Client/RoboDeFotoDoEmbateDeKi.cs`).
///
/// ============================ POR QUE UMA FOTO, SE A `--embatekiteste` MEDE 87 COISAS ============================
/// A bancada sem janela ja responde tudo o que se responde em numero: o gatilho, a fisica do medidor,
/// quem vence, o pixel que cada acerto empurra, o encontro chegando ao corpo, o preco do empate e as
/// bordas. Ela roda no boot, em quatro minutos, e nao precisa de ninguem na frente da tela.
///
/// O que ela NAO pode responder e o que o dono pediu com estas palavras: *"CADA ACERTO EMPURRA O BEAM
/// DO INIMIGO PRA TRAS"*. Entre o `Feixe.Pos` do servidor e o feixe que ESTICA na tela ha o snapshot,
/// o `ProjetilDesenhado`, a interpolacao e a camada de desenho -- e a bancada de numero ficaria verde
/// com os dois feixes desenhados do mesmo tamanho, ou com o rastro carimbado no lugar de sempre. Ja
/// aconteceu neste projeto: quatro defeitos visuais passaram por quatro mil checagens verdes.
///
/// Entao esta janela existe pra a metade que so a tela responde, e ela e minima de proposito -- monta
/// a cena pela porta de producao e deixa o robo do cliente LER o que foi desenhado.
/// ============================================================================================================
///
/// ============================ NADA AQUI RESOLVE A DISPUTA ============================
/// A cena inteira nasce por producao: os corpos pelo <see cref="ForjarCorpoDeFoto"/> (o mesmo das
/// outras fotos, que ja resolve `Visual`, `TrocarAparencias` e `PowerLevel`), os dois raios pelo
/// <see cref="Canalizar"/>, o encontro pelo gatilho de dentro do tique de projetil, o cabo de guerra
/// pelo `TickDosEmbatesDeKi` e a letra pelo <see cref="TeclaDeQualquerEmbate"/> -- a MESMA funcao que
/// o `case Protocol.C2S.ClashTecla` chama.
///
/// O UNICO PRIVILEGIO e o de sempre e esta escrito onde mora: o <see cref="_comTecladoDeTeste"/>, que
/// so responde "este corpo tem teclado". Sem ele os dois lados cairiam na taxa automatica da IA e a
/// foto mostraria duas maquinas se empurrando -- justamente o que o pedido do dono nao e.
/// =====================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Os dois duelistas da foto. A e o lado com teclado (quem o robo dirige).</summary>
	private int _fotoEkA, _fotoEkB;

	/// <summary>
	/// MONTA A CENA: dois corpos de frente, cada um com um raio, num corredor de chao de verdade.
	///
	/// O `bpDeA` diferente do `bpDeB` nao e enfeite: com forcas exatamente parelhas a deriva e zero e
	/// um jogador que acerta tudo empata com a taxa automatica do outro lado (e a calibragem do
	/// `ApertosPorLetra`, medida na `--embatekiteste`). Um empate e a foto da CENA 2; pra a foto do
	/// EMPURRAO tem que haver quem empurre.
	/// </summary>
	/// <returns>os dois ids, ou (0,0) se nao houver chao livre pra a cena caber.</returns>
	internal (int A, int B) EmbateDeFoto_Montar(int idDono, int tiles, double bpDeA, double bpDeB,
											   bool tecladoEmA, int tilesAbaixo)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? dono)) return (0, 0);

		// ============================ ESTA CENA NAO PEDE CHAO ATRAS DOS DOIS, E ISSO E ESCOLHA ============================
		// A `--embatekiteste` pede (a `folgaAtras` da familia do empate): la se mede o PAR DE VETORES do
		// arremesso, e sem chao livre atras o corpo bate no que houver e o vetor nao se le.
		//
		// Aqui nao da pra pagar isso: a cena tem que caber DENTRO DA TELA (6 tiles do observador) e num
		// pedaco de mapa de verdade -- em Namek nao ha corredor de oito celulas tao perto. Entao o que
		// mudou foi a MEDIDA, e nao o mapa: o robo afirma que ninguem foi puxado PRA DENTRO do estouro
		// (ver a conferencia no `DepoisDaOnda`), que e verdade mesmo com um dos dois parando num morro --
		// e o par de vetores continua sendo medido onde ele se le, que e na bancada sem janela.
		// =============================================================================================================
		if (!FaixaDeChaoDaFoto(dono, tiles, tilesAbaixo, out Vec2 esquerda)) return (0, 0);

		var direita = new Vec2(esquerda.X + tiles * ZoneCollision.TileSize, esquerda.Y);
		int a = ForjarCorpoDeFoto(idDono, esquerda - dono.Pos, "Duelista", bpDeA, comEscada: false);
		int b = ForjarCorpoDeFoto(idDono, direita - dono.Pos, "Rival", bpDeB, comEscada: false);
		if (a == 0 || b == 0) return (0, 0);

		ServerPlayer pa = _players[a], pb = _players[b];
		pa.Facing = Facing.East;
		pb.Facing = Facing.West;
		pa.Ficha.Ki = pa.Ficha.MaxKi;
		pb.Ficha.Ki = pb.Ficha.MaxKi;

		if (tecladoEmA) _comTecladoDeTeste.Add(a);

		// A MESMA RECEITA DA BANCADA SEM JANELA (`RaioDeTeste`), pra as duas medirem o mesmo tiro.
		Canalizar(pa, "Ki_Wave", 10 * pa.Ficha.BaseDrain(), RaioDeTeste());
		Canalizar(pb, "Ki_Wave", 10 * pb.Ficha.BaseDrain(), RaioDeTeste());

		_fotoEkA = a;
		_fotoEkB = b;
		return (a, b);
	}

	/// <summary>
	/// UM CORREDOR DE CHAO DE VERDADE PRA A CENA, ABAIXO DE QUEM ASSISTE.
	///
	/// ============================ DUAS RAZOES, E AS DUAS SAO MEDIDAS ============================
	/// (1) `ServeDeChao` e nao `BlockedCell`: um corredor de agua nao tem parede nenhuma e passaria na
	/// peneira errada -- e a `--embatekiteste` ja perdeu uma familia inteira por isso (ver
	/// `CorredorDeChao`). Aqui doeria igual: os corpos nasceriam no lago e o raio morreria na margem.
	///
	/// (2) ABAIXO de quem assiste, e com folga: a onda de choque do empate pega todo mundo dentro de
	/// `log(8,BP)/2` tiles do ponto (uns 3 pra os BPs desta cena). Com a camera dentro do raio, o
	/// arremesso jogaria o CORPO DO FOTOGRAFO no meio da foto do empate -- a cena certa, enquadrada
	/// errada. Tres tiles a mais poem a camera de fora e o quadro inteiro dentro dela.
	/// ==========================================================================================
	/// </summary>
	private bool FaixaDeChaoDaFoto(ServerPlayer dono, int tiles, int tilesAbaixo, out Vec2 esquerda)
	{
		esquerda = default;
		if (MapaDaZonaOuCatalogo(dono.Zone) is not { } mapa)
		{
			GD.PrintErr($"[embatekifoto] a zona {dono.Zone} nao tem mapa de colisao carregado");
			return false;
		}

		int cx = (int)MathF.Floor(dono.Pos.X / ZoneCollision.TileSize);
		int cy = (int)MathF.Floor(dono.Pos.Y / ZoneCollision.TileSize);

		// ============================ A BUSCA E DO MAIS PERTO PRO MAIS LONGE, E TEM TETO ============================
		// **Isto e um achado da propria bancada, e a foto e que o mostrou.** A primeira versao varria
		// linha por linha ate 24 tiles pra baixo e 20 pra os lados, e devolvia o PRIMEIRO corredor que
		// achasse -- que em Namek (agua por toda parte) ficava a vinte tiles dali. A cena acontecia
		// inteira, o `TirosDesenhados` contava os dois feixes, todas as conferencias ficavam VERDES...
		// e as fotos saiam com o chao vazio, porque o duelo estava FORA DA TELA.
		//
		// Entao: anel por RAIO crescente (o primeiro achado e o mais perto que existe) e um TETO -- a
		// camera mostra ~25 x 14 tiles, e um corredor alem disso nao aparece na foto. Melhor a bancada
		// dizer "nao cabe aqui" do que fotografar grama.
		//
		// O `tilesAbaixo` continua sendo a PREFERENCIA (a onda de choque do empate nao pode alcancar a
		// camera), e por isso ele entra como desempate dentro do mesmo raio -- e nao como exigencia.
		// =========================================================================================================
		// ============================ O TETO E O QUE A CAMERA MOSTRA, E ELE FOI MEDIDO NA FOTO ============================
		// A camera enquadra ~8 tiles pra cada lado do corpo (viewport de 1920x1080, tile desenhado a 64
		// px). Com o teto em 8 a busca achou um corredor a `(-8,-8)` e a bancada ficou TODA VERDE -- os
		// dois feixes desenhados, o comprimento de cada um, o empate, o arremesso -- com o duelo raspando
		// a BORDA DE CIMA da tela: as fotos sairam com uma tira de feixe no primeiro centimetro e chao no
		// resto. **Foi a foto que pegou isso, e nenhuma das 15 conferencias podia ter pegado**: todas elas
		// medem posicao no mundo, e o mundo estava certo -- quem estava errado era o enquadramento.
		//
		// Seis e o que cabe com folga (6 tiles = 384 px do centro, contra os 540 de meia tela).
		// ==============================================================================================================
		const int Teto = 6;
		for (int raio = tilesAbaixo; raio <= Teto; raio++)
			foreach (int dy in DeCimaPraBaixo(raio, tilesAbaixo))
				for (int dx = -raio; dx <= raio; dx++)
				{
					if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != raio) continue;

					// ============================ A CENA NAO PODE CAIR EM CIMA DA CAMERA ============================
					// O `tilesAbaixo` era so preferencia, e o raio zero passou na frente dele: o duelo nasceu
					// NA LINHA de quem assiste, com o corpo dele entre os dois. A foto ficou boa e a MEDIDA
					// quebrou -- o arremesso do empate saiu perpendicular (`cos 0`) porque um dos dois foi
					// empurrado contra o corpo do fotografo e escorregou pelo `MoveRules`.
					//
					// Ou seja: quem assiste tem que estar FORA do corredor e fora do raio do estouro (uns 3
					// tiles pra estes BPs). Isso e exigencia, e nao gosto.
					// ==========================================================================================
					if (Math.Abs(dy) < tilesAbaixo) continue;

					int y = cy + dy;
					int x0 = cx - tiles / 2 + dx;

					bool serve = true;
					for (int i = 0; i <= tiles && serve; i++) serve &= mapa.ServeDeChao(x0 + i, y);
					if (!serve) continue;

					GD.Print($"[embatekifoto] corredor de {tiles + 1} tiles achado a ({dx},{dy}) tiles "
							 + "de quem assiste");
					esquerda = mapa.CentroDaCelula(x0, y);
					return true;
				}

		GD.PrintErr($"[embatekifoto] nenhum corredor de {tiles + 1} tiles de chao a menos de {Teto} "
					+ $"tiles de ({cx},{cy}) na zona {dono.Zone} -- a cena nao caberia na tela");
		return false;
	}

	/// <summary>
	/// AS LINHAS DE UM RAIO, NA ORDEM DA PREFERENCIA: primeiro a que esta `tilesAbaixo` do observador,
	/// depois as outras. Duas linhas do mesmo raio sao igualmente boas pra a foto; o que as desempata e
	/// ficar longe da camera na hora do estouro.
	/// </summary>
	private static IEnumerable<int> DeCimaPraBaixo(int raio, int tilesAbaixo)
	{
		if (raio >= tilesAbaixo) yield return tilesAbaixo;
		for (int dy = raio; dy >= -raio; dy--)
			if (dy != tilesAbaixo)
				yield return dy;
	}

	/// <summary>
	/// O QUE A DISPUTA DA FOTO ESTA FAZENDO -- so leitura, e sempre do ponto de vista do lado A.
	///
	/// O `Medidor` sai virado pra A (100 = A engoliu) mesmo quando o gatilho o pos como lado B da
	/// disputa: quem comeca a disputa e o tique do projetil, e qual das duas cabecas chega primeiro na
	/// varredura e sorteio do mapa. Devolver o numero cru faria a foto dizer "o medidor caiu" numa
	/// cena em que o lado dirigido esta ganhando.
	/// </summary>
	internal (bool Existe, double Medidor, Vec2 Ponto, double Corridos, char Letra,
			  float FeixeDeA, float FeixeDeB) EmbateDeFoto_Estado()
	{
		if (_fotoEkA == 0 || !_emEmbateDeKi.TryGetValue(_fotoEkA, out DisputaDeKi? d))
			return (false, 0, default, 0, '\0', 0, 0);

		LadoDeKi la = d.A.Quem.Id == _fotoEkA ? d.A : d.B;
		LadoDeKi lb = la == d.A ? d.B : d.A;

		return (true,
				la == d.A ? d.Medidor : 100 - d.Medidor,
				d.Ponto, d.Corridos, la.Letra,
				la.Feixe != null ? (la.Feixe.Pos - la.Quem.Pos).Length : 0,
				lb.Feixe != null ? (lb.Feixe.Pos - lb.Quem.Pos).Length : 0);
	}

	/// <summary>
	/// O LADO A ACERTA A LETRA DELE -- pelo <see cref="TeclaDeQualquerEmbate"/>, o funil do pacote.
	/// Devolve falso quando nao ha letra na tela (e nao ha o que responder).
	/// </summary>
	internal bool EmbateDeFoto_Apertar()
	{
		if (_fotoEkA == 0 || !_emEmbateDeKi.TryGetValue(_fotoEkA, out DisputaDeKi? d)) return false;

		LadoDeKi la = d.A.Quem.Id == _fotoEkA ? d.A : d.B;
		if (la.Letra == '\0') return false;

		TeclaDeQualquerEmbate(la.Quem, la.Letra);
		return true;
	}

	/// <summary>O que a onda de choque cobra: vida, voo e posicao dos dois -- pra a foto do empate.</summary>
	internal (double VidaA, double VidaB, int VooA, int VooB, Vec2 PosA, Vec2 PosB) EmbateDeFoto_Corpos()
	{
		_players.TryGetValue(_fotoEkA, out ServerPlayer? a);
		_players.TryGetValue(_fotoEkB, out ServerPlayer? b);
		return (a?.Combate?.Corpo.Vida() ?? 0, b?.Combate?.Corpo.Vida() ?? 0,
				a?.TiquesDeVoo ?? 0, b?.TiquesDeVoo ?? 0,
				a?.Pos ?? default, b?.Pos ?? default);
	}

	/// <summary>
	/// TIRA A CENA DO MUNDO. A disputa sai SEM `Resolver` -- aquele solta cabeca, troca dono e manda
	/// pacote, e aqui nao ha o que resolver: os dois lados estao sendo apagados no mesmo instante. E a
	/// mesma disciplina do `LimparEmbatesDaBancada`.
	/// </summary>
	internal void EmbateDeFoto_Limpar()
	{
		foreach (int id in new[] { _fotoEkA, _fotoEkB })
		{
			if (id == 0) continue;

			if (_emEmbateDeKi.TryGetValue(id, out DisputaDeKi? d))
			{
				if (d.A.Feixe != null) d.A.Feixe.EmEmbate = false;
				if (d.B.Feixe != null) d.B.Feixe.EmEmbate = false;
				Fechar(d);
			}
			if (_canais.TryGetValue(id, out CanalDeKi? c)) FecharCanal(id, c, null);
			_comTecladoDeTeste.Remove(id);
		}

		_fotoEkA = _fotoEkB = 0;
		LimparAFoto();
	}
}
