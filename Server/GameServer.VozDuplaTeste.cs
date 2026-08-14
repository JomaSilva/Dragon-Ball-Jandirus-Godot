using Godot;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ A BANCADA DA VOZ, COM DOIS CORPOS -- `--vozdupla` ============================
/// Dois processos, dois corpos de verdade, e uma pergunta so: **cada afirmacao deste sistema tem uma
/// checagem que SABE OLHAR?**
///
/// ============================ COMO ELA SE DIVIDE DAS OUTRAS TRES ============================
///   | bancada       | o que ela faz                    | o que ela nao faz                        |
///   |---------------|----------------------------------|------------------------------------------|
///   | `--diagvoz`   | mede o codec e o filtro          | nao ha rede, nao ha corpo, nao ha corte  |
///   | `--vozteste`  | mede o CORTE com corpos forjados | corpo forjado nao tem `Peer`: nao entrega|
///   | `--vozviva`   | MEDE o fio com 4 clientes        | imprime tabela: nao da veredito nenhum   |
///   | **`--vozdupla`** | **julga, com dois corpos, e prova cada veredito com o DEFEITO na frente dele** ||
///
/// A diferenca com a `--vozviva` nao e o numero de processos -- e o que sai no fim. Aquela produz
/// tabelas pra um humano ler (amplitude, espectro, banda); esta produz **OK / FALHA por familia**, e
/// pra cada familia um MUTANTE: o mesmo cenario com o defeito posto no lugar da regra, exigindo que a
/// linha fique VERMELHA. Uma checagem verde numa rodada limpa e uma checagem que nao viu defeito; uma
/// que tambem fica vermelha com o defeito na frente e uma checagem que sabe olhar.
/// ==========================================================================================
///
/// ============================ AS DEZ FAMILIAS, E COMO CADA UMA REPROVA ============================
///   F1 -- **FORA DO ALCANCE NAO RECEBE.** Zero BYTES no ouvinte, contados no pacote e nao no volume.
///         **Reprova** quando chega um byte que seja. Injetado: o corte trocado por "manda pra zona
///         inteira" (`CorteVazado`), que e o caminho preguicoso que o sistema recusa. Uma checagem que
///         medisse VOLUME ficaria verde com a sala inteira recebendo tudo -- e por isso ela conta bytes.
///   F2 -- **DENTRO DO ALCANCE RECEBE.** O contra-exemplo de F1: sem ele, um servidor que nunca
///         mandasse nada passaria verde na bancada inteira. **Reprova** quando nao chega quadro.
///   F3 -- **MAIS LONGE CHEGA MAIS FRACO**, dentro do alcance. **Reprova** quando a distancia carimbada
///         no pacote nao cresce (ou o volume derivado dela nao cai). Injetado junto com F1: o corte
///         vazado carimba distancia 0 pra todo mundo.
///   F4 -- **PAREDE ABAFA**, e a prova compara com a MESMA distancia SEM parede -- senao mede distancia
///         e chama de parede. Medida em AMOSTRAS (energia em 3 kHz contra 300 Hz), no cliente.
///         **Reprova** quando a razao agudo/grave atras da parede nao despenca. Injetado: o corte que
///         responde "sem parede" sempre (`CorteSemParede`).
///   F5 -- **A PAREDE DA VOZ E A DA VISTA.** O ouvinte pergunta ao `.vis` DELE (o mesmo mapa com que ele
///         desenha a sombra) e compara com o bit que veio no pacote. **Reprova** na primeira divergencia
///         -- e um segundo tracador de raios no servidor divergiria em alguma geometria. Injetado pelo
///         mesmo mutante de F4.
///   F6 -- **SOLTAR A TECLA PARA DE MANDAR.** Bytes depois de soltar: zero. **Reprova** quando sai
///         qualquer coisa. Injetado: o modo de MICROFONE ABERTO (que existe no `Settings`), que e a
///         forma real deste defeito -- a tecla solta e o aparelho seguindo.
///   F7 -- **CALADO NAO E OUVIDO, e o admin consegue calar.** Pelo funil de verdade (`admin_calar`, com
///         a conferencia de admin e a marca no disco). **Reprova** quando o quadro atravessa. Injetado:
///         a marca esquecida no cache vivo.
///   F8 -- **O TETO POR FALANTE SEGURA QUEM MANDA DEMAIS.** O falante injeta 5x o ritmo; o ouvinte tem
///         que continuar recebendo ~50 quadros por segundo. **Reprova** quando o excesso passa.
///         Injetado: o balde de credito esvaziado a cada tique.
///   F9 -- **O FLY ESTA NO F, E QUEM REMAPEOU CONTINUA COM A TECLA DELE.** Mora no cliente `a`.
///         **Reprova** quando o padrao novo passa por cima do save -- ou quando os dois donos ficam na
///         mesma tecla, calados. Injetado no `InputMap` de verdade.
///  F10 -- **O V CONTINUA REMAPEAVEL.** Medido em BYTES: religada a voz pro J, apertar J manda e
///         apertar V nao manda nada. **Reprova** quando o microfone le a tecla e nao a acao.
/// ============================================================================================
///
/// ============================ O SERVIDOR DIRIGE; O VEREDITO SAI NOS DOIS ============================
/// Aqui (autoridade): por os corpos onde a cena precisa, anunciar a fase pelo canal de texto que ja
/// existe, calar de verdade, instalar e tirar os mutantes, e contar o que **ele** entregou.
///
/// No cliente `b` (`Client/RoboDeVozDupla.cs`): o que CHEGOU -- e essa e a metade que so existe do lado
/// de la, porque byte entregue nao volta pra ser conferido.
/// ================================================================================================
///
/// COMO RODAR: `testar-voz-dupla.bat`. Ou na mao (o ANFITRIAO e o OUVINTE, porque e ele quem cala):
///     Godot --headless --path . --host --rede 7982 --vozdupla b --conta bancada_vozdupla_b --nome VozDuplaB
///     Godot --headless --path . --rede 7982 --connect 127.0.0.1 --vozdupla a --conta bancada_vozdupla_a --nome VozDuplaA
/// </summary>
public partial class GameServer
{
	/// <summary>Ligada por `--vozdupla`. E ela que destranca o <see cref="CorteQuebradoDeTeste"/>.</summary>
	private bool _vozDuplaLigada;

	private bool _vozDuplaJaRodou;

	/// <summary>
	/// SEGUNDOS DE CADA FASE. Dois.
	///
	/// A 50 quadros por segundo sao 100 quadros por fase, e o que esta bancada pergunta e mais grosso
	/// do que o que a `--vozviva` pergunta: ali era media de espectro (que se move com um engasgo),
	/// aqui e "chegou byte ou nao chegou" e "quantos por segundo". Cem quadros bastam pra isso, e
	/// dezessete fases a tres segundos passariam de um minuto -- bancada que ninguem roda nao mede nada.
	/// </summary>
	private const double SegundosPorFaseDupla = 2.0;

	private int _vdFase = -1;
	private long _vdViraEm;

	/// <summary>O falante (conta `*_a`) e o ouvinte (conta `*_b`, que e o ANFITRIAO e o admin).</summary>
	private ServerPlayer? _vdFalante, _vdOuvinte;

	/// <summary>O que o SERVIDOR entregou nesta fase, por ouvinte. Ver <see cref="EspiaoDaVoz"/>.</summary>
	private readonly Dictionary<int, (int Quadros, long Bytes)> _vdEntregue = [];

	/// <summary>Quantos quadros a torneira ja tinha recusado quando a fase comecou.</summary>
	private int _vdRecusadosNoInicio;

	/// <summary>Os pontos da varredura de F5 e onde ela esta. Ver <see cref="MontarAVarredura"/>.</summary>
	private readonly List<Vec2> _vdVarredura = [];
	private int _vdNoPonto;
	private long _vdProximoPonto;

	private int _vdOk, _vdFalha;
	private readonly List<string> _vdReprovadas = [];

	private void ChecaVd(string oque, bool passou, string detalhe = "")
	{
		GD.Print($"[vozdupla] {(passou ? "  OK   " : "  FALHA")} {oque}"
			   + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _vdOk++;
		else { _vdFalha++; _vdReprovadas.Add(oque); }
	}

	// =====================================================================
	// A PORTA
	// =====================================================================
	/// <summary>
	/// Chamada no fim do <c>Entrar</c>, como as outras bancadas de dois processos e pelo mesmo motivo:
	/// o corpo que acabou de chegar so esta na `ZoneList` depois de tudo o que vem antes.
	/// </summary>
	private void VozDuplaNoLogin()
	{
		if (!_vozDuplaLigada || _vozDuplaJaRodou) return;

		int vivos = _players.Values.Count(p => p.Peer != null);
		if (vivos < 2)
		{
			GD.Print($"[vozdupla] {vivos} de 2 corpos de verdade no ar -- esperando.");
			return;
		}

		_vozDuplaJaRodou = true;
		// DOIS SEGUNDOS: os dois clientes ainda estao mandando posicao, e mexer no corpo de alguem no
		// meio disso poria a bancada brigando com o anti-cheat de movimento do proprio servidor.
		GetTree().CreateTimer(2.0).Timeout += ComecarAVozDupla;
	}

	private void ComecarAVozDupla()
	{
		GD.Print("[vozdupla] ============ A VOZ, COM DOIS CORPOS -- e com o DEFEITO na frente de cada regra ============");

		List<ServerPlayer> gente = _players.Values.Where(p => p.Peer != null).ToList();

		// OS PAPEIS SAEM DA CONTA e nao da ordem de chegada -- a ordem de login e uma corrida entre dois
		// processos, e ela ja inverteu falante e ouvinte em bancadas deste projeto antes.
		_vdFalante = gente.FirstOrDefault(p => p.Conta.EndsWith("_a", StringComparison.Ordinal));
		_vdOuvinte = gente.FirstOrDefault(p => p.Conta.EndsWith("_b", StringComparison.Ordinal));

		if (_vdFalante == null || _vdOuvinte == null)
		{
			GD.PrintErr("[vozdupla] FALHA: nao achei as contas *_a (falante) e *_b (ouvinte/anfitriao). "
					  + $"Contas no ar: {string.Join(", ", gente.Select(p => p.Conta))}");
			GetTree().CreateTimer(1.0).Timeout += () => GetTree().Quit(1);
			return;
		}

		// O PALCO E O MESMO DA `--vozviva`, e achado pela MESMA funcao: uma zona que tenha ao mesmo tempo
		// um corredor livre de 32 celulas (pra fase FORA, a 30 tiles) e uma parede de verdade no `.vis`
		// com o controle sem parede na MESMA distancia. Escrever uma segunda busca daria duas verdades
		// sobre a mesma parede -- que e o defeito que este sistema inteiro existe pra nao ter.
		if (!AcharOPalcoDaVoz())
		{
			GD.PrintErr("[vozdupla] FALHA: nao achei uma zona com parede de verdade E corredor livre.");
			GetTree().CreateTimer(1.0).Timeout += () => GetTree().Quit(1);
			return;
		}
		MontarAVarredura();

		foreach (ServerPlayer p in gente) MoveToZone(p.Id, _vvZona, _vvOuve);

		PorOAdminDePe();

		EspiaoDaVoz = ContarEntregaDupla;

		_vdFase = -1;
		_vdViraEm = NowMs();
	}

	/// <summary>A bancada promoveu a conta do ouvinte? Entao ela TEM que rebaixa-la no fim.</summary>
	private bool _vdPromoveu;

	/// <summary>
	/// ============================ O ADMIN, E POR QUE ELE NAO E DE GRACA AQUI ============================
	/// F7 diz *"o admin consegue calar"*, e quem confere isso e o funil do verb (`EhAdmin`, em
	/// `GameServer.Verbos.cs`). So que **esta bancada desarma o admin do proprio anfitriao, de proposito
	/// e por uma regra que este servidor tem**: duas contas chegando por endereco local desligam o "admin
	/// por endereco" na hora (ver `ConferirAmbiguidadeDeHost`) -- porque atras de um tunel *todo mundo*
	/// e local, e continuar dando admin por endereco seria dar admin a estranhos. Dois processos na mesma
	/// maquina sao exatamente esse caso.
	///
	/// Entao a bancada faz o que o proprio aviso do servidor manda fazer: **promove a conta**, que poe a
	/// marca no DISCO e vale de qualquer endereco. Pelo `AdminPromover`, que e a funcao do painel -- e
	/// nao escrevendo `PoderesConcedidos` na mao, que provaria um bit e nao um caminho.
	///
	/// E ANTES DE PROMOVER ela tenta calar SEM ser admin. E o contra-exemplo de F7, e ele so existe nesta
	/// janela: sem ele, "o admin cala" ficaria verde num servidor que deixasse qualquer um calar.
	/// ==================================================================================================
	/// </summary>
	private void PorOAdminDePe()
	{
		if (!EhAdmin(_vdOuvinte!))
		{
			Verbo(_vdOuvinte!, "admin_calar", _vdFalante!.Name);
			ChecaVd("F7 (contra-exemplo) quem NAO e admin nao cala ninguem -- o funil recusa antes do verb",
				!EstaCalado(_vdFalante), _vdFalante.Conta);

			AdminPromover(_vdOuvinte!, _vdOuvinte!.Conta, virar: true);
			_vdPromoveu = true;
		}

		ChecaVd("F7 o ouvinte e admin -- e quem cala tem que ser admin de verdade",
			EhAdmin(_vdOuvinte!), _vdOuvinte!.Conta);
	}

	/// <summary>
	/// DEVOLVE A CONTA COMO ELA ESTAVA. Quem rebaixa e o FALANTE e nao o proprio ouvinte: `AdminPromover`
	/// recusa que o unico administrador marcado se rebaixe sozinho (um servidor sem admin nenhum e um
	/// servidor que so se conserta mexendo no JSON com o jogo fechado), e essa recusa deixaria a conta da
	/// bancada administradora pra sempre.
	/// </summary>
	private void TirarOAdminDePe()
	{
		if (!_vdPromoveu) return;
		_vdPromoveu = false;
		AdminPromover(_vdFalante!, _vdOuvinte!.Conta, virar: false);
		ChecaVd("(volta) a conta do ouvinte deixou de ser administradora",
			!EhAdmin(_vdOuvinte), _vdOuvinte.Conta);
	}

	// =====================================================================
	// A VARREDURA DE F5
	// =====================================================================
	/// <summary>
	/// OS PONTOS DA VARREDURA: o falante para em cinco lugares entre o ouvinte e o outro lado da parede.
	///
	/// ============================ POR QUE VARIOS PONTOS, E POR QUE PARADO ============================
	/// F5 afirma que a voz e a vista respondem A MESMA COISA sobre parede. Com um ponto so, a afirmacao
	/// e "os dois disseram 'sim' uma vez" -- que um `return true` cravado tambem cumpre. O que separa as
	/// duas respostas e a VARIEDADE: alguns pontos deste caminho tem parede no meio e outros nao, porque
	/// o par foi achado assim (`AcharParedeEntreDoisLivres`), e as duas pontas concordam nos dois casos
	/// ou a familia reprova.
	///
	/// E PARADO em cada ponto, e nao andando: o ouvinte compara com a posicao que o SNAPSHOT lhe deu do
	/// corpo do falante, e essa posicao chega interpolada. Andando, uma divergencia de meio tile na
	/// borda da parede viraria "a voz e a vista discordam" sem nada estar errado -- a bancada estaria
	/// medindo o proprio atraso.
	/// </summary>
	private void MontarAVarredura()
	{
		_vdVarredura.Clear();
		ZoneCollision? col = _catalogo?.Get(_vvZona)?.Mapa;
		ZoneCollision? vis = _catalogo?.Get(_vvZona)?.Vista;

		Vec2 d = _vvParedeB - _vvParedeA;
		foreach (float f in new[] { 0.18f, 0.42f, 0.6f, 0.8f, 1.0f })
		{
			Vec2 p = _vvParedeA + d * f;
			// SO CELULA LIVRE NOS DOIS MAPAS: parada dentro de uma pedra, o cliente do falante empurraria
			// o corpo pra fora no primeiro passo de predicao e a posicao medida nao seria a que a bancada
			// mandou. E a mesma exigencia do corredor da `--vozviva`.
			if (col != null && col.BlockedAt(p)) continue;
			if (vis != null && vis.BlockedAt(p)) continue;
			_vdVarredura.Add(p);
		}
		if (_vdVarredura.Count == 0) _vdVarredura.Add(_vvParedeB);
	}

	// =====================================================================
	// OS MUTANTES
	// =====================================================================
	/// <summary>
	/// **O DEFEITO PRINCIPAL: o servidor manda pra todo mundo.**
	///
	/// E o caminho preguicoso descrito no cabecalho do `GameServer.Voz.cs` com todas as letras -- relatar
	/// cada quadro pra zona inteira e deixar o cliente atenuar pela distancia. Ele SOA igual e e outra
	/// coisa, e e essa diferenca que a linha do "zero bytes" existe pra pegar.
	///
	/// Carimba distancia 0 e "sem parede" porque e o que um servidor assim teria pra carimbar: sem o
	/// corte, ele nao calculou distancia nenhuma. Isso poe o mesmo mutante na frente de F1 e de F3.
	/// </summary>
	private void CorteVazado(ServerPlayer a, long agora,
							 List<(ServerPlayer Quem, byte Dist, bool Parede)> saida)
	{
		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
		{
			if (o.Id == a.Id) continue;
			saida.Add((o, 0, false));
		}
	}

	/// <summary>
	/// O DEFEITO DE F4 E F5: o corte de verdade decide quem ouve, e a resposta de PAREDE e apagada.
	///
	/// E a forma mais provavel do defeito real -- o servidor perguntando ao mapa errado (o `.col` em vez
	/// do `.vis`), ou nao perguntando a nenhum porque o `.vis` nunca foi carregado, que foi exatamente o
	/// buraco que este trabalho fechou. Nos dois casos o sintoma e este: parede nenhuma, em lugar nenhum.
	/// </summary>
	private void CorteSemParede(ServerPlayer a, long agora,
								List<(ServerPlayer Quem, byte Dist, bool Parede)> saida)
	{
		QuemOuveAVoz(a, agora, saida);
		for (int i = 0; i < saida.Count; i++) saida[i] = (saida[i].Quem, saida[i].Dist, false);
	}

	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>Vira a fase quando der a hora. Chamada do <c>Tick</c>, e sai na primeira linha em jogo.</summary>
	private void TickDaVozDupla()
	{
		if (!_vozDuplaLigada || _vdFase < -1 || _vdFalante == null || _vdOuvinte == null) return;

		long agora = NowMs();

		// A VARREDURA ANDA DENTRO DA FASE, e nao entre fases -- ela e a unica que se mexe.
		if (_vdFase == FaseDaVarredura && agora >= _vdProximoPonto && _vdVarredura.Count > 0)
		{
			PorNoLugar(_vdFalante, _vdVarredura[_vdNoPonto % _vdVarredura.Count]);
			_paredeDaVoz.Clear();   // o corpo pulou; a resposta guardada e da posicao velha
			_vdNoPonto++;
			// 600 ms EM CADA PONTO: o ouvinte descarta os primeiros 250 (o corpo ainda esta chegando --
			// ver `MsDeTransito` no robo), e o que sobra sao ~17 quadros medidos por parada. Menos que
			// isso e a maioria da varredura viraria "em transito"; mais e a fase nao visitaria os cinco.
			_vdProximoPonto = agora + 600;
		}

		// O MUTANTE DA TORNEIRA MORA NO TIQUE porque ele E um defeito de tique: o balde de credito
		// esvaziado a cada volta do relogio do servidor. Nao ha como injeta-lo de fora -- e por isso
		// que ele e injetado de dentro, no mesmo lugar em que um esquecimento de verdade moraria.
		if (_vdFase == FaseDaTorneiraSolta) _vozes.Remove(_vdFalante.Id);

		if (agora < _vdViraEm) return;

		if (_vdFase >= 0) FecharFaseDupla();

		_vdFase++;
		_vdEntregue.Clear();
		_vdRecusadosNoInicio = QuadrosDeVozRecusadosDeTeste(_vdFalante.Id);
		CorteQuebradoDeTeste = null;

		const float t = ZoneCollision.TileSize;
		string nome;
		float dist;
		bool paredeEsperada = false, mutante = false;

		switch (_vdFase)
		{
			case 0: nome = "aquecimento"; dist = 2 * t; EmLinhaDupla(dist); break;
			case 1: nome = "perto"; dist = 2 * t; EmLinhaDupla(dist); break;
			case 2: nome = "longe"; dist = 20 * t; EmLinhaDupla(dist); break;
			case 3: nome = "fora"; dist = 30 * t; EmLinhaDupla(dist); break;

			case 4:
				// **O MUTANTE QUE O DONO PEDIU EM PRIMEIRO LUGAR**: mesma cena da fase FORA, com o corte
				// trocado por "manda pra zona inteira". A linha do zero tem que ficar vermelha.
				nome = "fora_vazando"; dist = 30 * t; mutante = true;
				EmLinhaDupla(dist);
				CorteQuebradoDeTeste = CorteVazado;
				break;

			case 5:
				nome = "parede"; paredeEsperada = true;
				PorNoLugar(_vdOuvinte, _vvParedeA);
				PorNoLugar(_vdFalante, _vvParedeB);
				dist = (_vvParedeB - _vvParedeA).Length;
				break;

			case 6:
				// O CONTROLE: a MESMA distancia, sem parede. Sem ele a fase 5 estaria medindo distancia
				// e chamando de parede.
				nome = "aberto";
				PorNoLugar(_vdOuvinte, _vvAbertoA);
				PorNoLugar(_vdFalante, _vvAbertoB);
				dist = (_vvAbertoB - _vvAbertoA).Length;
				break;

			case 7:
				nome = "parede_cega"; paredeEsperada = true; mutante = true;
				PorNoLugar(_vdOuvinte, _vvParedeA);
				PorNoLugar(_vdFalante, _vvParedeB);
				dist = (_vvParedeB - _vvParedeA).Length;
				CorteQuebradoDeTeste = CorteSemParede;
				break;

			case FaseDaVarredura:
				nome = "vistavarrida";
				PorNoLugar(_vdOuvinte, _vvParedeA);
				_vdNoPonto = 0;
				_vdProximoPonto = agora;      // o primeiro ponto entra na proxima volta do tique
				dist = (_vvParedeB - _vvParedeA).Length;
				break;

			case 9: nome = "soltar"; dist = 2 * t; EmLinhaDupla(dist); break;
			case 10: nome = "mic_aberto"; dist = 2 * t; mutante = true; EmLinhaDupla(dist); break;
			case 11: nome = "remapeado"; dist = 2 * t; EmLinhaDupla(dist); break;
			case 12: nome = "remapeado_v"; dist = 2 * t; EmLinhaDupla(dist); break;
			case 13: nome = "torneira"; dist = 2 * t; EmLinhaDupla(dist); break;

			case FaseDaTorneiraSolta:
				nome = "torneira_solta"; dist = 2 * t; mutante = true; EmLinhaDupla(dist);
				break;

			case 15:
				nome = "calado"; dist = 2 * t; EmLinhaDupla(dist);
				// PELO FUNIL DE VERDADE: o `Verbo` confere `EhAdmin` antes de deixar passar, e o
				// `AdminCalar` grava no DISCO. Chamar `_calados.Add` na mao provaria o `HashSet`.
				Verbo(_vdOuvinte, "admin_calar", _vdFalante.Name);
				ChecaVd("F7 o admin calou de verdade (a marca esta no funil que a voz consulta)",
					EstaCalado(_vdFalante), _vdFalante.Conta);
				break;

			case 16:
				nome = "calado_esquecido"; dist = 2 * t; mutante = true; EmLinhaDupla(dist);
				// O DEFEITO: a marca continua no disco e some do cache vivo -- que e o que o `EstaCalado`
				// consulta. E a forma real do mute que "nao pegou".
				_calados.Remove(_vdFalante.Conta);
				break;

			default:
				// DESCALAR ANTES DE SAIR: o `admin_calar` ALTERNA lendo o DISCO (e nao o cache que o
				// mutante da fase 16 esvaziou), entao esta segunda chamada desfaz a primeira. Sem ela a
				// conta da bancada ficaria calada pra sempre e a rodada seguinte comecaria muda.
				Verbo(_vdOuvinte, "admin_calar", _vdFalante.Name);
				ChecaVd("F7 (volta) solto o mute, a marca sai do funil",
					!EstaCalado(_vdFalante));
				TirarOAdminDePe();

				AnunciarFaseDupla("fim", 0, false, false);
				CorteQuebradoDeTeste = null;
				EspiaoDaVoz = null;
				PlacarDaVozDupla();
				_vdFase = -99;
				GetTree().CreateTimer(4.0).Timeout += () => GetTree().Quit(_vdFalha == 0 ? 0 : 1);
				return;
		}

		// O CACHE DE PAREDE TEM 100 ms DE VALIDADE E OS CORPOS ACABARAM DE PULAR. Em jogo isso e o que
		// se quer (ninguem se teleporta); numa bancada que teleporta, e erro de medida.
		_paredeDaVoz.Clear();

		AnunciarFaseDupla(nome, dist, paredeEsperada, mutante);

		// A VARREDURA E A UNICA FASE MAIS LONGA: ela tem cinco paradas de 600 ms, e em dois segundos
		// visitaria tres. As outras nao ganham nada com mais tempo -- elas nao se mexem.
		double segundos = _vdFase == 0 ? 2.0 : _vdFase == FaseDaVarredura ? 3.2 : SegundosPorFaseDupla;
		_vdViraEm = agora + (long)(segundos * 1000);
	}

	private const int FaseDaVarredura = 8;
	private const int FaseDaTorneiraSolta = 14;

	private void EmLinhaDupla(float px)
	{
		PorNoLugar(_vdOuvinte!, _vvOuve);
		PorNoLugar(_vdFalante!, _vvOuve + new Vec2(px, 0));
	}

	/// <summary>
	/// A FASE VAI PELO CANAL DE TEXTO QUE JA EXISTE -- ver o mesmo bloco na `--vozviva`. Um
	/// `S2C.FaseDaBancada` seria codigo de producao que so a bancada usa.
	/// </summary>
	private void AnunciarFaseDupla(string nome, float dist, bool parede, bool mutante)
	{
		string linha = $"[vozdupla] fase={_vdFase} nome={nome} d={dist:0} parede={(parede ? 1 : 0)} "
					 + $"mut={(mutante ? 1 : 0)} falante={_vdFalante!.Id} ouvinte={_vdOuvinte!.Id}";
		foreach (ServerPlayer p in _players.Values.Where(p => p.Peer != null)) Avisar(p, linha);
		GD.Print($"[vozdupla] --> {linha}");
	}

	private void ContarEntregaDupla(int falante, int ouvinte, int bytes)
	{
		_vdEntregue.TryGetValue(ouvinte, out (int Quadros, long Bytes) v);
		_vdEntregue[ouvinte] = (v.Quadros + 1, v.Bytes + bytes);
	}

	// =====================================================================
	// O QUE A AUTORIDADE VIU EM CADA FASE
	// =====================================================================
	/// <summary>
	/// O VEREDITO DO LADO DE CA. Ele nao substitui o do ouvinte -- responde outra pergunta.
	///
	/// O cliente `b` sabe o que CHEGOU nele; so o servidor sabe o que ele MANDOU, e o que ele jogou
	/// fora antes de mandar (a torneira). Quando os dois numeros batem, o que o cliente mediu e o que a
	/// autoridade decidiu; quando nao batem, a diferenca e o cano -- e ai a bancada sabe que esta
	/// olhando pra rede e nao pra regra.
	/// </summary>
	private void FecharFaseDupla()
	{
		int idB = _vdOuvinte!.Id;
		_vdEntregue.TryGetValue(idB, out (int Quadros, long Bytes) b);
		int recusados = QuadrosDeVozRecusadosDeTeste(_vdFalante!.Id) - _vdRecusadosNoInicio;

		// NA FASE DO MUTANTE O CONTADOR NAO VALE, e sai dito: o defeito injetado e TROCAR o balde a cada
		// tique, e o balde novo nasce com o contador de recusas zerado. Imprimir a subtracao daria um
		// numero negativo com cara de bug -- e o numero de verdade (quantas recusas houve) nao existe,
		// porque quem as contava foi jogado fora.
		string torneira = _vdFase == FaseDaTorneiraSolta
			? "a torneira foi TROCADA a cada tique (o contador de recusas nao vale)"
			: $"a torneira recusou {recusados}";
		GD.Print($"[vozdupla]     fase {_vdFase}: entreguei ao ouvinte {b.Quadros} quadros / {b.Bytes} B | {torneira}"
			   + (_vdEntregue.Count > 1 ? $" | e {_vdEntregue.Count - 1} outro(s) corpo(s) ouviram" : ""));

		switch (_vdFase)
		{
			case 3:
				// A METADE DE AUTORIDADE DE F1. A do ouvinte (contando bytes que chegaram) e a que vale
				// contra vazamento; esta prova que o `Peer.Send` nao foi NEM chamado -- as duas juntas
				// separam "nao mandei" de "mandei e ele nao viu".
				ChecaVd("F1 fora do alcance: a AUTORIDADE nao chamou o envio nenhuma vez",
					b.Quadros == 0 && b.Bytes == 0, $"{b.Quadros} quadros / {b.Bytes} B");
				break;

			case 4:
				ChecaVd("F1 (injecao) com o corte vazado a autoridade MANDOU -- o mutante e mesmo um vazamento",
					b.Quadros > 0, $"{b.Quadros} quadros / {b.Bytes} B");
				break;

			case 13:
				// O TETO E UMA AFIRMACAO SOBRE O QUE NAO ACONTECE: sem contar as recusas, "chegaram 50
				// por segundo" tambem seria verdade se o falante tivesse mandado 50.
				ChecaVd("F8 o teto por falante recusou o excesso de quem manda demais",
					recusados > 0, $"recusados {recusados} nesta fase");
				ChecaVd("F8 ...e o que passou ficou no ritmo honesto",
					b.Quadros <= (int)(VozLocal.QuadrosPorSegundo * SegundosPorFaseDupla * 1.3),
					$"{b.Quadros} quadros em {SegundosPorFaseDupla:0.0} s");
				break;

			case FaseDaTorneiraSolta:
				ChecaVd("F8 (injecao) com o balde esvaziado por tique, o excesso ATRAVESSA",
					b.Quadros > (int)(VozLocal.QuadrosPorSegundo * SegundosPorFaseDupla * 1.3),
					$"{b.Quadros} quadros -- a checagem do ritmo ficaria vermelha");
				break;

			case 15:
				ChecaVd("F7 calado: a AUTORIDADE nao entregou quadro nenhum",
					b.Quadros == 0, $"{b.Quadros} quadros");
				break;

			case 16:
				ChecaVd("F7 (injecao) esquecida a marca, o quadro do calado atravessa",
					b.Quadros > 0, $"{b.Quadros} quadros -- a checagem do mute ficaria vermelha");
				break;
		}
	}

	private void PlacarDaVozDupla()
	{
		GD.Print(_vdFalha == 0
			? $"[vozdupla] ============ o lado da AUTORIDADE: {_vdOk} OK, 0 FALHA ============"
			: $"[vozdupla] ============ o lado da AUTORIDADE: {_vdOk} OK, {_vdFalha} FALHA(S): "
			  + $"{string.Join(" | ", _vdReprovadas)} ============");
		// O OUVINTE E O ANFITRIAO: as duas metades do placar saem NESTE console, uma com o prefixo
		// `[vozdupla]` (o que a autoridade mandou) e a outra com `[vozdupla:b]` (o que chegou nela).
		GD.Print("[vozdupla] o placar das dez familias sai logo abaixo, com o prefixo [vozdupla:b].");
	}
}
