namespace Jandirus.Core.World;

/// <summary>As 4 direcoes de sprite do BYOND. A arte convertida so tem estas (e as 8 nos blasts).</summary>
public enum Facing : byte { South = 0, North = 1, East = 2, West = 3 }

/// <summary>
/// REGRA DE MOVIMENTO: MORA AQUI, E SO AQUI.
///
/// O cliente chama <see cref="Integrate"/> pra andar (movimento livre em pixel, resposta
/// imediata) e o servidor chama <see cref="ValidateStep"/> pra conferir o que chegou. Como
/// as duas pontas usam ESTE arquivo, "cliente calcula, servidor valida" nao vira duas
/// implementacoes que divergem com o tempo.
///
/// O servidor NAO exige o mesmo float bit a bit: ele checa se o deslocamento cabe no que o
/// tempo decorrido permite, com uma folga pra jitter de rede. Exigir igualdade exata com
/// float e uma armadilha classica (o mesmo codigo da resultados diferentes entre maquinas).
/// </summary>
public static class MoveRules
{
	// ============================== CONFIG ======================================
	public const float BaseSpeedPx = 160f;     // pixels por segundo em stats base (5 tiles/s a 32px)
	public const float MaxDeltaSeconds = 0.25f;// teto do dt aceito num pacote: sem isso, "passei 10s offline" = teleporte
	public const float SpeedTolerance = 1.35f; // folga de validacao (jitter/aceleracao); acima disso e correcao
	public const float MinCorrectionPx = 6f;   // erro abaixo disto nao vale corrigir (evita briga cliente-servidor)

	/// <summary>
	/// A CAIXA DOS PES. O personagem tem 32px de altura mas so a base dele ocupa chao -- a
	/// cabeca passa por cima do muro no desenho, como no BYOND. O centro do sprite fica no
	/// meio do corpo, entao a caixa desce <see cref="FeetOffsetY"/> pra encostar no chao.
	/// </summary>
	public const float BodyHalfW = 8f;
	public const float BodyHalfH = 5f;
	public const float FeetOffsetY = 8f;
	// ============================ FIM DO CONFIG =================================

	/// <summary>
	/// CORRER. Quanto SHIFT multiplica a velocidade.
	///
	/// O original nao tinha um "multiplicador de corrida" pra copiar: la o movimento era por
	/// TEMPO POR PASSO (`mobTime += 0.5*Epspeed` quando dashing), e o unico numero limpo era
	/// o `move_boost *= 2` da aproximacao de ataque.
	///
	/// COMECOU EM 1,6 E O DONO PEDIU MAIS RAPIDO. 1,6 era um meio-termo defensivo -- eu tinha
	/// medo de o mundo passar voando --, e na pratica correr quase nao se distinguia de andar:
	/// 60% e pouco pra um jogo cujo assunto E velocidade sobre-humana. 2,2 e o `move_boost *= 2`
	/// do original com uma folga, e agora tem tres coisas dizendo "voce esta correndo" ao mesmo
	/// tempo -- a velocidade, o passo acelerado e o borrao (ver `CharacterVisual.Correr`).
	///
	/// O CUSTO SEGURA O ABUSO: correr consome Ki por segundo (`GameServer.PodeCorrer`), entao
	/// atravessar o mapa correndo custa o Ki que faria falta na luta do outro lado.
	/// </summary>
	public const float MultiplicadorCorrida = 2.2f;

	/// <summary>Velocidade final em px/s. O stat vem do Core de stats; 1.0 = base.</summary>
	public static float SpeedPx(float speedStat) => BaseSpeedPx * (speedStat <= 0 ? 1f : speedStat);

	/// <summary>Velocidade com a corrida ja aplicada, se estiver correndo.</summary>
	public static float SpeedPx(float speedStat, bool correndo)
		=> SpeedPx(speedStat) * (correndo ? MultiplicadorCorrida : 1f);

	/// <summary>
	/// Converte o Espeed (o stat de velocidade DEPOIS da curva de retorno decrescente) no
	/// multiplicador de movimento. A ancora e 2: um personagem de speed cru 1 sai do Statify
	/// com Espeed 2 (1 de base + 1 do buff temporario neutro), e esse e o andar "normal".
	/// Como o Espeed satura perto de 10, o teto de velocidade fica em ~5x -- ninguem some da
	/// tela por ter treinado velocidade.
	/// </summary>
	public const float EspeedBase = 2f;
	public static float SpeedStatFrom(double espeed) => (float)Math.Max(espeed / EspeedBase, 0.1);

	/// <summary>Passo do movimento livre, IGNORANDO parede. <paramref name="dir"/> nao precisa vir normalizado.</summary>
	public static Vec2 Integrate(Vec2 pos, Vec2 dir, float dtSeconds, float speedStat)
	{
		if (dtSeconds <= 0) return pos;
		if (dtSeconds > MaxDeltaSeconds) dtSeconds = MaxDeltaSeconds;
		Vec2 step = dir.Normalized() * (SpeedPx(speedStat) * dtSeconds);
		return pos + step;
	}

	/// <summary>
	/// O PASSO DE VERDADE: anda respeitando parede, com deslize nas quinas.
	///
	/// O cliente chama isto pra se mover e o servidor chama <see cref="ValidateStep"/>, que
	/// usa a MESMA <see cref="Occupied"/>. E o que elimina a briga entre os dois: antes o
	/// cliente atravessava a parede (o TileMap convertido nao tem fisica), o servidor recusava
	/// e devolvia correcao, o cliente empurrava de novo -- e o personagem TREMIA na parede.
	///
	/// Deslize: se o passo cheio nao cabe, tenta so o eixo X e depois so o Y. E o que faz
	/// andar rente a um muro em diagonal continuar andando em vez de travar.
	/// </summary>
	/// <param name="modo">
	/// COMO este corpo esta atravessando (ver <see cref="ModoDeTravessia"/>). Muda DUAS coisas: se a
	/// agua para ou nao, e se um CORPO alheio para ou nao (<see cref="ClasseDeCorpo.Bloqueia"/>).
	/// Parede para em todos os modos -- quem voa alto nem chega aqui, porque o chamador manda
	/// `mapa = null` (e o `isflying` do original, ver `Voo.AtravessaCenario`).
	/// </param>
	/// <param name="vizinhos">
	/// ============================ OS CORPOS DA ZONA -- **o segundo plano da MESMA pergunta** ============================
	/// Padrao vazio = nao consulta corpo nenhum, e e assim que todo chamador antigo continua valendo.
	/// Preenchido, ele entra EXATAMENTE nos mesmos tres pontos em que o mapa entra (passo cheio e os
	/// dois deslizes de quina) -- e nao num `if` antes ou depois desta funcao. Ver `ClasseDeCorpo`.
	///
	/// **O DESLIZE E O QUE IMPEDE O "ACHEI QUE TRAVEI".** Encostar num corpo em diagonal continua
	/// andando pelo eixo livre, exatamente como encostar num muro -- que e a diferenca entre contornar
	/// alguem e parar de frente pra ele sem entender.
	/// ==================================================================================================================
	/// </param>
	public static Vec2 Advance(Vec2 pos, Vec2 dir, float dtSeconds, float speedStat,
							   ZoneCollision? mapa, out bool blocked, bool correndo = false,
							   ModoDeTravessia modo = ModoDeTravessia.APe,
							   Vizinhanca vizinhos = default)
	{
		blocked = false;
		if (dtSeconds <= 0) return pos;
		if (dtSeconds > MaxDeltaSeconds) dtSeconds = MaxDeltaSeconds;

		Vec2 step = dir.Normalized() * (SpeedPx(speedStat, correndo) * dtSeconds);
		if (step.LengthSquared < 1e-9f) return pos;

		Vec2 alvo = pos + step;

		// ============================ O MAPA PODE FALTAR; OS CORPOS, NAO ============================
		// `mapa == null` quer dizer duas coisas diferentes -- "voando alto" e "zona sem colisao
		// carregada" -- e nenhuma das duas e "nao ha gente aqui". Por isso o `return` antigo virou este
		// ramo: sem mapa a geometria nao pergunta nada, e os corpos continuam perguntando.
		// (Voando, quem devolve zero e o proprio `Vizinhanca.Barra`, pelo modo. Nao ha `if` duplicado.)
		// ==========================================================================================
		if (mapa == null)
		{
			if (vizinhos.Barra(pos, alvo, modo) == 0) return alvo;
			blocked = true;
			var mx = new Vec2(alvo.X, pos.Y);
			if (step.X != 0 && vizinhos.Barra(pos, mx, modo) == 0) return mx;
			var my = new Vec2(pos.X, alvo.Y);
			if (step.Y != 0 && vizinhos.Barra(pos, my, modo) == 0) return my;
			return pos;
		}

		// JA PRESO NUMA CELULA QUE PARA ESTE CORPO -- ver <see cref="Escapar"/>, que e onde a regra
		// inteira mora e onde esta escrito por que ela deixou de ser "aprova tudo".
		//
		// ELA CONTINUA SENDO SO SOBRE **GEOMETRIA**, e isso e deliberado: estar dentro de um CORPO nao
		// entra aqui. Quem esta em cima de alguem nao precisa de socorro nenhum -- precisa so que
		// AQUELE corpo pare de contar, que e o `jaSobrepondo` do `GradeDeCorpos.Quem`.
		if (Occupied(mapa, pos, modo))
		{
			Escape esc = Escapar(mapa, pos, modo, out Vec2 refugio);

			// MAPA QUEBRADO: nao ha lugar valido em `RaioDoEscape` tiles. Vale o comportamento antigo
			// -- passo cheio, sem consultar nada --, porque o defeito ali e do mapa e prender o corpo
			// nao o conserta.
			if (esc == Escape.SemRefugio) return alvo;

			// PRESO PELA GEOMETRIA, COM SAIDA: so vale o passo que APROXIMA. O deslize de quina
			// continua existindo, pelo mesmo motivo de sempre (nao travar de frente), so que agora ele
			// tambem tem que aproximar.
			if (esc == Escape.Dirigido)
			{
				if (Aproxima(pos, alvo, refugio)) return alvo;
				blocked = true;
				if (step.X != 0)
				{
					var ex = new Vec2(alvo.X, pos.Y);
					if (Aproxima(pos, ex, refugio)) return ex;
				}
				if (step.Y != 0)
				{
					var ey = new Vec2(pos.X, alvo.Y);
					if (Aproxima(pos, ey, refugio)) return ey;
				}
				return pos;
			}

			// ============================ PRESO PELO **MODO**: CAI NA REGRA NORMAL, E ISSO E O CONSERTO ============================
			// Nao ha escape nenhum a conceder -- quem muda e o MODO (voltar a nadar, voar), nao a
			// posicao. Mas tambem nao ha `return pos` aqui, e essa diferenca tem um caso real: a caixa
			// dos pes encosta em ate QUATRO celulas, entao um corpo parado na beira do lago pode ter uma
			// quina na agua e tres no seco. Um `return pos` o congelaria a dois pixels da margem, e ele
			// teria de gastar tres segundos de Ki nadando pra andar esses dois pixels.
			//
			// Caindo na regra normal ele ganha exatamente o que qualquer corpo tem: **o passo que TERMINA
			// valido**. Da beira, esse passo existe e ele sai andando; do meio do lago, nao existe --
			// todo alvo a 2,7 px continua sendo agua --, e ele fica onde esta, que e o pedido do dono.
			// ================================================================================================================
		}

		if (!Occupied(mapa, alvo, modo) && vizinhos.Barra(pos, alvo, modo) == 0) return alvo;

		blocked = true;
		if (step.X != 0)
		{
			var sx = new Vec2(alvo.X, pos.Y);
			if (!Occupied(mapa, sx, modo) && vizinhos.Barra(pos, sx, modo) == 0) return sx;
		}
		if (step.Y != 0)
		{
			var sy = new Vec2(pos.X, alvo.Y);
			if (!Occupied(mapa, sy, modo) && vizinhos.Barra(pos, sy, modo) == 0) return sy;
		}
		return pos;   // encostou de frente: para
	}

	// =====================================================================
	// A SAIDA DE EMERGENCIA
	// =====================================================================
	/// <summary>O que fazer com um corpo que ja esta numa celula que o para. Ver <see cref="Escapar"/>.</summary>
	public enum Escape : byte
	{
		/// <summary>Nao ha saida: a celula o para pelo MODO, e o modo e coisa que ele muda.</summary>
		Nenhum = 0,
		/// <summary>Ha saida, e ela e na direcao do refugio devolvido.</summary>
		Dirigido = 1,
		/// <summary>Preso pela geometria e sem lugar valido por perto: vale o passo cheio.</summary>
		SemRefugio = 2,
	}

	/// <summary>
	/// ATE ONDE PROCURAR UM LUGAR VALIDO pra quem esta preso, em aneis de celula.
	///
	/// OITO E O PIOR CASO **MEDIDO** deste projeto: o comentario do `ZoneCollision.PontoLivrePerto`
	/// registra que o pouso em Icer -- o mapa mais fechado do jogo, 309 celulas densas num quadrado
	/// de 21x21 -- para no anel 8. Um corpo enterrado mais fundo que isso esta num mapa quebrado, e o
	/// <see cref="Escape.SemRefugio"/> e o que responde por ele.
	///
	/// O TETO IMPORTA porque esta busca roda POR QUADRO enquanto o corpo estiver preso, e nao uma vez
	/// por nascimento como a do `PontoLivrePerto`. Oito aneis sao ~2.100 celulas no pior caso -- e o
	/// pior caso e o unico que paga, porque o caso comum (uma parede de um tile) resolve no anel 1.
	/// </summary>
	public const int RaioDoEscape = 8;

	/// <summary>
	/// ============================ ESTE CORPO PRESO TEM DIREITO DE ANDAR? E PRA ONDE? ============================
	/// Esta funcao substituiu uma linha -- `if (Occupied(mapa, pos, modo)) return alvo;`, com o
	/// comentario *"ja estava dentro de parede: deixa sair"*. A linha aprovava **todo** passo, sem
	/// olhar celula nenhuma, e o dono achou o buraco dela sozinho:
	///
	///   *"ao acabar o ki nadando, eu sou JOGADO DE VOLTA PRA MARGEM mas da um bug q se eu estiver
	///    SEGURANDO O BOTAO DE ANDAR eu consigo VOLTAR PRA AGUA ANDANDO POR CIMA DELA"*
	///
	/// Medido contra o mapa da Terra: um corpo a pe em cima da agua andava **160 px/s sem limite** --
	/// 50 tiles em 10 s, saindo do mapa --, e atravessava rocha (480 px em 3 s, 36 quadros com os pes
	/// DENTRO da parede). Teleportar o corpo pra margem nao fechava nada: escondia. E em 28,4% das
	/// celulas de agua da Terra nao ha margem em 12 tiles, entao nem escondia.
	///
	/// ============================ AS DUAS PERGUNTAS, E ELAS SAO DIFERENTES ============================
	/// **1. QUEM ESTA TE PARANDO: a geometria ou o seu jeito de atravessar?**
	/// Uma parede para todo mundo. A agua so para quem esta A PE (`ClasseDeAgua.Bloqueia`) -- ela nao
	/// e um lugar invalido, e um lugar que exige OUTRO MODO. Quem esta nela tem o que fazer: nadar
	/// (ou voar). E o pedido do dono, com todas as letras: *"faca o personagem FICAR PRESO LA tendo q
	/// RECARREGAR O KI pra voltar a nadar e continuar"*.
	///
	/// A pergunta se faz com um modo que NAO seja `APe`: `Nadando` responde so pela geometria, porque
	/// e o unico eixo em que ele difere. Se a celula para ate quem nada, e parede.
	///
	/// **2. PAREDE MESMO: pra que lado fica a saida?**
	/// Aqui o corpo nao tem o que mudar em si -- ele foi POSTO num lugar impossivel (nasceu na pedra,
	/// uma obra subiu em cima dele, uma porta fechou, um mapa recarregou). Ele precisa sair, mas nao
	/// precisa de licenca pra atravessar o mundo: sai **na direcao do lugar valido mais proximo**, e
	/// so. O caminho e monotono, entao o total que ele anda de graca e a distancia inicial ate a
	/// saida -- e nao "para sempre".
	/// ==========================================================================================================
	///
	/// ============================ POR QUE O REFUGIO NAO SAI DO `PontoLivrePerto` ============================
	/// Porque aquele pergunta pela CELULA (`ServeDeChao(cx,cy)`) e aqui a pergunta e pelo CORPO: a
	/// caixa dos pes tem 16x10 px e desce 8 px do centro do sprite, entao ela encosta em ate quatro
	/// celulas. Uma celula "livre" cujo vizinho de baixo e parede nao serve de refugio -- o corpo
	/// chegaria la e continuaria preso, e o escape nunca terminaria.
	///
	/// A busca tambem recusa a BEIRADA do mapa (`NaBorda`) pelo mesmo motivo que o `ServeDeChao`
	/// recusa: um corpo posto la esta num lugar de onde ele nao pode dar um passo.
	/// ====================================================================================================
	/// </summary>
	/// <param name="modo">Como este corpo atravessa AGORA -- e o que separa a pergunta 1 da 2.</param>
	/// <param name="refugio">Onde por o centro do sprite. So vale com <see cref="Escape.Dirigido"/>.</param>
	public static Escape Escapar(ZoneCollision mapa, Vec2 pos, ModoDeTravessia modo, out Vec2 refugio)
	{
		refugio = pos;

		// 1. A GEOMETRIA TE PARA? `Nadando` responde so por ela -- ver o cabecalho.
		if (!Occupied(mapa, pos, ModoDeTravessia.Nadando))
		{
			// ============================ **UM PE EM TERRA FIRME**, e este caso e uma medicao ============================
			// A caixa dos pes tem 16x10 px e encosta em ate QUATRO celulas, entao o corpo que a exaustao
			// derruba na beira do lago pode ficar com UMA quina molhada e tres no seco. Ele nao esta na
			// agua em nenhum sentido util -- ele esta a dez pixels de estar completamente fora dela.
			//
			// A primeira versao disto o deixava cair na regra normal ("vale o passo que TERMINA valido"),
			// e a bancada mostrou que isso NAO resolve: o passo de um quadro tem 2,7 px e a caixa so sai
			// da agua depois de ~10, entao TODO alvo intermediario continua invalido e o corpo fica
			// congelado -- medido, `andando PRO SECO 1s: 0 px`. Ele teria de gastar tres segundos de Ki
			// nadando pra andar dez pixels, o que pra um novato e ~45 s parado olhando pra praia.
			//
			// A REGRA E A MAIS APERTADA QUE RESOLVE: se a caixa dos pes JA TOCA uma celula valida, o
			// corpo se puxa pra ela. Nada de busca, nada de raio -- so as quatro quinas que ele ja ocupa.
			// No meio do lago as quatro sao agua e a resposta continua sendo `Nenhum`, que e o pedido do
			// dono. Isto nao vira travessia por construcao: o refugio e uma celula que ele JA ESTA
			// TOCANDO, entao o total que se anda com ele e menos de um tile.
			// ========================================================================================================
			if (QuinaValida(mapa, pos, modo) is { } quina) { refugio = quina; return Escape.Dirigido; }
			return Escape.Nenhum;
		}

		// 2. UM LUGAR EM QUE ESTE CORPO, DO JEITO QUE ELE ESTA, CONSEGUE FICAR.
		if (RefugioPerto(mapa, pos, modo) is { } bom) { refugio = bom; return Escape.Dirigido; }

		// 3. NAO HA -- e ele esta dentro da pedra. Entao vale sair da PEDRA, ainda que o destino seja
		//    agua: la ele deixa de estar preso pela geometria e passa a estar preso pelo modo, que e
		//    um estado que ele mesmo desfaz. E o caso do corpo enterrado numa ilha de rocha no meio
		//    do oceano -- sem esta segunda busca ele cairia no `SemRefugio` e ganharia o passe livre
		//    de volta, que e exatamente o que esta funcao existe pra tirar.
		if (modo != ModoDeTravessia.Nadando
			&& RefugioPerto(mapa, pos, ModoDeTravessia.Nadando) is { } qualquer)
		{ refugio = qualquer; return Escape.Dirigido; }

		return Escape.SemRefugio;
	}

	/// <summary>
	/// UMA DAS QUATRO CELULAS QUE A CAIXA DOS PES JA TOCA E VALIDA PRA ESTE CORPO? Devolve o centro de
	/// sprite correspondente a mais PROXIMA delas, ou nulo.
	///
	/// E o "um pe em terra firme" do <see cref="Escapar"/>: quatro consultas de celula, sem busca.
	/// Escolhe a mais proxima (e nao a primeira) porque um corpo com duas quinas secas tem que sair
	/// pelo lado em que ele ja esta mais fora -- pelo outro ele andaria mais dentro antes de sair.
	/// </summary>
	public static Vec2? QuinaValida(ZoneCollision mapa, Vec2 pos, ModoDeTravessia modo)
	{
		float y = pos.Y + FeetOffsetY;
		Vec2? melhor = null;
		float melhorD = float.MaxValue;

		foreach (Vec2 quina in new[]
		{
			new Vec2(pos.X - BodyHalfW, y - BodyHalfH), new Vec2(pos.X + BodyHalfW, y - BodyHalfH),
			new Vec2(pos.X - BodyHalfW, y + BodyHalfH), new Vec2(pos.X + BodyHalfW, y + BodyHalfH),
		})
		{
			int cx = (int)MathF.Floor(quina.X / ZoneCollision.TileSize);
			int cy = (int)MathF.Floor(quina.Y / ZoneCollision.TileSize);
			if (mapa.NaBorda(cx, cy) || mapa.Bloqueia(cx, cy, modo)) continue;

			Vec2 c = mapa.CentroDaCelula(cx, cy);
			var corpo = new Vec2(c.X, c.Y - FeetOffsetY);
			float d = (corpo - pos).LengthSquared;
			if (d < melhorD) { melhorD = d; melhor = corpo; }
		}

		return melhor;
	}

	/// <summary>
	/// O CENTRO DE SPRITE MAIS PROXIMO EM QUE ESTE CORPO CABE, anel por anel. Nulo se nao houver
	/// nenhum em <see cref="RaioDoEscape"/> aneis.
	///
	/// O anel comeca em ZERO de proposito: a celula dos pes pode estar livre e o corpo preso mesmo
	/// assim (a caixa encosta na vizinha), e nesse caso recentrar nela ja resolve.
	/// </summary>
	public static Vec2? RefugioPerto(ZoneCollision mapa, Vec2 pos, ModoDeTravessia modo,
									 int raioMax = RaioDoEscape)
	{
		// PELOS PES e nao pelo centro do sprite: e a caixa dos pes que ocupa chao, e e dela que a
		// celula de referencia tem que sair (a cabeca passa por cima do muro no desenho).
		int cx = (int)MathF.Floor(pos.X / ZoneCollision.TileSize);
		int cy = (int)MathF.Floor((pos.Y + FeetOffsetY) / ZoneCollision.TileSize);

		for (int r = 0; r <= raioMax; r++)
			for (int dx = -r; dx <= r; dx++)
				for (int dy = -r; dy <= r; dy++)
				{
					// So a BORDA do quadrado de raio r: o miolo ja foi olhado nos aneis anteriores.
					if (r > 0 && Math.Abs(dx) != r && Math.Abs(dy) != r) continue;

					int tx = cx + dx, ty = cy + dy;
					if (mapa.NaBorda(tx, ty)) continue;

					// O corpo fica com os PES no centro desta celula: o centro do sprite sobe.
					Vec2 c = mapa.CentroDaCelula(tx, ty);
					var corpo = new Vec2(c.X, c.Y - FeetOffsetY);
					if (!Occupied(mapa, corpo, modo)) return corpo;
				}

		return null;
	}

	/// <summary>
	/// ESTE PASSO APROXIMA DO REFUGIO? -- o unico passo que um corpo preso tem direito de dar.
	///
	/// <paramref name="folga"/> existe pro lado do SERVIDOR: ele confere a posicao que o cliente
	/// afirma, e as duas pontas podem estar em celulas vizinhas na hora de escolher o refugio (a
	/// escolha e deterministica, mas a POSICAO de partida nao e a mesma bit a bit). Sem folga, um
	/// escape honesto viraria correcao -- o corpo tremendo, o defeito de sempre deste projeto.
	/// </summary>
	public static bool Aproxima(Vec2 de, Vec2 para, Vec2 refugio, float folga = 0f)
		=> (para - refugio).Length < (de - refugio).Length + folga;

	/// <summary>
	/// A FOLGA DO ESCAPE, em pixels por PACOTE. **Meio pixel, e nao os 6 do `MinCorrectionPx`.**
	///
	/// A tentacao e reusar `MinCorrectionPx`, e ela e um buraco: o pacote do cliente carrega ~5 px de
	/// passo, entao 6 px de folga aprovam qualquer direcao -- inclusive a contraria -- e a 30 pacotes
	/// por segundo isso e 180 px/s de caminhada livre. Meio pixel esta abaixo de qualquer passo real e
	/// acima de qualquer ruido de float, e so vale enquanto o corpo esta DENTRO da geometria, que dura
	/// fracao de segundo.
	/// </summary>
	public const float FolgaDoEscape = 0.5f;

	/// <summary>
	/// O caminho de <paramref name="from"/> ate <paramref name="to"/> encosta em parede?
	///
	/// Usa a MESMA caixa dos pes do <see cref="Advance"/>. O `PathBlocked` do mapa testa um
	/// PONTO no centro do corpo, 3 px acima do topo da caixa -- e essa diferenca de 3 px era
	/// uma faixa onde o servidor reprovava um passo que o cliente considerou legal, gerando
	/// correcao em jogo honesto. Regra compartilhada so vale se for a MESMA regra.
	/// </summary>
	public static bool PathOccupied(ZoneCollision mapa, Vec2 from, Vec2 to,
									ModoDeTravessia modo = ModoDeTravessia.APe)
	{
		Vec2 d = to - from;
		float dist = d.Length;
		int passos = Math.Max(1, (int)MathF.Ceiling(dist / (ZoneCollision.TileSize * 0.5f)));
		for (int i = 1; i <= passos; i++)
			if (Occupied(mapa, from + d * (i / (float)passos), modo)) return true;
		return false;
	}

	/// <summary>
	/// A caixa dos pes encosta em algo que PARA ESTE CORPO nesta posicao?
	///
	/// Parede sempre; agua so pra quem esta a pe (ver <see cref="ClasseDeAgua"/>). O padrao e
	/// <see cref="ModoDeTravessia.APe"/> de proposito: o modo mais restritivo e o certo pra quem
	/// esta perguntando "cabe um corpo aqui?" -- pouso, teleporte, construcao, empurrao contra
	/// parede. Quem passa pela agua tem que DIZER que passa.
	/// </summary>
	public static bool Occupied(ZoneCollision mapa, Vec2 centro,
								ModoDeTravessia modo = ModoDeTravessia.APe)
	{
		float y = centro.Y + FeetOffsetY;
		return mapa.BloqueiaEm(new Vec2(centro.X - BodyHalfW, y - BodyHalfH), modo)
			|| mapa.BloqueiaEm(new Vec2(centro.X + BodyHalfW, y - BodyHalfH), modo)
			|| mapa.BloqueiaEm(new Vec2(centro.X - BodyHalfW, y + BodyHalfH), modo)
			|| mapa.BloqueiaEm(new Vec2(centro.X + BodyHalfW, y + BodyHalfH), modo);
	}

	/// <summary>
	/// HA AGUA DEBAIXO DESTE CORPO? -- a MESMA caixa dos pes do <see cref="Occupied"/>.
	///
	/// ============================ POR QUE NAO BASTA PERGUNTAR PELO CENTRO ============================
	/// Porque quem decide se o corpo PASSA ja usa a caixa de quatro quinas, e perguntar "ainda estou
	/// na agua?" pelo ponto do meio abre uma faixa de ~8 px na beira onde as duas respostas
	/// discordam: o nado desligaria (centro no seco) com a caixa ainda encostando na agua. E nessa
	/// faixa o corpo fica **livre pela colisao e parado pela regra**, que e o estado que dispara a
	/// saida de emergencia do <see cref="Advance"/> -- ela devolve o passo CHEIO, sem olhar celula
	/// nenhuma, e por alguns quadros daria pra atravessar parede na margem.
	///
	/// Com a mesma caixa a faixa nao existe: o nado so cai quando o corpo inteiro esta no seco, e a
	/// saida de emergencia volta a ser o que ela e -- socorro pra quem foi POSTO na agua (deslogar
	/// dentro do lago, um arremesso), e nao um efeito colateral de andar ate a praia.
	/// ============================================================================================
	/// </summary>
	public static bool NaAgua(ZoneCollision mapa, Vec2 centro)
	{
		float y = centro.Y + FeetOffsetY;
		return mapa.EhAguaEm(new Vec2(centro.X - BodyHalfW, y - BodyHalfH))
			|| mapa.EhAguaEm(new Vec2(centro.X + BodyHalfW, y - BodyHalfH))
			|| mapa.EhAguaEm(new Vec2(centro.X - BodyHalfW, y + BodyHalfH))
			|| mapa.EhAguaEm(new Vec2(centro.X + BodyHalfW, y + BodyHalfH));
	}

	/// <summary>
	/// ESTE CORPO ESTA EM CIMA DE NUVEM? -- a MESMA caixa dos pes do <see cref="Occupied"/> e do
	/// <see cref="NaAgua"/>, e pelo mesmo motivo escrito la em cima.
	///
	/// ============================ MAS AQUI ELA E **QUALQUER QUINA**, E ISSO PRECISA SER DITO ============================
	/// A caixa devolve verdadeiro se UMA das quatro quinas esta na nuvem, e pra a agua isso e
	/// generoso do lado seguro (o nado continua ligado mais um pouco). Pra a nuvem que DERRUBA e o
	/// contrario: encostar uma quina ja faz cair.
	///
	/// E e o desfecho certo, porque ele e o do original. No BYOND o corpo ocupa UM tile e o `Enter()`
	/// dispara quando ele entra nele -- nao ha meio-tile, nao ha "encostou de raspao". A quina e o
	/// analogo mais proximo disso num jogo que anda em pixel: o instante em que qualquer parte do
	/// corpo pisou na nuvem. Perguntar pelo CENTRO deixaria o jogador andar meia caixa por cima do
	/// ceu antes de cair -- visivelmente flutuando sobre o vazio, que e a queixa a consertar.
	/// ==============================================================================================================
	/// </summary>
	public static bool NaNuvem(ZoneCollision mapa, Vec2 centro)
	{
		float y = centro.Y + FeetOffsetY;
		return mapa.EhNuvemEm(new Vec2(centro.X - BodyHalfW, y - BodyHalfH))
			|| mapa.EhNuvemEm(new Vec2(centro.X + BodyHalfW, y - BodyHalfH))
			|| mapa.EhNuvemEm(new Vec2(centro.X - BodyHalfW, y + BodyHalfH))
			|| mapa.EhNuvemEm(new Vec2(centro.X + BodyHalfW, y + BodyHalfH));
	}

	/// <summary>
	/// O servidor confere o passo que o cliente afirma ter dado.
	/// Devolve true se aceitou; se recusou, <paramref name="corrected"/> traz a posicao
	/// mais longe que o cliente PODERIA ter alcancado na direcao que ele tentou.
	/// </summary>
	/// <summary>
	/// Validacao COMPLETA: velocidade + parede. O <paramref name="mapa"/> pode vir nulo
	/// (zona procedural ainda sem colisao carregada) e ai so a velocidade e conferida.
	/// </summary>
	/// <remarks>
	/// ============================ **AQUI NAO SE CONSULTA CORPO, E ISSO E UMA DECISAO** ============================
	/// O <see cref="Advance"/> ganhou o plano dos corpos e esta funcao NAO -- e a assimetria precisa
	/// estar escrita, porque ela parece esquecimento e nao e.
	///
	/// **O MAPA E ESTATICO E COMPARTILHADO BIT A BIT.** As duas pontas leem o mesmo `.col`, entao exigir
	/// que elas concordem sobre parede e de graca: elas concordam. **OS CORPOS SAO DINAMICOS E CADA
	/// PONTA OS VE NOUTRO INSTANTE** -- o cliente desenha os remotos interpolados ~1 tique atras
	/// (`RemotePlayer`), e o servidor tem a posicao de agora. A distancia entre as duas leituras e a
	/// velocidade do OUTRO corpo vezes o atraso: ~5 px andando, ~12 px correndo, mais o ping. A caixa
	/// tem 16 px de largura. Ou seja: nao existe folga capaz de fazer as duas concordarem, e cobrar a
	/// concordancia produziria **correcao em jogo honesto** -- que neste projeto tem nome, sintoma e
	/// historico: o personagem tremendo.
	///
	/// **E O QUE SE PERDE E NADA.** O que a validacao protege e o mapa: atravessar parede da acesso a
	/// lugar. Atravessar uma PESSOA nao da acesso a nada -- da pra contornar em 0,2 s, e o alcance do
	/// soco e um cone de 0-40 px que estar "dentro" de alguem nao melhora. Um cliente modificado que
	/// ignore corpos ganha o direito de ficar visualmente sobreposto, e mais nada.
	///
	/// **E OS TRES CASOS QUE O DONO PEDIU CONTINUAM COBERTOS**, porque nenhum deles passa por aqui:
	///   * ANDANDO   -- o cliente preve pelo <see cref="Advance"/> (e o NPC anda pelo mesmo `Advance`,
	///                  no servidor: quem GERA a posicao dele e o servidor, nao um cliente);
	///   * KNOCKBACK -- `GameServer.Empurrao.TickDoEmpurrao`, 100% servidor;
	///   * ARREMESSO DO GRAB -- desemboca no mesmo `TickDoEmpurrao`.
	/// Velocidade, parede e agua continuam validadas exatamente como antes.
	/// ==========================================================================================================
	/// </remarks>
	public static bool ValidateStep(Vec2 from, Vec2 claimed, float dtSeconds, float speedStat,
		ZoneCollision? mapa, ref float orcamentoPx, out Vec2 corrected, bool correndo = false,
		ModoDeTravessia modo = ModoDeTravessia.APe)
	{
		if (!ValidateStep(from, claimed, dtSeconds, speedStat, ref orcamentoPx, out corrected, correndo)) return false;
		if (mapa == null) return true;

		// ============================ JA ESTAVA PRESO: A MESMA REGRA DO `Advance`, E TEM QUE SER ============================
		// Esta linha era `if (Occupied(mapa, from, modo)) return true;` -- "nao ha o que conferir". Ela
		// e a metade SERVIDOR do buraco que o dono achou andando por cima da agua: fechar so o
		// `Advance` deixaria o cliente parando e o servidor aceitando qualquer coisa que um cliente
		// modificado afirmasse. As duas pontas caem juntas, pela MESMA <see cref="Escapar"/>.
		//
		// **NAO SE MEXER E SEMPRE LEGITIMO.** Quando o escape nega o passo, o cliente (que roda a mesma
		// regra) ja parou -- ele afirma a posicao em que esta. Devolver `false` ai seria mandar um
		// pacote de correcao POR QUADRO pra corrigir nada: correcao em jogo honesto, que e o corpo
		// tremendo. Por isso a folga de <see cref="MinCorrectionPx"/> antes de qualquer recusa.
		// ==============================================================================================================
		if (Occupied(mapa, from, modo))
		{
			// "NAO ME MEXI" TEM QUE SER UMA MEDIDA APERTADA, E ISSO CUSTOU UMA MEDICAO. A primeira
			// versao aceitava qualquer coisa dentro de `MinCorrectionPx` (6 px) -- e um passo de UM
			// QUADRO a 160 px/s tem 2,7 px. Ou seja: a guarda anti-tremor engolia a regra inteira, e um
			// cliente modificado atravessava o lago a velocidade cheia, 2,7 px por pacote. A bancada
			// pegou (`passo NO SENTIDO OPOSTO aceito? True`).
			//
			// Aqui "parado" quer dizer PARADO: o cliente roda a mesma `Escapar` e, negado, devolve a
			// posicao em que ja estava -- bit a bit a mesma que o servidor guardou. O epsilon so existe
			// pra o float, e nao pra dar folga a passo nenhum.
			bool parado = (corrected - from).LengthSquared <= 1e-4f;
			Escape esc = Escapar(mapa, from, modo, out Vec2 refugio);

			if (esc == Escape.SemRefugio) return true;   // mapa quebrado: o comportamento antigo

			if (esc == Escape.Dirigido)
			{
				if (parado || Aproxima(from, corrected, refugio, FolgaDoEscape)) return true;
				corrected = from;
				return false;
			}

			// PRESO PELO MODO: cai na regra normal, exatamente como o `Advance` faz -- ver o bloco
			// grande de la. Vale o passo que TERMINA valido (a quina na beira do lago) e nada mais; o
			// `PathOccupied` logo abaixo e quem confere isso, e ele e a MESMA pergunta que o cliente
			// respondeu ao decidir o passo.
			if (parado) return true;
		}

		// velocidade OK, mas atravessou parede? volta pra onde estava. A checagem e a MESMA
		// que o cliente usou pra andar -- divergir aqui gera correcao em jogo honesto, e
		// correcao em jogo honesto e o que o jogador ve como o personagem tremendo.
		//
		// O `modo` TEM QUE SER O MESMO NAS DUAS PONTAS. Quem decide se o corpo esta nadando e o
		// SERVIDOR (como ja decide se esta correndo) -- se fosse a afirmacao do cliente, "estou
		// nadando" seria atravessar todo lago do mapa de graca.
		if (PathOccupied(mapa, from, corrected, modo))
		{
			corrected = from; // fica onde estava
			return false;
		}
		return true;
	}

	/// <summary>
	/// Quantos passos de folga o orcamento acumula, no maximo.
	///
	/// ============================ POR QUE ORCAMENTO E NAO TETO INSTANTANEO ============================
	/// A conta antiga comparava a distancia de UM pacote com `velocidade * dt * tolerancia`, e o
	/// `dt` era medido entre CHEGADAS de pacote. So que o cliente integra por quadro de render e
	/// envia por acumulador: o intervalo que ele SIMULOU nunca e o que o servidor MEDIU -- os dois
	/// so batem na media.
	///
	/// Medido no log do proprio dono: o cliente manda a cada 33,3 ms e o servidor recebeu com
	/// 25 ms de intervalo. A razao 33,3/25 = 1,333 sozinha ja consumia 98,8% da tolerancia de
	/// 1,35 -- em jogo HONESTO, sem nada de errado. Nao sobrava folga pra mais nada, e qualquer
	/// jitter virava correcao.
	///
	/// O orcamento resolve na raiz: o direito de andar ACUMULA com o tempo e e gasto ao andar. Um
	/// pacote atrasado gasta o que sobrou do anterior, e a media continua limitada exatamente pela
	/// velocidade -- nao ha velocidade de graca, so ha tolerancia a jitter. O teto de acumulo e o
	/// que impede alguem de ficar parado juntando credito pra dar um salto depois.
	/// ================================================================================================
	/// </summary>
	public const float PassosDeFolga = 3f;

	/// <summary>
	/// VALIDA UM PASSO, com orcamento.
	///
	/// <paramref name="orcamentoPx"/> entra com o credito acumulado e sai com o que sobrou. O
	/// chamador guarda esse numero por jogador.
	/// </summary>
	public static bool ValidateStep(Vec2 from, Vec2 claimed, float dtSeconds, float speedStat,
		ref float orcamentoPx, out Vec2 corrected, bool correndo = false)
	{
		if (dtSeconds < 0) dtSeconds = 0;
		if (dtSeconds > MaxDeltaSeconds) dtSeconds = MaxDeltaSeconds;

		// O `correndo` que chega aqui e o que o SERVIDOR concedeu, nao o que o cliente
		// afirmou -- ver GameServer.Input(). Se fosse a afirmacao do cliente, "estou
		// correndo" seria 60% de velocidade de graca e pra sempre.
		float porPasso = SpeedPx(speedStat, correndo) * dtSeconds;
		float teto = MathF.Max(SpeedPx(speedStat, correndo) / 30f, 1f) * PassosDeFolga * SpeedTolerance;

		orcamentoPx = MathF.Min(orcamentoPx + porPasso * SpeedTolerance, teto);

		Vec2 delta = claimed - from;
		float dist = delta.Length;

		if (dist <= orcamentoPx + MinCorrectionPx)
		{
			orcamentoPx = MathF.Max(0f, orcamentoPx - dist);
			corrected = claimed;
			return true;
		}

		// ANDOU DEMAIS: para no limite do que era possivel, mantendo a direcao.
		//
		// ATENCAO AO SINAL. Este clamp SO faz sentido quando `delta` aponta PRA FRENTE. Se o
		// cliente afirmar uma posicao ATRAS da que o servidor tem -- o que acontece depois de um
		// teleporte, com os pacotes que ja estavam em voo --, `delta` aponta pra tras e este
		// clamp vira um PASSO PRA TRAS na velocidade maxima. Era essa a causa do "o servidor me
		// puxa pra tras no dash": cada pacote obsoleto desfazia um pedaco da investida, e o
		// servidor GRAVAVA o recuo. Quem impede isso e o descarte por sequencia (ver
		// `GameServer.Input`), que faz o pacote obsoleto nem chegar aqui.
		float passo = MathF.Min(orcamentoPx, dist);
		orcamentoPx = MathF.Max(0f, orcamentoPx - passo);
		corrected = from + delta.Normalized() * passo;
		return false;
	}

	/// <summary>
	/// Direcao do sprite a partir do vetor de movimento. O eixo DOMINANTE vence, e o
	/// desempate favorece o horizontal (mesma sensacao do BYOND, que nunca teve diagonal
	/// de sprite pra personagem).
	/// </summary>
	public static Facing FacingFrom(Vec2 dir, Facing atual)
	{
		if (dir.LengthSquared < 1e-6f) return atual; // parado mantem pra onde olhava
		return MathF.Abs(dir.X) >= MathF.Abs(dir.Y)
			? (dir.X >= 0 ? Facing.East : Facing.West)
			: (dir.Y >= 0 ? Facing.South : Facing.North);
	}

	/// <summary>Sufixo que o conversor de .dmi gravou no nome da animacao.</summary>
	public static string FacingSuffix(Facing f) => f switch
	{
		Facing.North => "north",
		Facing.East => "east",
		Facing.West => "west",
		_ => "south",
	};
}
