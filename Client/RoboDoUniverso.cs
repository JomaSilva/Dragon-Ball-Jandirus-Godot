using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DO UNIVERSO (`--diaguniverso`) -- a irma do <see cref="RoboDeNav"/>.
///
/// ============================ POR QUE UMA IRMA E NAO UM PEDACO DAQUELA ============================
/// O `--diagnav` mede a CARTA: quantos pontos o enquadramento cobre, se clicar responde, se viajar
/// liga o piloto. Ela e uma bancada de widget, e o universo entra nela so como fonte de pontinhos.
///
/// Esta mede o UNIVERSO: se as duas pontas enumeram o mesmo, se os sete pre-feitos continuam onde
/// sempre estiveram, se a faixa de 1 a 10 mundos e verdade numa amostra grande, e se morrer no sol
/// chega ate o cliente. Sao perguntas de determinismo, e elas reprovam por motivos completamente
/// diferentes -- juntar as duas faria uma bancada de 900 linhas em que ninguem acha o que quebrou.
/// ===============================================================================================
///
/// ============================ O QUE "AS DUAS PONTAS" SIGNIFICA AQUI ============================
/// Nao e o mesmo processo se perguntando duas vezes. Esta bancada roda no processo do CLIENTE e
/// pergunta ao processo do SERVIDOR pelo fio (`universo_assinatura`, ver `GameServer.Universo.cs`).
/// O cliente calcula a assinatura com a seed que CHEGOU no login; o servidor calcula com a
/// <c>SeedDoUniverso</c> dele. Os dois numeros sao produzidos por processos separados, cada um com
/// o proprio estado estatico, e a comparacao acontece em codigo -- e nao num par de logs que
/// alguem le com o olho, que e o que o precedente do terreno faz.
///
///     Servidor:  Godot --headless -- --server --port 7951
///     Cliente:   Godot --headless --rede 7951 --connect 127.0.0.1 --diaguniverso
///                      --conta universo1 --nome Cartografo
///
/// **`--host` nao serve aqui.** Ele poe as duas pontas na mesma classe estatica do mesmo processo, e
/// ai "as duas pontas concordam" vira tautologia -- a comparacao fica verde por construcao, inclusive
/// com o universo errado dos dois lados.
///
/// E desde a rodada da grade a familia 1 faz DUAS perguntas e nao uma: se as duas pontas concordam, e
/// se elas concordam no universo de HOJE (a assinatura do universo de ontem tem que NAO bater com a do
/// servidor). A segunda e a que pega o servidor que subiu com o binario velho.
/// ============================================================================================
///
/// ============================ COMO CADA FAMILIA REPROVA ============================
/// Uma checagem que ninguem sabe como quebrar e uma checagem que ninguem sabe se funciona. Cada
/// familia abaixo carrega o defeito que ela existe pra pegar, e um CONTROLE NEGATIVO que a faz
/// ficar vermelha de proposito -- porque a arvore esta estavel, e afirmar que uma checagem tem
/// dentes sem nunca ver os dentes e a Parte 3 item 1 escrita de outro jeito.
/// ================================================================================
/// </summary>
public partial class RoboDoUniverso : Node
{
	private double _t;
	private int _passo;
	private bool _acabou;

	private readonly List<string> _falhas = [];
	private readonly List<string> _linhas = [];

	/// <summary>Tudo que o servidor disse desde o login. A familia 1 e a 6 leem daqui.</summary>
	private readonly List<string> _ditoPeloServidor = [];
	private bool _escutando;

	private static GameClient? C => GameClient.Instance;

	// =====================================================================
	// PLACAR
	// =====================================================================
	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Titulo(string t) { _linhas.Add(""); _linhas.Add("--- " + t + " ---"); }

	/// <summary>
	/// UM CONTROLE NEGATIVO: a mesma comparacao, com a entrada estragada de proposito. Ela tem que
	/// dar FALSO -- e e isso que prova que a checagem irma tem dentes.
	/// </summary>
	private void Contraprova(bool rejeitou, string oque) =>
		Conferir(rejeitou, "[contraprova] " + oque);

	// =====================================================================
	// A ESCUTA DO SERVIDOR
	// =====================================================================
	/// <summary>
	/// Metodo NOMEADO e nao lambda, e com `-=` no fim.
	///
	/// A licao ja custou 19 assinaturas orfas por ciclo de relog neste projeto: quem assina com
	/// lambda nao tem como cancelar, e um `Node` de bancada que fica pendurado no `GameClient`
	/// continua recebendo chat depois de a bancada acabar.
	/// </summary>
	private void Ouvir(Protocol.Fala canal, string autor, string texto) => _ditoPeloServidor.Add(texto);

	public override void _ExitTree()
	{
		if (_escutando && C is { } c) { c.Falou -= Ouvir; _escutando = false; }
	}

	/// <summary>
	/// A ULTIMA RESPOSTA DO VERB DE ASSINATURA, ja partida em campos.
	///
	/// Formato: `univ|ok|sx|sy|lado|HEX|sistemas|planetas|anuladas|seed|ms`. A marca existe porque
	/// uma bancada que procurasse um hexadecimal no meio de uma frase quebraria no dia em que
	/// alguem mexesse na frase -- e quebraria dizendo "as duas pontas divergem", que e a pior
	/// mentira que esta bancada tem como contar.
	/// </summary>
	private string[]? RespostaDoServidor()
	{
		for (int i = _ditoPeloServidor.Count - 1; i >= 0; i--)
			if (_ditoPeloServidor[i].StartsWith(GameServerMarca, StringComparison.Ordinal))
				return _ditoPeloServidor[i].Split('|');
		return null;
	}

	/// <summary>A marca da linha legivel por maquina. Copia consciente de `GameServer.MarcaDaAssinatura`.</summary>
	private const string GameServerMarca = "univ|";

	private void Perguntar(int sx, int sy, int lado)
	{
		_ditoPeloServidor.RemoveAll(l => l.StartsWith(GameServerMarca, StringComparison.Ordinal));
		C?.SendVerbo("universo_assinatura", $"{sx} {sy} {lado}");
	}

	// =====================================================================
	// AS REGIOES QUE A FAMILIA 1 CONFERE
	// =====================================================================
	/// <summary>
	/// SEIS REGIOES, e elas nao sao amostra aleatoria.
	///
	/// Uma assinatura conferida so em volta da origem provaria o universo perto da Terra, que e
	/// justamente o pedaco onde os dois lados tem mais chance de concordar por acidente (os sete
	/// ancorados sao literais nos dois). O que separa "as duas pontas concordam" de "as duas pontas
	/// concordam AQUI" e ir longe: a ultima regiao esta a 19,6 milhoes de px da origem, onde o
	/// `float` do `Vec2` ja so representa multiplos de ~2 px -- e e la que uma diferenca de
	/// arredondamento entre dois processos apareceria primeiro.
	/// </summary>
	private static readonly (int Sx, int Sy, int Lado, string Onde)[] Regioes =
	[
		(-2, -2, 4, "em volta da Terra (celulas ancoradas)"),
		(16, -19, 4, "em volta de Namek"),
		(-28, 9, 4, "em volta de Arconia"),
		(-100, -100, 8, "quadrante vazio a 6,5 milhoes de px"),
		(300, -400, 8, "longe: 19,6 milhoes de px da origem"),
		(0, 0, 32, "o maior lado que o servidor aceita (1.024 sistemas)"),
	];

	private int _regiao;
	private int _regioesConferidas;
	private double _msDoServidor;

	// =====================================================================
	// ESTADO QUE ATRAVESSA OS PASSOS
	// =====================================================================
	private MapaEstelar? _mapa;
	private SistemaSolar _daTerra, _outro;
	private double _vidaAntesDoSol = -1;
	private int _tiquesNoSol;
	private ZoneKey _zonaAntesDeMorrer;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || MenuJogo.Instancia is not { } menu) return;
		if (cli.Atributos.Raca is not { Length: > 0 }) return;

		if (!_escutando) { cli.Falou += Ouvir; _escutando = true; }

		_t += delta;
		if (_t < 0.5) return;
		_t = 0;

		// ============================ UMA EXCECAO AQUI JA APAGOU UMA FAMILIA INTEIRA ============================
		// O Godot ENGOLE a excecao de um `_Process`: ele imprime o rastro no stderr e chama o quadro
		// seguinte como se nada tivesse acontecido. Na primeira rodada desta bancada um `long.Parse`
		// estourou no meio da familia 1 e as SEIS regioes ficaram sem ser conferidas -- e o relatorio
		// saiu com 55 linhas verdes e nenhuma delas dizendo que a familia central nao rodou.
		//
		// Foi so a linha "as N regioes foram conferidas" que denunciou. Ela e a aplicacao da regra 5
		// da Parte 3 (falhar alto) e da lei de que SILENCIO NAO E SUCESSO -- mas contar com uma unica
		// linha de contagem pra isso e sorte. Daqui em diante toda excecao vira uma FALHA nomeada.
		// ===================================================================================================
		try { Passo(cli, menu); }
		catch (Exception ex) { Conferir(false, $"o passo {_passo} ESTOUROU: {ex.GetType().Name} -- {ex.Message}"); }
	}

	private void Passo(GameClient cli, MenuJogo menu)
	{
		switch (_passo++)
		{
			// =============================================================
			// FAMILIA 1 -- AS DUAS PONTAS ENUMERAM O MESMO UNIVERSO
			// =============================================================
			// COMO ELA REPROVA: qualquer divergencia entre o que o cliente enumera e o que o
			// servidor enumera. Os casos reais que ela pega, e que a bancada do AssetPipeline NAO
			// pega (porque la ha um binario so, se perguntando duas vezes):
			//   * a seed nao chegou, chegou zerada ou chegou de outro servidor;
			//   * alguem trocou uma constante do `Sistemas` e so um dos dois lados foi recompilado;
			//   * o cliente passou a montar posicao de planeta por conta propria em vez de perguntar
			//     ao Core -- a duplicacao que a Parte 3 item 4 descreve.
			// =============================================================
			case 0:
				Titulo("1) AS DUAS PONTAS");
				Conferir(cli.SeedDoUniverso != 0, $"a seed do universo chegou no login ({cli.SeedDoUniverso})");

				// ============================ A ABA NAV AGORA EXIGE O ITEM ============================
				// A familia 5 abre a carta pela aba Nav, e ela deixou de existir pra quem nao tem o Nav
				// System na mochila (pedido do dono, ver `GameServer.Sigilo.PoderesVisiveis`). O item entra
				// AQUI, no primeiro passo, porque o bit so volta no proximo pacote de atributos -- pedi-lo no
				// mesmo passo em que se abre a aba mediria o instante anterior.
				//
				// Esta bancada NAO prova o portao (quem prova e a `--diagnav`): aqui ele so precisa estar
				// aberto, senao a familia 5 mediria uma aba inexistente.
				// =====================================================================================
				Jandirus.Server.GameServer.Instance?.NavSystemNaMochilaDeTeste(cli.LocalId, true);

				Perguntar(Regioes[0].Sx, Regioes[0].Sy, Regioes[0].Lado);
				break;

			case 1:
			{
				// UMA REGIAO POR VOLTA: o verb responde por chat, e a resposta chega no tique
				// seguinte. Este `case` se repete ate acabar a lista (o `_passo--` no fim).
				(int sx, int sy, int lado, string onde) = Regioes[_regiao];
				string[]? r = RespostaDoServidor();

				if (r == null) { _passo--; return; }   // ainda nao voltou: espera mais meio segundo
				if (r.Length < 10 || r[1] != "ok")
				{ Conferir(false, $"a regiao {sx},{sy} lado {lado} voltou com erro: {string.Join("|", r)}"); }
				else
				{
					ulong minha = Sistemas.Assinatura(cli.SeedDoUniverso, sx, sy, lado);
					string doServidor = r[5];
					int sistemas = int.Parse(r[6]), planetas = int.Parse(r[7]);
					// MICROSSEGUNDOS INTEIROS: ver o comentario do lado do servidor. Um `double`
					// formatado com a cultura da maquina atravessaria o fio com virgula e seria lido
					// aqui como mil vezes maior.
					// `TryParse` e nao `Parse`: este campo alimenta uma linha de LOG, e um numero de
					// log nao pode derrubar a familia inteira. Foi exatamente o que aconteceu na
					// primeira rodada (ver a blindagem do `_Process`).
					if (long.TryParse(r[10], out long us)) _msDoServidor += us / 1000.0;
					else Conferir(false, $"o tempo do servidor veio ilegivel ('{r[10]}') -- separador decimal no fio?");

					Conferir($"{minha:X16}" == doServidor,
						$"assinatura BATE {onde}: {minha:X16} (servidor {doServidor})");

					// ============================ "IGUAL" NAO PODE SER "VAZIO DOS DOIS LADOS" ============================
					// Uma regiao inteiramente anulada assinaria igual nos dois lados sem nenhum corpo
					// dentro, e a familia inteira ficaria verde enumerando o nada. Por isso os dois
					// numeros vem junto com o hash, e por isso eles sao AFIRMADOS.
					// ==============================================================================================
					Conferir(sistemas > 0 && planetas > 0,
						$"...e a regiao NAO esta vazia: {sistemas} sistemas, {planetas} mundos");

					// E O CLIENTE CONTA A MESMA COISA. O hash prova que a enumeracao inteira bate; isto
					// prova que os dois estao contando a mesma GRANDEZA, e nao dois hashes iguais por
					// acaso de uma soma que se cancelou.
					int meusSis = 0, meusPla = 0;
					for (int y = sy; y < sy + lado; y++)
						for (int x = sx; x < sx + lado; x++)
							if (Sistemas.Do(cli.SeedDoUniverso, x, y) is { } s) { meusSis++; meusPla += s.Orbitas; }
					Conferir(meusSis == sistemas && meusPla == planetas,
						$"...e as contagens batem ({meusSis}/{meusPla} aqui contra {sistemas}/{planetas} la)");

					Conferir(ulong.Parse(r[9]) == cli.SeedDoUniverso,
						"...e as duas pontas estao na MESMA seed");

					// ============================ OS CONTROLES NEGATIVOS ============================
					// Sem eles, uma `Assinatura` que devolvesse uma constante passaria em tudo acima.
					// ==========================================================================
					Contraprova($"{Sistemas.Assinatura(cli.SeedDoUniverso + 1, sx, sy, lado):X16}" != doServidor,
						$"com a seed+1 a assinatura MUDA (regiao {sx},{sy})");
					Contraprova($"{Sistemas.Assinatura(cli.SeedDoUniverso, sx + 1, sy, lado):X16}" != doServidor,
						$"deslocada uma celula ela MUDA (regiao {sx},{sy})");

					// ============================ A ASSINATURA TEM QUE TER MUDADO, E ESTA E A PONTA QUE PROVA ============================
					// "As duas pontas concordam" e "as duas pontas concordam no universo CERTO" sao duas
					// perguntas, e ate aqui esta bancada so fazia a primeira: dois processos no build
					// velho concordariam entre si, verdinhos, com a reticula de volta na carta do dono.
					//
					// O cliente calcula tambem a assinatura do universo de ONTEM (`RegraDaGrade`, a mesma
					// `Assinatura` com os botoes da grade nas posicoes antigas) e exige que ela NAO bata
					// com a que veio pelo fio. Ela reprova em dois casos reais, e os dois sao calados:
					//   * o servidor subiu com o binario velho (o `.dmb` de ontem -- ja aconteceu neste
					//     projeto, e o sintoma era "o fix nao faz nada");
					//   * alguem escreveu a margem por celula e deixou a chamada orfa, e o gerador
					//     continuou usando o teto. O universo ficaria identico ao da reclamacao com a
					//     bancada inteira verde.
					// ============================================================================================================
					Contraprova($"{Sistemas.Assinatura(cli.SeedDoUniverso, sx, sy, lado, RegraDaGrade.AntesDaReclamacao):X16}" != doServidor,
						$"o universo de ONTEM (margem = teto pra todos, nenhuma celula vazia) NAO bate com o do "
						+ $"servidor: a assinatura MUDOU e o servidor esta no build de hoje (regiao {sx},{sy})");

					// E O VAZIO EXISTE DO LADO DE LA. A contagem que o servidor manda junto e o unico jeito
					// de saber que as celulas vazias sao do UNIVERSO e nao do cliente -- o hash bate de
					// qualquer jeito se os dois lados estiverem errados igual.
					if (lado >= 8)
						Conferir(sistemas < lado * lado,
							$"...e o SERVIDOR tambem enxerga celulas VAZIAS ali ({sistemas} sistemas em "
							+ $"{lado * lado} celulas, {(lado * lado - sistemas) * 100.0 / (lado * lado):0.0}% de vazio)");

					_regioesConferidas++;
				}

				if (++_regiao < Regioes.Length)
				{
					(int nx, int ny, int nl, _) = Regioes[_regiao];
					Perguntar(nx, ny, nl);
					_passo--;
				}
				return;
			}

			case 2:
				// O TETO DO VERB TEM QUE DISPARAR (regra 0.7). Ele nao e decorativo: a assinatura
				// enumera todos os corpos da regiao, e o custo cresce com o QUADRADO do lado -- um
				// pedido de lado 1000 pararia o tique do servidor por minutos, pra todo mundo.
				Perguntar(0, 0, 33);
				break;

			case 3:
			{
				string[]? r = RespostaDoServidor();
				if (r == null) { _passo--; return; }
				Conferir(r.Length > 1 && r[1] == "erro",
					$"o teto do lado da regiao DISPARA (pedi 33, o servidor recusou: {string.Join("|", r[2..])})");
				Conferir(_regioesConferidas == Regioes.Length,
					$"as {Regioes.Length} regioes foram conferidas ({_regioesConferidas})");
				break;
			}

			// =============================================================
			// FAMILIA 2 -- OS SETE NAO SE MOVERAM, E A TERRA E O BERCO
			// =============================================================
			// COMO ELA REPROVA: se `SistemaSolar.Planeta(k)` voltar a CALCULAR a posicao do
			// pre-feito em vez de devolve-la literal. O calculo (`estrela + direcao * semieixo`)
			// erra por arredondamento de `float`, e a Terra ANDA -- e ela e o zero do sistema de
			// coordenadas do universo, entao andar 0,001 px move tudo o mais em relacao a ela.
			//
			// A comparacao e BIT A BIT (`SingleToInt32Bits`) e nao por tolerancia, porque a falha
			// temida vale um ULP: qualquer folga escolhida a olho deixaria passar exatamente o
			// defeito que esta familia existe pra pegar. Esta linha vale por todas as outras -- se
			// ela cair, o resto da bancada esta medindo outro universo.
			// =============================================================
			case 4:
			{
				Titulo("2) OS SETE PRE-FEITOS");
				PlanetaNoEspaco[] sete = Espaco.PreFeitos().ToArray();

				Conferir(sete.Length == 7, $"os pre-feitos continuam sendo sete ({sete.Length})");
				Conferir(sete[0].Nome == "Earth" && Bits(sete[0].Pos.X) == Bits(0f) && Bits(sete[0].Pos.Y) == Bits(0f),
					"a Terra e o primeiro e esta em (0,0), bit a bit");

				foreach (PlanetaNoEspaco p in sete)
				{
					// PELO CAMINHO DE PRODUCAO: o sistema da celula dele, a orbita ancorada dele.
					SistemaSolar? s = Sistemas.Em(cli.SeedDoUniverso, p.Pos);
					if (s is not { Ancorado: true } sis || sis.PreFeito.Nome != p.Nome)
					{ Conferir(false, $"{p.Nome}: a celula dele nao devolveu o sistema ancorado"); continue; }

					PlanetaNoEspaco naOrbita = sis.Planeta(sis.OrbitaPreFeita);
					Conferir(Bits(naOrbita.Pos.X) == Bits(p.Pos.X) && Bits(naOrbita.Pos.Y) == Bits(p.Pos.Y),
						$"{p.Nome,-11} orbita#{sis.OrbitaPreFeita} devolve ({p.Pos.X:0},{p.Pos.Y:0}) bit a bit");

					// E ELE APARECE NA VIZINHANCA, que e a lista de onde o pouso sai. Um pre-feito
					// que a assinatura enxerga e o `PorPerto` nao seria um planeta que existe na
					// carta e nao existe pra quem voa ate ele.
					Conferir(Espaco.PorPerto(cli.SeedDoUniverso, p.Chunk).Exists(q => q.Nome == p.Nome),
						$"...e {p.Nome} aparece no `PorPerto` da chunk dele");
				}

				// A TERRA ORBITA UM SOL AMARELO POR REGRA ESCRITA, e nao por sorteio.
				_daTerra = Sistemas.Em(cli.SeedDoUniverso, new Vec2(0, 0))!.Value;
				Conferir(_daTerra.Estrela.Classe == ClasseDeEstrela.Amarela,
					$"a Terra orbita uma estrela amarela ({_daTerra.Estrela.Classe})");

				// O BERCO: e aqui que este corpo acordou, e e pra ca que ele volta na familia 6.
				Conferir(cli.Zone.Kind == ZoneKey.KindPremade
						 && string.Equals(cli.Zone.Name, "Earth", StringComparison.OrdinalIgnoreCase),
					$"este corpo nasceu na Terra (zona {cli.Zone.Name})");
				_zonaAntesDeMorrer = cli.Zone;

				// CONTRAPROVA: um unico ULP de deslocamento tem que ser REJEITADO. Se a comparacao
				// fosse por tolerancia, esta linha ficaria verde -- e a de cima tambem, com a Terra
				// fora do lugar.
				float umUlpAdiante = BitConverter.Int32BitsToSingle(Bits(sete[1].Pos.X) + 1);
				Contraprova(Bits(umUlpAdiante) != Bits(sete[1].Pos.X),
					$"um ULP em Namek e rejeitado ({sete[1].Pos.X:F6} contra {umUlpAdiante:F6})");
				break;
			}

			// =============================================================
			// FAMILIA 3 -- TERRA -> NAMEK CONTINUA NOS 7 DIAS
			// =============================================================
			// COMO ELA REPROVA: se alguem cravar a distancia em vez de deriva-la, ou mexer na
			// duracao do dia (`Ceu.SegundosPorDia`) ou na velocidade de voo (`MoveRules.BaseSpeedPx`)
			// sem que a escala do universo acompanhe. As duas ultimas sao a razao de a checagem
			// medir o NUMERO DE DIAS e nao o numero de pixels: a distancia em px pode mudar
			// legitimamente; sete dias nao pode.
			// =============================================================
			case 5:
			{
				Titulo("3) TERRA -> NAMEK");
				PlanetaNoEspaco[] sete = Espaco.PreFeitos().ToArray();
				double d = (sete[1].Pos - sete[0].Pos).Length;
				double dias = Espaco.DiasInGame(d);
				double minutos = Espaco.SegundosDeViagem(d) / 60;

				// ============================ NAO SAO 7,00 DIAS. SAO 6,98 -- E ISSO E UM ACHADO ============================
				// A constante `DistanciaTerraNamek` vale 1.612.800 px = 7,00 dias exatos, e todo mundo
				// que conferiu isso ate hoje conferiu A CONSTANTE (a bancada da Fase 2 imprimiu
				// "1.612.800 px = 7,00 dias" comparando o numero consigo mesmo). Mas quem manda nao e
				// ela: sao as duas POSICOES LITERAIS de `Espaco.PreFeitos()`, e Namek esta em
				// `(d*0,71, -d*0,70)` -- um vetor de comprimento `d * sqrt(0,71^2 + 0,70^2)` =
				// `d * 0,99702`. A viagem de verdade mede 1.608.035 px = **6,98 dias = 167,5 min**.
				//
				// POR QUE A BANCADA AFIRMA 6,98 E NAO CONSERTA PRA 7,00: o conserto e mudar a posicao
				// de Namek, e a posicao de um pre-feito e o que ANCORA o sistema solar dele -- mover
				// Namek move a estrela dele, muda a celula, e MUDA A ASSINATURA DO UNIVERSO pra todo
				// mundo que ja tem personagem. O erro vale 0,3% (30 s numa viagem de 168 min) e o
				// conserto vale um universo diferente. Fica medido e dito em voz alta.
				//
				// A tolerancia e 0,05 dia (~72 s reais): apertada o bastante pra pegar qualquer
				// mudanca de escala de verdade, larga o bastante pra nao reprovar por esses 0,3%.
				// ======================================================================================================
				Conferir(Math.Abs(dias - Espaco.DiasTerraNamek) < 0.05,
					$"a viagem sao {dias:0.00} dias in-game ({d:N0} px, {minutos:0.0} min reais) "
					+ $"-- e nao os {Espaco.DiasTerraNamek:0.00} da constante");
				Conferir(Math.Abs(d - Espaco.DistanciaTerraNamek) / Espaco.DistanciaTerraNamek < 0.005,
					$"...e ela fica a 0,3% da DERIVADA ({Espaco.DistanciaTerraNamek:N0} px): a diferenca e "
					+ "o vetor (0,71 / -0,70) nao ter comprimento 1");

				// A ESCALA DO UNIVERSO NAO ENCOLHEU COM OS SISTEMAS: a reta Terra->Namek atravessa
				// muito menos mundos do que atravessava, e e esse o ponto -- agora eles estao
				// agrupados. O numero e afirmado pra que uma mudanca de densidade apareca aqui.
				int sistemasNaReta = 0;
				for (int i = 0; i <= 40; i++)
				{
					var p = new Vec2(sete[0].Pos.X + (sete[1].Pos.X - sete[0].Pos.X) * i / 40f,
									 sete[0].Pos.Y + (sete[1].Pos.Y - sete[0].Pos.Y) * i / 40f);
					if (Sistemas.Em(cli.SeedDoUniverso, p) != null) sistemasNaReta++;
				}
				Conferir(sistemasNaReta > 0, $"a reta Terra->Namek passa por sistemas ({sistemasNaReta} de 41 amostras)");

				Contraprova(Math.Abs(Espaco.DiasInGame(d * 2) - Espaco.DiasTerraNamek) >= 0.05,
					"o dobro da distancia NAO passa por sete dias");
				break;
			}

			// =============================================================
			// FAMILIA 4 -- A FAIXA DE 1 A 10 MUNDOS
			// =============================================================
			// COMO ELA REPROVA: se o `% PlanetasMaximo` do sorteio virar `% (Maximo-1)` ou o `1 +`
			// sumir, a faixa some sem nada quebrar -- o jogo continua rodando com sistemas de 0 ou
			// de 9. A regra 0.7 e explicita: um extremo que nunca acontece e uma faixa mentirosa,
			// entao os DOIS extremos sao afirmados numa amostra grande, com a contagem de cada um.
			// =============================================================
			case 6:
			{
				Titulo("4) DE 1 A 10 MUNDOS POR SISTEMA");
				const int Lado = 200;   // 40.000 celulas
				var quantos = new int[Sistemas.PlanetasMaximo + 2];
				int anuladas = 0, vazias = 0, ancorados = 0, foraDaFaixa = 0, total = 0;

				ulong t0 = Time.GetTicksUsec();
				for (int y = -Lado / 2; y < Lado / 2; y++)
					for (int x = -Lado / 2; x < Lado / 2; x++)
					{
						// OS DOIS NULOS SAO CONTADOS SEPARADO. Somar "sorteada vazia" com "anulada pela
						// guarda" deixaria a asercao la embaixo verde por causa do sorteio, que e o
						// modo de falha que ela existe pra evitar (ver `CelulaVazia`).
						if (Sistemas.Do(cli.SeedDoUniverso, x, y, out CelulaVazia porque) is not { } s)
						{
							if (porque == CelulaVazia.Sorteada) vazias++; else anuladas++;
							continue;
						}
						total++;
						if (s.Ancorado) { ancorados++; continue; }
						if (s.Orbitas < 1 || s.Orbitas > Sistemas.PlanetasMaximo) { foraDaFaixa++; continue; }
						quantos[s.Orbitas]++;
					}
				double ms = (Time.GetTicksUsec() - t0) / 1000.0;

				int gerados = total - ancorados;
				double media = 0;
				for (int k = 1; k <= Sistemas.PlanetasMaximo; k++) media += (double)k * quantos[k] / Math.Max(1, gerados);

				_linhas.Add($"         {Lado * Lado:N0} celulas em {ms:0} ms | {gerados:N0} gerados, "
							+ $"{ancorados} ancorados, {vazias:N0} vazias ({vazias * 100.0 / (Lado * Lado):0.0}%), "
							+ $"{anuladas} anuladas | media {media:0.00} mundos");

				// O VAZIO E DE PROJETO, entao ele e AFIRMADO e nao so impresso: se o `VaziosPor256`
				// voltar a zero num refactor, a carta volta a ser uma reticula cheia e nada mais aqui
				// reprovaria. A faixa e larga de proposito -- o que se afirma e a ORDEM, nao o sorteio.
				Conferir(Math.Abs(vazias * 100.0 / (Lado * Lado) - Sistemas.VaziosPor256 / 2.56) < 2.0,
					$"a taxa de celula vazia bate com a constante ({Sistemas.VaziosPor256}/256 = "
					+ $"{Sistemas.VaziosPor256 / 2.56:0.0}%; medido {vazias * 100.0 / (Lado * Lado):0.0}%)");
				_linhas.Add("         " + string.Join("  ", Enumerable.Range(1, Sistemas.PlanetasMaximo)
									 .Select(k => $"{k}:{quantos[k]}")));

				Conferir(quantos[1] > 0, $"o extremo de BAIXO acontece: {quantos[1]:N0} sistemas de 1 mundo");
				Conferir(quantos[Sistemas.PlanetasMaximo] > 0,
					$"o extremo de CIMA acontece: {quantos[Sistemas.PlanetasMaximo]:N0} sistemas de {Sistemas.PlanetasMaximo} mundos");
				Conferir(foraDaFaixa == 0, $"nenhum sistema fora de 1..{Sistemas.PlanetasMaximo} ({foraDaFaixa})");

				// TODOS OS DEZ DEGRAUS ACONTECEM, e nao so os dois extremos. Uma faixa que so
				// produzisse 1 e 10 passaria nas duas linhas de cima e seria uma faixa falsa.
				int vazios = Enumerable.Range(1, Sistemas.PlanetasMaximo).Count(k => quantos[k] == 0);
				Conferir(vazios == 0, $"os {Sistemas.PlanetasMaximo} degraus da faixa acontecem (nenhum vazio)");
				Conferir(ancorados == 7, $"os sete ancorados aparecem uma vez cada ({ancorados})");

				// ============================ A CELULA ANULADA TEM QUE DISPARAR EM ALGUM LUGAR ============================
				// Com a seed deste servidor ela nao dispara nenhuma vez em 40.000 celulas -- e uma
				// guarda que nunca roda e uma guarda que nao existe (Parte 3, item 1). Ela nao e
				// supérflua: um sistema ancorado nasce onde o pre-feito ESTA e pode encostar na divisa
				// da celula vizinha, e sem ela nasceria um planeta procedural dentro do sol da Terra.
				//
				// Entao a bancada VARRE SEEDS ate achar uma em que ela dispara, e falha alto se nao
				// achar. E a mesma receita que a bancada do `AssetPipeline` ja usa; o que muda e que
				// aqui ela roda no cliente de verdade, junto com o resto.
				// =====================================================================================================
				// E ELA SO PODE DISPARAR PERTO DE UM ANCORADO -- varrer 40.000 celulas no miolo do
				// mapa nao ajuda em nada. A anulacao acontece quando um sistema GERADO nasce perto
				// demais de um sistema ANCORADO, e os sete ancorados moram em sete celulas
				// conhecidas: e o 5x5 em volta de cada uma que vale procurar.
				ulong seedQueAnula = 0;
				int anuladasLa = 0, seedsOlhadas = 0;
				for (ulong tentativa = 1; tentativa <= 3000 && seedQueAnula == 0; tentativa++)
				{
					seedsOlhadas++;
					foreach (SistemaSolar anc in Sistemas.ComPreFeito)
						for (int y = -2; y <= 2; y++)
							for (int x = -2; x <= 2; x++)
							{
								Sistemas.Do(tentativa, anc.Id.Sx + x, anc.Id.Sy + y, out CelulaVazia pq);
								if (pq == CelulaVazia.AnuladaPorAncorado) { seedQueAnula = tentativa; anuladasLa++; }
							}
				}

				Conferir(seedQueAnula != 0,
					$"a celula ANULADA PELA GUARDA dispara (seed {seedQueAnula} depois de {seedsOlhadas} seeds, "
					+ $"{anuladasLa} celula(s)) -- na seed deste servidor ela nao disparou nenhuma vez "
					+ $"em {Lado * Lado:N0} celulas ({anuladas}), e as {vazias:N0} vazias de la sao OUTRA coisa");

				// TODO SISTEMA ANCORADO TEM EXATAMENTE 4. E outra faixa, e ela tambem e afirmada:
				// os ancorados nao podem crescer sem empurrar vizinho.
				Conferir(Sistemas.ComPreFeito.All(s => s.Orbitas == Sistemas.OrbitasAncoradas),
					$"todo sistema ancorado tem {Sistemas.OrbitasAncoradas} orbitas");

				// CONTRAPROVA: o histograma sai da SEED e nao de uma tabela fixa. Sem esta linha,
				// um `Orbitas` que devolvesse sempre a mesma coisa por celula (ou uma tabela
				// escrita a mao) passaria em tudo acima.
				var outroHist = new int[Sistemas.PlanetasMaximo + 2];
				for (int y = -20; y < 20; y++)
					for (int x = -20; x < 20; x++)
						if (Sistemas.Do(cli.SeedDoUniverso + 1, x, y) is { Ancorado: false } s2)
							outroHist[s2.Orbitas]++;
				var esteHist = new int[Sistemas.PlanetasMaximo + 2];
				for (int y = -20; y < 20; y++)
					for (int x = -20; x < 20; x++)
						if (Sistemas.Do(cli.SeedDoUniverso, x, y) is { Ancorado: false } s3)
							esteHist[s3.Orbitas]++;
				Contraprova(!esteHist.SequenceEqual(outroHist),
					"com outra seed o histograma da MESMA regiao muda (a faixa sai do hash)");
				break;
			}

			// =============================================================
			// FAMILIA 5 -- O DUPLO CLIQUE ABRE A TELA, E NO CORPO CERTO
			// =============================================================
			case 7:
				Titulo("5) O DUPLO CLIQUE ABRE A TELA DO SISTEMA");
				menu.Abrir();
				Conferir(Array.IndexOf(menu.AbasDeTeste, "Nav") >= 0,
					"a aba Nav existe (o Nav System foi posto na mochila no passo 0)");
				menu.IrPara("Nav");
				_mapa = menu.MapaDeTeste;
				Conferir(_mapa != null, "a aba Nav monta o mapa");
				break;

			case 8:
			{
				if (_mapa == null) { Conferir(false, "sem mapa: familia 5 e 7 nao dao pra medir"); break; }

				// ENQUADRA O SISTEMA DA TERRA e entra nele pelo GESTO, e nao pelo evento na mao:
				// dois `InputEventMouseButton` no `_GuiInput`, um simples e um com `DoubleClick`.
				Enquadrar(_mapa, _daTerra);

				Conferir(_mapa.DuploCliqueNoSistema(_daTerra),
					"o duplo clique de verdade (dois eventos de mouse) mira o sistema da Terra");

				TelaDoSistema? tela = menu.SistemaDeTeste;
				Conferir(tela is { Visible: true }, "a tela do sistema abriu");
				Conferir(!_mapa.Visible, "...e a carta da galaxia saiu de cena");
				Conferir(tela?.SistemaDeTeste?.Id == _daTerra.Id,
					$"...NO SISTEMA CERTO ({tela?.SistemaDeTeste?.NomeDaEstrela})");

				// E O CORPO CERTO ESTA LA DENTRO, no lugar exato.
				bool achouTerra = false;
				if (tela?.SistemaDeTeste is { } s)
					for (int k = 0; k < s.Orbitas; k++)
						if (s.Planeta(k).Nome == "Earth")
							achouTerra = Bits(s.Planeta(k).Pos.X) == Bits(0f) && Bits(s.Planeta(k).Pos.Y) == Bits(0f);
				Conferir(achouTerra, "...com a Terra em (0,0) dentro dela");
				break;
			}

			case 9:
			{
				if (_mapa == null || menu.SistemaDeTeste is not { } tela) break;

				// UM SISTEMA GERADO, E A TELA TEM QUE TROCAR DE CORPO. Abrir sempre no da Terra
				// ficaria verde no passo anterior mesmo se a tela ignorasse qual sistema recebeu.
				for (int cx = 1; cx < 400; cx++)
					if (Sistemas.Do(cli.SeedDoUniverso, cx, 0) is { Ancorado: false, Orbitas: >= 5 } g)
					{ _outro = g; break; }

				menu.SistemaDeTeste?.VoltarDeTeste();
				Enquadrar(_mapa, _outro);

				Conferir(_mapa.DuploCliqueNoSistema(_outro), "duplo clique num sistema GERADO");
				Conferir(menu.SistemaDeTeste?.SistemaDeTeste?.Id == _outro.Id,
					$"...e a tela trocou pro sistema clicado ({_outro.Id}, {_outro.Orbitas} mundos)");
				Conferir(menu.SistemaDeTeste?.SistemaDeTeste?.Id != _daTerra.Id,
					"...ou seja: ela NAO abre sempre no mesmo");

				// CONTRAPROVA: o duplo clique SOZINHO, sem a selecao antes, nao pode abrir nada.
				// E a guarda que impede um duplo clique perdido de arrastar o jogador pra dentro de
				// um sistema que ele nem mirou.
				menu.SistemaDeTeste?.VoltarDeTeste();
				// REENQUADRA A TERRA ANTES: com a camera parada no outro sistema, o duplo clique
				// cairia num ponto fora da tela e a contraprova ficaria verde por AUSENCIA -- que e
				// a mesma coisa que nao ter contraprova.
				Enquadrar(_mapa, _daTerra);
				_mapa.SoDuploCliqueNoSistema(_daTerra);
				Contraprova(!(menu.SistemaDeTeste?.Visible ?? false) || menu.SistemaDeTeste?.SistemaDeTeste?.Id != _daTerra.Id,
					"duplo clique SEM selecao antes nao abre a tela");
				break;
			}

			// =============================================================
			// FAMILIA 7 -- O CUSTO NAO CRESCE SEM TETO (medido antes da 6,
			// que mata o corpo e derruba o menu)
			// =============================================================
			// COMO ELA REPROVA: se a magnitude limite parar de cortar, ou se o teto de legibilidade
			// sumir. Nenhum dos dois QUEBRA nada -- a carta so fica lenta, e lentidao nao reprova
			// bancada nenhuma (Parte 3 item 3). Por isso o corte e medido pela RAZAO entre o que o
			// enquadramento cobre e o que ele desenha: sem o corte, os dois numeros seriam o mesmo.
			// =============================================================
			case 10:
			{
				if (_mapa == null) break;
				Titulo("7) O CUSTO E OS TETOS");
				menu.SistemaDeTeste?.VoltarDeTeste();
				_mapa.VerTudo();

				// A PRIMEIRA PASSADA ESQUENTA O CACHE e nao entra na conta -- o que roda a 60 Hz e
				// a segunda. Medir as duas juntas reportaria o pior caso como se fosse o normal.
				_mapa.SistemasDeTeste();
				ulong t0 = Time.GetTicksUsec();
				int desenhados = _mapa.SistemasDeTeste().Count;
				double ms = (Time.GetTicksUsec() - t0) / 1000.0;

				long cobertas = _mapa.CelulasDoEnquadramentoDeTeste;
				_linhas.Add($"         zoom aberto: {cobertas:N0} celulas cobertas -> {desenhados:N0} estrelas "
							+ $"desenhadas em {ms:0.00} ms (magnitude {_mapa.MagnitudeDeTeste:0} px)");

				Conferir(ms < 8, $"a varredura da galaxia inteira cabe num quadro ({ms:0.00} ms)");
				Conferir(desenhados < cobertas / 2,
					$"a magnitude limite CORTA de verdade: {desenhados:N0} desenhados de {cobertas:N0} cobertas");
				Conferir(_mapa.MagnitudeDeTeste > Sistemas.RaioDaClasse(ClasseDeEstrela.AnaVermelha),
					$"...e ela subiu acima da ana vermelha ({_mapa.MagnitudeDeTeste:0} px)");

				// O TETO DE LEGIBILIDADE DISPARA. Ele e px POR CELULA e nao contagem de celulas, e
				// essa diferenca importa: contagem depende do tamanho da janela, entao um teto por
				// contagem dispararia pra um jogador e nao pra outro.
				(float celulaMinima, int celulasMax) = MapaEstelar.TetosDeTeste;
				for (int i = 0; i < 60 && _mapa.VendoSistemas; i++) _mapa.Zoom(1f / 1.25f);
				float pxPorCelula = (float)Sistemas.CelulaPx * _mapa.EscalaDeTeste;
				Conferir(!_mapa.VendoSistemas,
					$"afastando, o teto de legibilidade DISPARA ({pxPorCelula:0.0} px por celula, minimo {celulaMinima:0})");
				Conferir(_mapa.SistemasDeTeste().Count == 0, "...e a carta volta ao esqueleto dos sete");
				_linhas.Add($"         tetos: {celulaMinima:0} px por celula, {celulasMax:N0} celulas por quadro");

				// CONTRAPROVA: com o zoom de volta, ele PARA de disparar. Um teto que dispara sempre
				// seria a carta nunca desenhar estrela nenhuma, e ficaria verde na linha de cima.
				_mapa.VerTudo();
				Contraprova(_mapa.VendoSistemas && _mapa.SistemasDeTeste().Count > 0,
					"o mesmo teto NAO dispara no zoom em que a carta funciona");
				break;
			}

			// =============================================================
			// FAMILIA 6 -- MORRER NO SOL, PELO FUNIL DE MORTE, ATE O CLIENTE
			// =============================================================
			// COMO ELA REPROVA: o `--solteste` ja prova o funil do servidor com corpos forjados
			// (`Peer = null`). O que NENHUMA bancada provava e que a morte CHEGA em quem esta
			// jogando -- corpo sem soquete nao manda pacote nenhum. Esta familia reprova se:
			//   * o `TickDoSol` nao rodar pra um jogador de verdade;
			//   * o dano nao aparecer no `SheetState` do cliente (o HP viaja pela ficha rapida);
			//   * o `Morrer()` nao anunciar, ou anunciar sem matar;
			//   * o renascimento nao devolver o corpo ao berco.
			// =============================================================
			case 11:
				Titulo("6) MORRER NO SOL");
				menu.Fechar();
				// A CONTRAPROVA VEM ANTES DO FATO: se a frase da morte ja estivesse no historico
				// agora, a checagem la embaixo estaria lendo lixo antigo e ficaria verde sozinha.
				Contraprova(!_ditoPeloServidor.Exists(l => l.Contains("consumido")),
					"antes de entrar na estrela ninguem foi consumido por nada");
				C?.SendHabilidade("decolar");   // decolar e HABILIDADE, nao verb
				break;

			case 12:
				Conferir(Espaco.EhEspaco(cli.Zone), "decolei: estou no espaco");
				_vidaAntesDoSol = cli.Sheet.HP;
				C?.SendVerbo("admin_estrela");
				break;

			case 13:
			{
				// O SALTO E O AVISO. O verb usa a MESMA `Sistemas.EstrelaPerto` que o `TickDoSol`
				// consulta, entao chegar aqui ja prova que as duas concordam sobre onde a estrela e.
				Sistemas.EstrelaPerto(cli.SeedDoUniverso, World.Instancia?.PosicaoLocal is { } pp
					? new Vec2(pp.X, pp.Y) : new Vec2(0, 0), out Estrela e, out double dist);

				Conferir(dist < e.Raio,
					$"estou DENTRO do raio da estrela ({dist:0} px de {e.Raio:0}, classe {e.Classe})");
				Conferir(_ditoPeloServidor.Exists(l => l.Contains("DENTRO DE")),
					"o servidor BERRA que estou dentro dela, com o dano por segundo");
				break;
			}

			case 14:
				// UM SEGUNDO E MEIO COZINHANDO. O corpo de um recem-nascido nao aguenta dois.
				_tiquesNoSol++;
				if (_tiquesNoSol < 3) { _passo--; return; }

				Conferir(cli.Sheet.HP < _vidaAntesDoSol,
					$"a vida CAI no cliente: {_vidaAntesDoSol:0.#} -> {cli.Sheet.HP:0.#}");
				Conferir(_ditoPeloServidor.Exists(l => l.Contains("consumido")),
					"o servidor anuncia a morte pelo funil unico ('voce e consumido por ...')");
				break;

			case 15:
			{
				// E O RENASCIMENTO DEVOLVE O CORPO AO BERCO -- a Terra, a mesma zona do passo 4.
				// Sem esta linha, "morreu" poderia significar "o servidor esqueceu o corpo dentro
				// do sol", que e uma morte de que ninguem volta.
				bool voltou = !Espaco.EhEspaco(cli.Zone);
				if (!voltou && _tiquesNoSol < 70) { _tiquesNoSol++; _passo--; return; }

				Conferir(voltou, $"o corpo renasceu fora do espaco (zona {cli.Zone.Name})");
				Conferir(string.Equals(cli.Zone.Name, _zonaAntesDeMorrer.Name, StringComparison.OrdinalIgnoreCase),
					$"...e no berco de onde ele saiu ({_zonaAntesDeMorrer.Name})");
				break;
			}

			default:
			{
				_acabou = true;
				if (_escutando && C is { } c) { c.Falou -= Ouvir; _escutando = false; }

				GD.Print("\n[universo] ===== BANCADA DO UNIVERSO =====");
				foreach (string l in _linhas) GD.Print("[universo] " + l);
				GD.Print($"[universo]   seed {cli.SeedDoUniverso} | {_regioesConferidas} de {Regioes.Length} regioes conferidas "
						 + $"| o servidor gastou {_msDoServidor:0.0} ms assinando");
				int ok = _linhas.Count(l => l.StartsWith("  ok", StringComparison.Ordinal));
				GD.Print(_falhas.Count == 0
					? $"[universo] ===== {ok} OK, 0 FALHAS ====="
					: $"[universo] ===== {ok} OK, {_falhas.Count} FALHA(S) =====\n[universo]   "
					  + string.Join("\n[universo]   ", _falhas));

				// SAI COM O PLACAR NO CODIGO DE SAIDA -- mesmo padrao do `RoboDeBalao`. Uma bancada
				// que fica no ar depois de terminar obriga quem a roda a ler o log pra saber se
				// passou, e prende a saida no buffer do processo vivo.
				GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
				break;
			}
		}
	}

	/// <summary>
	/// ENQUADRA UM SISTEMA NUM ZOOM EM QUE A ESTRELA E CLICAVEL -- e nao so visivel.
	///
	/// ============================ POR QUE "APROXIMAR ATE VER PLANETA" NAO SERVE ============================
	/// O `_GuiInput` da carta da o clique ao PLANETA quando os dois estao debaixo do cursor, e isso
	/// esta certo: no zoom em que os mundos aparecem, o alvo que o jogador mira e o mundo. Mas no
	/// limiar em que a varredura de planetas LIGA, a escala ainda e ~0,0012 px por px de mundo -- a
	/// orbita mais interna (1.600 px) cai a DOIS pixels do centro da estrela, dentro do alvo
	/// generoso de 12 px do `PlanetaEm`. O duplo clique na estrela selecionaria o planeta.
	///
	/// Isso nao e defeito da carta: e o que "clicar em cima" significa naquele zoom. O que seria
	/// defeito e a bancada afirmar que o gesto funciona medindo-o num enquadramento onde nem o
	/// jogador conseguiria acertar. Por isso o criterio e geometrico e sai do proprio sistema: a
	/// primeira orbita tem que estar a pelo menos 30 px de tela do centro.
	/// ===================================================================================================
	/// </summary>
	private static void Enquadrar(MapaEstelar mapa, SistemaSolar s)
	{
		mapa.IrPara(new Vector2(s.Estrela.Pos.X, s.Estrela.Pos.Y));
		for (int i = 0; i < 80 && s.A0 * mapa.EscalaDeTeste < 30; i++) mapa.Zoom(1.25f);
	}

	/// <summary>
	/// OS BITS DE UM `float`. A comparacao de posicao de pre-feito e feita aqui e nao com `==`
	/// nem com tolerancia: a falha temida vale um ULP, e qualquer folga escolhida a olho deixaria
	/// passar exatamente o defeito que a familia 2 existe pra pegar.
	/// </summary>
	private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);
}
