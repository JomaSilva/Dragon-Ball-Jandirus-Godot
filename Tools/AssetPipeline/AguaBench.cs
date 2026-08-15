using Jandirus.Core.World;

namespace Jandirus.Tools;

/// <summary>
/// A BANCADA DA AGUA -- e ela e uma LISTA DE NEGACOES, porque o pedido do dono era uma.
///
/// ============================ POR QUE FAMILIAS, E NAO UMA LISTA DE PROVAS ============================
/// O dono nao descreveu um recurso: ele descreveu uma CLASSE DE CELULA dizendo, item por item, o que
/// ela NAO e --
///
///     "todo TILE DE AGUA n da pra passar andando, so NADANDO ou usando o FLIGHT, ela e tipo uma
///      parede, mas N DA PRA SOCAR (igual o chao), n tem SOMBRA nem nada disso, simplesmente e um
///      chao q n da pra andar por cima, so voando ou nadando"
///
/// Cada negacao dessas tem um CONSUMIDOR diferente no jogo (o passo, o punho, o `.vis`, a IA, o
/// gerador), e o jeito conhecido de uma passar batido e provar so a primeira -- "nao da pra andar
/// por cima, pronto" -- e descobrir seis meses depois que o lago fazia sombra ou que dava pra socar.
/// Entao cada negacao virou uma FAMILIA: um grupo de provas que morrem juntas e que tem dono.
///
/// ============================ E CADA FAMILIA TEM QUE SABER REPROVAR ============================
/// Uma prova que nunca ficou vermelha nao e uma prova, e uma frase. Toda familia aqui declara os
/// DEFEITOS que ela existe pra pegar, a bancada INJETA cada um e exige que a familia fique vermelha.
/// Se ela continuar verde com o defeito dentro, isso e reportado como `CEGA` -- e um buraco de
/// cobertura, nao um sucesso.
///
/// Os defeitos nao sao inventados: cada um e um erro que este projeto ja cometeu ou que a proxima
/// pessoa cometeria de boa fe. "Marcar agua no `.col`" e a tentacao obvia (ela *e* tipo uma parede);
/// "o chamador esqueceu de passar o modo" foi achado de verdade no `GameServer.Voo.cs:315`; "a agua
/// entrou no `.vis`" e o que acontece no dia em que alguem por `Density = true` no `DmTurfScanner`.
///
/// ============================ OS CONTRA-EXEMPLOS SAO METADE DA BANCADA ============================
/// Metade das provas daqui afirma o CONTRARIO da agua, e elas nao sao enfeite: sem "a parede normal
/// QUEBRA", um soco que nunca alcanca nada passaria verde e o cenario destrutivel estaria morto sem
/// ninguem notar; sem "a parede normal ESCONDE", uma vista que atravessa tudo passaria verde; sem "o
/// VOO sobe e faz sombra", um jogo sem sombra nenhuma passaria verde. Um sistema so esta provado
/// quando as duas respostas -- sim e nao -- saem do mesmo lugar.
/// ==================================================================================================
///
///     dotnet run --project Tools/AssetPipeline -- agua-prova [pastaMaps] [raizDoRepo]
/// </summary>
public static class AguaBench
{
	// ==================================================================================
	// AS PECAS: o cenario (dados) e as regras (as funcoes de producao que o jogo chama)
	// ==================================================================================

	/// <summary>O passo do movimento, com o modo explicito. E o `MoveRules.Advance` de producao.</summary>
	private delegate Vec2 PassoDelegado(Vec2 pos, Vec2 dir, float dt, float vel,
										ZoneCollision? mapa, ModoDeTravessia modo);

	/// <summary>A saida de emergencia. E o `MoveRules.Escapar` de producao.</summary>
	private delegate MoveRules.Escape EscapeDelegado(ZoneCollision mapa, Vec2 pos,
													 ModoDeTravessia modo, out Vec2 refugio);

	/// <summary>
	/// AS PERGUNTAS QUE O JOGO FAZ -- por padrao, as funcoes DE PRODUCAO, ponto.
	///
	/// ============================ POR QUE DELEGADOS E NAO CHAMADAS DIRETAS ============================
	/// Porque e isto que permite injetar o defeito de verdade. Nem todo defeito e um dado errado --
	/// "o soco parou de perguntar a classe e voltou a olhar so o bitset" e uma mudanca de CODIGO, e a
	/// unica forma honesta de provar que a familia pegaria isso e rodar as MESMAS provas contra o
	/// codigo mutante. Trocar um campo aqui e trocar a implementacao debaixo das provas.
	///
	/// O caminho saudavel nao paga nada por isso: o valor inicial de cada campo e o metodo de
	/// producao, e a bancada verde e a bancada medindo o jogo.
	/// ================================================================================================
	/// </summary>
	private sealed class Regras
	{
		/// <summary>`MoveRules.Occupied` -- esta celula PARA este corpo?</summary>
		public Func<ZoneCollision, Vec2, ModoDeTravessia, bool> Para = MoveRules.Occupied;

		/// <summary>`MoveRules.Advance` -- o passo com deslize.</summary>
		public PassoDelegado Passo =
			(p, d, dt, v, m, modo) => MoveRules.Advance(p, d, dt, v, m, out _, false, modo);

		/// <summary>
		/// `MoveRules.Escapar` -- **a saida de emergencia**, e ela e um campo proprio porque as
		/// familias 8 a 10 perguntam a ela DIRETO, e nao so pelo passo.
		///
		/// A pergunta seca ("o que este corpo preso tem direito de fazer?") e o que separa "o corpo nao
		/// andou" de "o corpo nao andou POR ESTE MOTIVO". Sem ela, um `Advance` que congelasse todo
		/// mundo passaria verde na familia 8 inteira -- e congelar todo mundo e exatamente o defeito
		/// que a familia 9 existe pra pegar.
		/// </summary>
		public EscapeDelegado Escape = MoveRules.Escapar;

		/// <summary>`ClasseDeAgua.SocoAlcanca` -- o punho alcanca o cenario desta celula?</summary>
		public Func<ZoneCollision, int, int, bool> SocoAlcanca = ClasseDeAgua.SocoAlcanca;

		/// <summary>
		/// `MoveRules.ValidateStep` -- o servidor conferindo o passo que o cliente afirmou.
		///
		/// Ela entra aqui, e nao como chamada direta, porque o defeito "o chamador esqueceu o modo"
		/// tem DOIS lados: se so o passo do cliente for consertado, o servidor recusa o que o cliente
		/// faz de certo -- e o sintoma disso e o corpo tremendo, que e pior que a agua nao funcionar.
		/// </summary>
		public Func<Vec2, Vec2, ZoneCollision, ModoDeTravessia, bool> Valida =
			(de, ate, m, modo) =>
			{
				float orcamento = 0f;
				return MoveRules.ValidateStep(de, ate, 1f / 30, 1f, m, ref orcamento, out _, false, modo);
			};

		/// <summary>A vista de um ponto a outro, pelo mapa do que CEGA (o `.vis`).</summary>
		public Func<ZoneCollision, Vec2, Vec2, bool> Ve = (vis, a, b) => !vis.PathBlocked(a, b);

		/// <summary>`Voo.TemSombra` -- esta altura projeta sombra no chao?</summary>
		public Func<float, bool> TemSombra = Voo.TemSombra;

		/// <summary>`GeradorDeTerreno.Gerar` -- o mundo por semente.</summary>
		public Func<ParametrosDeTerreno, TerrenoGerado> Gerar = GeradorDeTerreno.Gerar;

		/// <summary>
		/// `ZoneCollision.PontoLivrePerto` -- o funil de "onde da pra POR um corpo".
		///
		/// E por ele que passam o berco, o pouso de nave, a invasao e o povoamento, e e nele que a
		/// clausula de agua foi escrita. Ele entra aqui porque e o unico dos dois caminhos de pouso
		/// que da pra reprovar de verdade -- ver a familia 6.
		/// </summary>
		public Func<ZoneCollision, Vec2, Vec2> PontoLivre = (m, v) => m.PontoLivrePerto(v);

		/// <summary>
		/// A ALTURA QUE O NADO PRODUZ. Zero -- e a frase inteira do dono ("N CRIA A SOMBRA em baixo e
		/// NEM FICA MAIS ALTO") sai dela sozinha. Injetar um valor aqui e o defeito "nadar virou voo
		/// rente ao chao".
		/// </summary>
		public float AlturaDoNado;

		/// <summary>
		/// COMO A BANCADA LE O FONTE DO SERVIDOR. Existe pra a prova estrutural da familia 5 poder
		/// ser reprovada: o defeito e injetado devolvendo o arquivo com uma linha a mais.
		/// </summary>
		public Func<string, string> LerFonte = File.ReadAllText;

		/// <summary>O que a injecao faz com o mapa depois de montado (por padrao, nada).</summary>
		public Action<Cenario> MexerNoMapa = _ => { };
	}

	/// <summary>
	/// O MAPA DE TESTE: um lago de 4 tiles de largura e um muro de verdade, no mesmo mundo.
	///
	/// Os dois juntos, e nao um por vez, porque quase toda prova daqui e um PAR: "a agua faz X" so
	/// significa alguma coisa ao lado de "a parede faz Y". Ter os dois no mesmo mapa tambem garante
	/// que a diferenca medida vem da CLASSE DA CELULA, e nao de dois mapas diferentes.
	///
	///        col:  . . . . . . . . [muro em MuroX]
	///        agua: . . [LagoX0..LagoX1] . . . . .
	///        vis:  . . . . . . . . [muro em MuroX]   <- a agua NAO entra aqui
	/// </summary>
	private sealed class Cenario
	{
		public const int Lado = 48;
		public const int LagoX0 = 18, LagoX1 = 21;
		public const int MuroX = 34;
		public const int Linha = 20;

		/// <summary>O mapa que PARA o corpo. E ele que carrega o plano de agua.</summary>
		public required ZoneCollision Col { get; init; }

		/// <summary>O mapa que CEGA -- o irmao do `.vis`. So o muro entra aqui.</summary>
		public required ZoneCollision Vis { get; init; }

		/// <summary>
		/// O MESMO MUNDO SEM NADA DENTRO -- a regua do deslize.
		///
		/// Ele existe porque uma prova de deslize sem controle fica verde com o deslize QUEBRADO: um
		/// corpo indo na diagonal ja anda em Y ate encostar, e esse pedaco sozinho passou por
		/// "deslizou" na primeira versao desta bancada (a injecao "o deslize sumiu" saiu CEGA). A
		/// pergunta certa nao e "andou em Y?", e "andou em Y TANTO QUANTO andaria sem obstaculo?".
		/// </summary>
		public required ZoneCollision Vazio { get; init; }

		/// <summary>
		/// O MESMO MAPA COM O LAGO VIRADO PAREDE -- o controle da familia 7.
		///
		/// Nao e um defeito injetado: e a REFERENCIA. A afirmacao "a IA nao trava na agua" so tem
		/// conteudo comparada com o que ela ja fazia num muro, que e o obstaculo que sempre existiu.
		/// </summary>
		public required ZoneCollision LagoComoParede { get; init; }

		/// <summary>Centro de um tile, na altura em que a caixa dos pes cabe dentro dele.</summary>
		public static Vec2 NoTile(int cx, int cy) =>
			new(cx * ZoneCollision.TileSize + 16, cy * ZoneCollision.TileSize + 16 - MoveRules.FeetOffsetY);

		public static Vec2 Oeste => NoTile(10, Linha);        // terra firme, antes do lago
		public static Vec2 Leste => NoTile(26, Linha);        // terra firme, depois do lago
		public static Vec2 Dentro => NoTile(19, Linha);       // dentro do lago
		public static Vec2 DepoisDoMuro => NoTile(40, Linha); // do outro lado do muro
	}

	// ==================================================================================
	// AS DUAS INJECOES DE DADO -- e elas leem o plano de agua em vez de saber coordenadas
	// ==================================================================================

	/// <summary>
	/// "ALGUEM MARCOU AGUA NO `.col`" -- a tentacao obvia, porque ela *e* tipo uma parede.
	///
	/// Le o proprio plano de agua do mapa em vez de carregar as coordenadas do lago sintetico: assim
	/// a mesma injecao vale pro cenario de teste E pro mapa da Terra, sem duas versoes pra manter.
	/// </summary>
	private static void AguaViraParede(Cenario c)
	{
		for (int y = 0; y < c.Col.Height; y++)
			for (int x = 0; x < c.Col.Width; x++)
				if (c.Col.EhAgua(x, y)) c.Col.Bloquear(x, y);
	}

	// ==================================================================================
	// OS ESCAPES MUTANTES -- cada um e o `MoveRules.Escapar` com **uma linha trocada**
	//
	// Eles existem porque o escape e o coracao do relato B do dono, e um teste que so o viu
	// aprovando nao sabe se ele sabe negar. Nenhum deles e invencao: o primeiro e o codigo que
	// ESTAVA escrito (a linha que o dono achou segurando a tecla), e os outros tres sao os quatro
	// jeitos de errar que a propria funcao lista no cabecalho dela.
	// ==================================================================================

	/// <summary>
	/// O ESCAPE COMO ELE ERA: <c>if (Occupied(mapa, pos, modo)) return alvo;</c> -- *"ja estava dentro
	/// de parede: deixa sair"*.
	///
	/// Com o corpo a pe em cima da agua, TODO passo dele era aprovado sem checagem nenhuma, parede
	/// inclusa. E o buraco literal do relato: *"se eu estiver SEGURANDO O BOTAO DE ANDAR eu consigo
	/// VOLTAR PRA AGUA ANDANDO POR CIMA DELA"*.
	/// </summary>
	private static Vec2 PassoComEscapeQueAprovaTudo(Vec2 pos, Vec2 dir, float dt, float vel,
													ZoneCollision? mapa, ModoDeTravessia modo)
		=> mapa != null && MoveRules.Occupied(mapa, pos, modo)
			? MoveRules.Integrate(pos, dir, dt, vel)
			: MoveRules.Advance(pos, dir, dt, vel, mapa, out _, false, modo);

	/// <summary>
	/// O ESCAPE FECHADO SECO: `return pos`. E a correcao apressada -- "o dono pediu pra PRENDER, entao
	/// prende" --, e ela troca um bug chato por um corpo perdido: quem nasce dentro da pedra (o berco
	/// num mapa recarregado, uma obra erguida em cima) nunca mais anda.
	/// </summary>
	private static Vec2 PassoQueCongelaOPreso(Vec2 pos, Vec2 dir, float dt, float vel,
											  ZoneCollision? mapa, ModoDeTravessia modo)
		=> mapa != null && MoveRules.Occupied(mapa, pos, modo)
			? pos
			: MoveRules.Advance(pos, dir, dt, vel, mapa, out _, false, modo);

	/// <summary>
	/// O `Advance` COM OUTRO ESCAPE NO LUGAR DO DE PRODUCAO -- e so o escape muda.
	///
	/// Fora do estado "ja preso" ele DELEGA pro `Advance` de verdade, entao a caminhada comum
	/// continua sendo a do jogo. O que esta reescrito aqui e exatamente o bloco que o `Advance` roda
	/// quando `Occupied(pos, modo)` e verdadeiro, linha por linha -- inclusive o ramo `Nenhum`, que
	/// **nao** e `return pos`: ele cai na regra normal (o passo que TERMINA valido), que e o que
	/// destrava a beira do lago.
	/// </summary>
	private static Vec2 PassoComEscape(EscapeDelegado escape, Vec2 pos, Vec2 dir, float dt, float vel,
									   ZoneCollision? mapa, ModoDeTravessia modo)
	{
		if (mapa == null || !MoveRules.Occupied(mapa, pos, modo))
			return MoveRules.Advance(pos, dir, dt, vel, mapa, out _, false, modo);

		Vec2 alvo = MoveRules.Integrate(pos, dir, dt, vel);
		MoveRules.Escape e = escape(mapa, pos, modo, out Vec2 refugio);

		if (e == MoveRules.Escape.SemRefugio) return alvo;

		if (e == MoveRules.Escape.Dirigido)
		{
			if (MoveRules.Aproxima(pos, alvo, refugio)) return alvo;
			var ex = new Vec2(alvo.X, pos.Y);
			if (MoveRules.Aproxima(pos, ex, refugio)) return ex;
			var ey = new Vec2(pos.X, alvo.Y);
			if (MoveRules.Aproxima(pos, ey, refugio)) return ey;
			return pos;
		}

		// NENHUM: a regra normal -- o passo que termina valido, com deslize de quina.
		if (!MoveRules.Occupied(mapa, alvo, modo)) return alvo;
		var sx = new Vec2(alvo.X, pos.Y);
		if (!MoveRules.Occupied(mapa, sx, modo)) return sx;
		var sy = new Vec2(pos.X, alvo.Y);
		if (!MoveRules.Occupied(mapa, sy, modo)) return sy;
		return pos;
	}

	/// <summary>
	/// O `Escapar` COM A PERGUNTA 1 FEITA PELO MODO DO CORPO -- o erro sutil, e o mais provavel de
	/// todos: `Occupied(pos, modo)` em vez de `Occupied(pos, Nadando)`.
	///
	/// Parece a correcao obvia ("pergunte com o modo que o corpo tem"), e ela desfaz a coisa inteira:
	/// a pe a agua para, entao o corpo em cima do lago volta a ser "preso pela GEOMETRIA" e ganha
	/// refugio -- ou seja, volta a **sair andando por cima da agua**, so que agora em linha reta pra
	/// margem. E o relato do dono de novo, com outra roupa.
	/// </summary>
	private static MoveRules.Escape EscaparPeloModoDoCorpo(ZoneCollision m, Vec2 pos,
														   ModoDeTravessia modo, out Vec2 refugio)
	{
		refugio = pos;
		if (!MoveRules.Occupied(m, pos, modo)) return MoveRules.Escape.Nenhum;
		if (MoveRules.RefugioPerto(m, pos, modo) is { } bom) { refugio = bom; return MoveRules.Escape.Dirigido; }
		if (modo != ModoDeTravessia.Nadando
			&& MoveRules.RefugioPerto(m, pos, ModoDeTravessia.Nadando) is { } q)
		{ refugio = q; return MoveRules.Escape.Dirigido; }
		return MoveRules.Escape.SemRefugio;
	}

	/// <summary>
	/// O `Escapar` COM A BEIRA RESOLVIDA POR BUSCA EM ANEIS, e nao pelas quatro quinas que o corpo ja
	/// toca. E a "generalizacao" natural do `QuinaValida` -- e ela transforma socorro em TRAVESSIA:
	/// do meio do lago a busca acha a margem a dois tiles e o corpo caminha ate la por cima da agua.
	/// </summary>
	private static MoveRules.Escape EscaparComBuscaNaBeira(ZoneCollision m, Vec2 pos,
														   ModoDeTravessia modo, out Vec2 refugio)
	{
		refugio = pos;
		if (!MoveRules.Occupied(m, pos, ModoDeTravessia.Nadando))
		{
			if (MoveRules.RefugioPerto(m, pos, modo) is { } perto) { refugio = perto; return MoveRules.Escape.Dirigido; }
			return MoveRules.Escape.Nenhum;
		}
		if (MoveRules.RefugioPerto(m, pos, modo) is { } bom) { refugio = bom; return MoveRules.Escape.Dirigido; }
		if (modo != ModoDeTravessia.Nadando
			&& MoveRules.RefugioPerto(m, pos, ModoDeTravessia.Nadando) is { } q)
		{ refugio = q; return MoveRules.Escape.Dirigido; }
		return MoveRules.Escape.SemRefugio;
	}

	/// <summary>
	/// O `Escapar` PERGUNTANDO A QUINA PELO **CENTRO DO SPRITE**, e nao pela caixa dos pes.
	///
	/// E o descuido classico deste arquivo (o `NaAgua` ja carrega um cabecalho inteiro sobre ele): a
	/// caixa tem 16x10 px e desce 8 do centro, entao ha uma faixa em que o centro esta molhado e uma
	/// quina ja esta seca. Perguntando pelo centro, esse corpo nao tem refugio nenhum -- e ele congela
	/// a dez pixels da praia, tendo que pagar tres segundos de Ki pra andar esses dez pixels.
	/// </summary>
	private static MoveRules.Escape EscaparComQuinaPeloCentro(ZoneCollision m, Vec2 pos,
															  ModoDeTravessia modo, out Vec2 refugio)
	{
		refugio = pos;
		if (!MoveRules.Occupied(m, pos, ModoDeTravessia.Nadando))
		{
			int cx = (int)MathF.Floor(pos.X / ZoneCollision.TileSize);
			int cy = (int)MathF.Floor((pos.Y + MoveRules.FeetOffsetY) / ZoneCollision.TileSize);
			if (m.NaBorda(cx, cy) || m.Bloqueia(cx, cy, modo)) return MoveRules.Escape.Nenhum;
			Vec2 c = m.CentroDaCelula(cx, cy);
			refugio = new Vec2(c.X, c.Y - MoveRules.FeetOffsetY);
			return MoveRules.Escape.Dirigido;
		}
		if (MoveRules.RefugioPerto(m, pos, modo) is { } bom) { refugio = bom; return MoveRules.Escape.Dirigido; }
		if (modo != ModoDeTravessia.Nadando
			&& MoveRules.RefugioPerto(m, pos, ModoDeTravessia.Nadando) is { } q)
		{ refugio = q; return MoveRules.Escape.Dirigido; }
		return MoveRules.Escape.SemRefugio;
	}

	/// <summary>
	/// O `Escapar` COM O RAIO DE BUSCA CURTO -- um anel em vez de <see cref="MoveRules.RaioDoEscape"/>.
	///
	/// Nao e um numero absurdo: um anel e o que resolve o caso comum (uma parede de um tile), e quem
	/// "otimizasse" a busca por causa do custo por quadro chegaria exatamente nele. O que ele quebra e
	/// o caso raro e grave: enterrado FUNDO, o corpo cai no <see cref="MoveRules.Escape.SemRefugio"/>
	/// e ganha o passe livre de volta -- ou seja, o buraco do dono volta pela porta dos fundos.
	/// </summary>
	private static MoveRules.Escape EscaparComRaioDeUmAnel(ZoneCollision m, Vec2 pos,
														   ModoDeTravessia modo, out Vec2 refugio)
	{
		refugio = pos;
		if (!MoveRules.Occupied(m, pos, ModoDeTravessia.Nadando))
		{
			if (MoveRules.QuinaValida(m, pos, modo) is { } quina) { refugio = quina; return MoveRules.Escape.Dirigido; }
			return MoveRules.Escape.Nenhum;
		}
		if (MoveRules.RefugioPerto(m, pos, modo, 1) is { } bom) { refugio = bom; return MoveRules.Escape.Dirigido; }
		if (modo != ModoDeTravessia.Nadando
			&& MoveRules.RefugioPerto(m, pos, ModoDeTravessia.Nadando, 1) is { } q)
		{ refugio = q; return MoveRules.Escape.Dirigido; }
		return MoveRules.Escape.SemRefugio;
	}

	// ==================================================================================
	// O SERVIDOR CONFERINDO -- e as duas versoes erradas dele
	// ==================================================================================

	/// <summary>
	/// A METADE SERVIDOR DO BURACO: <c>if (Occupied(mapa, from, modo)) return true;</c>.
	///
	/// Fechar so o `Advance` deixaria o cliente honesto parando e o servidor aceitando qualquer coisa
	/// que um cliente MODIFICADO afirmasse -- e o lago inteiro atravessado a velocidade cheia.
	/// </summary>
	private static bool ValidarComEscapeQueAprovaTudo(Vec2 de, Vec2 ate, ZoneCollision m, ModoDeTravessia modo)
	{
		if (MoveRules.Occupied(m, de, modo)) return true;
		float o = 0f;
		return MoveRules.ValidateStep(de, ate, 1f / 30, 1f, m, ref o, out _, false, modo);
	}

	/// <summary>
	/// A GUARDA ANTI-TREMOR ENGOLINDO A REGRA -- a folga do escape de volta aos
	/// <see cref="MoveRules.MinCorrectionPx"/> (6 px).
	///
	/// **Foi a primeira versao do conserto, e a bancada pegou.** Um passo de UM QUADRO a 160 px/s tem
	/// 2,7 px: com 6 px de folga tudo passa, inclusive o sentido CONTRARIO ao refugio, e a 30 pacotes
	/// por segundo isso e 81 px/s de caminhada livre por cima do lago.
	/// </summary>
	private static bool ValidarComAFolgaAntiTremor(Vec2 de, Vec2 ate, ZoneCollision m, ModoDeTravessia modo)
	{
		if (MoveRules.Occupied(m, de, modo))
		{
			bool parado = (ate - de).Length <= MoveRules.MinCorrectionPx;   // <- a folga folgada
			MoveRules.Escape e = MoveRules.Escapar(m, de, modo, out Vec2 refugio);
			if (e == MoveRules.Escape.SemRefugio) return true;
			if (e == MoveRules.Escape.Dirigido)
				return parado || MoveRules.Aproxima(de, ate, refugio, MoveRules.MinCorrectionPx);
			if (parado) return true;
		}
		float o = 0f;
		return MoveRules.ValidateStep(de, ate, 1f / 30, 1f, m, ref o, out _, false, modo);
	}

	/// <summary>"A AGUA ENTROU NO `.vis`" -- o `Density = true` no `DmTurfScanner`.</summary>
	private static void AguaVaiCegar(Cenario c)
	{
		for (int y = 0; y < c.Col.Height; y++)
			for (int x = 0; x < c.Col.Width; x++)
				if (c.Col.EhAgua(x, y)) c.Vis.Bloquear(x, y);
	}

	/// <summary>Monta o cenario e deixa a injecao mexer nele.</summary>
	private static Cenario Montar(Regras r)
	{
		int w = Cenario.Lado, h = Cenario.Lado;
		int bytes = (w * h + 7) / 8;
		var col = new byte[bytes];
		var agua = new byte[bytes];
		var vis = new byte[bytes];
		var lagoParede = new byte[bytes];

		for (int y = 0; y < h; y++)
		{
			for (int x = Cenario.LagoX0; x <= Cenario.LagoX1; x++)
			{
				Marcar(agua, y * w + x);
				Marcar(lagoParede, y * w + x);   // o controle: o mesmo lago, como parede
			}
			// o muro de verdade: bloqueia (col) E cega (vis), nos tres mapas
			Marcar(col, y * w + Cenario.MuroX);
			Marcar(vis, y * w + Cenario.MuroX);
			Marcar(lagoParede, y * w + Cenario.MuroX);
		}

		ZoneCollision mapa = ZoneCollision.Montar(w, h, col);
		mapa.DefinirAgua(agua);

		var c = new Cenario
		{
			Col = mapa,
			Vis = ZoneCollision.Montar(w, h, vis),
			LagoComoParede = ZoneCollision.Montar(w, h, lagoParede),
			Vazio = ZoneCollision.Montar(w, h, new byte[bytes]),
		};
		r.MexerNoMapa(c);
		return c;

		static void Marcar(byte[] b, int i) => b[i >> 3] |= (byte)(1 << (i & 7));
	}

	// ==================================================================================
	// O MAPA DE VERDADE: a Terra, com 44% de agua
	// ==================================================================================

	/// <summary>
	/// O DADO DE PRODUCAO. Cenario sintetico prova a REGRA; so o mapa de verdade prova que a regra
	/// foi APLICADA ao mundo em que se joga -- sao 110 mil celulas de agua escritas por um conversor
	/// que pode ter marcado a coisa errada.
	///
	/// Os bytes ficam em cache e cada rodada faz um `Load` NOVO: as injecoes mexem no objeto (a
	/// camada de obras do `Bloquear`), e um objeto compartilhado levaria o defeito de uma familia
	/// pra dentro da seguinte.
	/// </summary>
	private sealed class Terra
	{
		public required byte[] BytesCol { get; init; }
		public required byte[] BytesAgua { get; init; }
		public required byte[] BytesVis { get; init; }

		/// <summary>Uma travessia de verdade: seco, agua estreita o bastante pra atravessar, seco.</summary>
		public int TravessiaX, TravessiaY, TravessiaLargura;

		public (ZoneCollision col, ZoneCollision vis) Abrir()
		{
			ZoneCollision col = ZoneCollision.Load(BytesCol)!;
			col.CarregarAgua(BytesAgua);
			return (col, ZoneCollision.Load(BytesVis)!);
		}
	}

	private static Terra? _terra;

	private static Terra? CarregarTerra(string pastaMaps)
	{
		string col = Path.Combine(pastaMaps, "z01_Earth.col");
		string agua = Path.Combine(pastaMaps, "z01_Earth.agua");
		string vis = Path.Combine(pastaMaps, "z01_Earth.vis");
		if (!File.Exists(col) || !File.Exists(agua) || !File.Exists(vis)) return null;

		var t = new Terra
		{
			BytesCol = File.ReadAllBytes(col),
			BytesAgua = File.ReadAllBytes(agua),
			BytesVis = File.ReadAllBytes(vis),
		};

		// ACHA UM RIO DE VERDADE: uma faixa horizontal com terra seca dos dois lados. E o unico
		// jeito de a prova "atravessou nadando" ter significado no mapa real -- num oceano de 200
		// tiles ninguem atravessa em 10 segundos, e a prova ficaria vermelha estando tudo certo.
		(ZoneCollision mapa, _) = t.Abrir();
		for (int y = 4; y < mapa.Height - 4 && t.TravessiaLargura == 0; y++)
			for (int x = 8; x < mapa.Width - 48; x++)
			{
				if (!mapa.EhAgua(x, y) || mapa.EhAgua(x - 1, y)) continue;   // so o comeco do rio
				int n = 0;
				while (n < 24 && mapa.EhAgua(x + n, y)) n++;
				if (n < 3 || mapa.EhAgua(x + n, y)) continue;                // largo demais: pula
				if (!SecoELivre(mapa, x - 6, x - 1, y) || !SecoELivre(mapa, x + n, x + n + 7, y)) continue;
				t.TravessiaX = x; t.TravessiaY = y; t.TravessiaLargura = n;
				break;
			}
		return t;

		static bool SecoELivre(ZoneCollision m, int x0, int x1, int y)
		{
			for (int x = x0; x <= x1; x++)
				if (m.EhAgua(x, y) || m.BlockedCell(x, y) || m.EhAgua(x, y + 1) || m.BlockedCell(x, y + 1))
					return false;
			return true;
		}
	}

	// ==================================================================================
	// O MOTOR: familias, placar e injecao
	// ==================================================================================

	private sealed class Placar
	{
		public int Ok, Falhas;
		public bool Mudo;
		public readonly List<string> Vermelhas = [];

		public void Prova(string oQue, bool passou)
		{
			if (passou) Ok++; else { Falhas++; Vermelhas.Add(oQue); }
			if (!Mudo) Console.WriteLine($"  [{(passou ? "ok  " : "FALHA")}] {oQue}");
		}

		/// <summary>Prova que nao deu pra fazer (falta o mapa de verdade, falta o fonte).</summary>
		public readonly List<string> SemCobertura = [];
		public void NaoDeu(string oQue)
		{
			SemCobertura.Add(oQue);
			if (!Mudo) Console.WriteLine($"  [ -- ] {oQue}");
		}
	}

	private sealed class Familia
	{
		public required string Nome { get; init; }
		public required string Frase { get; init; }
		public required Action<Regras, Placar> Provas { get; init; }
		public required List<(string Nome, Action<Regras> Injetar)> Defeitos { get; init; }
	}

	public static int Run(string pastaMaps, string raiz)
	{
		_terra = CarregarTerra(pastaMaps);
		Console.WriteLine("============================================================");
		Console.WriteLine(" BANCADA DA AGUA -- uma familia por negacao do pedido");
		Console.WriteLine("============================================================");
		Console.WriteLine(_terra == null
			? $"  mapa de verdade: NAO ENCONTRADO em {pastaMaps} (as provas de dado real ficam de fora)"
			: $"  mapa de verdade: z01_Earth | travessia achada em ({_terra.TravessiaX},{_terra.TravessiaY}), "
			  + $"{_terra.TravessiaLargura} tiles de agua");
		Console.WriteLine($"  fonte do nado    : {(File.Exists(FonteDoNado(raiz)) ? FonteDoNado(raiz) : "NAO ENCONTRADO (prova estrutural fica de fora)")}");
		Console.WriteLine($"  fonte do voo     : {(File.Exists(FonteDoVoo(raiz)) ? FonteDoVoo(raiz) : "NAO ENCONTRADO (prova estrutural fica de fora)")}");

		List<Familia> familias = [
			APeNaoAtravessa(),
			NadandoEVoandoAtravessam(),
			NaoDaPraSocar(),
			NaoFazSombra(),
			NadarNaoSobeNemFazSombra(),
			MesmaSementeMesmoMundo(),
			IaNaoTrava(),
			// AS TRES DO RELATO B -- a saida de emergencia. Elas vem depois porque dependem das
			// anteriores: "zero pixel" so significa alguma coisa se "nadando atravessa" (familia 2)
			// estiver verde ao lado.
			SemKiNaAguaNaoAnda(),
			NascidoNaPedraSai(),
			ABeiraNaoCongela(),
		];

		int provas = 0, falhas = 0, defeitos = 0, cegos = 0, semCobertura = 0;
		var buracos = new List<string>();

		foreach (Familia f in familias)
		{
			Console.WriteLine($"\n=== {f.Nome} ===");
			Console.WriteLine($"    \"{f.Frase}\"");

			var sao = new Placar();
			f.Provas(new Regras(), sao);
			provas += sao.Ok + sao.Falhas;
			falhas += sao.Falhas;
			semCobertura += sao.SemCobertura.Count;
			foreach (string s in sao.SemCobertura) buracos.Add($"{f.Nome}: {s}");

			Console.WriteLine("  -- e ela reprova assim:");
			foreach ((string nome, Action<Regras> injetar) in f.Defeitos)
			{
				defeitos++;
				var r = new Regras();
				injetar(r);
				var p = new Placar { Mudo = true };
				try { f.Provas(r, p); }
				catch (Exception e)
				{
					// UM DEFEITO QUE FAZ A BANCADA ESTOURAR TAMBEM E UM DEFEITO PEGO -- so nao pode
					// passar despercebido, entao ele sai com o nome da excecao.
					p.Prova($"estourou: {e.GetType().Name}", false);
				}

				if (p.Falhas == 0)
				{
					cegos++;
					Console.WriteLine($"     [CEGA] {nome}");
					Console.WriteLine("            ...a familia continuou VERDE com o defeito dentro.");
					buracos.Add($"{f.Nome}: cega para \"{nome}\"");
				}
				else
				{
					Console.WriteLine($"     [pega] {nome}");
					Console.WriteLine($"            -> {p.Falhas} prova(s) em vermelho, a primeira: \"{Curto(p.Vermelhas[0])}\"");
				}
			}
		}

		Console.WriteLine("\n============================================================");
		Console.WriteLine(" PLACAR");
		Console.WriteLine("============================================================");
		Console.WriteLine($"  familias           : {familias.Count}");
		Console.WriteLine($"  provas             : {provas}   ({provas - falhas} verdes, {falhas} vermelhas)");
		Console.WriteLine($"  defeitos injetados : {defeitos}   ({defeitos - cegos} pegos, {cegos} passaram batido)");
		Console.WriteLine($"  provas sem rodar   : {semCobertura}");
		if (buracos.Count > 0)
		{
			Console.WriteLine("\n  O QUE FICOU SEM COBERTURA:");
			foreach (string b in buracos) Console.WriteLine($"    - {b}");
		}
		bool ok = falhas == 0 && cegos == 0;
		Console.WriteLine($"\n  {(ok ? "OK -- toda familia esta verde E sabe ficar vermelha."
							   : "ATENCAO -- ha familia vermelha ou cega acima.")}");
		return ok ? 0 : 1;
	}

	private static string Curto(string s) => s.Length <= 72 ? s : s[..70] + "..";

	// ==================================================================================
	// FAMILIA 1 -- A PE NAO ATRAVESSA
	// ==================================================================================

	/// <summary>
	/// O ITEM LITERAL DO PEDIDO: *"todo TILE DE AGUA n da pra passar andando"*.
	///
	/// As tres provas sao os tres lugares onde "andando" e decidido, e eles nao sao o mesmo codigo:
	/// o PASSO do cliente (`Advance`), a PERGUNTA seca (`Occupied`) e a VALIDACAO do servidor
	/// (`ValidateStep`). Uma so das tres passar seria o pior desfecho deste projeto -- o cliente e o
	/// servidor discordando sobre onde da pra andar, com o corpo tremendo na costura.
	/// </summary>
	private static Familia APeNaoAtravessa() => new()
	{
		Nome = "FAMILIA 1 -- A PE NAO ATRAVESSA",
		Frase = "n da pra passar andando",
		Provas = (r, p) =>
		{
			Cenario c = Montar(r);

			// ---- o passo: anda 10 s de leste contra o lago
			Caminhada a = Andar(r, c.Col, Cenario.Oeste, new Vec2(1, 0), ModoDeTravessia.APe, 600);
			p.Prova($"a pe o corpo PARA na beira (x={a.Fim.X:0}, o lago comeca em {Cenario.LagoX0 * 32})",
					a.Fim.X < Cenario.LagoX0 * 32);
			p.Prova("...e em nenhum quadro os pes entraram na agua", a.PxNaAgua == 0);

			// ---- a pergunta seca
			p.Prova("`Occupied` a pe devolve BLOQUEADO dentro do lago",
					r.Para(c.Col, Cenario.Dentro, ModoDeTravessia.APe));

			// ---- a validacao do servidor: o cliente afirma um passo pra dentro da agua
			Vec2 beira = a.Fim;
			p.Prova("o SERVIDOR recusa o passo que o cliente afirmar por cima da agua",
					!r.Valida(beira, beira + new Vec2(6, 0), c.Col, ModoDeTravessia.APe));

			// ---- e no mapa de verdade
			if (_terra is not { TravessiaLargura: > 0 } t) { p.NaoDeu("o mesmo, no rio de verdade da Terra"); return; }
			(ZoneCollision terra, _) = t.Abrir();
			r.MexerNoMapa(new Cenario { Col = terra, Vis = terra, LagoComoParede = terra, Vazio = terra });

			Vec2 partida = Cenario.NoTile(t.TravessiaX - 4, t.TravessiaY);
			Caminhada b = Andar(r, terra, partida, new Vec2(1, 0), ModoDeTravessia.APe, 600);
			p.Prova($"no rio de verdade da Terra ({t.TravessiaLargura} tiles), a pe ele NAO atravessa",
					b.PxNaAgua == 0 && b.Fim.X < t.TravessiaX * 32);
		},
		Defeitos = [
			("o `.agua` nao foi carregado (plano nulo -- o `.col` abre, a agua some)",
			 r => r.MexerNoMapa = c => c.Col.DefinirAgua(null)),

			("o chamador passou `Voando` por engano (o modo errado nos tres funis)",
			 r => { r.Para = (m, v, _) => MoveRules.Occupied(m, v, ModoDeTravessia.Voando);
					r.Passo = (pos, d, dt, v, m, ignorado) =>
						MoveRules.Advance(pos, d, dt, v, m, out _, false, ModoDeTravessia.Voando);
					r.Valida = (de, ate, m, ignorado) => { float o = 0f;
						return MoveRules.ValidateStep(de, ate, 1f / 30, 1f, m, ref o, out Vec2 _, false,
													  ModoDeTravessia.Voando); }; }),

			("a agua foi gravada no mapa que CEGA e nao no que PARA (os arquivos trocados)",
			 r => r.MexerNoMapa = c => { AguaVaiCegar(c); c.Col.DefinirAgua(null); }),
		],
	};

	// ==================================================================================
	// FAMILIA 2 -- NADANDO ATRAVESSA, VOANDO ATRAVESSA
	// ==================================================================================

	/// <summary>
	/// OS DOIS CONTRA-EXEMPLOS DO PEDIDO: *"so NADANDO ou usando o FLIGHT"*.
	///
	/// Sem eles, "agua e parede" passaria verde na familia 1 inteira -- e parede foi exatamente o
	/// que o dono disse que ela NAO e. Aqui tambem mora a prova que impede o inverso: nadando, a
	/// PAREDE continua parando. Sem ela, "nadar" seria so um nome pra voar.
	/// </summary>
	private static Familia NadandoEVoandoAtravessam() => new()
	{
		Nome = "FAMILIA 2 -- NADANDO ATRAVESSA, VOANDO ATRAVESSA",
		Frase = "so NADANDO ou usando o FLIGHT",
		Provas = (r, p) =>
		{
			Cenario c = Montar(r);

			// ---- NADANDO: atravessa o lago... e para no muro que vem depois
			Caminhada n = Andar(r, c.Col, Cenario.Oeste, new Vec2(1, 0), ModoDeTravessia.Nadando, 600);
			p.Prova($"NADANDO ele atravessa o lago (chegou a x={n.Fim.X:0}, o lago acaba em {(Cenario.LagoX1 + 1) * 32})",
					n.Fim.X > (Cenario.LagoX1 + 1) * 32);
			p.Prova($"...e passou POR CIMA da agua de verdade ({n.PxNaAgua:0} px com os pes nela)",
					n.PxNaAgua > ZoneCollision.TileSize);
			p.Prova($"...e a PAREDE continua parando quem nada (parou em x={n.Fim.X:0} < {Cenario.MuroX * 32})",
					n.Fim.X < Cenario.MuroX * 32);

			// ---- VOANDO BAIXO: a janela da decolagem, em que o corpo AINDA consulta o mapa
			Caminhada v = Andar(r, c.Col, Cenario.Oeste, new Vec2(1, 0), ModoDeTravessia.Voando, 600);
			p.Prova("VOANDO (ainda na janela da decolagem, com mapa) ele atravessa",
					v.Fim.X > (Cenario.LagoX1 + 1) * 32);
			p.Prova("...e a parede tambem para quem voa baixo (so quem passa de 1 tile ignora o mapa)",
					v.Fim.X < Cenario.MuroX * 32);

			// ---- VOANDO ALTO: nem ha mapa (o `isflying` do original manda `mapa = null`)
			p.Prova("voando ALTO nao ha mapa nenhum a consultar (`AtravessaCenario`)",
					Voo.AtravessaCenario(Voo.AlturaQueAtravessa)
					&& r.Passo(Cenario.Dentro, new Vec2(1, 0), 1f / 60, 1f, null, ModoDeTravessia.Voando).X > Cenario.Dentro.X);

			// ---- ARREMESSADO: o `M.KB` do `testWaters`
			Caminhada k = Andar(r, c.Col, Cenario.Oeste, new Vec2(1, 0), ModoDeTravessia.Arremessado, 600);
			p.Prova("ARREMESSADO atravessa (o `M.KB` do `Swim.dm:31`)", k.Fim.X > (Cenario.LagoX1 + 1) * 32);

			// ---- o modo, que e a origem de tudo isso
			p.Prova("o modo sai de UM dono so: a pe/nadando/no ar/arremessado",
					ClasseDeAgua.ModoDe(false, false, false) == ModoDeTravessia.APe
					&& ClasseDeAgua.ModoDe(false, false, true) == ModoDeTravessia.Nadando
					&& ClasseDeAgua.ModoDe(false, true, false) == ModoDeTravessia.Voando
					&& ClasseDeAgua.ModoDe(true, true, true) == ModoDeTravessia.Arremessado);

			// ============================ A PROVA ESTRUTURAL: O POUSO PERGUNTA COM O MODO ============================
			// Esta bancada ja sabia dizer "o chamador esqueceu o modo" -- o defeito esta injetado logo
			// abaixo desde a primeira versao. O que ela NAO sabia era APONTAR o chamador: as funcoes
			// aqui sao puras, e um chamador esquecido mora no servidor, que nao roda daqui.
			//
			// E havia um esquecido de verdade. `GameServer.Voo.cs`, no `DescerAte`, perguntava
			// `MoveRules.Occupied(mapa, pl.Pos)` sem modo -- ou seja, A PE --, e a pe a agua para:
			// quem largava o voo em cima do lago NADANDO caia no desvio do "pousou dentro da pedra",
			// era teleportado pra margem, e o tique seguinte apagava o nado por falta de agua embaixo.
			// Um dos dois caminhos que o jogador tem pra comecar a nadar, morto por um argumento
			// omitido.
			//
			// Ler o fonte e feio, e e o unico jeito honesto: nao ha funcao pura a que perguntar "o
			// pouso passou o modo?". A alternativa era uma COPIA da regra dentro da bancada, e copia
			// que concorda consigo mesma fica verde pra sempre.
			// =====================================================================================================
			string arqVoo = _fonteDoVoo;
			if (arqVoo.Length == 0 || !File.Exists(arqVoo)) p.NaoDeu("o `DescerAte` pergunta com o MODO");
			else
			{
				List<string> nuas = r.LerFonte(arqVoo).Split('\n')
					.Where(l => !l.TrimStart().StartsWith("//"))
					.Where(l => l.Contains("MoveRules.Occupied(mapa, pl.Pos)"))
					.Select(l => l.Trim()).ToList();
				p.Prova($"`{Path.GetFileName(arqVoo)}` nunca pergunta `Occupied` SEM o modo"
						+ (nuas.Count > 0 ? $" (achei: {Curto(nuas[0])})" : ""),
						nuas.Count == 0);
			}

			if (_terra is not { TravessiaLargura: > 0 } t) { p.NaoDeu("atravessar nadando o rio de verdade"); return; }
			(ZoneCollision terra, _) = t.Abrir();
			r.MexerNoMapa(new Cenario { Col = terra, Vis = terra, LagoComoParede = terra, Vazio = terra });
			Vec2 partida = Cenario.NoTile(t.TravessiaX - 4, t.TravessiaY);
			Caminhada b = Andar(r, terra, partida, new Vec2(1, 0), ModoDeTravessia.Nadando, 600);
			p.Prova($"no rio de verdade da Terra, NADANDO ele chega na outra margem "
					+ $"({b.PxNaAgua:0} px sobre agua, saiu em x={b.Fim.X / 32:0})",
					b.PxNaAgua > ZoneCollision.TileSize && b.Fim.X > (t.TravessiaX + t.TravessiaLargura) * 32);
		},
		Defeitos = [
			("a agua foi marcada no `.col` (\"e tipo uma parede\") -- e ai nem quem nada passa",
			 r => r.MexerNoMapa = AguaViraParede),

			("o chamador esqueceu o modo (o `Occupied` sem argumento -- foi o defeito do `DescerAte`)",
			 r => { r.Para = (m, v, _) => MoveRules.Occupied(m, v);
					r.Passo = (pos, d, dt, v, m, ignorado) => MoveRules.Advance(pos, d, dt, v, m, out _); }),

			("o `DescerAte` VOLTOU a perguntar sem o modo (o pouso do nadador vira desvio pra margem)",
			 r => { Func<string, string> ler = r.LerFonte;
					r.LerFonte = a => ler(a)
						+ "\n\t\tif (MoveRules.Occupied(mapa, pl.Pos)) DesviarProLado(pl);\n"; }),

			("`ClasseDeAgua.Bloqueia` passou a valer pra todo modo (a excecao virou regra)",
			 r => { r.Para = (m, v, _) => MoveRules.Occupied(m, v, ModoDeTravessia.APe);
					r.Passo = (pos, d, dt, v, m, ignorado) =>
						MoveRules.Advance(pos, d, dt, v, m, out _, false, ModoDeTravessia.APe); }),
		],
	};

	// ==================================================================================
	// FAMILIA 3 -- NAO DA PRA SOCAR
	// ==================================================================================

	/// <summary>
	/// *"ela e tipo uma parede, mas N DA PRA SOCAR (igual o chao)"*.
	///
	/// O CONTRA-EXEMPLO E OBRIGATORIO AQUI, e mais do que em qualquer outra familia: "nada quebra"
	/// satisfaz "agua nao quebra" perfeitamente, e um soco que parasse de alcancar QUALQUER cenario
	/// deixaria o destrutivel inteiro morto com a bancada verde. Entao toda prova de negacao vem em
	/// par com a mesma pergunta feita a uma parede de verdade.
	/// </summary>
	private static Familia NaoDaPraSocar() => new()
	{
		Nome = "FAMILIA 3 -- NAO DA PRA SOCAR",
		Frase = "N DA PRA SOCAR (igual o chao)",
		Provas = (r, p) =>
		{
			Cenario c = Montar(r);

			p.Prova("o punho NAO alcanca a celula de agua", !r.SocoAlcanca(c.Col, 19, Cenario.Linha));
			p.Prova("...mas alcanca a PAREDE de verdade ao lado (senao 'nada quebra' passaria verde)",
					r.SocoAlcanca(c.Col, Cenario.MuroX, Cenario.Linha));
			p.Prova("...e nao alcanca chao livre nenhum", !r.SocoAlcanca(c.Col, 5, Cenario.Linha));

			// SOCAR NAO MUDA NADA: a celula continua sendo agua depois da pergunta. O soco de
			// verdade so chama `DerrubarCelula` quando esta funcao diz sim -- entao "nada mudou" e
			// exatamente "ela disse nao".
			bool antes = c.Col.EhAgua(19, Cenario.Linha);
			for (int i = 0; i < 8; i++) r.SocoAlcanca(c.Col, 19, Cenario.Linha);
			p.Prova("depois de 8 socos a celula continua sendo agua (nada foi derrubado)",
					antes && c.Col.EhAgua(19, Cenario.Linha));

			if (_terra == null) { p.NaoDeu("as 110 mil celulas de agua da Terra, uma a uma"); return; }
			(ZoneCollision terra, _) = _terra.Abrir();
			r.MexerNoMapa(new Cenario { Col = terra, Vis = terra, LagoComoParede = terra, Vazio = terra });

			int agua = 0, aguaSocavel = 0, parede = 0, paredeSocavel = 0;
			for (int y = 0; y < terra.Height; y++)
				for (int x = 0; x < terra.Width; x++)
				{
					if (terra.EhAgua(x, y)) { agua++; if (r.SocoAlcanca(terra, x, y)) aguaSocavel++; }
					else if (terra.BlockedCell(x, y)) { parede++; if (r.SocoAlcanca(terra, x, y)) paredeSocavel++; }
				}
			p.Prova($"na Terra, {aguaSocavel} das {agua:N0} celulas de agua sao socaveis", aguaSocavel == 0);
			p.Prova($"...e {paredeSocavel:N0} das {parede:N0} paredes SAO (o destrutivel esta vivo)",
					paredeSocavel == parede && parede > 0);
		},
		Defeitos = [
			("o soco voltou a olhar so o bitset, e alguem marcou agua no `.col`",
			 r => { r.SocoAlcanca = (m, x, y) => m.BlockedCell(x, y);
					r.MexerNoMapa = AguaViraParede; }),

			("alguem achou que agua devia ceder ao soco (`Destrutivel` virou true)",
			 r => r.SocoAlcanca = (m, x, y) => m.EhAgua(x, y) || m.BlockedCell(x, y)),

			("o soco parou de alcancar QUALQUER cenario (o destrutivel morreu calado)",
			 r => r.SocoAlcanca = (_, _, _) => false),
		],
	};

	// ==================================================================================
	// FAMILIA 4 -- NAO FAZ SOMBRA
	// ==================================================================================

	/// <summary>
	/// *"n tem SOMBRA nem nada disso"* -- e aqui "sombra" e OCLUSAO DE VISTA, o leque preto que uma
	/// parede projeta. (A outra sombra, a mancha embaixo do corpo, e a familia 5.)
	///
	/// A resposta sai de graca e vale dizer por que: o `.vis` e um arquivo separado do `.col` e so
	/// recebe celula com `density` no DM. Agua tem `density = 0`, entao ela nunca entrou. "De graca"
	/// e justamente o que precisa de prova -- ninguem escreveu uma linha pra isso acontecer, e
	/// ninguem vai escrever uma linha no dia em que parar de acontecer.
	/// </summary>
	private static Familia NaoFazSombra() => new()
	{
		Nome = "FAMILIA 4 -- NAO FAZ SOMBRA (a agua nao cega)",
		Frase = "n tem SOMBRA nem nada disso",
		Provas = (r, p) =>
		{
			Cenario c = Montar(r);

			p.Prova("quem esta na outra margem do lago E VISTO", r.Ve(c.Vis, Cenario.Oeste, Cenario.Leste));
			p.Prova("...mas quem esta atras da PAREDE nao e (senao 'tudo se ve' passaria verde)",
					!r.Ve(c.Vis, Cenario.Leste, Cenario.DepoisDoMuro));
			p.Prova("a classe declara que agua nao cega", !ClasseDeAgua.Cega);

			if (_terra == null) { p.NaoDeu("as celulas de agua da Terra dentro do `.vis` de verdade"); return; }
			(ZoneCollision terra, ZoneCollision vis) = _terra.Abrir();
			r.MexerNoMapa(new Cenario { Col = terra, Vis = vis, LagoComoParede = terra, Vazio = terra });

			int agua = 0, aguaCega = 0, cegas = 0;
			for (int y = 0; y < terra.Height; y++)
				for (int x = 0; x < terra.Width; x++)
				{
					if (vis.BlockedCell(x, y)) cegas++;
					if (!terra.EhAgua(x, y)) continue;
					agua++;
					if (vis.BlockedCell(x, y)) aguaCega++;
				}
			p.Prova($"na Terra, {aguaCega} das {agua:N0} celulas de agua entram no `.vis`", aguaCega == 0);
			p.Prova($"...e o `.vis` nao esta vazio: {cegas:N0} celulas cegam de verdade", cegas > 0);

			if (_terra is not { TravessiaLargura: > 0 } t) { p.NaoDeu("ver o outro lado do rio de verdade"); return; }
			p.Prova($"no rio de verdade ({t.TravessiaLargura} tiles), as duas margens se veem",
					r.Ve(vis, Cenario.NoTile(t.TravessiaX - 2, t.TravessiaY),
						 Cenario.NoTile(t.TravessiaX + t.TravessiaLargura + 1, t.TravessiaY)));
		},
		Defeitos = [
			("a agua entrou no `.vis` (o `Density = true` no `DmTurfScanner`)",
			 r => r.MexerNoMapa = AguaVaiCegar),

			("ninguem pergunta ao `.vis` (a vista atravessa tudo -- so o contra-exemplo pega)",
			 r => r.Ve = (_, _, _) => true),

			("a vista passou a esconder tudo (o leque preto virou o mundo inteiro)",
			 r => r.Ve = (_, _, _) => false),
		],
	};

	// ==================================================================================
	// FAMILIA 5 -- NADAR NAO SOBE E NAO FAZ SOMBRA
	// ==================================================================================

	/// <summary>
	/// *"uma animacao PARECIDA COM A DO FLY, porem N CRIA A SOMBRA em baixo e NEM FICA MAIS ALTO"*.
	///
	/// ============================ ESTA FAMILIA SO EXISTE EM PAR ============================
	/// "Nadar nao sobe e nao faz sombra" e satisfeito perfeitamente por um jogo em que NINGUEM sobe e
	/// NINGUEM faz sombra -- ou seja, pelo voo quebrado. As duas linhas juntas (o nado nao, o voo
	/// sim) sao a unica prova de que as duas poses sao coisas diferentes na tela; separadas, cada uma
	/// e metade de uma frase.
	///
	/// A PROVA ESTRUTURAL fecha o que o resto nao alcanca. "Nadar produz altura ZERO" e uma
	/// propriedade do SERVIDOR (`GameServer.Nado.cs` simplesmente nunca escreve `Altitude`), e nao
	/// ha funcao pura pra perguntar isso -- entao a bancada le o fonte e exige que continue assim. E
	/// literal: o dia em que alguem "melhorar" o nado com um `pl.Altitude = 4f` (a tentacao e obvia,
	/// "so pra dar um flutuadinho"), a sombra volta e o item do pedido morre.
	/// =====================================================================================
	/// </summary>
	private static Familia NadarNaoSobeNemFazSombra() => new()
	{
		Nome = "FAMILIA 5 -- NADAR NAO SOBE E NAO FAZ SOMBRA (o voo faz os dois)",
		Frase = "N CRIA A SOMBRA em baixo e NEM FICA MAIS ALTO",
		Provas = (r, p) =>
		{
			float nado = r.AlturaDoNado;
			float voo = Voo.AlturaDePairar;

			p.Prova($"NADANDO nao ha sombra (altura {nado:0.##} px)", !r.TemSombra(nado));
			p.Prova("...e VOANDO ha (senao 'ninguem faz sombra' passaria verde)", r.TemSombra(voo));

			p.Prova($"NADANDO o desenho nao sobe ({nado * Voo.EscalaNaTela:0.##} px na tela)",
					nado * Voo.EscalaNaTela == 0f);
			p.Prova($"...e VOANDO sobe ({voo * Voo.EscalaNaTela:0} px)", voo * Voo.EscalaNaTela > 1f);

			p.Prova("NADANDO o corpo fica no andar do chao", Voo.Andar(nado) == 0);
			p.Prova("...e VOANDO sai dele", Voo.Andar(voo) >= 1);

			// O ANDAR NAO E DETALHE DE DESENHO: e quem alcanca quem. Nadar tem que continuar sendo
			// alcancavel por quem esta em pe na margem -- e voar rasante, nao (a assimetria do `PodeAcertar`).
			p.Prova("quem nada e alcancado por quem esta em pe no chao (mesmo andar)",
					Voo.PodeAcertar(Voo.Andar(nado), 0) && Voo.PodeAcertar(0, Voo.Andar(nado)));
			p.Prova("...e quem voa rasante NAO (e a vantagem de voar -- as duas poses diferem)",
					Voo.PodeAcertar(Voo.Andar(voo), 0) && !Voo.PodeAcertar(0, Voo.Andar(voo)));

			// ---- a prova estrutural
			string arq = _fonteDoNado;
			if (arq.Length == 0 || !File.Exists(arq)) { p.NaoDeu("o fonte do nado nao escreve `Altitude`"); return; }
			string fonte = r.LerFonte(arq);
			string[] linhas = fonte.Split('\n');
			var escritas = linhas.Where(l => !l.TrimStart().StartsWith("//"))
								 .Where(l => l.Contains("Altitude") &&
											 (l.Contains("Altitude =") || l.Contains("Altitude +=")
											  || l.Contains("Altitude -=")))
								 .Select(l => l.Trim()).ToList();
			p.Prova($"`{Path.GetFileName(arq)}` nao escreve altitude em nenhuma linha"
					+ (escritas.Count > 0 ? $" (achei: {Curto(escritas[0])})" : ""),
					escritas.Count == 0);
		},
		Defeitos = [
			("nadar virou \"voo rente ao chao\" (alguem deu 8 px de altura pro nado)",
			 r => r.AlturaDoNado = 8f),

			("a sombra parou de nascer pra todo mundo (o voo perdeu a dele tambem)",
			 r => r.TemSombra = _ => false),

			("a sombra passou a nascer sempre (o nado ganhou a mancha embaixo)",
			 r => r.TemSombra = _ => true),

			("alguem pos um `pl.Altitude = 4f` no tique do nado (\"so um flutuadinho\")",
			 r => { Func<string, string> ler = r.LerFonte;
					r.LerFonte = a => ler(a) + "\n\t\tpl.Altitude = 4f;   // flutuadinho\n"; }),
		],
	};

	// ==================================================================================
	// FAMILIA 6 -- MESMA SEMENTE, MESMO MUNDO
	// ==================================================================================

	/// <summary>
	/// A agua dos planetas gerados nasce no MESMO laco que a parede, e a busca da clareira passou a
	/// recusar agua -- ou seja, a mudanca entrou no caminho que decide ONDE SE POUSA. Mundo que se
	/// reescreve entre duas geracoes iguais e um mundo que muda debaixo de quem esta nele: o cliente
	/// e o servidor geram cada um o seu a partir da semente e nunca trocam um byte de mapa.
	///
	/// O CONTRA-EXEMPLO AQUI E O MAIS FACIL DE ESQUECER: um gerador que devolvesse SEMPRE o mesmo
	/// mundo, ignorando a semente, passa em "mesma semente = mesmo mundo" com nota maxima.
	/// </summary>
	private static Familia MesmaSementeMesmoMundo() => new()
	{
		Nome = "FAMILIA 6 -- MESMA SEMENTE, MESMO MUNDO",
		Frase = "o terreno gerado sai igual na mesma semente, depois da agua",
		Provas = (r, p) =>
		{
			BiomaDeTerreno[] biomas = [BiomaDeTerreno.Jardim, BiomaDeTerreno.Gelado];
			int divergiu = 0, iguaisEntreSementes = 0, noMar = 0, mundos = 0, comAgua = 0;
			ulong primeira = 0;

			foreach (BiomaDeTerreno bioma in biomas)
				for (int i = 0; i < 8; i++)
				{
					ParametrosDeTerreno par = Params((ulong)(0x4A6UL + (ulong)i * 104729), bioma);

					TerrenoGerado a = r.Gerar(par);
					TerrenoGerado b = r.Gerar(Params(par.Seed, bioma));   // objeto NOVO, mesma semente
					mundos++;

					if (a.Assinatura() != b.Assinatura()) divergiu++;
					else if (!Iguais(a.BytesDeAgua, b.BytesDeAgua)) divergiu++;
					else if (a.SpawnCelX != b.SpawnCelX || a.SpawnCelY != b.SpawnCelY) divergiu++;

					if (i == 0 && bioma == biomas[0]) primeira = a.Assinatura();
					else if (a.Assinatura() == primeira) iguaisEntreSementes++;

					if (a.Colisao.EhAgua(a.SpawnCelX, a.SpawnCelY)) noMar++;
					if (a.BytesDeAgua.Any(v => v != 0)) comAgua++;
				}

			p.Prova($"mesma semente = mesmo mundo, mesma agua e mesmo pouso ({mundos} mundos, {divergiu} divergencia(s))",
					divergiu == 0);
			p.Prova($"...e sementes DIFERENTES dao mundos diferentes ({iguaisEntreSementes} repetido(s))",
					iguaisEntreSementes == 0);
			p.Prova($"...e {comAgua} dos {mundos} mundos tem agua de verdade (senao nada disso mediu agua)",
					comAgua > 0);
			p.Prova($"o gerador nunca manda pousar dentro do mar ({noMar} caso(s))", noMar == 0);

			// ============================ O OUTRO CAMINHO DE POUSO, E E ELE QUE DA PRA REPROVAR ============================
			// Sao DOIS os caminhos que poem um corpo no chao, e so este segundo passa por todo mundo:
			// o berco, o pouso de nave, a invasao e o povoamento chamam `PontoLivrePerto`, e foi nele
			// que a clausula de agua entrou.
			//
			// O PRIMEIRO -- o degrau 2 da clareira do gerador (`AcharOuAbrirClareira`, `exigente == 0`) --
			// FICA SEM COBERTURA, e vale dizer por que em vez de fingir que nao: ele so decide alguma
			// coisa num mundo que nao tenha NENHUMA planicie limpa no mapa inteiro, porque o degrau 1
			// varre o planeta todo antes dele e planicie nunca e agua. Injetar a regra antiga ali
			// deixava a familia VERDE (a bancada marcou `CEGA`), e um defeito que a bancada nao alcanca
			// tem que aparecer no placar, nao sumir. Os seis biomas sorteaveis todos produzem planicie.
			// ==========================================================================================================
			p.NaoDeu("o degrau 2 da clareira do gerador (so decide em mundo sem planicie limpa nenhuma)");
			TerrenoGerado mundo = r.Gerar(Params(0x4A6, BiomaDeTerreno.Jardim));
			if (AcharAgua(mundo.Colisao) is not { } lago) { p.NaoDeu("desviar de um lago no mundo gerado"); }
			else
			{
				Vec2 saida = r.PontoLivre(mundo.Colisao, Centro(lago));
				p.Prova($"pedindo pra por um corpo dentro de um lago do mundo gerado, o funil desvia "
						+ $"(pediu {lago}, deu ({Tile(saida)}))",
						!mundo.Colisao.EhAguaEm(saida) && !mundo.Colisao.BlockedAt(saida));
			}

			if (_terra == null) { p.NaoDeu("o mesmo, no oceano de verdade da Terra"); return; }
			(ZoneCollision terra, _) = _terra.Abrir();
			if (AcharAgua(terra) is not { } oceano) { p.NaoDeu("achar agua na Terra"); return; }
			Vec2 seco = r.PontoLivre(terra, Centro(oceano));
			p.Prova($"...e no oceano de verdade da Terra tambem (pediu {oceano}, deu ({Tile(seco)}))",
					!terra.EhAguaEm(seco) && !terra.BlockedAt(seco));
		},
		Defeitos = [
			("a semente ganhou uma parcela que muda entre geracoes (o classico do relogio)",
			 r => { int n = 0;
					r.Gerar = par => GeradorDeTerreno.Gerar(++n % 2 == 0
						? Params(par.Seed + 1, par.Bioma) : par); }),

			("o gerador passou a ignorar a semente (todo planeta igual)",
			 r => r.Gerar = par => GeradorDeTerreno.Gerar(Params(7, par.Bioma))),

			("o `PontoLivrePerto` voltou a aceitar \"qualquer celula que nao seja parede\" (a regra ANTIGA)",
			 r => r.PontoLivre = PontoLivreDaRegraAntiga),
		],
	};

	/// <summary>Uma celula de agua qualquer, longe da borda -- pra pedir um corpo dentro dela.</summary>
	private static (int X, int Y)? AcharAgua(ZoneCollision m)
	{
		for (int y = 8; y < m.Height - 8; y++)
			for (int x = 8; x < m.Width - 8; x++)
				if (m.EhAgua(x, y)) return (x, y);
		return null;
	}

	private static Vec2 Centro((int X, int Y) c) => new(c.X * 32 + 16, c.Y * 32 + 16);

	/// <summary>
	/// A CELULA de um ponto, pro relato. Com divisao INTEIRA: `{x/32:0}` arredonda, e o centro de um
	/// tile (x*32+16) cai exatamente no meio -- o relato dizia "pediu (103,8), deu (103,8)" com a
	/// resposta certa sendo a celula 102. Numero de relato que mente vale menos que numero nenhum.
	/// </summary>
	private static string Tile(Vec2 v) => $"{(int)MathF.Floor(v.X / 32)}, {(int)MathF.Floor(v.Y / 32)}";

	/// <summary>
	/// O `PontoLivrePerto` COMO ELE ERA ANTES DA AGUA: anel por anel, aceita a primeira celula que
	/// nao seja parede nem beirada. Nao e uma invencao pra ficar vermelha -- e o codigo que estava
	/// escrito ali, e o comentario do proprio `ZoneCollision:426` diz que a linha da agua entrou
	/// depois e por que.
	/// </summary>
	private static Vec2 PontoLivreDaRegraAntiga(ZoneCollision m, Vec2 desejado)
	{
		int cx = (int)MathF.Floor(desejado.X / 32), cy = (int)MathF.Floor(desejado.Y / 32);
		if (Serve(cx, cy)) return desejado;
		for (int r = 1; r <= 64; r++)
			for (int dx = -r; dx <= r; dx++)
				for (int dy = -r; dy <= r; dy++)
				{
					if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
					if (Serve(cx + dx, cy + dy)) return new Vec2((cx + dx) * 32 + 16, (cy + dy) * 32 + 16);
				}
		return desejado;

		bool Serve(int x, int y) => !m.BlockedCell(x, y) && !m.NaBorda(x, y);
	}

	private static ParametrosDeTerreno Params(ulong seed, BiomaDeTerreno bioma) => new()
	{
		Seed = seed,
		Largura = 192,
		Altura = 192,
		Bioma = bioma,
		Gravidade = 5,
		Nome = "bancada-agua",
	};

	private static bool Iguais(byte[] a, byte[] b)
	{
		if (a.Length != b.Length) return false;
		for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
		return true;
	}

	// ==================================================================================
	// FAMILIA 7 -- A IA NAO TRAVA
	// ==================================================================================

	/// <summary>
	/// ============================ O QUE "NAO TRAVA" QUER DIZER AQUI ============================
	/// Nao ha caminhamento nenhum na IA deste port -- nao ha A*, nao ha contorno, nao ha desencalhe
	/// (`GameServer.Ia.PassoDaIa`). O NPC anda no rumo direto do alvo e, contra obstaculo, desliza no
	/// eixo livre e para. Entao a afirmacao verificavel nao e "ele contorna o lago": e
	///
	///     A AGUA NAO E PIOR QUE O MURO QUE JA EXISTIA.
	///
	/// Por isso toda prova daqui e comparada com o MESMO mapa tendo o lago virado parede. Se a agua
	/// travasse de um jeito que a parede nao trava (o corpo vibrando entre dois estados, o passo
	/// virando NaN, o deslize sumindo), a comparacao abre.
	///
	/// E A OUTRA METADE: o NPC que VOA tem que passar por cima. Ele passa pela mesma funcao do
	/// jogador (`ModoDeTravessiaDe`), e o defeito classico -- "a IA ficou com um `if` proprio" -- e
	/// injetado abaixo.
	///
	/// ============================ O QUE ESTA BANCADA **NAO** ALCANCA ============================
	/// `PassoDaIa` mora no assembly do Godot e nao da pra chamar daqui. O que roda aqui sao as DUAS
	/// funcoes de producao que ele chama em sequencia (`ClasseDeAgua.ModoDe` e `MoveRules.Advance`),
	/// que hoje sao o passo inteiro dele -- nao ha mais nada entre elas. No dia em que houver, esta
	/// bancada deixa de cobrir a diferenca, e por isso isto esta escrito aqui e no relatorio.
	/// ==========================================================================================
	/// </summary>
	private static Familia IaNaoTrava() => new()
	{
		Nome = "FAMILIA 7 -- A IA NAO TRAVA CONTRA AGUA",
		Frase = "o lago se comporta como o muro que ja existia; e quem voa passa por cima",
		Provas = (r, p) =>
		{
			Cenario c = Montar(r);

			// ---- O NPC A PE, DE FRENTE: para na margem, e para IGUAL a como pararia num muro.
			ModoDeTravessia aPe = ClasseDeAgua.ModoDe(false, false, false);
			Caminhada naAgua = Andar(r, c.Col, Cenario.Oeste, new Vec2(1, 0), aPe, 600);
			Caminhada noMuro = Andar(r, c.LagoComoParede, Cenario.Oeste, new Vec2(1, 0), aPe, 600);
			p.Prova($"a pe o NPC para na margem, no MESMO ponto em que pararia num muro "
					+ $"(agua x={naAgua.Fim.X:0}, muro x={noMuro.Fim.X:0})",
					Math.Abs(naAgua.Fim.X - noMuro.Fim.X) < 1f);
			p.Prova("...e ele PARA de verdade (nao fica vibrando: o ultimo quadro nao andou)",
					naAgua.UltimoPasso < 0.01f && noMuro.UltimoPasso < 0.01f);

			// ---- O DESLIZE: e ele que evita o travamento de verdade. Em diagonal contra a margem,
			// o eixo X zera e o Y continua -- e o NPC segue andando rente ao lago em vez de congelar.
			//
			// A REGUA E O MAPA VAZIO, e nao um numero escolhido a dedo: indo na diagonal o corpo ja
			// anda em Y ate encostar, e so esse pedaco chega a 240 px -- que passaria por "deslizou"
			// se a prova pedisse "mais que 100". Comparado com o mundo sem obstaculo, o que se mede e
			// se ele CONTINUOU andando depois de o eixo X travar.
			Caminhada diagonal = Andar(r, c.Col, Cenario.Oeste, new Vec2(1, 1), aPe, 600);
			Caminhada diagMuro = Andar(r, c.LagoComoParede, Cenario.Oeste, new Vec2(1, 1), aPe, 600);
			Caminhada diagLivre = Andar(r, c.Vazio, Cenario.Oeste, new Vec2(1, 1), aPe, 600);
			float andouY = diagonal.Fim.Y - Cenario.Oeste.Y, livreY = diagLivre.Fim.Y - Cenario.Oeste.Y;
			p.Prova($"na diagonal ele DESLIZA pela margem em vez de travar "
					+ $"({andouY:0} px em Y, contra {livreY:0} px sem obstaculo nenhum)",
					livreY > 1f && andouY >= livreY * 0.95f);
			p.Prova("...e desliza exatamente como desliza num muro",
					Math.Abs(diagonal.Fim.Y - diagMuro.Fim.Y) < 1f);

			// ---- O NPC QUE VOA: passa por cima.
			ModoDeTravessia voando = ClasseDeAgua.ModoDe(false, true, false);
			Caminhada aereo = Andar(r, c.Col, Cenario.Oeste, new Vec2(1, 0), voando, 600);
			p.Prova($"o NPC que VOA atravessa o lago (chegou a x={aereo.Fim.X:0})",
					aereo.Fim.X > (Cenario.LagoX1 + 1) * 32);

			// ---- E NENHUM QUADRO PODRE: nem NaN, nem pe dentro da agua a pe.
			p.Prova("nenhum quadro com posicao invalida (NaN) em nenhuma das travessias",
					!naAgua.Podre && !diagonal.Podre && !aereo.Podre);
			p.Prova("e a pe ele nunca chegou a pisar na agua", naAgua.PxNaAgua == 0 && diagonal.PxNaAgua == 0);
		},
		Defeitos = [
			("a IA ganhou um `if` proprio e manda sempre `APe` (o NPC que voa trava no lago)",
			 r => { r.Passo = (pos, d, dt, v, m, ignorado) =>
						MoveRules.Advance(pos, d, dt, v, m, out _, false, ModoDeTravessia.APe); }),

			("o deslize sumiu do `Advance` (o passo passou a ser tudo-ou-nada)",
			 r => r.Passo = (pos, d, dt, v, m, modo) =>
			 {
				 Vec2 alvo = MoveRules.Integrate(pos, d, dt, v);
				 return m == null || !MoveRules.Occupied(m, alvo, modo) ? alvo : pos;
			 }),

			("a agua virou parede no `.col` (e ai nem o NPC voador passa)",
			 r => r.MexerNoMapa = AguaViraParede),
		],
	};

	// ==================================================================================
	// FAMILIA 8 -- SEM KI, NA AGUA: ZERO PIXEL, E ELE NAO FOI TELEPORTADO
	// ==================================================================================

	/// <summary>
	/// ============================ O RELATO B DO DONO, LITERAL ============================
	/// *"ao acabar o ki nadando, eu sou JOGADO DE VOLTA PRA MARGEM mas da um bug q se eu estiver
	/// SEGURANDO O BOTAO DE ANDAR eu consigo VOLTAR PRA AGUA ANDANDO POR CIMA DELA. (...) entao TIRA
	/// ISSO DE TELEPORTAR PRA MARGEM, e faca o personagem FICAR PRESO LA tendo q RECARREGAR O KI pra
	/// voltar a nadar e continuar"*.
	///
	/// Sao DUAS afirmacoes e elas se separam: **nao anda** e **nao foi movido**. A segunda parece
	/// consequencia da primeira e nao e -- o `LevarProSeco` cumpria "o corpo nao esta mais na agua"
	/// com nota cheia, e era exatamente o que o dono mandou tirar. Uma bancada que so medisse "ele nao
	/// atravessou o lago" ficaria VERDE com o teleporte de volta.
	///
	/// ============================ POR QUE OS QUATRO RUMOS, E NAO UM ============================
	/// Porque o escape que erra nao erra pra todo lado: ele aponta pra ALGUM lugar. Um `Escapar` que
	/// concedesse refugio no meio do lago deixaria o corpo parado em tres rumos e andando no quarto --
	/// e a prova de um rumo so tem 75% de chance de estar olhando pro lado errado.
	///
	/// A NAO-REGRESSAO ANDA JUNTO (o item 6 do pedido: *"nadando com Ki, atravessa normal"*). Ela ja
	/// tem familia propria -- a FAMILIA 2, com quatro defeitos injetados --, e esta aqui de novo pelo
	/// mesmo motivo que os contra-exemplos existem no resto do arquivo: **"zero pixel" e satisfeito
	/// perfeitamente por um corpo que nao anda nunca**. As duas linhas juntas, no MESMO ponto do
	/// MESMO lago, sao a unica prova de que o que parou foi o modo e nao o movimento.
	/// ======================================================================================
	/// </summary>
	private static Familia SemKiNaAguaNaoAnda() => new()
	{
		Nome = "FAMILIA 8 -- SEM KI, NA AGUA: ZERO PIXEL (e ele NAO foi teleportado)",
		Frase = "faca o personagem FICAR PRESO LA tendo q RECARREGAR O KI pra voltar a nadar",
		Provas = (r, p) =>
		{
			Cenario c = Montar(r);
			Vec2 meio = Cenario.Dentro;

			// ---- A PERGUNTA SECA, ANTES DO PASSO: por que ele nao anda.
			MoveRules.Escape esc = r.Escape(c.Col, meio, ModoDeTravessia.APe, out _);
			p.Prova($"no meio do lago, a pe, o escape responde NENHUM (deu {esc}) -- "
					+ "quem muda e o MODO, nao a posicao",
					esc == MoveRules.Escape.Nenhum);

			// ---- OS QUATRO RUMOS, 10 s cada.
			(string Nome, Vec2 Rumo)[] rumos =
				[("leste", new Vec2(1, 0)), ("oeste", new Vec2(-1, 0)),
				 ("sul", new Vec2(0, 1)), ("norte", new Vec2(0, -1))];
			float pior = 0f;
			var andou = new List<string>();
			foreach ((string nome, Vec2 rumo) in rumos)
			{
				Caminhada a = Andar(r, c.Col, meio, rumo, ModoDeTravessia.APe, 600);
				float d = (a.Fim - meio).Length;
				if (d > pior) pior = d;
				if (d > 0.01f) andou.Add($"{nome} {d:0.0}px");
			}
			p.Prova($"a pe, no meio do lago, 10 s segurando a tecla nos QUATRO rumos: {pior:0.###} px"
					+ (andou.Count > 0 ? $" (andou pra {string.Join(", ", andou)})" : ""),
					pior <= 0.01f);

			// ---- E ELE NAO FOI MOVIDO: a posicao final e a de partida, bit a bit.
			Caminhada leste = Andar(r, c.Col, meio, new Vec2(1, 0), ModoDeTravessia.APe, 600);
			p.Prova($"...e a posicao final e A MESMA da partida, bit a bit "
					+ $"({leste.Fim.X:0.###},{leste.Fim.Y:0.###} = {meio.X:0.###},{meio.Y:0.###})",
					leste.Fim.X == meio.X && leste.Fim.Y == meio.Y);

			// ---- A NAO-REGRESSAO: o MESMO corpo, no MESMO ponto, NADANDO.
			Caminhada nadando = Andar(r, c.Col, meio, new Vec2(1, 0), ModoDeTravessia.Nadando, 600);
			p.Prova($"...mas NADANDO o mesmo corpo no mesmo ponto atravessa "
					+ $"(andou {(nadando.Fim - meio).Length:0} px, {nadando.PxNaAgua:0} deles sobre agua)",
					nadando.Fim.X > (Cenario.LagoX1 + 1) * 32 && nadando.PxNaAgua > ZoneCollision.TileSize);

			// ---- O SERVIDOR: parado e legitimo, o passo afirmado nao e.
			p.Prova("o servidor ACEITA \"nao me mexi\" (senao seria correcao por quadro em jogo honesto)",
					r.Valida(meio, meio, c.Col, ModoDeTravessia.APe));
			p.Prova("...e RECUSA o passo de um quadro (2,7 px) que um cliente afirmasse por cima da agua",
					!r.Valida(meio, meio + new Vec2(2.7f, 0), c.Col, ModoDeTravessia.APe));
			p.Prova("...e ACEITA o mesmo passo NADANDO (a recusa e do modo, nao do lugar)",
					r.Valida(meio, meio + new Vec2(2.7f, 0), c.Col, ModoDeTravessia.Nadando));

			// ---- E NO OCEANO DE VERDADE DA TERRA.
			if (_terra == null) { p.NaoDeu("o mesmo, no oceano de verdade da Terra"); return; }
			(ZoneCollision terra, _) = _terra.Abrir();
			r.MexerNoMapa(new Cenario { Col = terra, Vis = terra, LagoComoParede = terra, Vazio = terra });
			if (AguaFunda(terra) is not { } funda) { p.NaoDeu("achar agua funda na Terra"); return; }

			Vec2 noMar = Cenario.NoTile(funda.X, funda.Y);
			float piorReal = 0f;
			foreach ((_, Vec2 rumo) in rumos)
			{
				Caminhada a = Andar(r, terra, noMar, rumo, ModoDeTravessia.APe, 600);
				piorReal = MathF.Max(piorReal, (a.Fim - noMar).Length);
			}
			p.Prova($"no oceano de verdade da Terra (celula {funda.X},{funda.Y}), os quatro rumos: {piorReal:0.###} px",
					piorReal <= 0.01f);
			Caminhada real = Andar(r, terra, noMar, new Vec2(1, 0), ModoDeTravessia.Nadando, 600);
			p.Prova($"...e NADANDO, do mesmo ponto, ele percorre {real.PxNaAgua:0} px de agua de verdade",
					real.PxNaAgua > 8 * ZoneCollision.TileSize);
		},
		Defeitos = [
			("o escape voltou a ser `return alvo` -- a linha literal que o dono achou segurando a tecla",
			 r => r.Passo = PassoComEscapeQueAprovaTudo),

			("o `Escapar` pergunta a geometria com o MODO DO CORPO (a agua volta a ser lugar de onde se SAI andando)",
			 r => { r.Escape = EscaparPeloModoDoCorpo;
					r.Passo = (pos, d, dt, v, m, modo) => PassoComEscape(EscaparPeloModoDoCorpo, pos, d, dt, v, m, modo); }),

			("a folga do escape voltou aos 6 px do `MinCorrectionPx` (o cliente modificado atravessa a 2,7 px por pacote)",
			 r => r.Valida = ValidarComAFolgaAntiTremor),

			("so o CLIENTE foi consertado: o servidor manteve o `if (Occupied(from)) return true`",
			 r => r.Valida = ValidarComEscapeQueAprovaTudo),
		],
	};

	// ==================================================================================
	// FAMILIA 9 -- QUEM NASCE DENTRO DA PEDRA AINDA SAI
	// ==================================================================================

	/// <summary>
	/// ============================ O CONTRA-EXEMPLO DO PEDIDO, E O MAIS IMPORTANTE DAQUI ============================
	/// O escape existe por um motivo, e o motivo nao sumiu porque o dono reclamou do efeito colateral:
	/// corpo nasce dentro de pedra (o berco num mapa recarregado), uma obra sobe em cima dele, uma
	/// porta fecha, um arremesso o enterra. **Fechar o escape sem esta familia troca um bug chato por
	/// um corpo perdido** -- e um corpo perdido nao tem tecla nenhuma que o salve.
	///
	/// E O QUE ELE GANHA E MEDIDO NOS DOIS SENTIDOS: sai NA DIRECAO do refugio, e no sentido contrario
	/// nao anda. Sem a segunda metade, "ele sai" e satisfeito pelo passe livre de volta -- que e
	/// exatamente o que esta fase tirou.
	/// ==========================================================================================================
	/// </summary>
	private static Familia NascidoNaPedraSai() => new()
	{
		Nome = "FAMILIA 9 -- QUEM NASCE DENTRO DA PEDRA AINDA SAI",
		Frase = "o escape continua existindo pra quem foi POSTO num lugar impossivel",
		Provas = (r, p) =>
		{
			Cenario c = Montar(r);

			// ---- DENTRO DO MURO: um tile de espessura, o caso comum.
			Vec2 naPedra = Cenario.NoTile(Cenario.MuroX, Cenario.Linha);
			MoveRules.Escape esc = r.Escape(c.Col, naPedra, ModoDeTravessia.APe, out Vec2 refugio);
			p.Prova($"dentro da pedra o escape e DIRIGIDO (deu {esc})", esc == MoveRules.Escape.Dirigido);
			p.Prova("...e o refugio devolvido e um lugar onde o CORPO cabe (nao so a celula)",
					!MoveRules.Occupied(c.Col, refugio, ModoDeTravessia.APe));

			// O RUMO DA SAIDA sai do refugio e nao de um chute da bancada: o escape aponta pra oeste ou
			// pra leste conforme a ordem dos aneis, e cravar um dos dois aqui seria a bancada testando
			// a propria suposicao.
			Vec2 praFora = new Vec2(MathF.Sign(refugio.X - naPedra.X), 0);
			Vec2 praDentro = new Vec2(-praFora.X, 0);

			Caminhada saida = Andar(r, c.Col, naPedra, praFora, ModoDeTravessia.APe, 60);
			p.Prova($"andando NO RUMO do refugio ele sai da pedra em 1 s ({(saida.Fim - naPedra).Length:0} px)",
					!MoveRules.Occupied(c.Col, saida.Fim, ModoDeTravessia.APe));

			Caminhada oposto = Andar(r, c.Col, naPedra, praDentro, ModoDeTravessia.APe, 600);
			p.Prova($"...e no sentido OPOSTO ele nao anda ({(oposto.Fim - naPedra).Length:0.###} px) -- "
					+ "o escape e DIRIGIDO, nao passe livre",
					(oposto.Fim - naPedra).Length <= 0.01f);

			// ---- O SERVIDOR CONCORDA COM AS DUAS METADES.
			p.Prova("o servidor aceita o passo rumo ao refugio",
					r.Valida(naPedra, naPedra + praFora * 2.7f, c.Col, ModoDeTravessia.APe));
			p.Prova("...e recusa o do sentido contrario",
					!r.Valida(naPedra, naPedra + praDentro * 2.7f, c.Col, ModoDeTravessia.APe));

			// ---- PEDRA CERCADA DE AGUA: o degrau 3 do `Escapar`.
			//
			// Sem ele, o corpo enterrado numa ilha de rocha no meio do oceano cairia no `SemRefugio`
			// (nao ha chao seco em 8 aneis) e ganharia o passe livre -- exatamente o que esta fase
			// tirou. Com ele, o corpo troca "preso pela GEOMETRIA" por "preso pelo MODO", que e um
			// estado que ele mesmo desfaz nadando.
			Cenario ilha = MontarPedraNoLago(r);
			Vec2 naIlha = Cenario.NoTile(PedraX, PedraY);
			MoveRules.Escape escIlha = r.Escape(ilha.Col, naIlha, ModoDeTravessia.APe, out Vec2 refIlha);
			p.Prova($"enterrado numa rocha CERCADA DE AGUA o escape ainda e dirigido (deu {escIlha})",
					escIlha == MoveRules.Escape.Dirigido);
			p.Prova("...e o refugio e a agua ao lado (trocar geometria por modo e o certo: dali ele nada)",
					ilha.Col.EhAguaEm(new Vec2(refIlha.X, refIlha.Y + MoveRules.FeetOffsetY)));

			Vec2 rumoIlha = new Vec2(MathF.Sign(refIlha.X - naIlha.X), MathF.Sign(refIlha.Y - naIlha.Y));
			Caminhada saiDaIlha = Andar(r, ilha.Col, naIlha, rumoIlha, ModoDeTravessia.APe, 600);
			p.Prova($"...ele sai da ROCHA ({(saiDaIlha.Fim - naIlha).Length:0} px) e PARA na agua",
					!ilha.Col.BlockedAt(new Vec2(saiDaIlha.Fim.X, saiDaIlha.Fim.Y + MoveRules.FeetOffsetY))
					&& (saiDaIlha.Fim - naIlha).Length < 3 * ZoneCollision.TileSize);

			// ---- ENTERRADO FUNDO: o bloco macico de 5x5.
			Cenario fundo = MontarPedraFunda(r);
			Vec2 noFundo = Cenario.NoTile(PedraX, PedraY);
			MoveRules.Escape escFundo = r.Escape(fundo.Col, noFundo, ModoDeTravessia.APe, out _);
			p.Prova($"enterrado no meio de um bloco macico de 5x5 ele ainda tem saida (deu {escFundo})",
					escFundo == MoveRules.Escape.Dirigido);
			Caminhada saiDoFundo = Andar(r, fundo.Col, noFundo, new Vec2(1, 0), ModoDeTravessia.APe, 600);
			Caminhada opostoFundo = Andar(r, fundo.Col, noFundo, new Vec2(-1, 0), ModoDeTravessia.APe, 600);
			p.Prova("...e SO um dos dois sentidos anda (fundo ou nao, o escape continua tendo rumo)",
					((saiDoFundo.Fim - noFundo).Length > 1f) != ((opostoFundo.Fim - noFundo).Length > 1f));

			// ---- MAPA QUEBRADO: nao ha lugar valido em 8 aneis.
			//
			// AQUI O PASSE LIVRE E A RESPOSTA CERTA, e e a unica vez em que ele e. O defeito ali e do
			// MAPA, e prender o corpo nao o conserta -- prende so o jogador.
			ZoneCollision tudoParede = TudoParede(24);
			Vec2 nolugar = Cenario.NoTile(12, 12);
			p.Prova("num mapa inteiramente parede o escape responde SEM REFUGIO",
					r.Escape(tudoParede, nolugar, ModoDeTravessia.APe, out _) == MoveRules.Escape.SemRefugio);
			Caminhada perdido = Andar(r, tudoParede, nolugar, new Vec2(1, 0), ModoDeTravessia.APe, 60);
			p.Prova($"...e ai vale o passo cheio: o corpo nao fica perdido ({(perdido.Fim - nolugar).Length:0} px em 1 s)",
					(perdido.Fim - nolugar).Length > 100f);
		},
		Defeitos = [
			("o escape foi fechado seco (`return pos`) -- a correcao apressada: quem nasce na pedra nunca mais anda",
			 r => r.Passo = PassoQueCongelaOPreso),

			("o raio do escape caiu pra UM anel (enterrado fundo, o passe livre volta pela porta dos fundos)",
			 r => { r.Escape = EscaparComRaioDeUmAnel;
					r.Passo = (pos, d, dt, v, m, modo) => PassoComEscape(EscaparComRaioDeUmAnel, pos, d, dt, v, m, modo); }),

			("o escape voltou a aprovar tudo (`return alvo`) -- e ai o rumo deixa de existir",
			 r => { r.Passo = PassoComEscapeQueAprovaTudo; r.Valida = ValidarComEscapeQueAprovaTudo; }),

			("a folga do escape voltou aos 6 px (o servidor aceita o sentido contrario ao refugio)",
			 r => r.Valida = ValidarComAFolgaAntiTremor),
		],
	};

	// ==================================================================================
	// FAMILIA 10 -- A BEIRA DO LAGO NAO CONGELA
	// ==================================================================================

	/// <summary>
	/// ============================ A ARMADILHA QUE SO A MEDICAO MOSTROU ============================
	/// A caixa dos pes tem 16x10 px e encosta em ate QUATRO celulas. A exaustao pode entao largar o
	/// corpo com **uma quina molhada e tres no seco** -- ele nao esta na agua em nenhum sentido util,
	/// esta a dez pixels de estar completamente fora dela.
	///
	/// Com o `Nenhum` puro ele congelava ali (medido: `andando PRO SECO 1 s: 0 px`), e teria de pagar
	/// tres segundos de Ki nadando pra andar dez pixels -- pra um novato, ~45 s parado olhando pra
	/// praia. O conserto e o <see cref="MoveRules.QuinaValida"/>: se a caixa dos pes JA TOCA uma
	/// celula valida, o corpo se puxa pra ela.
	///
	/// ============================ E ELE NAO PODE VIRAR TRAVESSIA ============================
	/// Esta e a familia que segura a familia 8: um `QuinaValida` generoso (busca por aneis, quina sem
	/// checar bloqueio) devolve o corpo a andar por cima do lago -- o buraco do dono de volta, so que
	/// escrito por quem estava consertando a beira. Por isso a prova do MEIO DO LAGO mora aqui
	/// tambem, e nao so na familia 8.
	/// ====================================================================================
	/// </summary>
	private static Familia ABeiraNaoCongela() => new()
	{
		Nome = "FAMILIA 10 -- A BEIRA DO LAGO NAO CONGELA (a quina molhada)",
		Frase = "uma quina na agua e tres no seco: ele sai andando, e nao paga Ki por dez pixels",
		Provas = (r, p) =>
		{
			Cenario c = Montar(r);

			// A BEIRA: o centro do sprite AINDA na celula de agua, as duas quinas da direita ja no seco.
			// O numero nao e escolhido a dedo -- e a geometria da caixa (BodyHalfW = 8): a 26 px dentro
			// do ultimo tile de agua, as quinas caem em 18 e 34, e 34 ja e o tile seguinte.
			Vec2 beira = new((Cenario.LagoX1 * 32) + 26,
							 Cenario.Linha * 32 + 16 - MoveRules.FeetOffsetY);

			p.Prova("o corpo da beira esta MESMO com a caixa dos pes molhada (senao a prova nao mede nada)",
					MoveRules.Occupied(c.Col, beira, ModoDeTravessia.APe));
			p.Prova("...e com o CENTRO do sprite dentro da agua (e a faixa que a pergunta pelo centro perde)",
					c.Col.EhAguaEm(new Vec2(beira.X, beira.Y + MoveRules.FeetOffsetY)));

			MoveRules.Escape esc = r.Escape(c.Col, beira, ModoDeTravessia.APe, out Vec2 refugio);
			p.Prova($"na beira o escape e DIRIGIDO pra quina seca (deu {esc})",
					esc == MoveRules.Escape.Dirigido);
			p.Prova($"...e o refugio esta a menos de um tile ({(refugio - beira).Length:0} px) -- "
					+ "e uma celula que o corpo JA TOCA, entao isto nao vira travessia",
					(refugio - beira).Length < ZoneCollision.TileSize);

			Caminhada praOSeco = Andar(r, c.Col, beira, new Vec2(1, 0), ModoDeTravessia.APe, 60);
			p.Prova($"andando PRO SECO ele sai em menos de 1 s ({(praOSeco.Fim - beira).Length:0} px)",
					(praOSeco.Fim - beira).Length > 8f
					&& !MoveRules.Occupied(c.Col, praOSeco.Fim, ModoDeTravessia.APe));

			Caminhada praAgua = Andar(r, c.Col, beira, new Vec2(-1, 0), ModoDeTravessia.APe, 600);
			p.Prova($"...e PRA DENTRO da agua, {(praAgua.Fim - beira).Length:0.###} px em 10 s",
					(praAgua.Fim - beira).Length <= 0.01f);

			// O CONTRA-EXEMPLO QUE SEGURA A FAMILIA 8: no meio do lago as quatro quinas sao agua.
			p.Prova("no MEIO do lago as quatro quinas sao agua e a resposta volta a ser NENHUM",
					r.Escape(c.Col, Cenario.Dentro, ModoDeTravessia.APe, out _) == MoveRules.Escape.Nenhum);
			p.Prova("...e o `QuinaValida` de producao devolve NULO la (nao ha quina a que se puxar)",
					MoveRules.QuinaValida(c.Col, Cenario.Dentro, ModoDeTravessia.APe) == null);

			// E A BEIRA DE UMA PAREDE NAO GANHA NADA DISSO: a quina so vale onde a geometria nao para.
			// Sem esta linha, "a beira destrava" viraria "toda quina destrava", e um corpo com a quina
			// dentro do muro sairia andando pra dentro dele.
			Vec2 naParede = new((Cenario.MuroX * 32) + 26,
								Cenario.Linha * 32 + 16 - MoveRules.FeetOffsetY);
			p.Prova("...e um corpo com a quina dentro do MURO nao cai na regra da beira (a geometria para)",
					r.Escape(c.Col, naParede, ModoDeTravessia.APe, out _) == MoveRules.Escape.Dirigido
					&& MoveRules.Occupied(c.Col, naParede, ModoDeTravessia.Nadando));
		},
		Defeitos = [
			("o `QuinaValida` sumiu: o `Nenhum` puro congela quem tem uma quina molhada",
			 r => { r.Escape = EscaparComQuinaPeloCentro;   // sem quina nenhuma na faixa medida
					r.Passo = (pos, d, dt, v, m, modo) => PassoComEscape(EscaparComQuinaPeloCentro, pos, d, dt, v, m, modo); }),

			("a beira ganhou BUSCA POR ANEIS em vez das quatro quinas (o socorro virou travessia)",
			 r => { r.Escape = EscaparComBuscaNaBeira;
					r.Passo = (pos, d, dt, v, m, modo) => PassoComEscape(EscaparComBuscaNaBeira, pos, d, dt, v, m, modo); }),

			("o `Escapar` pergunta a geometria com o MODO DO CORPO (a beira e o meio do lago viram a mesma coisa)",
			 r => { r.Escape = EscaparPeloModoDoCorpo;
					r.Passo = (pos, d, dt, v, m, modo) => PassoComEscape(EscaparPeloModoDoCorpo, pos, d, dt, v, m, modo); }),
		],
	};

	// ==================================================================================
	// OS CENARIOS DAS FAMILIAS 9 E 10
	// ==================================================================================

	/// <summary>Onde a rocha das provas de "enterrado" fica, nos dois cenarios abaixo.</summary>
	private const int PedraX = 24, PedraY = 24;

	/// <summary>
	/// UMA ROCHA DE UM TILE CERCADA DE AGUA ATE onde a busca alcanca -- o degrau 3 do `Escapar`.
	///
	/// A agua vai a 12 tiles de raio de proposito: <see cref="MoveRules.RaioDoEscape"/> e 8, entao
	/// nao ha chao seco nenhum ao alcance e a primeira busca (a do modo do corpo) TEM que falhar. Um
	/// lago menor deixaria a prova verde pelo motivo errado.
	/// </summary>
	private static Cenario MontarPedraNoLago(Regras r)
	{
		const int lado = 48;
		int bytes = (lado * lado + 7) / 8;
		var col = new byte[bytes];
		var agua = new byte[bytes];

		for (int y = PedraY - 12; y <= PedraY + 12; y++)
			for (int x = PedraX - 12; x <= PedraX + 12; x++)
				agua[(y * lado + x) >> 3] |= (byte)(1 << ((y * lado + x) & 7));

		int i = PedraY * lado + PedraX;
		col[i >> 3] |= (byte)(1 << (i & 7));

		ZoneCollision mapa = ZoneCollision.Montar(lado, lado, col);
		mapa.DefinirAgua(agua);
		var c = new Cenario { Col = mapa, Vis = mapa, LagoComoParede = mapa, Vazio = mapa };
		r.MexerNoMapa(c);
		return c;
	}

	/// <summary>Um bloco macico de 5x5 -- o corpo no centro esta a tres aneis de qualquer saida.</summary>
	private static Cenario MontarPedraFunda(Regras r)
	{
		const int lado = 48;
		var col = new byte[(lado * lado + 7) / 8];
		for (int y = PedraY - 2; y <= PedraY + 2; y++)
			for (int x = PedraX - 2; x <= PedraX + 2; x++)
				col[(y * lado + x) >> 3] |= (byte)(1 << ((y * lado + x) & 7));

		ZoneCollision mapa = ZoneCollision.Montar(lado, lado, col);
		var c = new Cenario { Col = mapa, Vis = mapa, LagoComoParede = mapa, Vazio = mapa };
		r.MexerNoMapa(c);
		return c;
	}

	/// <summary>Um mapa em que NAO HA lugar valido -- o `SemRefugio` da familia 9.</summary>
	private static ZoneCollision TudoParede(int lado)
	{
		var col = new byte[(lado * lado + 7) / 8];
		for (int i = 0; i < lado * lado; i++) col[i >> 3] |= (byte)(1 << (i & 7));
		return ZoneCollision.Montar(lado, lado, col);
	}

	/// <summary>
	/// UMA CELULA DE AGUA COM AS OITO VIZINHAS TAMBEM DE AGUA -- e o `AcharAgua` nao serve pra isto.
	///
	/// Aquele devolve a PRIMEIRA celula de agua do mapa, que costuma ser beira: o corpo posto la tem
	/// quina seca e sai andando **com razao** (familia 10). Medir "zero pixel" ali reprovaria a regra
	/// certa. O que a familia 8 precisa e de mar aberto.
	/// </summary>
	private static (int X, int Y)? AguaFunda(ZoneCollision m)
	{
		for (int y = 8; y < m.Height - 8; y++)
			for (int x = 8; x < m.Width - 8; x++)
			{
				if (!m.EhAgua(x, y)) continue;
				bool todas = true;
				for (int dy = -1; dy <= 1 && todas; dy++)
					for (int dx = -1; dx <= 1 && todas; dx++)
						if (!m.EhAgua(x + dx, y + dy)) todas = false;
				if (todas) return (x, y);
			}
		return null;
	}

	// ==================================================================================
	// O ANDAR: um corpo caminhando de verdade, quadro a quadro
	// ==================================================================================

	private readonly struct Caminhada
	{
		public required Vec2 Fim { get; init; }

		/// <summary>Pixels percorridos COM OS PES NA AGUA -- a medida honesta de "atravessou nadando".</summary>
		public required float PxNaAgua { get; init; }

		/// <summary>Deslocamento do ultimo quadro: zero = parou de verdade, nao ficou vibrando.</summary>
		public required float UltimoPasso { get; init; }

		/// <summary>Algum quadro saiu NaN/infinito.</summary>
		public required bool Podre { get; init; }
	}

	/// <summary>
	/// ANDA DE VERDADE, quadro a quadro, com a mesma cadencia do cliente (60 Hz).
	///
	/// UM QUADRO NAO PROVA NADA -- e esta e uma licao pagas na sessao passada: a 160 px/s o passo e
	/// de 2,7 px, e daqui ate o obstaculo ha um tile inteiro. Uma prova que media "o corpo andou"
	/// nasceu VERMELHA com o codigo certo. Por isso aqui se anda o percurso todo e se pergunta se o
	/// corpo PASSOU.
	/// </summary>
	private static Caminhada Andar(Regras r, ZoneCollision mapa, Vec2 de, Vec2 rumo,
								   ModoDeTravessia modo, int quadros)
	{
		Vec2 pos = de;
		float naAgua = 0f, ultimo = 0f;
		bool podre = false;

		for (int i = 0; i < quadros; i++)
		{
			Vec2 antes = pos;
			pos = r.Passo(pos, rumo, 1f / 60, 1f, mapa, modo);
			if (float.IsNaN(pos.X) || float.IsNaN(pos.Y) || float.IsInfinity(pos.X) || float.IsInfinity(pos.Y))
			{
				podre = true;
				pos = antes;
				break;
			}
			ultimo = (pos - antes).Length;
			// SO CONTA COM OS PES NA AGUA: `NaAgua` usa a MESMA caixa dos pes da colisao (ver
			// `MoveRules.NaAgua`). Somar a distancia "com o bit ligado" mediria a intencao, e nao a
			// travessia -- foi assim que a bancada visual ficou verde com o corpo andando no seco.
			if (MoveRules.NaAgua(mapa, pos)) naAgua += ultimo;
		}

		return new Caminhada { Fim = pos, PxNaAgua = naAgua, UltimoPasso = ultimo, Podre = podre };
	}

	// ==================================================================================
	// O FONTE DO SERVIDOR (prova estrutural da familia 5)
	// ==================================================================================

	private static string _fonteDoNado = "";

	/// <summary>...e o do VOO, que e onde mora o pouso (`DescerAte`). Ver a prova estrutural da familia 2.</summary>
	private static string _fonteDoVoo = "";

	private static string FonteDoNado(string raiz)
		=> _fonteDoNado.Length > 0 ? _fonteDoNado : _fonteDoNado = Procurar(raiz, "GameServer.Nado.cs");

	private static string FonteDoVoo(string raiz)
		=> _fonteDoVoo.Length > 0 ? _fonteDoVoo : _fonteDoVoo = Procurar(raiz, "GameServer.Voo.cs");

	/// <summary>Sobe do diretorio dado ate achar o repo -- a bancada roda tanto da raiz quanto do bin/.</summary>
	private static string Procurar(string raiz, string arquivo)
	{
		var dir = new DirectoryInfo(Path.GetFullPath(raiz));
		for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
		{
			string tentativa = Path.Combine(dir.FullName, "Server", arquivo);
			if (File.Exists(tentativa)) return tentativa;
		}
		return "";
	}
}
