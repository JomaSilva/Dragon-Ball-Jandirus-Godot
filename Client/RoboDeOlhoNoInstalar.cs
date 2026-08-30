using Godot;
using Jandirus.Core.Tech;

namespace Jandirus.Client;

/// <summary>
/// ============================ O SEGUNDO CORPO (`--olhoinstalar`) ============================
/// A metade do pedido do dono que **nao cabe num processo so**:
///
///   *"...uma versao transparente dele vai ficar no mouse (**isso claramente so aparece pro jogador
///   local**) ao clicar o objeto vai ser instalado nesse local (e nesse momento q o server vai
///   sincronizar com o resto dos jogadores) e **todos vao poder ver**."*
///
/// Sao duas afirmacoes sobre a TELA DE OUTRA PESSOA, e um cliente sozinho nao tem como fazer nenhuma
/// das duas. A `--diaginstalar` mede o que da pra medir de dentro (a lista dela nao mudou, nenhum
/// pacote saiu do funil) e **infere** o resto -- e inferencia e exatamente o lugar onde este projeto
/// ja escondeu bug: "o corpo esta branco" foi assinado lendo um uniform, e a foto mostrou 0,0% de
/// branco.
///
/// Entao este processo e uma SEGUNDA PESSOA de verdade: soquete proprio, conta propria, corpo
/// proprio no mundo. Ele nao fabrica, nao segura previa e nao clica em nada. Ele OLHA.
/// ==========================================================================================
///
/// ============================ COMO OS DOIS SE ENTENDEM ============================
/// Pelo canal OOC, que e jogo de verdade -- `SendChat` de la, `Falou` daqui, com o servidor no meio.
/// Um combinado por relogio ("aos 12 s ele segura a previa") ficaria verde numa rodada em que o
/// roteiro atrasou, com as duas metades medindo instantes diferentes.
///
/// Os marcos, na ordem:
///     comecou           o roteiro de la vai comecar. Aqui viram as linhas de sanidade.
///     previa            o fantasma esta na mao e o mouse vai andar. **Comeca a janela do silencio.**
///     previafim         o fantasma foi largado. **Fecha a janela**, e e aqui que a foto do "antes"
///                       e tirada -- o instante imediatamente anterior ao clique.
///     clicou cx cy tipo o clique aconteceu e o servidor decidiu.
///     recolheu          a obra foi recolhida (a bancada nao deixa lixo no `mundo.json`).
///     fim               pode fechar o placar.
/// =================================================================================
///
/// ============================ E ELE PRECISA PROVAR QUE ESTA VIVO ============================
/// "Nada mudou na minha lista" fica verde num cliente desconectado, numa zona errada, ou com o canal
/// de construcoes morto -- e as tres coisas ja aconteceram neste port. Por isso as linhas de sanidade
/// vem antes de tudo e sao cobradas do mesmo jeito: eu vejo o OUTRO CORPO no mundo, eu ouvi os marcos
/// (logo o fio esta de pe nos dois sentidos), e o meu canal de construcoes ENTREGOU pelo menos uma
/// lista na rodada. Sem elas, este arquivo seria uma maquina de fabricar linhas verdes.
/// =======================================================================================
///
/// COMO RODAR -- ele e CLIENTE, entao nao leva `--host`; quem hospeda e a `--diaginstalar`:
///     Godot --headless --path . --rede 7975 --olhoinstalar --raca Human
///           --conta bancada_olho --nome Olheiro
///
/// O `testar-instalar-dois.bat` sobe os dois na ordem certa.
/// </summary>
public partial class RoboDeOlhoNoInstalar : Node
{
	/// <summary>
	/// O QUE MARCA UMA FALA COMO RECADO DE BANCADA. Escrito uma vez, lido pelos dois lados -- e a
	/// mesma razao de o nome da acao "posicionar" morar numa constante so.
	/// </summary>
	public const string Prefixo = "[[instalar]] ";

	private static GameClient? C => GameClient.Instance;

	private int _ok, _falhou;
	private readonly List<string> _vermelhas = [];

	private void Checa(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"  ok    {nome}"); }
		else { _falhou++; _vermelhas.Add(nome); GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
	}

	private static void Nota(string linha) => GD.Print("[olho] " + linha);

	// =====================================================================
	// O QUE ESTE CORPO VE
	// =====================================================================
	/// <summary>Uma construcao como o OUTRO cliente a enxerga: tipo e celula.</summary>
	private readonly record struct Vista(string Tipo, int Cx, int Cy);

	private static List<Vista> OQueVejo()
	{
		var l = new List<Vista>();
		if (C is not { } cli) return l;
		foreach (GameClient.ObraInfo o in cli.Obras)
		{
			(int cx, int cy) = CatalogoDeObras.Celula(o.Pos.X, o.Pos.Y);
			l.Add(new Vista(o.Tipo, cx, cy));
		}
		return l;
	}

	/// <summary>
	/// QUANTAS CONSTRUCOES ESTAO DESENHADAS NA MINHA TELA -- e nao quantas o pacote listou.
	///
	/// A regra 4 diz *"todos vao poder ver"*, e ver e o node existir. Um pacote que chega e nao vira
	/// desenho e a diferenca entre o servidor ter sincronizado e o jogador ter visto.
	/// </summary>
	private static int DesenhosNaMinhaTela()
	{
		if (World.Instancia is not { } mundo) return -1;
		int n = 0;
		var fila = new Queue<Node>();
		fila.Enqueue(mundo);
		while (fila.Count > 0)
		{
			Node no = fila.Dequeue();
			if (no is ObraDesenhada) n++;
			foreach (Node f in no.GetChildren()) fila.Enqueue(f);
		}
		return n;
	}

	/// <summary>Quantos corpos de OUTRAS pessoas eu desenho. Zero = eu estou sozinho e nao provo nada.</summary>
	private static int CorposAlheios()
	{
		if (World.Instancia is not { } mundo) return 0;
		int n = 0;
		var fila = new Queue<Node>();
		fila.Enqueue(mundo);
		while (fila.Count > 0)
		{
			Node no = fila.Dequeue();
			if (no is RemotePlayer) n++;
			foreach (Node f in no.GetChildren()) fila.Enqueue(f);
		}
		return n;
	}

	// =====================================================================
	// O ESTADO DA VIGIA
	// =====================================================================
	private bool _ouviuAlgo;
	private int _listasRecebidas;

	/// <summary>Ligada entre `previa` e `previafim`. Enquanto ela vale, o mundo tem que ficar parado.</summary>
	private bool _janelaAberta;
	private List<Vista> _naAbertura = [];
	private int _mudancasNaJanela;
	private readonly List<string> _oQueMudou = [];

	/// <summary>A foto do instante imediatamente ANTERIOR ao clique -- tirada no `previafim`.</summary>
	private List<Vista>? _antesDoClique;
	private int _desenhosAntesDoClique = -1;

	private bool _fechou;
	private double _semNoticia;

	public override void _Ready()
	{
		if (C is { } cli)
		{
			cli.Falou += AoOuvir;
			// O CANAL DE CONSTRUCOES PRECISA DAR SINAL DE VIDA. Sem esta contagem, "a minha lista nao
			// mudou" seria indistinguivel de "o meu canal esta morto" -- e a segunda ja aconteceu.
			cli.ObrasMudaram += AoMudarObras;
		}
		Nota("de pe, esperando os marcos do outro processo.");
	}

	public override void _ExitTree()
	{
		if (C is { } cli) { cli.Falou -= AoOuvir; cli.ObrasMudaram -= AoMudarObras; }
	}

	private void AoMudarObras() => _listasRecebidas++;

	private void AoOuvir(Jandirus.Net.Protocol.Fala canal, string quem, string texto)
	{
		int i = texto.IndexOf(Prefixo, StringComparison.Ordinal);
		if (i < 0) return;
		string marco = texto[(i + Prefixo.Length)..].Trim();
		_ouviuAlgo = true;
		_semNoticia = 0;
		Nota($"marco de {quem}: '{marco}'");

		string[] p = marco.Split(' ');
		switch (p[0])
		{
			case "comecou": Comecou(); break;
			case "previa": AbrirJanela(); break;
			case "previafim": FecharJanela(); break;
			case "clicou" when p.Length >= 4: Clicou(p[3], Int(p[1]), Int(p[2])); break;
			case "recolheu": Recolheu(); break;
			case "fim": Fechar(); break;
		}

		static int Int(string s) => int.TryParse(s, out int v) ? v : int.MinValue;
	}

	// =====================================================================
	// OS MARCOS
	// =====================================================================
	private void Comecou()
	{
		GD.Print("\n--- O: eu sou o segundo corpo, e eu estou mesmo aqui ---");

		// AS TRES LINHAS DE SANIDADE. Elas nao medem o instalar -- elas medem se as linhas que medem
		// o instalar valem alguma coisa.
		Checa("O.0 eu estou conectado ao mesmo servidor", C is { Connected: true });
		Checa("O.1 eu estou dentro do mundo", World.Instancia?.PosicaoLocal != null);
		Checa("O.2 eu VEJO o corpo do outro jogador", CorposAlheios() >= 1,
			  $"{CorposAlheios()} corpo(s) alheio(s) na minha tela -- se e zero, tudo o que vier "
			  + "depois fica verde por ausencia");
		Nota($"minha lista comeca com {OQueVejo().Count} construcao(oes), "
			 + $"{DesenhosNaMinhaTela()} desenhada(s)");
	}

	private void AbrirJanela()
	{
		_janelaAberta = true;
		_naAbertura = OQueVejo();
		_mudancasNaJanela = 0;
		_oQueMudou.Clear();
		Nota($"janela do silencio ABERTA com {_naAbertura.Count} construcao(oes) na lista.");
	}

	private void FecharJanela()
	{
		_janelaAberta = false;
		_antesDoClique = OQueVejo();
		_desenhosAntesDoClique = DesenhosNaMinhaTela();

		GD.Print("\n--- O: enquanto a previa estava no mouse DELE (regra 3) ---");
		Checa("O.3 nada mudou na MINHA lista de construcoes", _mudancasNaJanela == 0,
			  string.Join(" | ", _oQueMudou));
		Checa("O.4 e o que eu tenho desenhado e o mesmo de quando a janela abriu",
			  DesenhosNaMinhaTela() == _naAbertura.Count,
			  $"{_naAbertura.Count} -> {DesenhosNaMinhaTela()}");
		Nota("janela FECHADA -- foto do 'antes do clique' tirada.");
	}

	private void Clicou(string tipo, int cx, int cy)
	{
		GD.Print("\n--- O: depois do clique DELE (regra 4) ---");

		if (_antesDoClique is not { } antes)
		{ Checa("O.5 eu tinha a foto do antes", false, "o marco `previafim` nunca chegou"); return; }

		// A METADE QUE FALTAVA EM TODA BANCADA ATE AQUI: **eu NAO via essa coisa antes do clique**.
		// Sem ela, "eu vejo a bancada" ficaria verde com uma bancada que ja estava la desde o boot.
		Checa($"O.5 eu NAO via {tipo} em ({cx},{cy}) antes do clique",
			  !antes.Contains(new Vista(tipo, cx, cy)),
			  "minha lista de antes: " + Resumo(antes));

		// ...E AGORA EU VEJO. O pacote e confiavel e ordenado, mas ele viaja: esperar e o certo.
		// (O `Ate` daqui e sincrono de proposito -- este metodo roda dentro do handler do chat.)
		_esperandoAparecer = new Vista(tipo, cx, cy);
		_prazoDeAparecer = 4.0;
		_antesDoClique = antes;
	}

	private Vista? _esperandoAparecer;
	private double _prazoDeAparecer;

	private void Recolheu()
	{
		// A OUTRA PONTA DA SINCRONIA: a obra sumiu pra mim tambem. Um mundo que so sabe ACRESCENTAR
		// deixaria a bancada de teste de pe na tela de todo mundo pra sempre.
		GD.Print("\n--- O: quando ele recolheu (a sincronia vale nos dois sentidos) ---");
		_esperandoSumir = _viAparecer;
		_prazoDeSumir = _viAparecer != null ? 4.0 : 0;
		if (_viAparecer == null)
			Checa("O.8 eu tinha visto a obra pra poder ve-la sumir", false,
				  "ela nunca apareceu pra mim");
	}

	private Vista? _viAparecer;
	private Vista? _esperandoSumir;
	private double _prazoDeSumir;

	private static string Resumo(List<Vista> l) =>
		l.Count == 0 ? "(vazia)" : string.Join(", ", l.Select(v => $"{v.Tipo}@{v.Cx},{v.Cy}"));

	// =====================================================================
	// O RELOGIO
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_fechou) return;

		if (_janelaAberta)
		{
			// A JANELA E VIGIADA A CADA QUADRO, e nao so nas pontas: um pacote que chegasse e outro
			// que desfizesse a mudanca no meio dela deixariam as duas pontas iguais.
			List<Vista> agora = OQueVejo();
			if (agora.Count != _naAbertura.Count || !agora.All(_naAbertura.Contains))
			{
				_mudancasNaJanela++;
				if (_oQueMudou.Count < 5) _oQueMudou.Add(Resumo(agora));
				_naAbertura = agora;   // nao reclamar do mesmo desvio sessenta vezes por segundo
			}
		}

		if (_esperandoAparecer is { } alvo)
		{
			_prazoDeAparecer -= delta;
			bool vejo = OQueVejo().Contains(alvo);
			if (vejo || _prazoDeAparecer <= 0)
			{
				_esperandoAparecer = null;
				Checa($"O.6 agora eu VEJO {alvo.Tipo} em ({alvo.Cx},{alvo.Cy})", vejo,
					  "minha lista: " + Resumo(OQueVejo()));
				Checa("O.7 e ela virou DESENHO na minha tela",
					  DesenhosNaMinhaTela() > _desenhosAntesDoClique,
					  $"{_desenhosAntesDoClique} -> {DesenhosNaMinhaTela()} node(s) ObraDesenhada");
				if (vejo) _viAparecer = alvo;
			}
		}

		if (_esperandoSumir is { } indo)
		{
			_prazoDeSumir -= delta;
			bool sumiu = !OQueVejo().Contains(indo);
			if (sumiu || _prazoDeSumir <= 0)
			{
				_esperandoSumir = null;
				Checa($"O.8 e quando ele recolheu, ela sumiu da minha tela tambem", sumiu,
					  "minha lista: " + Resumo(OQueVejo()));
			}
		}

		// ============================ O SILENCIO TOTAL TAMBEM TEM QUE REPROVAR ============================
		// Se o outro processo nunca subiu, ou a porta estava ocupada, ou o chat nao chega, este
		// arquivo terminaria sem uma linha vermelha -- e um placar "0 OK, 0 FALHA" e lido como sucesso
		// por qualquer um com pressa. Entao a ausencia de noticia e, ela mesma, uma falha.
		// ==============================================================================================
		_semNoticia += delta;
		if (_semNoticia > 90) Fechar();
	}

	private void Fechar()
	{
		if (_fechou) return;
		_fechou = true;

		Checa("O.9 eu ouvi os marcos do outro processo (o fio esta de pe nos dois sentidos)", _ouviuAlgo,
			  "nenhum marco chegou em 90 s -- o outro processo subiu? a porta e a mesma?");
		Checa("O.10 e o meu canal de construcoes entregou lista pelo menos uma vez", _listasRecebidas > 0,
			  $"{_listasRecebidas} pacote(s) de construcoes -- sem nenhum, 'nada mudou' seria o "
			  + "silencio de um canal morto");

		GD.Print($"\n[olho] ===== {_ok} OK, {_falhou} FALHA(S) =====");
		if (_falhou > 0) GD.PrintErr("[olho] vermelhas: " + string.Join(" | ", _vermelhas));
		Nota("fim.");
	}
}
