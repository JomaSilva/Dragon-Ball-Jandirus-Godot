using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DA VISTA POR ALTURA, COM **DUAS TELAS** (`--vista a` e `--vista b`).
///
/// ============================ POR QUE ELA NAO CABE NUM PROCESSO SO ============================
/// O pedido do dono e literalmente sobre DUAS telas:
///
///   *"voar mt alto faz vc N CONSEGUIR VER jogadores e npcs mt abaixo de vc e isso NAO DEVERIA
///   ACONTECER. somente o oposto: pessoas mt abaixo da sua altura N CONSEGUIRIAM TE VER, mas
///   pessoas em ALTURAS MAIORES q vc CONSEGUEM TE VER"*
///
/// Uma regra ASSIMETRICA so se prova comparando duas telas no mesmo instante: numa o corpo esta,
/// na outra nao. Num processo so os dois lados sao a MESMA memoria e a mesma resposta -- e o
/// `--diagvoo`, que ja cobre a regra pura, ficaria verde mesmo se `Client/World.cs` nunca
/// chamasse `Voo.Enxerga` (foi assim durante meses: o comentario descrevia a regra e um
/// `r.Visible = !e.Oculto` vinte linhas abaixo a apagava).
///
/// ============================ E POR QUE ELA NAO PODE PERGUNTAR AO `Voo.Enxerga` ============================
/// **A TABELA DE EXPECTATIVA DESTA BANCADA E ESCRITA A MAO** (<see cref="EuDevoVerOOutro"/>), das
/// palavras do dono, e nao chama a funcao que ela julga. Comparar a tela com `Voo.Enxerga` seria o
/// cego que a memoria "a bancada mede INTENCAO" ja catalogou: *"as duas telas concordam" fica verde
/// com as duas erradas igual*. Com a tabela a mao, devolver a regra antiga (simetrica) poe a linha
/// "DO ALTO SE VE O CHAO" vermelha -- e e exatamente o defeito que se injeta pra provar a bancada.
/// =========================================================================================================
///
/// ============================ O ROTEIRO -- QUATRO CONFIGURACOES ============================
/// Os dois papeis medem, os dois fotografam. Quem AGE em cada fase muda:
///
///   1. CHAO      A no andar 0, B no andar 0  -> os DOIS se veem      (o CONTRA-EXEMPLO)
///   2. RASANTE   A no andar 1, B no andar 0  -> os DOIS se veem      (o limiar NAO corta aqui)
///   3. ALTO      A no andar 2+, B no andar 0 -> A ve B; **B NAO ve A**   (o pedido)
///   4. INVERSAO  A no andar 0, B no andar 2+ -> B ve A; **A NAO ve B**   (quem esta em cima
///                                                                        mudou, e a vista virou
///                                                                        junto)
///
/// A fase 4 e a que separa "a regra pergunta QUEM esta em cima" de "a regra pergunta se a
/// diferenca e grande": as duas explicam as fases 1-3 igual, e so a inversao as distingue.
///
/// ============================ O GATILHO DA MEDIDA E A CONFIGURACAO, NAO O RELOGIO ============================
/// Dois processos nao tem relogio comum. Entao a medida NAO e agendada: cada lado observa o par
/// (meu andar, andar dele) -- os dois numeros vem do MESMO servidor, pelo snapshot -- e mede quando
/// o par fica estavel. Os dois medem a MESMA configuracao mesmo que alguns quadros separados, e cada
/// foto sai carimbada com o relogio de parede da maquina (os dois processos moram na mesma), o que
/// deixa a distancia entre as duas fotos LEGIVEL no relatorio em vez de prometida.
/// ==========================================================================================================
///
/// COMO RODAR: `testar-vista.bat` (ou os dois comandos que o .bat imprime).
/// </summary>
public partial class RoboDeVista : Node
{
	/// <summary>`a` sobe primeiro; `b` sobe na inversao. Os DOIS medem. Vem do `--vista`.</summary>
	public string Papel = "b";

	/// <summary>
	/// O NOME DO OUTRO -- e nao "o primeiro corpo do snapshot". O berco e povoado de NPC, e a
	/// primeira rodada de duas outras bancadas deste projeto mediu um Krillin. Vai nos DOIS
	/// processos, com o nome trocado. Ver `World.IdPeloNome`.
	/// </summary>
	public string Alvo = "";

	/// <summary>Segundos ate a bancada fechar sozinha, se as quatro fases nao sairem antes.</summary>
	public double Fim = 90;

	/// <summary>
	/// A ALTURA DO "ALTO". Fica no MEIO do andar 2 de proposito, e nao no teto: no teto o servidor
	/// tenta ROMPER A ATMOSFERA (`GameServer.Voo.TentarRomperAAtmosfera`) e, com o `--bpteste` que
	/// esta bancada precisa pro tanque de Ki durar, os portoes do `Space_Flight` ABREM -- o corpo
	/// sairia do planeta no meio da medida e as duas telas ficariam vazias por outro motivo.
	/// </summary>
	private static float AlturaDoAlto => Voo.AlturaMaxima / Voo.Andares * 1.5f;   // 320 px = meio do andar 2

	// =====================================================================
	// O PLACAR
	// =====================================================================
	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];
	private int _oks;

	private void Conferir(bool ok, string oque)
	{
		if (ok) _oks++; else _falhas.Add(oque);
		string linha = (ok ? "  ok    " : "  FALHA ") + oque;
		_passos.Add(linha);
		GD.Print($"[vista-{Papel}] {linha}");
	}

	private void Nota(string s)
	{
		_passos.Add("  --     " + s);
		GD.Print($"[vista-{Papel}] {s}");
	}

	// =====================================================================
	// AS FASES
	// =====================================================================
	private enum Fase { Nenhuma, Chao, Rasante, Alto, Inversao }

	/// <summary>
	/// QUE CONFIGURACAO E ESTA. Traduz o par de andares em uma das quatro do roteiro -- e traduz
	/// SEMPRE em termos de A e B, nao de "eu e ele", pra que os dois processos deem o MESMO nome a
	/// mesma cena. Sem isso cada lado teria a propria fase 3 e nao haveria par de fotos.
	/// </summary>
	private Fase Configuracao(int meuAndar, int andarDele)
	{
		int a = Papel == "a" ? meuAndar : andarDele;
		int b = Papel == "a" ? andarDele : meuAndar;

		if (a == 0 && b == 0) return Fase.Chao;
		if (a == 1 && b == 0) return Fase.Rasante;
		if (a >= 2 && b == 0) return Fase.Alto;
		if (a == 0 && b >= 2) return Fase.Inversao;
		return Fase.Nenhuma;   // no meio de uma subida ou descida: nao se mede transicao
	}

	/// <summary>
	/// ============================ A TABELA ESCRITA A MAO -- ELA E O JUIZ ============================
	/// Das palavras do dono, e de nenhuma funcao do jogo. Cada linha tem escrito o motivo:
	///
	///   CHAO      os dois no chao -> se veem. E o contra-exemplo: sem ele, um "some sempre" passaria
	///             verde nas outras tres.
	///   RASANTE   um andar de diferenca -> se veem. E a folga que impede o SOCADOR INVISIVEL: quem
	///             paira rasante ALCANCA quem esta no chao (`Voo.PodeAcertar(1,0)`), e levar soco de
	///             quem nao se ve seria incompreensivel.
	///   ALTO      A dois andares acima -> **A ve B, B NAO ve A**. *"pessoas mt abaixo da sua altura
	///             N CONSEGUIRIAM TE VER, mas pessoas em ALTURAS MAIORES q vc CONSEGUEM TE VER"*.
	///   INVERSAO  o mesmo, com os papeis trocados. Se a resposta NAO virar junto, a regra esta
	///             perguntando pela DIFERENCA e nao por QUEM esta em cima -- que e o defeito exato
	///             que o dono relatou.
	///
	/// **NAO CHAMA `Voo.Enxerga`.** Ver o cabecalho da classe.
	/// ============================================================================================
	/// </summary>
	private bool EuDevoVerOOutro(Fase f) => f switch
	{
		Fase.Chao => true,
		Fase.Rasante => true,
		Fase.Alto => Papel == "a",       // o de cima e o A
		Fase.Inversao => Papel == "b",   // ...e agora o de cima e o B
		_ => true,
	};

	private readonly HashSet<Fase> _medidas = [];
	private Fase _faseFalando = Fase.Nenhuma;
	private double _medirEm = -1;
	private double _limiteDoAperto;
	private Fase _ultimaConfig = Fase.Nenhuma;
	private double _estavel;

	// =====================================================================
	// AS DUAS INVARIANTES POR QUADRO
	// =====================================================================
	/// <summary>
	/// ============================ O QUE UMA FOTO POR FASE NAO PEGA ============================
	/// As quatro medidas sao quatro instantes. Um corpo que PISCA -- aparece e some entre snapshots,
	/// que e o sintoma classico de duas regras escrevendo o mesmo `Visible` -- passaria pelas quatro
	/// e ainda assim seria o defeito. Entao a regra do dono tambem e cobrada TODO QUADRO, nos dois
	/// sentidos, e o que se conta e quantos quadros a violaram.
	/// =======================================================================================
	/// </summary>
	private int _quadrosSumicoIndevido;   // ele esta no meu andar ou ABAIXO e mesmo assim sumiu
	private int _quadrosApariciaoIndevida;   // ele esta 2+ andares ACIMA e mesmo assim aparece
	private int _quadrosVigiados;

	// =====================================================================
	// O ESTADO DO SCRIPT
	// =====================================================================
	private bool _dentro;
	private bool _acabou;
	private double _relogio;
	private int _idDoOutro;
	private bool _separou, _decolou, _subindo, _pousou;
	private double _pousarEm, _decolarEm;

	/// <summary>O que o OUTRO disse no chat local. E a prova de que a fala NAO corta por altura.</summary>
	private string _ouviDoOutro = "";
	private int _falasDoOutro;

	/// <summary>
	/// ============================ A FASE QUE O OUTRO ANUNCIOU -- E O APERTO DE MAO ============================
	/// Dois processos nao tem relogio comum, e a primeira rodada desta bancada mostrou o preco: o
	/// Alfa reconheceu a fase CHAO no quadro em que o corpo do Beta apareceu, mediu 1,2 s depois e
	/// fotografou ANTES de o Beta ter dito qualquer coisa -- tres linhas vermelhas por relogio, nao
	/// por defeito.
	///
	/// Entao a fala deixou de ser so um consumidor medido e virou o SINCRONIZADOR: cada lado anuncia
	/// a fase em que se ve, e so mede quando ouve o OUTRO anunciar A MESMA. E o unico relogio que os
	/// dois processos compartilham -- o do servidor, pelo caminho do chat -- e ele e o que faz as
	/// duas fotos sairem no mesmo instante por CONSTRUCAO, e nao por sorte.
	/// =======================================================================================================
	/// </summary>
	private string _faseOuvida = "";

	private int _fotos;

	public override void _Ready()
	{
		if (GameClient.Instance is not { } cli) return;
		cli.Falou += AoOuvir;
		GD.Print($"[vista-{Papel}] no ar (alvo '{Alvo}', fim {Fim}s)");

		// O `Joined` JA PASSOU: este no nasce dentro do `Boot.EntrarNoJogo`, que e a resposta do
		// proprio `Joined`. Assinar o evento aqui e chegar depois da festa -- ja custou uma rodada
		// inteira em silencio na bancada dos dois corpos.
		cli.Joined += AoEntrar;
		if (cli.LocalId != 0) AoEntrar(cli.LocalId, cli.ZonaDeTeste, default, "(ja estava dentro)");
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is not { } cli) return;
		cli.Falou -= AoOuvir;
		cli.Joined -= AoEntrar;
		SoltarTudo();
	}

	private void AoEntrar(int id, ZoneKey z, Vec2 spawn, string nome)
	{
		if (_dentro) return;
		_dentro = true;
		_relogio = 0;
		GD.Print($"[vista-{Papel}] ENTREI: id {id} nome '{nome}' zona {z}");
	}

	private void AoOuvir(Protocol.Fala canal, string quem, string texto)
	{
		if (quem != Alvo || !texto.Contains("[vista]")) return;
		_ouviDoOutro = texto;
		_falasDoOutro++;

		// O rotulo da fase vem no fim do texto -- ver o `SendChat` do `Medir` e `_faseOuvida`.
		int i = texto.LastIndexOf("na fase ", StringComparison.Ordinal);
		if (i >= 0) _faseOuvida = texto[(i + 8)..].Trim();
	}

	private static void SoltarTudo()
	{
		Godot.Input.ActionRelease("subir");
		Godot.Input.ActionRelease("descer");
		Godot.Input.ActionRelease("move_right");
	}

	// =====================================================================
	// O LACO
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_acabou || !_dentro) return;

		// ============================ A QUEDA DA CONEXAO E UM FIM, E NAO UM SILENCIO ============================
		// O outro processo sai primeiro e leva o servidor junto (ele e o `--host`). Sem esta linha o
		// lado que sobra so ve `Connected` virar falso, sai deste metodo por cima e NUNCA imprime
		// placar -- a primeira rodada terminou assim, com o relatorio do Beta inteiro medido e nunca
		// escrito. Bancada que mede e nao fecha se le igual a bancada que travou.
		// ======================================================================================================
		if (GameClient.Instance is not { Connected: true } cli)
		{
			if (_medidas.Count > 0) Fechar("a conexao caiu (o outro lado fechou primeiro)");
			return;
		}
		if (World.Instancia is not { } mundo) return;

		_relogio += delta;

		// ---- achar o corpo do outro, pelo NOME ----
		if (_idDoOutro == 0)
		{
			_idDoOutro = mundo.IdPeloNome(Alvo);
			if (_idDoOutro == 0)
			{
				if (_relogio > Fim) Fechar("o outro nunca entrou no meu campo de visao");
				return;
			}
			GD.Print($"[vista-{Papel}] o outro ('{Alvo}') e o id {_idDoOutro}");
			_relogio = 0;   // o relogio do roteiro so comeca quando HA os dois corpos
		}

		if (mundo.CorpoDeTeste(_idDoOutro) is not { } corpoDele || !IsInstanceValid(corpoDele))
		{
			if (_relogio > Fim) Fechar("o corpo do outro sumiu da minha arvore");
			return;
		}

		int meuAndar = Voo.Andar(mundo.AlturaDeTeste);
		int andarDele = Voo.Andar(corpoDele is RemotePlayer rp ? rp.AlturaDeTeste : 0f);
		bool naTela = corpoDele.IsVisibleInTree();

		VigiarPorQuadro(meuAndar, andarDele, naTela);
		Agir(cli, mundo, corpoDele, meuAndar, andarDele);
		Medir(delta, cli, mundo, corpoDele, meuAndar, andarDele, naTela);

		if (_medidas.Count >= 4 && _relogio > _medirEm + 2) Fechar("as quatro configuracoes foram medidas");
		else if (_relogio > Fim) Fechar($"o tempo acabou ({Fim:0}s)");
	}

	/// <summary>A regra do dono cobrada TODO QUADRO, nos dois sentidos. Ver os dois contadores.</summary>
	private void VigiarPorQuadro(int meuAndar, int andarDele, bool naTela)
	{
		// So depois de a primeira configuracao ter sido reconhecida: antes disso o snapshot do outro
		// corpo pode nem ter chegado, e contar isso como "sumiu" seria contar a propria partida.
		if (_medidas.Count == 0) return;
		_quadrosVigiados++;

		if (andarDele <= meuAndar && !naTela) _quadrosSumicoIndevido++;
		if (andarDele - meuAndar >= 2 && naTela) _quadrosApariciaoIndevida++;
	}

	// =====================================================================
	// O QUE CADA PAPEL FAZ -- e NADA e escrito na mao: tudo passa pelo funil do jogador
	// =====================================================================
	/// <summary>
	/// ============================ A ALTURA NAO E FORJADA ============================
	/// Nenhuma linha aqui escreve altitude. O corpo decola pelo `SendHabilidade("voar")` (o mesmo
	/// pacote do botao) e sobe segurando a acao `subir` (a mesma que o teclado escreve) -- quem
	/// integra a altura e o servidor, e e ele quem a devolve no snapshot. Uma bancada que escrevesse
	/// a altura nos dois lados provaria que `Voo.Andar` divide numeros, e nao que o jogo desenha.
	/// ==============================================================================
	/// </summary>
	private void Agir(GameClient cli, World mundo, Node2D corpoDele, int meuAndar, int andarDele)
	{
		// ---------- 1) SAIR DE CIMA DO OUTRO ----------
		// Os dois nascem no MESMO ponto de spawn. Corpos empilhados nao se leem na foto (a bancada
		// dos dois corpos ja quase colheu um falso positivo com o cabelo de um aparecendo atras do
		// outro) e, pior aqui, `CaixasSeTocam` daria verdade pros dois na grade de colisao -- a
		// medida do consumidor de colisao mediria "encostados" em vez de "andares diferentes".
		if (!_separou)
		{
			Vector2 meu = mundo.PosicaoLocal ?? Vector2.Zero;
			float dist = meu.DistanceTo(corpoDele.Position);
			bool longe = dist >= 3 * ZoneCollision.TileSize;
			if (Papel == "b" && !longe) { Godot.Input.ActionPress("move_right"); return; }
			Godot.Input.ActionRelease("move_right");
			if (!longe && _relogio < 12) return;   // o A espera o B andar
			_separou = true;
			_decolarEm = _relogio + 3;   // um respiro no chao: a fase CHAO precisa existir
			Nota($"os dois corpos estao separados por {dist:0} px");
			return;
		}

		// ---------- 2) O DE CIMA DA VEZ SOBE ----------
		// Na fase ALTO quem sobe e o A; na INVERSAO, o B. Um papel so age quando chega a vez dele.
		bool minhaVezDeSubir = (Papel == "a" && !_medidas.Contains(Fase.Alto))
							|| (Papel == "b" && _medidas.Contains(Fase.Alto));

		if (minhaVezDeSubir)
		{
			if (!_decolou && _relogio >= _decolarEm && _medidas.Contains(Fase.Chao))
			{
				_decolou = true;
				cli.SendHabilidade("voar");   // para sozinho na altura de pairar = andar 1
				Nota("decolando (`SendHabilidade voar`) -- o corpo para no andar 1");
				return;
			}

			// So depois de a fase RASANTE existir e que faz sentido subir: subir antes apagaria a
			// prova de que UM andar de diferenca NAO esconde ninguem.
			bool podeSubir = Papel == "a" ? _medidas.Contains(Fase.Rasante) : _decolou;
			if (_decolou && podeSubir && mundo.AlturaDeTeste < AlturaDoAlto)
			{
				if (!_subindo) { _subindo = true; Nota($"subindo ate {AlturaDoAlto:0} px (o meio do andar 2)"); }
				Godot.Input.ActionPress("subir");
			}
			else if (_subindo)
			{
				_subindo = false;
				Godot.Input.ActionRelease("subir");
				Nota($"parei de subir em {mundo.AlturaDeTeste:0} px = andar {meuAndar}");
			}
			return;
		}

		// ---------- 3) O DE CIMA DA FASE ANTERIOR DESCE ----------
		// A INVERSAO exige que o A volte pro chao: se ele ficasse la em cima com o B subindo, a
		// configuracao seria "dois voando" e a pergunta do dono nao seria feita.
		if (Papel == "a" && _medidas.Contains(Fase.Alto) && !_pousou)
		{
			_pousou = true;
			_subindo = false;
			Godot.Input.ActionRelease("subir");
			if (_decolou) cli.SendHabilidade("voar");   // o mesmo botao, desligando
			Godot.Input.ActionPress("descer");
			Nota("pousando -- e a vez do OUTRO subir (a INVERSAO)");
		}
		if (_pousou && mundo.AlturaDeTeste <= 0.01f) Godot.Input.ActionRelease("descer");
	}

	// =====================================================================
	// A MEDIDA -- duas telas, o mesmo instante
	// =====================================================================
	private void Medir(double delta, GameClient cli, World mundo, Node2D corpoDele,
					   int meuAndar, int andarDele, bool naTela)
	{
		Fase agora = Configuracao(meuAndar, andarDele);
		if (agora != _ultimaConfig) { _ultimaConfig = agora; _estavel = 0; }
		else _estavel += delta;

		if (agora == Fase.Nenhuma || _medidas.Contains(agora)) return;

		// ---- passo 1: a configuracao ficou parada -> ANUNCIO em que fase estou ----
		if (_faseFalando != agora)
		{
			if (_estavel < 0.7) return;
			_faseFalando = agora;
			_medirEm = -1;
			_limiteDoAperto = _relogio + 8;
			cli.SendChat(Protocol.Fala.Diz, $"[vista] {cli.LocalName} na fase {agora}");
			return;
		}

		// ---- passo 2: O APERTO DE MAO -- so mede quando o OUTRO anuncia a MESMA fase ----
		// Ver `_faseOuvida`: e o unico relogio que os dois processos compartilham, e e ele que faz
		// as duas fotos sairem no mesmo instante por construcao. O prazo existe pra que uma rodada
		// em que o outro emudeceu ainda ENTREGUE veredito -- vermelho na linha do chat, e nao
		// travada esperando pra sempre.
		if (_medirEm < 0)
		{
			if (_faseOuvida == agora.ToString()) _medirEm = _relogio + 0.3;
			else if (_relogio >= _limiteDoAperto) _medirEm = _relogio;
			else return;
		}

		// ---- passo 3: mede, fotografa, carimba a hora ----
		if (_relogio < _medirEm) return;
		_medidas.Add(agora);

		bool esperado = EuDevoVerOOutro(agora);
		string hora = DateTime.Now.ToString("HH:mm:ss.fff");
		string cena = $"fase {agora} (eu no andar {meuAndar}, {Alvo} no andar {andarDele})";

		Nota($"===== {cena} -- relogio de parede {hora} =====");

		// ============================ A LINHA QUE O DONO PEDIU ============================
		// O `IsVisibleInTree` e a pergunta da TELA e nao a do campo: um `Visible = true` num node
		// cujo pai esta apagado nao desenha pixel nenhum, e "esta na tela" e o que se fotografa.
		// ================================================================================
		Conferir(naTela == esperado,
			esperado
				? $"{cena}: EU VEJO {Alvo} -- o corpo dele esta desenhado na minha tela"
				: $"{cena}: EU **NAO** VEJO {Alvo} -- o corpo dele NAO esta na minha tela");

		// ---- o consumidor 1: o CHAT LOCAL ----
		// Ele NAO corta por altura, e isso e informacao pro dono decidir: na fase ALTO o de baixo
		// LE o que o de cima falou sem ver de quem veio. Se um dia isso tiver que mudar, e aqui que
		// a linha vira vermelha.
		//
		// A COBRANCA E PELA FASE ANUNCIADA, e nao por "chegou alguma fala": um contador maior que
		// zero ficaria verde com a fala da fase ANTERIOR, que e justamente o que aconteceria se o
		// chat tivesse parado de chegar no meio da rodada.
		Conferir(_faseOuvida == agora.ToString(),
			$"{cena}: o CHAT LOCAL de {Alvo} chegou no meu cliente e ele anunciou ESTA fase "
			+ $"(\"{_ouviDoOutro}\") -- a fala nao corta por altura, so por zona/distancia");

		// ---- o consumidor 2: o BALAO DE FALA ----
		// Ele e FILHO do corpo, entao ele segue a visibilidade do corpo sem uma regra propria. As
		// duas metades sao cobradas juntas: o TEXTO chegou no node (o chat entregou) e o node so
		// DESENHA se o dono desenha.
		if (corpoDele.GetNodeOrNull<BalaoDeFala>("Balao") is { } balao)
		{
			bool temTexto = balao.TextoDeTeste.Contains(agora.ToString()) || balao.NaFilaDeTeste > 0;
			Conferir(temTexto,
				$"{cena}: o texto dele CHEGOU no balao do corpo dele (\"{balao.TextoDeTeste}\")");
			Conferir(balao.IsVisibleInTree() == esperado,
				esperado
					? $"{cena}: ...e o balao DESENHA, porque o corpo desenha"
					: $"{cena}: ...mas o balao **NAO DESENHA**, porque ele e filho de um corpo apagado");
		}
		else Nota($"{cena}: o corpo dele nao tem node `Balao` -- o consumidor do balao ficou sem medida");

		// ---- o consumidor 3: a GRADE DE COLISAO DO CLIENTE ----
		// `World.MontarGradeDeCorpos` filtra por `Visible`, entao a correcao POE mais corpos na
		// grade de quem esta em cima. A pergunta que importa nao e essa: e se algum deles passou a
		// BARRAR. Nao passou, e o motivo e `ClasseDeCorpo.MesmoAndar`, que e igualdade estrita.
		var grade = new GradeDeCorpos();
		mundo.MontarGradeDeCorpos(grade);
		Vec2 pesDele = ClasseDeCorpo.Pes(new Vec2(corpoDele.Position.X, corpoDele.Position.Y));
		Vector2 minha = mundo.PosicaoLocal ?? Vector2.Zero;
		Vec2 pesMeus = ClasseDeCorpo.Pes(new Vec2(minha.X, minha.Y));

		// O MODO E `APe` PORQUE A PERGUNTA AQUI E DE ANDAR, E NAO DE OCUPACAO: o que esta bancada mede e
		// se o corpo apagado sumiu da grade e se andares diferentes deixam de colidir. Perguntar voando
		// misturaria a regra nova (`ClasseDeCorpo.Bloqueia` com a `Ocupacao`) numa medida que e sobre
		// visibilidade.
		int noAndarDele = grade.Quem(pesDele, andarDele, cli.LocalId, pesMeus, ModoDeTravessia.APe);
		int noMeuAndar = grade.Quem(pesDele, meuAndar, cli.LocalId, pesMeus, ModoDeTravessia.APe);

		Conferir(esperado ? noAndarDele == _idDoOutro : noAndarDele == 0,
			esperado
				? $"{cena}: {Alvo} ENTROU na minha grade de corpos (id {noAndarDele})"
				: $"{cena}: {Alvo} NAO entrou na minha grade -- corpo apagado nao barra ninguem");
		Conferir(noMeuAndar == (meuAndar == andarDele ? _idDoOutro : 0),
			meuAndar == andarDele
				? $"{cena}: ...e no MEU andar ele me barra (o contra-exemplo da colisao)"
				: $"{cena}: ...e mesmo na grade ele NAO me barra: andares diferentes nao colidem "
				+ "(`ClasseDeCorpo.MesmoAndar` e igualdade estrita)");

		// ---- o consumidor 4: o ALCANCE DO SOCO ----
		// `Voo.PodeAcertar` nao foi tocado e continua sendo outra funcao. O que esta bancada cobra e
		// a INCLUSAO, na configuracao viva: se ele me alcanca, eu tenho que ve-lo. Sem isso, a folga
		// de um andar pra cima poderia ser zerada por engano e o jogo ganharia um socador invisivel.
		bool eleMeAlcanca = Voo.PodeAcertar(andarDele, meuAndar);
		Conferir(!eleMeAlcanca || naTela,
			$"{cena}: ele {(eleMeAlcanca ? "ME ALCANCA" : "nao me alcanca")} e eu "
			+ $"{(naTela ? "O VEJO" : "NAO o vejo")} -- ninguem leva soco de quem nao enxerga");

		Nota($"{cena}: Ki {cli.Sheet.Ki:0} | minha altura {mundo.AlturaDeTeste:0} px "
			 + $"| altura dele {(corpoDele is RemotePlayer r2 ? r2.AlturaDeTeste : 0f):0} px");

		Fotografar($"user://vista-{Papel}-{(int)agora}-{agora}.png", hora, cena);
	}

	/// <summary>Salva a tela. No headless o `GetImage` volta vazio -- esta bancada QUER janela.</summary>
	private void Fotografar(string destino, string hora, string cena)
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty())
			{
				Nota($"SEM FOTO de '{cena}' (headless nao renderiza) -- e METADE da prova que falta");
				return;
			}
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			_fotos++;
			Nota($"foto {hora}: {caminho}");
		}
		catch (Exception e) { Nota("sem foto: " + e.Message); }
	}

	// =====================================================================
	// O FECHO
	// =====================================================================
	private void Fechar(string porque)
	{
		if (_acabou) return;
		_acabou = true;
		SoltarTudo();

		// ============================ UMA BANCADA QUE NAO MEDIU NADA REPROVA ============================
		// O modo de falha mais perigoso de duas telas: o outro processo nao subiu, o corpo nunca
		// entrou no campo de visao, a altura nunca chegou no andar 2 -- e o placar sai "0 falhas",
		// que se le exatamente igual a "tudo certo".
		// ==============================================================================================
		Conferir(_medidas.Count == 4,
			$"as QUATRO configuracoes foram medidas ({_medidas.Count}: "
			+ $"{string.Join(", ", _medidas)}) -- sem a INVERSAO nao se prova que a regra pergunta "
			+ "QUEM esta em cima, e nao se a diferenca e grande");

		Conferir(_fotos >= 4 || Array.IndexOf(OS.GetCmdlineArgs(), "--headless") >= 0,
			$"as quatro fotos sairam ({_fotos})");

		// ---- as duas invariantes por quadro ----
		Conferir(_quadrosSumicoIndevido == 0,
			$"em NENHUM dos {_quadrosVigiados} quadros vigiados alguem no meu andar ou ABAIXO sumiu "
			+ $"da minha tela ({_quadrosSumicoIndevido} quadro(s) com sumico indevido) -- e o pedido "
			+ "do dono cobrado por quadro, e nao por foto");
		Conferir(_quadrosApariciaoIndevida == 0,
			$"...e em nenhum deles alguem DOIS andares acima apareceu ({_quadrosApariciaoIndevida} "
			+ "quadro(s)) -- a folga pra cima continua sendo de UM andar so");

		// ---- a inclusao, na tabela inteira e nao so nas configuracoes vividas ----
		bool ninguemBateInvisivel = true;
		for (int atacante = 0; atacante <= Voo.Andares; atacante++)
			for (int alvo = 0; alvo <= Voo.Andares; alvo++)
				if (Voo.PodeAcertar(atacante, alvo)
					&& !Voo.Enxerga(andarDeQuemOlha: alvo, andarDeQuemEVisto: atacante))
					ninguemBateInvisivel = false;
		Conferir(ninguemBateInvisivel,
			"e na TABELA INTEIRA de andares, tudo o que acerta alguem e visto por ele "
			+ "(`PodeAcertar` cabe dentro de `Enxerga`)");

		var sb = new StringBuilder();
		sb.AppendLine($"\n[vista-{Papel}] ===== BANCADA DA VISTA POR ALTURA ({porque}) =====");
		foreach (string l in _passos) sb.AppendLine($"[vista-{Papel}] " + l);
		sb.AppendLine($"[vista-{Papel}] ===== {_oks} OK, {_falhas.Count} FALHA =====");
		foreach (string f in _falhas) sb.AppendLine($"[vista-{Papel}]   FALHA  " + f);
		GD.Print(sb.ToString());
		if (_falhas.Count > 0) GD.PrintErr($"[vista-{Papel}] {_falhas.Count} FALHA(S)");

		// O RELATORIO EM DISCO: sao dois processos, e duas janelas fechando nao deixam um console
		// unico pra ler. O .bat imprime os dois arquivos no fim.
		try
		{
			using var f = Godot.FileAccess.Open($"user://vista-{Papel}.txt", Godot.FileAccess.ModeFlags.Write);
			f?.StoreString(sb.ToString());
		}
		catch (Exception e) { GD.Print($"[vista-{Papel}] sem relatorio em disco: {e.Message}"); }

		GetTree().Quit();
	}
}
