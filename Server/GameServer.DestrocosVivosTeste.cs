using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ O RESCALDO COM DOIS CLIENTES DE VERDADE (`--destrocosvivos`) ============================
/// O pedido do dono, na parte que ele grifou: *"ele vai sumir do espaco pra todos os jogadores (server
/// sync)"*.
///
/// ============================ POR QUE AS DUAS BANCADAS QUE JA EXISTIAM NAO RESPONDEM ISSO ============================
/// O rescaldo ja tinha cobertura dos dois lados, e nenhum dos dois lados alcanca a palavra "todos":
///
///   * a **`--planetateste`** (PROVA 10) e de servidor puro: ela prova que o relogio do rescaldo anda,
///     para no fim da janela e volta fechado do disco. Ela nao desenha nada e nao tem cliente -- por
///     construcao ela nao sabe dizer o que apareceu na tela de ninguem;
///   * a **`--diagagonia`** mede o pixel inteiro do efeito, mas num processo so. O que ela chama de
///     "duas telas" sao dois `DestrocosNoEspaco` na MESMA memoria, com a mesma DLL, os mesmos
///     `static` e uma lista de mortos que ela mesma escreveu. Ali "as duas telas concordam" e quase
///     uma tautologia -- e este projeto ja registrou esse cego com todas as letras: *"as duas telas
///     concordam" fica verde com as duas erradas igual*.
///
/// Falta o meio: **a lista de mortos VIAJANDO no fio e dois processos obedecendo a ela**. E e ai que
/// mora o unico defeito que o dono nomeou -- o planeta sumir pra um e continuar pro outro.
/// ================================================================================================================
///
/// ============================ O QUE ELA PROVA, E COMO CADA LINHA REPROVA ============================
///   D2 (o sumico e do servidor):
///     * antes: os DOIS clientes desenham o disco de <see cref="PlanetaDaCena"/>;
///     * depois: NENHUM dos dois desenha. Se um so parar de desenhar, a linha cai -- e e exatamente
///       isso que a injecao "o planeta some so num cliente" faz.
///   D3/D4 (determinismo entre MAQUINAS):
///     * os dois campos tem o mesmo numero de cacos, na MESMA raiz, com as MESMAS posicoes relativas.
///       Um `Random` local em `DestrocosDeMundo.De` derruba esta linha e nao derruba nenhuma outra do
///       projeto -- as posicoes continuariam plausiveis dos dois lados, so que diferentes.
///   E a MONTAGEM, que e o que impede tudo acima de ficar verde por engano: os dois relatos tem que
///   ser DESTA rodada (token) e o campo tem que ter nascido (cacos > 0). Sem isso, dois clientes
///   mortos concordariam perfeitamente sobre o nada.
/// ==================================================================================================
///
/// ============================ O PAVIO E ENCURTADO PELA SONDA, E SO ELE ============================
/// A explosao de verdade leva 310 s (`MortePlanetaria.SegundosDeExplosao`), e uma bancada de seis
/// minutos e uma bancada que ninguem roda. <see cref="SondasDaAgonia.SegundosDeExplosao"/> existe
/// exatamente pra isso e ja e usada assim pela `--planetateste`.
///
/// **O QUE NAO E ENCURTADO E O CAMINHO**: `ComecarDestruicao` -> `TickDaDestruicao` -> commit por
/// conta propria em `ConsumarDestruicao` -> `MandarMortosPraTodos`. Nenhuma linha deste arquivo
/// escreve `Fase = Destruido`, nem monta destroco, nem mexe na tela de ninguem.
/// ==============================================================================================
///
/// COMO RODAR -- `testar-destrocos.bat` (dois processos). Ou na mao:
///     Godot --headless --path . --host --rede 7983 --destrocosvivos --destrocos a
///           --raca Saiyan --conta bancada_destrocos_a --nome DestrocoA
///     Godot --headless --path . --rede 7983 --connect 127.0.0.1 --destrocos b
///           --raca Saiyan --conta bancada_destrocos_b --nome DestrocoB
/// </summary>
public partial class GameServer
{
	/// <summary>Ligada por `--destrocosvivos`.</summary>
	private bool _destrocosVivosLigados;
	private bool _destrocosVivosJaComecou;

	/// <summary>
	/// O MUNDO DA CENA. A Terra porque ela e a ORIGEM do espaco (`Espaco.PreFeitos`: (0,0)) e porque
	/// e o unico corpo cuja posicao nao depende da semente do universo -- ou seja, a cena e a mesma em
	/// qualquer save, e a bancada nao precisa procurar um planeta antes de comecar.
	/// </summary>
	private const string PlanetaDaCena = "Earth";

	/// <summary>
	/// O PAVIO DA BANCADA. Quatro segundos: tempo de o `S2C.Mortos` da fase "condenado" chegar nos dois
	/// clientes e de eles pintarem pelo menos um quadro de agonia antes do commit.
	/// </summary>
	private const double PavioDaBancada = 4.0;

	/// <summary>Quantos clientes de VERDADE a cena precisa. Dois, e o numero e o assunto.</summary>
	private const int ClientesDaCena = 2;

	private int _dvFase = -1;
	private long _dvViraEm;
	private string _dvToken = "";
	private int _dvOk, _dvFalhou;

	/// <summary>Uma linha que nao e veredito -- numero medido, explicacao, contexto.</summary>
	private static void Nota_Dv(string t) => GD.Print($"[destrocosvivos]   --    {t}");

	private void AfirmarDv(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _dvOk++; GD.Print($"[destrocosvivos]   OK    {oque}"); return; }
		_dvFalhou++;
		GD.PrintErr($"[destrocosvivos]   FALHA {oque}   {detalhe}");
	}

	// =====================================================================
	// A PORTA
	// =====================================================================
	/// <summary>
	/// Chamada no fim do <c>Entrar</c>, como a `--menteviva` e as duas da voz, e pelo mesmo motivo: o
	/// corpo que acabou de chegar so esta na `ZoneList` depois de tudo o que vem antes.
	/// </summary>
	private void DestrocosVivosNoLogin()
	{
		if (!_destrocosVivosLigados || _destrocosVivosJaComecou) return;

		int vivos = _players.Values.Count(p => p.Peer != null);
		if (vivos < ClientesDaCena)
		{
			GD.Print($"[destrocosvivos] {vivos} de {ClientesDaCena} clientes de verdade no ar -- "
				   + "esperando o outro.");
			return;
		}

		_destrocosVivosJaComecou = true;

		// O PAVIO CURTO ENTRA AQUI e nao la em cima: mexer numa sonda no boot valeria pra qualquer
		// destruicao do processo, inclusive as que nao sao desta bancada.
		Agonia.SegundosDeExplosao = PavioDaBancada;

		// O TOKEN DA RODADA. Ver o cabecalho do `RoboDeDestrocos`: sem ele um relato de meia hora atras
		// (o `user://` e o mesmo entre rodadas) seria lido como se fosse desta cena.
		_dvToken = "T" + Time.GetTicksUsec().ToString(System.Globalization.CultureInfo.InvariantCulture);

		// OS RELATOS VELHOS SAEM DE CENA. Apagar e mais forte do que so conferir o token: um arquivo
		// que sobrou e um arquivo que alguem pode ler por engano depois.
		foreach (string papel in new[] { "a", "b" })
		{
			string caminho = $"user://destrocos-relato-{papel}.txt";
			if (Godot.FileAccess.FileExists(caminho)) DirAccess.RemoveAbsolute(caminho);
		}

		// ============================ O MUNDO VOLTA A VIVER ANTES DE COMECAR ============================
		// **Esta bancada MATA UM PLANETA DE VERDADE, e o cadaver fica no save.** Sem esta linha a
		// segunda rodada na mesma pasta comeca com a Terra ja destruida -- e foi exatamente isso que
		// aconteceu na primeira vez que ela rodou duas vezes seguidas: a fase de CONTROLE saiu com
		// "a desenha=0, b desenha=0" e seis linhas caíram. O placar leu "o planeta nao aparece pra
		// ninguem", que e o oposto do que estava acontecendo (ele nunca chegou a existir naquela cena).
		//
		// `RessuscitarPlaneta` e a porta de producao do `admin_restaurar_planeta`. Ela vai no COMECO e
		// nao no fim de proposito: o fim de uma bancada de dois processos nao e um lugar confiavel pra
		// desfazer nada (qualquer um dos dois pode cair antes), e o que importa e que a PROXIMA rodada
		// encontre a cena montada.
		// ==========================================================================================
		ZoneKey zonaDaCena = Espaco.ZonaDe(Espaco.PreFeitos().First(p => p.Nome == PlanetaDaCena));
		if (ZonaCondenada(zonaDaCena) || ZonaMorta(zonaDaCena))
		{
			RessuscitarPlaneta(zonaDaCena);
			GD.Print($"[destrocosvivos] {PlanetaDaCena} estava morta de uma rodada anterior -- "
				   + "ressuscitada antes de comecar.");
		}

		GD.Print($"\n===== BANCADA DO RESCALDO COM DOIS CLIENTES ({_dvToken}) =====");
		_dvViraEm = NowMs();   // a proxima batida do tique ja vira a fase 0
	}

	// =====================================================================
	// O TIQUE -- as quatro fases
	// =====================================================================
	/// <summary>
	/// AS QUATRO FASES, e cada uma existe por uma pergunta:
	///   0. `orbita` .... os dois corpos vao pro espaco, em orbita do mesmo mundo. Ninguem mede: e
	///                    montagem, e ela precisa de tempo pra o `S2C.Vizinhanca` chegar e o
	///                    `DesenharPlanetas` rodar dos dois lados;
	///   1. `vivo` ...... **o controle**. Os dois tem que estar DESENHANDO o disco. Sem esta fase,
	///                    "o planeta sumiu pros dois" ficaria verde num cliente que nunca o desenhou;
	///   2. `rescaldo` .. o mundo caiu (pela porta de producao) e o campo esta no ceu. Aqui moram o D2
	///                    e o determinismo entre processos;
	///   3. `fim` ....... o veredito, lido dos dois relatos.
	/// </summary>
	private void TickDosDestrocosVivos()
	{
		if (!_destrocosVivosLigados || !_destrocosVivosJaComecou || _dvFase > 3) return;

		long agora = NowMs();
		if (agora < _dvViraEm) return;

		_dvFase++;
		double segundos = 3.0;

		switch (_dvFase)
		{
			case 0:
			{
				PlanetaNoEspaco alvo = Espaco.PreFeitos().First(p => p.Nome == PlanetaDaCena);
				Vec2 orbita = Espaco.PontoDeDecolagem(alvo);

				// OS DOIS NAO NASCEM NO MESMO PIXEL: corpos empilhados sao um estado que o jogo nao
				// produz, e a bancada nao pode ser a unica cena do servidor em que ele acontece. O
				// afastamento e pequeno de proposito -- os dois tem que ficar na MESMA vizinhanca
				// (3x3 chunks), senao um deles nao receberia o planeta e a fase 1 cairia por
				// enquadramento e nao por defeito.
				int i = 0;
				foreach (ServerPlayer pl in _players.Values.Where(p => p.Peer != null).ToList())
					MoveToZone(pl.Id, ZonaDoEspaco, orbita + new Vec2(i++ * 120f, 0));

				AnunciarDv("orbita");
				segundos = 4.0;
				break;
			}

			case 1:
				AnunciarDv("vivo");
				segundos = 3.0;
				break;

			case 2:
			{
				// O CONTROLE E COLHIDO ANTES DA MORTE: o relato e "o que eu vejo AGORA" e vai ser
				// sobrescrito na fase seguinte. Ver <see cref="ColherOControleDv"/>.
				ColherOControleDv();

				// ============================ A MORTE, PELA PORTA DE PRODUCAO ============================
				// `ComecarDestruicao` e a MESMA chamada do `admin_destruir_planeta` e do `PlanetDestroy`
				// do jogador. Dali em diante ninguem aqui toca em nada: o `TickDaDestruicao` desce o
				// pavio sozinho, `ConsumarDestruicao` faz o commit e `MandarMortosPraTodos` avisa os
				// dois clientes. A bancada so espera.
				// ====================================================================================
				// A ZONA SAI DO `Espaco.ZonaDe`, e nao de um `ZoneKey` montado a mao: e a mesma funcao
				// que o pouso, a carta e a `ChaveDePlaneta` usam, entao a chave que entra no registro
				// dos mortos e exatamente a que o cliente vai procurar.
				ZoneKey zona = Espaco.ZonaDe(Espaco.PreFeitos().First(p => p.Nome == PlanetaDaCena));
				bool comecou = ComecarDestruicao(zona, 1e12, "bancada dos destrocos vivos");
				AfirmarDv($"(montagem) a destruicao de {PlanetaDaCena} comecou pela porta de producao",
						  comecou);

				// O PRAZO: o pavio, mais o tempo em que o disco ainda fica na tela tocando a explosao
				// (`MortePlanetaria.SegundosDoEstouro`), mais folga pra o pacote do commit chegar. Antes
				// disso o disco AINDA ESTA la de proposito -- e a fase 2 leria "o planeta nao sumiu".
				segundos = PavioDaBancada + MortePlanetaria.SegundosDoEstouro + 3.0;
				break;
			}

			case 3:
				AnunciarDv("rescaldo");
				segundos = 3.0;
				break;

			default:
				JulgarOsDoisRelatos();
				return;
		}

		_dvViraEm = agora + (long)(segundos * 1000);
	}

	/// <summary>
	/// O ANUNCIO -- pelo canal de texto que ja existe, e nao por um opcode novo.
	///
	/// Mesma escolha da `--vozviva`: o fio carrega o jogo, e nao a medicao. O que viaja e uma linha de
	/// sistema que os dois clientes ja sabem receber.
	/// </summary>
	private void AnunciarDv(string fase)
	{
		string linha = $"[destrocos] fase={fase} token={_dvToken} planeta={PlanetaDaCena}";
		foreach (ServerPlayer p in _players.Values.Where(p => p.Peer != null)) Avisar(p, linha);
		GD.Print($"[destrocosvivos] --> fase '{fase}'");

		// A FASE 'vivo' E CONFERIDA NA PROXIMA VIRADA, e nao aqui: o relato do cliente leva
		// `RoboDeDestrocos.EsperaAntesDeRelatar` pra ser escrito. Guardar o nome e o que permite a
		// virada seguinte cobrar o relato certo.
		_dvUltimaFase = fase;
		if (fase == "vivo") _dvPendenteVivo = true;
	}

	private string _dvUltimaFase = "";
	private bool _dvPendenteVivo;

	// =====================================================================
	// O VEREDITO -- lido dos DOIS relatos
	// =====================================================================
	/// <summary>Um relato de cliente, como ele foi escrito no `user://`.</summary>
	private sealed class RelatoDeCliente
	{
		public readonly Dictionary<string, string> Campos = [];
		public string Ler(string chave) => Campos.GetValueOrDefault(chave, "");
		public int Inteiro(string chave) => int.TryParse(Ler(chave), out int v) ? v : -1;
	}

	private static RelatoDeCliente? LerRelato(string papel)
	{
		string caminho = $"user://destrocos-relato-{papel}.txt";
		if (!Godot.FileAccess.FileExists(caminho)) return null;

		using Godot.FileAccess? f = Godot.FileAccess.Open(caminho, Godot.FileAccess.ModeFlags.Read);
		if (f == null) return null;

		var r = new RelatoDeCliente();
		foreach (string linha in f.GetAsText().Split('\n'))
		{
			string[] kv = linha.Split('=', 2);
			if (kv.Length == 2) r.Campos[kv[0].Trim()] = kv[1].Trim();
		}
		return r;
	}

	/// <summary>
	/// ============================ O VEREDITO, E POR QUE ELE MORA NO SERVIDOR ============================
	/// A pergunta e *"os dois viram a mesma coisa?"*. Nenhum dos dois pode responder isso sobre si
	/// mesmo -- e um deles respondendo pelos dois seria a pior das duas opcoes, porque o defeito que
	/// esta bancada existe pra pegar e justamente um cliente com opiniao propria.
	///
	/// O que o servidor le sao os dois relatos como eles foram escritos, sem interpretar: numero de
	/// cacos, raiz do campo e a lista de posicoes, byte a byte.
	/// ==================================================================================================
	/// </summary>
	private void JulgarOsDoisRelatos()
	{
		_dvFase = 99;

		RelatoDeCliente? a = LerRelato("a"), b = LerRelato("b");

		AfirmarDv("(montagem) os DOIS clientes escreveram relato", a != null && b != null,
				  $"a={(a == null ? "faltou" : "ok")}, b={(b == null ? "faltou" : "ok")}");
		if (a == null || b == null) { FecharDv(); return; }

		AfirmarDv("(montagem) os dois relatos sao DESTA rodada (token), e nao de uma anterior deixada "
				+ "no `user://`",
				  a.Ler("token") == _dvToken && b.Ler("token") == _dvToken,
				  $"a={a.Ler("token")}, b={b.Ler("token")}, esperado {_dvToken}");

		AfirmarDv("(montagem) os dois relataram a MESMA fase (o rescaldo), e nao um cada",
				  a.Ler("fase") == "rescaldo" && b.Ler("fase") == "rescaldo",
				  $"a={a.Ler("fase")}, b={b.Ler("fase")}");

		// ---- D2: O PLANETA SUMIU **PRA TODOS** ----
		AfirmarDv($"**O PLANETA SUMIU PRA TODOS**: nenhum dos dois clientes desenha mais o disco de "
				+ $"{PlanetaDaCena} (o \"server sync\" do dono -- e ele nao e uma decisao local, e a "
				+ "lista de mortos que chegou pelo fio)",
				  a.Inteiro("planeta") == 0 && b.Inteiro("planeta") == 0,
				  $"a desenha={a.Ler("planeta")}, b desenha={b.Ler("planeta")}");

		AfirmarDv("...e o CONTROLE: com o mundo vivo os dois DESENHAVAM esse mesmo disco (senao "
				+ "\"sumiu pros dois\" ficaria verde num cliente que nunca o desenhou)",
				  _dvVivoA == 1 && _dvVivoB == 1,
				  $"na fase 'vivo': a={_dvVivoA}, b={_dvVivoB}");

		// ---- D3/D4: O CAMPO NASCEU NOS DOIS, E ELE E O MESMO ----
		int cacosA = a.Inteiro("cacos"), cacosB = b.Inteiro("cacos");

		AfirmarDv("**O CAMPO DE DESTROCOS NASCEU NOS DOIS**, sem um byte de asteroide no fio: cada "
				+ "cliente derivou o campo da semente do planeta que ele ja tinha",
				  a.Inteiro("campo") == 1 && b.Inteiro("campo") == 1 && cacosA > 0 && cacosB > 0,
				  $"a: campo={a.Ler("campo")} cacos={cacosA} | b: campo={b.Ler("campo")} cacos={cacosB}");

		AfirmarDv("...e os dois contam o MESMO numero de pedacos", cacosA == cacosB,
				  $"{cacosA} contra {cacosB}");

		AfirmarDv("...e montaram o campo no MESMO lugar do espaco (a raiz dele)",
				  a.Ler("raiz") == b.Ler("raiz") && a.Ler("raiz").Length > 0,
				  $"a={a.Ler("raiz")}, b={b.Ler("raiz")}");

		// ============================ O DETERMINISMO SE MEDE NO MESMO INSTANTE ============================
		// A primeira rodada desta bancada comparou as posicoes VIVAS e reprovou -- e reprovou por medir
		// a coisa errada. Os dois relatos sao escritos com uns 0,3 s de diferenca (latencia do anuncio
		// + o `_Process` de cada cliente), e o campo SE MOVE: duas fotos de instantes diferentes de uma
		// coisa que anda nao tem como bater. O proprio relato dizia isso na linha do lado (prazo -6,8
		// contra -7,1).
		//
		// Entao a comparacao e do `posfixo`: os dois perguntaram ao campo DELES onde as pedras estariam
		// num instante que e uma constante compilada, pela mesma porta que o jogo usa por quadro. As
		// posicoes vivas continuam no relato, e viram a linha de baixo (os relogios andam juntos).
		// ==============================================================================================
		// ============================ O DEFEITO QUE **SO** ESTA LINHA PEGA, E ELE FOI MEDIDO ============================
		// Um `Random.Shared` solto na formula tambem cai na `--diagagonia` (la os dois campos de
		// comparacao nascem de duas chamadas seguidas, e o sorteio muda entre elas). O que ela NAO pega
		// -- porque nao pode -- e o defeito **estavel dentro do processo e diferente entre processos**:
		// `GetHashCode()` de string, um `Random` estatico semeado no boot, qualquer coisa que dependa da
		// maquina. Num processo so, os dois campos concordam perfeitamente; em dois, eles discordam.
		//
		// Isso foi injetado e medido, e nao suposto: trocando o `Espaco.Misturar` por
		// `$"{semente}:{i}".GetHashCode()` (que o cabecalho do `DestrocosDeMundo` ja dizia que "nao
		// serve", sem prova), a `--diagagonia` fechou **74 OK, 0 FALHA** e esta linha aqui apontou duas
		// pedras a 46 px de distancia uma da outra. Essa e a rodada que justifica a bancada existir.
		// ==========================================================================================================
		string posA = a.Ler("posfixo"), posB = b.Ler("posfixo");
		AfirmarDv("**DUAS MAQUINAS, O MESMO INSTANTE, AS MESMAS PEDRAS NOS MESMOS LUGARES** -- dois "
				+ "processos, duas memorias, dois relogios, e a lista de posicoes bate caractere por "
				+ "caractere. E a unica prova deste projeto que pega um sorteio ESTAVEL dentro de cada "
				+ "processo e diferente entre eles (o `GetHashCode()` do .NET e o exemplo classico)",
				  posA.Length > 0 && posA == posB,
				  PrimeiraDiferenca(posA, posB));

		// E OS DOIS RELOGIOS ANDAM JUNTOS. Nao e a mesma pergunta: as pedras podiam bater no instante
		// canonico com os dois clientes vendo o mundo em fases diferentes do rescaldo -- e ai um veria
		// pedra onde o outro ja nao ve nada. O numero vem do `Faltam` do `S2C.Mortos`, ou seja da
		// autoridade, e o que se cobra e que os dois estejam no MESMO ponto do minuto.
		bool prazoA = double.TryParse(a.Ler("prazo"), System.Globalization.NumberStyles.Float,
									  System.Globalization.CultureInfo.InvariantCulture, out double pa);
		bool prazoB = double.TryParse(b.Ler("prazo"), System.Globalization.NumberStyles.Float,
									  System.Globalization.CultureInfo.InvariantCulture, out double pb);

		AfirmarDv("...e os dois relogios do rescaldo andam JUNTOS (o `Faltam` do `S2C.Mortos` e da "
				+ "autoridade: os dois estao no mesmo ponto do minuto, e nao um no comeco e outro no fim)",
				  prazoA && prazoB && Math.Abs(pa - pb) < 1.5,
				  $"a={a.Ler("prazo")}s, b={b.Ler("prazo")}s");

		Nota_Dv($"as posicoes VIVAS diferem em fracoes de pixel entre os dois relatos porque eles foram "
			  + $"escritos com {Math.Abs(pa - pb):0.0}s de diferenca -- e o campo anda. Por isso o "
			  + "determinismo e cobrado no instante canonico, e nao nelas.");

		FecharDv();
	}

	/// <summary>
	/// ONDE as duas listas divergem, em texto -- e nao "elas divergem".
	///
	/// Uma bancada de determinismo que so diz "nao bateu" obriga quem le a repetir a rodada com um
	/// depurador. Duas telas discordando na TERCEIRA pedra e um sorteio; discordando na primeira e
	/// outra semente.
	/// </summary>
	private static string PrimeiraDiferenca(string a, string b)
	{
		if (a == b) return "iguais";

		string[] pa = a.Split(';'), pb = b.Split(';');
		if (pa.Length != pb.Length) return $"listas de tamanhos diferentes: {pa.Length} contra {pb.Length}";

		for (int i = 0; i < pa.Length; i++)
			if (pa[i] != pb[i]) return $"a pedra {i} esta em ({pa[i]}) num cliente e em ({pb[i]}) no outro";

		return "iguais";
	}

	/// <summary>O que cada cliente relatou na fase de CONTROLE. Ver o `case 3` do tique.</summary>
	private int _dvVivoA = -1, _dvVivoB = -1;

	/// <summary>
	/// Guarda o relato da fase 'vivo' ANTES de a fase seguinte sobrescrever o arquivo.
	///
	/// O arquivo e UM por papel de proposito (o relato e "o que eu vejo AGORA", nao um historico), e
	/// por isso a fase de controle tem que ser colhida enquanto ela e a atual.
	/// </summary>
	private void ColherOControleDv()
	{
		if (!_dvPendenteVivo) return;
		_dvPendenteVivo = false;

		_dvVivoA = LerRelato("a")?.Inteiro("planeta") ?? -1;
		_dvVivoB = LerRelato("b")?.Inteiro("planeta") ?? -1;
		GD.Print($"[destrocosvivos] controle (mundo vivo): a desenha={_dvVivoA}, b desenha={_dvVivoB}");
	}

	private void FecharDv()
	{
		GD.Print($"[destrocosvivos] ============ {_dvOk} OK, {_dvFalhou} FALHA(S) ============");
		GD.Print("[destrocosvivos] (o servidor continua no ar; feche os dois processos)");
	}
}
