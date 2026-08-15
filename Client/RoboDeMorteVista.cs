using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DA MORTE VISTA DE FORA (`--morte a` e `--morte b`). DOIS PROCESSOS, e nao cabe em um.
///
/// ============================ POR QUE ELA PRECISA EXISTIR ============================
/// A auréola ja tinha DUAS bancadas verdes, e nenhuma das duas olhava pra ela do jeito que o jogo
/// olha:
///
///   * a `--alemteste` e de SERVIDOR: mede a viagem, a triagem e o byte do fio. Ela nunca desenha
///     nada, e por construcao nao pode dizer se aquilo aparece na tela de alguem.
///   * o bloco do `--diagforma` (`RoboDeForma.AAureolaSobreACabeca`) chama `MostrarAureola(true)` NA
///     MAO, num boneco do proprio processo. Prova a FUNCAO, e nao o percurso -- o corpo dele nunca
///     morreu, nunca viajou, e nunca veio pela rede.
///
/// O sinal da auréola e **pros OUTROS**: ela existe pra que quem olha saiba que aquele corpo esta
/// morto. Um processo so nao tem "os outros". Aqui a morte acontece num soquete e e olhada de outro,
/// que e a unica configuracao em que a afirmacao do dono ("a auréola aparece sobre a cabeca") pode
/// ser verdadeira ou falsa.
///
/// ============================ E ELA MATA NO COMBATE, NAO NA BANDEIRA ============================
/// O `admin_matar` chama o mesmo `Morrer()` e seria mais curto -- e mediria menos. O caminho que o
/// jogo usa e SOCO LETAL ATE UM VITAL QUEBRAR (`Body.Ferir` com `letal: true`, o piso de 19,8% que
/// o nao-letal respeita deixa de valer, `MeleeResolver.AplicarNoMembro`). Entre a bandeira e o soco
/// moram o nocaute, a exposicao do caido (10% do BP) e a diferenca entre "KO" e "morto" -- que sao
/// coisas distintas neste jogo e a auréola tem que separar. Uma bancada que forca a bandeira nunca
/// olharia pra um corpo NOCAUTEADO pra conferir que ele NAO tem auréola.
///
/// ============================ OS DOIS PAPEIS ============================
/// `--morte a` e O MORTO. Ele nao bate, nao foge e nao mede nada da tela: ele APANHA. O que ele
/// mede e o que so o dono do corpo sabe -- em que zona ele estava quando morreu, em que zona ele
/// acordou, e se o servidor deixou um MORTO voar (a resposta do DM e sim, e e por isso que a folha
/// da auréola tem `flight_mov`). Despeja num arquivo do `user://`, que o olhador le no veredito.
///
/// `--morte b` e O OLHADOR. Ele hospeda (e por isso e admin), mata no combate, e fotografa. Ele e
/// quem tem placar. As fotos sao o unico juiz de "da pra entender?" -- nenhum numero de camada
/// responde isso.
///
/// ============================ O ROTEIRO, E O PORQUE DE CADA PARADA ============================
///   1. os dois VIVOS, lado a lado          -- a foto de controle. Sem ela, "tem um circulo amarelo
///                                             na tela" nao prova nada: podia estar sempre la.
///   2. o CADAVER caido no berco, SEM auréola -- os 15 s de `Alem.MsNoChao`, e o unico instante em
///                                             que o morto e o vivo aparecem no MESMO quadro sem que
///                                             ninguem tenha teleportado. **Esta parada era o bug do
///                                             dono** (*"o corpo q fica no MAPA DOS VIVOS deveria ser
///                                             o EXATO CORPO DELE QUANDO MORRE, sem a aureola"*) e a
///                                             bancada a afirmava: ela media o crivo em vez do DM,
///                                             onde o cadaver e fotografado antes de a auréola
///                                             existir (`Death.dm:64-67` x `:106-108`).
///   3. o Outro Mundo                       -- o olhador segue pelo `admin_ir` e SE AFASTA antes de
///                                             fotografar: o `admin_ir` pousa os dois na MESMA
///                                             posicao, e dois bonecos empilhados nao sao uma foto.
///   4. o morto VOANDO                      -- a familia que a folha de `Icons/Misc` existe pra
///                                             atender (a de `Clothes` nao tem pose de voo).
///   5. o REVIVIDO                          -- `admin_reviver` chama `Restaurar`, que NAO teleporta:
///                                             o corpo fica no lugar e so a auréola muda. E o par
///                                             exato da foto 3, mesmo enquadramento, um bit de
///                                             diferenca.
///
/// COMO RODAR -- `ver-a-morte.bat`. O B PRIMEIRO, e com JANELA: headless nao renderiza e as fotos
/// saem vazias (`GetImage` volta nulo). O A entra 8 s depois, e headless mesmo, porque a tela dele
/// nao e a pergunta.
///
/// ============================ POR QUE O B PROMOVE A PROPRIA CONTA NO 1o SEGUNDO ============================
/// O servidor desliga o admin-por-endereco assim que DUAS contas chegam de endereco local
/// (`GameServer.ConferirAmbiguidadeDeHost`), e com razao. Os dois processos moram na mesma maquina
/// por construcao, entao sem a marca no disco o `admin_ir` do meio da rodada sairia e ninguem
/// responderia -- e o log leria "a viagem nao acontece" quando na verdade o comando nunca valeu.
/// ==========================================================================================
/// </summary>
public partial class RoboDeMorteVista : Node
{
	/// <summary>`a` morre, `b` mata e olha. Vem do `--morte`.</summary>
	public string Papel = "b";

	/// <summary>A conta DESTE processo, pro `admin_promover` -- ver o cabecalho.</summary>
	public string Conta = "";

	/// <summary>O nome do OUTRO (`--mortealvo`). O berco tem NPC: "o primeiro do snapshot" nao serve.</summary>
	public string Alvo = "";

	/// <summary>Segundos de rodada antes de desistir e fechar com o que houver.</summary>
	public double Fim = 200;

	// =====================================================================
	// PLACAR
	// =====================================================================
	private int _oks;
	private readonly List<string> _falhas = [];
	private readonly List<string> _diario = [];

	private void Conferir(bool ok, string oque)
	{
		if (ok) _oks++; else _falhas.Add(oque);
		string linha = (ok ? "  ok   " : "  FALHA") + "  " + oque;
		_diario.Add(linha);
		GD.Print($"[morte-{Papel}] " + linha);
	}

	private void Anotar(string s)
	{
		_diario.Add("  --     " + s);
		GD.Print($"[morte-{Papel}] {s}");
	}

	// =====================================================================
	// ENTRADA
	// =====================================================================
	private bool _dentro, _fechou;
	private double _relogio;
	private int _idDoOutro;
	private Vector2? _posDoOutro;
	private ZoneKey _minhaZona;

	public override void _Ready()
	{
		if (GameClient.Instance is not { } cli) return;
		// METODO NOMEADO E `-=` NO `_ExitTree`, nunca lambda: lambda nao da pra cancelar, e este
		// projeto ja pagou 19 assinaturas orfas por ciclo de relog por causa disso.
		cli.Joined += AoEntrar;
		cli.ZoneChanged += AoTrocarDeZona;
		cli.SnapshotReceived += AoReceberSnapshot;
		cli.AureolaMudou += AoMudarAureola;
		cli.Golpe += AoGolpe;
		GD.Print($"[morte-{Papel}] no ar (alvo '{Alvo}', fim {Fim}s)");

		// O `Joined` JA PASSOU quando este no nasce -- ele e criado dentro da resposta dele. Sem esta
		// linha o relogio nunca comecava, e a rodada inteira saia muda.
		if (cli.LocalId != 0) AoEntrar(cli.LocalId, cli.ZonaDeTeste, default, cli.LocalName);
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is not { } cli) return;
		cli.Joined -= AoEntrar;
		cli.ZoneChanged -= AoTrocarDeZona;
		cli.SnapshotReceived -= AoReceberSnapshot;
		cli.AureolaMudou -= AoMudarAureola;
		cli.Golpe -= AoGolpe;
	}

	private void AoEntrar(int id, ZoneKey z, Vec2 spawn, string nome)
	{
		if (_dentro) return;
		_dentro = true;
		_relogio = 0;
		_minhaZona = z;
		Anotar($"ENTREI: id {id} nome '{nome}' zona '{z.Name}'");
	}

	/// <summary>
	/// A ZONA E O SEGUNDO MAIS IMPORTANTE DESTA BANCADA, e ela chega aqui pros DOIS papeis com
	/// sentidos diferentes: no A e a resposta de "pra onde a morte me levou"; no B e a confirmacao
	/// de que o `admin_ir` funcionou e de que ele esta olhando o Outro Mundo, e nao o berco.
	/// </summary>
	private void AoTrocarDeZona(ZoneKey z, Vec2 spawn)
	{
		_minhaZona = z;
		_trocasDeZona++;
		Anotar($"t={_relogio:0.0}s  TROQUEI DE ZONA -> '{z.Name}'"
			 + (Alem.EhOAlem(z.Name) ? "  (E O ALEM)" : ""));
		if (Papel == "a" && Alem.EhOAlem(z.Name) && _zonaDoAlem.Length == 0)
		{
			_zonaDoAlem = z.Name;
			_cheguei = _relogio;
		}
	}

	private int _trocasDeZona;
	private string _zonaDoAlem = "";
	private double _cheguei;

	/// <summary>
	/// O SNAPSHOT E A UNICA FONTE DE POSE E DE VOO, pros DOIS corpos -- e ele carrega o meu tambem
	/// (o servidor manda a zona inteira, e por isso o `RoboDeSoco` precisa daquele
	/// `if (e.Id == LocalId) continue`).
	///
	/// ============================ O NOCAUTE TEM QUE SER COLHIDO POR PACOTE ============================
	/// Ele e um estado que PASSA: a cabeca quebra, o corpo cai, e a morte vem por cima em poucos
	/// segundos. Uma leitura pontual no fim acharia so o cadaver -- e a afirmacao que separa as duas
	/// coisas ("KO nao acende auréola") ficaria sem prova nenhuma, que e justamente a confusao que a
	/// auréola existe pra evitar na tela.
	/// ==============================================================================================
	/// </summary>
	private void AoReceberSnapshot(List<EntityState> estados)
	{
		if (GameClient.Instance is not { } cli) return;
		foreach (EntityState e in estados)
		{
			if (e.Id == cli.LocalId) { _euVoando = e.Voando; continue; }
			if (e.Id != _idDoOutro) continue;
			_posDoOutro = new Vector2(e.Pos.X, e.Pos.Y);
			_outroVoando = e.Voando;
			_poseDoOutro = e.Pose;
			if (_morreuEm <= 0 && e.Pose == Protocol.Pose.Nocauteado && !cli.ComAureola.Contains(_idDoOutro))
				_viOKoSemAureola = true;
		}
	}

	private bool _euVoando, _outroVoando;
	private Protocol.Pose _poseDoOutro;

	/// <summary>
	/// O NOCAUTE SEM AUREOLA foi visto ao menos uma vez, no meio da briga. Ver
	/// <see cref="AoReceberSnapshot"/>.
	/// </summary>
	private bool _viOKoSemAureola;

	private void AoGolpe(Protocol.HitEvent h)
	{
		if (GameClient.Instance is not { } cli) return;
		if (h.Atacante == cli.LocalId && h.TemDano) { _acertos++; _danoDado += h.Dano; }
		if (h.Alvo == cli.LocalId && h.TemDano) _danoLevado += h.Dano;
	}

	private int _acertos;
	private double _danoDado, _danoLevado;

	/// <summary>
	/// O PACOTE DA AUREOLA CHEGANDO. E o unico sinal que separa "morto" de "nocauteado" na tela de
	/// quem olha -- o snapshot manda a pose `Nocauteado` pros dois (ver `Protocol.cs:735-757`).
	/// </summary>
	private void AoMudarAureola(int quem)
	{
		if (GameClient.Instance is not { } cli) return;
		bool tem = cli.ComAureola.Contains(quem);
		Anotar($"t={_relogio:0.0}s  S2C.Aureola: id {quem} -> {(tem ? "ACENDEU" : "apagou")}"
			 + (quem == _idDoOutro ? "  (e o outro)" : quem == cli.LocalId ? "  (sou eu)" : ""));

		// ============================ AQUI *NAO* SE MARCA A MORTE, E ISSO E O ACHADO ============================
		// Esta linha era `if (... tem && _morreuEm <= 0) _morreuEm = _relogio;` -- a bancada usava a
		// AUREOLA como gatilho pra tudo o que vem depois da morte, inclusive pra tirar a foto do
		// cadaver e pra afirmar que o cadaver **nao** tem auréola.
		//
		// Enquanto a auréola acendia junto com `Ficha.dead` isso funcionava por acidente. Com o
		// conserto (`Alem.TemAureola` = morto **e ja viajou**) o pacote passou a sair 15 s depois, na
		// VIAGEM, e a rodada inteira ficou muda: houve morte de verdade (o proprio morto anotou
		// `morto=1` em t=40,3s e acordou no 'Afterlife'), a bancada nunca tirou as fotos 2 e 2b, e o
		// placar leu *"NAO CONSEGUI MATAR no combate em 150 s"*. **A linha que mede "o cadaver nao tem
		// auréola" morava dentro de um caminho que so a auréola abria.**
		//
		// A regra que faltava: uma bancada nao pode se disparar pelo SINAL QUE ELA JULGA. Quem sabe a
		// hora da morte e o dono do corpo, e ele ja escreve isso no `user://` a cada 0,5 s -- ver
		// <see cref="OMortoJaSeAnunciou"/>.
		// ==================================================================================================
		if (quem == _idDoOutro && tem && _aureolaAcendeuEm <= 0) _aureolaAcendeuEm = _relogio;
	}

	private double _morreuEm = -1;

	/// <summary>Quando o fio anunciou a auréola DELE. Ver <see cref="AoMudarAureola"/>.</summary>
	private double _aureolaAcendeuEm = -1;

	/// <summary>
	/// ============================ QUEM AVISA DA MORTE E O MORTO, E NAO A AUREOLA ============================
	/// O papel A despeja a propria ficha no `user://` a cada meio segundo (<see cref="Despejar"/>), e
	/// `morto` la vem do byte `Estado &amp; 2` -- o mesmo que a HUD le, e um caminho INDEPENDENTE do pacote
	/// da auréola. E por isso que ele serve de gatilho: o que esta sendo julgado aqui e o desenho, e o
	/// gatilho tem que vir de fora do desenho.
	///
	/// O NOME E CONFERIDO de proposito: o `user://` e um so por projeto, e um arquivo deixado por uma
	/// rodada anterior (ou por outra sessao rodando a mesma bancada na mesma maquina) marcaria uma
	/// morte que nao aconteceu nesta briga -- que e exatamente o que ja aconteceu uma vez aqui, com o
	/// veredito lendo `zona_do_alem` de um morto de meia hora antes.
	/// ====================================================================================================
	/// </summary>
	private bool OMortoJaSeAnunciou()
	{
		Dictionary<string, string> d = LerOMorto();
		return d.TryGetValue("nome", out string? n) && n == Alvo
			&& d.TryGetValue("morto", out string? m) && m == "1";
	}

	public override void _Process(double delta)
	{
		if (_fechou || !_dentro) return;
		if (GameClient.Instance is not { Connected: true } cli) return;
		_relogio += delta;

		// O OUTRO SE ACHA PELO NOME. "O primeiro corpo do snapshot" nao serve: o berco tem NPC, e a
		// primeira rodada da bancada do desvio terminou com o socador nocauteado pelo Krillin.
		if (_idDoOutro == 0 && Alvo.Length > 0 && World.Instancia is { } mundo)
		{
			int achei = mundo.IdPeloNome(Alvo);
			if (achei != 0 && achei != cli.LocalId)
			{
				_idDoOutro = achei;
				Anotar($"achei o outro: '{Alvo}' = id {_idDoOutro} (t={_relogio:0.0}s)");
				if (Papel == "b") cli.SendAlvo(_idDoOutro);
			}
		}

		if (Papel == "a") PassoDoMorto(cli, delta);
		else PassoDoOlhador(cli, delta);

		if (_relogio >= Fim) { Julgar(cli); Fechar(); }
	}

	// =====================================================================================
	// O PAPEL A -- O MORTO. Ele apanha e conta onde acordou.
	// =====================================================================================
	private bool _viMinhaMorte, _pediuVoo, _voando, _pousou;
	private double _morriEm = -1;
	private string _zonaAoMorrer = "";
	private double _proximoDespejo;

	/// <summary>
	/// Quantos segundos depois de CHEGAR no alem o morto tenta decolar.
	///
	/// Nao e numero redondo: a viagem so acontece aos 15 s (`Alem.MsNoChao`) e a volta automatica aos
	/// 60 s depois dela (`Alem.MsNoAlem`), entao a janela inteira do Outro Mundo tem um minuto. O voo
	/// precisa caber ANTES do olhador pedir o revive, e o olhador precisa ter tempo de chegar
	/// (`admin_ir`) e de se afastar antes de fotografar.
	/// </summary>
	private const double EsperaAntesDeVoar = 14.0;

	private void PassoDoMorto(GameClient cli, double delta)
	{
		// A MORTE, PELA MINHA PROPRIA FICHA. O byte `Estado & 2` e o mesmo que a HUD le; ele nao vem
		// do pacote da auréola, entao os dois lados desta bancada afirmam a morte por caminhos
		// diferentes -- e e isso que faz uma confirmar a outra em vez de repeti-la.
		if (!_viMinhaMorte && cli.Sheet.Morto)
		{
			_viMinhaMorte = true;
			_morriEm = _relogio;
			_zonaAoMorrer = _minhaZona.Name;
			Anotar($"MORRI em t={_relogio:0.0}s, na zona '{_zonaAoMorrer}' -- HP {cli.Sheet.HP:0.0}");
		}

		// O VOO DO MORTO. Vai pelo mesmo canal da tecla do jogador (`SendHabilidade("voar")`) e nao
		// por atalho de teste: assim o servidor cobra o Ki de decolagem e recusa quem nao pode, que e
		// justamente a pergunta ("o morto PODE voar?"). Exige `--vooteste` no servidor.
		if (!_pediuVoo && _cheguei > 0 && _relogio >= _cheguei + EsperaAntesDeVoar)
		{
			_pediuVoo = true;
			cli.SendHabilidade("voar");
			Anotar($"t={_relogio:0.0}s  pedi pra VOAR (morto, no '{_zonaDoAlem}')");
		}

		// ANDA ENQUANTO VOA, uns segundos. A folha da auréola tem `flight_mov` -- parado no ar ela
		// cairia no quadro congelado, e a pergunta do dono ("a auréola acompanha?") e sobre o corpo
		// em movimento. Anda com as TECLAS de verdade, como o `RoboDeSoco`.
		if (_pediuVoo && _relogio >= _cheguei + EsperaAntesDeVoar + 1
					  && _relogio <= _cheguei + EsperaAntesDeVoar + 8)
			Segurar("move_left", true);
		else Parar();

		// E POUSA DEPOIS. Nao e capricho: o olhador so pede o revive com o corpo no chao, pra que a
		// ultima foto seja o PAR EXATO da penultima -- mesma pose, mesmo lugar, um bit de diferenca.
		// Sem o pouso, a foto do "sem auréola" mudaria duas coisas ao mesmo tempo.
		if (_pediuVoo && !_pousou && _relogio >= _cheguei + EsperaAntesDeVoar + 10)
		{
			_pousou = true;
			cli.SendHabilidade("voar");
			Anotar($"t={_relogio:0.0}s  POUSEI (ainda morto)");
		}

		if (_pediuVoo && !_voando && _euVoando) { _voando = true; Anotar($"t={_relogio:0.0}s  DECOLEI (morto e no ar)"); }

		_proximoDespejo -= delta;
		if (_proximoDespejo <= 0) { _proximoDespejo = 0.5; Despejar(cli); }
	}

	/// <summary>
	/// ONDE O MORTO POUSA O QUE SO ELE SABE. Os dois processos moram na mesma maquina por
	/// construcao, entao o `user://` e o mesmo -- mesma receita do `RoboDeSigiloDeVida`.
	///
	/// E NAO HA OUTRO CAMINHO: "pra que zona a morte me levou" e uma pergunta que so o dono do corpo
	/// responde. O olhador ate chega la, mas ele chega de `admin_ir`, e isso prova que a zona EXISTE
	/// -- nao que a MORTE levou alguem ate ela.
	/// </summary>
	private const string ArquivoDoMorto = "user://morte-do-morto.txt";

	private void Despejar(GameClient cli)
	{
		var sb = new StringBuilder();
		sb.AppendLine($"nome={cli.LocalName}");
		sb.AppendLine($"id={cli.LocalId}");
		sb.AppendLine($"t={F(_relogio)}");
		sb.AppendLine($"morto={(cli.Sheet.Morto ? 1 : 0)}");
		sb.AppendLine($"ko={(cli.Sheet.KO ? 1 : 0)}");
		sb.AppendLine($"hp={F(cli.Sheet.HP)}");
		sb.AppendLine($"zona={_minhaZona.Name}");
		sb.AppendLine($"zona_ao_morrer={_zonaAoMorrer}");
		sb.AppendLine($"zona_do_alem={_zonaDoAlem}");
		sb.AppendLine($"morri_em={F(_morriEm)}");
		sb.AppendLine($"cheguei_em={F(_cheguei)}");
		sb.AppendLine($"trocas_de_zona={_trocasDeZona}");
		sb.AppendLine($"voando={(_euVoando ? 1 : 0)}");
		sb.AppendLine($"voou_alguma_vez={(_voando ? 1 : 0)}");
		using Godot.FileAccess? f = Godot.FileAccess.Open(ArquivoDoMorto, Godot.FileAccess.ModeFlags.Write);
		f?.StoreString(sb.ToString());
	}

	private static string F(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

	private Dictionary<string, string> LerOMorto()
	{
		var d = new Dictionary<string, string>();
		using Godot.FileAccess? f = Godot.FileAccess.Open(ArquivoDoMorto, Godot.FileAccess.ModeFlags.Read);
		if (f == null) return d;
		foreach (string linha in f.GetAsText().Split('\n'))
		{
			int i = linha.IndexOf('=');
			if (i > 0) d[linha[..i].Trim()] = linha[(i + 1)..].Trim();
		}
		return d;
	}

	// =====================================================================================
	// O PAPEL B -- O OLHADOR. Mata no combate e fotografa.
	// =====================================================================================
	private enum Fase { Chegando, Batendo, Caido, Seguindo, NoAlem, Revivendo, Fim }
	private Fase _fase = Fase.Chegando;

	/// <summary>A zona mirada: CABECA. Um membro sorteavel so, entao o dano se concentra.</summary>
	private const byte ZonaMirada = 1;

	/// <summary>Encostado o bastante pra socar.</summary>
	private const float Perto = 30f;

	/// <summary>Maior que a pose do golpe de proposito: no ritmo da pose o vigor acaba em 20 s.</summary>
	private const double Cadencia = 0.55;

	private bool _promoveu, _pediuIr, _pediuRevive, _acendeuODia;
	private double _proximoSoco, _reafirmar, _proximaCura, _proximaEscuta;
	private int _golpes;
	private double _cheguelNoAlem = -1, _reviveiEm = -1;
	private RemotePlayer? _corpoAlheio;
	private double _proximaBusca;

	private void PassoDoOlhador(GameClient cli, double delta)
	{
		// ---------- o admin vai pro disco ANTES de o outro chegar ----------
		if (!_promoveu && _relogio >= 1 && Conta.Length > 0)
		{
			_promoveu = true;
			cli.SendVerbo("admin_promover", Conta);
			Anotar($"promovi a minha propria conta ('{Conta}') -- o endereco para de valer quando o "
				 + "segundo processo chegar");
		}

		// ============================ ACENDE A LUZ, E ISSO NAO E ENFEITE ============================
		// A primeira rodada desta bancada caiu de NOITE no berco (a HUD dizia "noite", a lua estava
		// minguante) e as tres primeiras fotos sairam pretas: da pra ler o log, mas nao da pra
		// responder a pergunta do dono, que e "**da pra entender?**". Uma foto ilegivel nao e uma
		// medida ruim -- e medida nenhuma.
		//
		// Vale so pro planeta em que eu estou; o Outro Mundo nao tem noite (a foto 3 da mesma rodada
		// saiu clara), entao isto so conserta as fotos do berco.
		if (!_acendeuODia && _relogio >= 2 && Conta.Length > 0)
		{
			_acendeuODia = true;
			cli.SendVerbo("admin_meio_dia");
			Anotar("pedi MEIO-DIA -- a rodada anterior caiu de noite e as fotos do berco sairam pretas");
		}

		// ---------- eu preciso continuar de pe ----------
		// O berco e POVOADO e os NPCs batem. Um olhador nocauteado nao anda ate o corpo, nao soca e
		// nao fotografa nada -- e o relatorio leria "a morte nunca aconteceu". Curar a mim mesmo nao
		// toca no outro lado da medida.
		_proximaCura -= delta;
		if (_proximaCura <= 0) { _proximaCura = 8; if (_relogio > 3) cli.SendVerbo("admin_curar"); }

		switch (_fase)
		{
			case Fase.Chegando:
				if (_idDoOutro == 0) break;

				// ---------- FOTO 1: OS DOIS VIVOS ----------
				// A foto de CONTROLE, e ela vem antes de qualquer soco. Sem ela "ha um circulo
				// amarelo sobre aquela cabeca" nao prova nada -- podia estar la desde sempre.
				//
				// E ela e tirada DE LONGE, antes de eu andar ate ele: pra socar eu preciso encostar
				// (30 px), e a versao anterior desta foto saiu com os dois bonecos empilhados -- o
				// enquadramento em que a auréola de um parece estar sobre a cabeca do outro e
				// justamente o que esta bancada existe pra nao produzir.
				if (!_fotoDeControle && _relogio > 7)
				{
					_fotoDeControle = true;
					Parar();
					OsDoisSemAureola(cli);
					Fotografar("user://morte-1-os-dois-vivos.png", "1/5 -- OS DOIS VIVOS, ninguem tem auréola",
							   PontosDosDois());
					// O CONTRA-EXEMPLO NO PIXEL. Sem esta linha, "80 pixels amarelos no morto" ficaria
					// verde num jogo em que a folha estivesse desenhada em cima de todo mundo.
					Conferir(_amarelosNaFoto == 0,
							 $"e na foto dos dois VIVOS nao ha um pixel do amarelo da folha ({_amarelosNaFoto})");
					break;
				}

				AndarAte(_posDoOutro);
				if (_fotoDeControle && Encostado()) { Parar(); ComecarAPancada(cli); }
				break;

			case Fase.Batendo:
				AndarEBater(cli, delta);

				// ---------- A MORTE, PELO LADO DE QUEM MORREU ----------
				// Meio segundo de cadencia porque o despejo do outro processo tambem e de meio segundo:
				// ler mais rapido so gasta disco, e ler mais devagar atrasa a foto do cadaver dentro de
				// uma janela que tem 15 s no total (`Alem.MsNoChao`).
				_proximaEscuta -= delta;
				if (_proximaEscuta <= 0)
				{
					_proximaEscuta = 0.5;
					if (_morreuEm <= 0 && OMortoJaSeAnunciou())
					{
						_morreuEm = _relogio;
						Anotar($"t={_relogio:0.0}s  O MORTO SE ANUNCIOU (`morto=1` no despejo dele) -- e "
							 + "nao ha auréola nenhuma no fio ainda, que e o ponto");
					}
				}

				if (_morreuEm > 0) EleCaiu(cli);
				// Se em 90 s de soco letal na cabeca ninguem morreu, o problema nao e a auréola e a
				// bancada nao pode ficar batendo pra sempre.
				// ============================ 150 s, E O NUMERO E MEDIDO ============================
				// Com 100 s uma rodada fechou sem morte: 170 golpes, 113 acertos, 324 de dano -- e a
				// causa nao era o combate, era a CONTA REUSADA. O corpo que ja tinha morrido uma vez
				// voltou com o Zenkai da derrota (`GameServer` paga na hora), e o dano por golpe caiu
				// de 4,9 pra 2,9. A correcao de verdade e conta nova a cada rodada (ver o cabecalho do
				// `ver-a-morte.bat`); a folga aqui e so pra que uma conta um pouco mais velha ainda
				// consiga fechar o roteiro.
				// ==============================================================================
				else if (_relogio > 150) { Anotar("NAO CONSEGUI MATAR no combate em 150 s -- fechando"); _fase = Fase.Fim; }
				break;

			case Fase.Caido:
				// ---------- FOTO 2: O INSTANTE DA MORTE, meio segundo depois ----------
				// ============================ MEIO SEGUNDO NAO E FOLGA, E CORRECAO ============================
				// Esta foto era tirada NO QUADRO da morte, e saia com ZERO pixel de auréola enquanto a
				// linha do `AureolaVisivelDeTeste` fechava verde ao lado dela. O `GetTexture()` devolve
				// o ultimo quadro JA DESENHADO -- o de antes de a camada existir. Ver `ContarAmarelo`.
				// ==========================================================================================
				if (_fotosDoCaido == 0 && _relogio >= _morreuEm + 0.5)
				{
					_fotosDoCaido = 1;
					Fotografar("user://morte-2-cadaver-sem-aureola.png",
							   "2/5 -- ELE MORREU: o cadaver caido no berco, SEM auréola; eu vivo ao lado",
							   PontosDosDois());
					// ============================ ESTA FOTO ERA O BUG, E AGORA E A PROVA ============================
					// Ela se chamava `morte-2-caido-com-aureola` e cobrava >= 20 pixels amarelos -- ou seja,
					// a bancada AFIRMAVA exatamente a tela de que o dono reclamou: *"o corpo q fica no MAPA
					// DOS VIVOS deveria ser o EXATO CORPO DELE QUANDO MORRE, sem a aureola"*. Ela media o
					// crivo (`TemAureola(morto) => morto`) em vez de medir o DM, e por isso ficou verde por
					// cima do defeito. Ver `Alem.TemAureola`.
					// ==========================================================================================
					ONaoAmareloNaFoto("no quadro em que ele caiu (o cadaver e o corpo exato de quem morreu)");
				}

				// ============================ EU ESTAVA EM CIMA DELE ============================
				// Pra socar eu tenho que encostar (`Perto`, 30 px), e a foto do quadro da morte saiu
				// com os dois bonecos empilhados: o meu corpo cobre o dele, e a auréola dele parece
				// estar sobre a MINHA cabeca. E o mesmo defeito de enquadramento que o `admin_ir`
				// produz do outro lado -- e a mesma correcao: dar dois passos pra tras.
				if (_relogio < _morreuEm + 3.5) { Segurar("move_up", true); Segurar("move_right", true); break; }
				Parar();
				// Segunda foto do caido: a primeira sai no quadro da morte, com a pose de golpe ainda
				// no ar e os dois corpos colados. Esta pega o corpo assentado, e de longe.
				if (_relogio >= _morreuEm + 5.0 && _fotosDoCaido < 2)
				{
					_fotosDoCaido++;
					Fotografar("user://morte-2b-cadaver-assentado.png",
							   "2/5 (bis) -- o cadaver assentado no chao do berco, sem auréola, com o vivo a dois passos",
							   PontosDosDois());
					Conferir(VisualAlheio() is { } vc && !vc.AureolaVisivelDeTeste,
							 "CAIDO NO BERCO, longe de mim: NAO ha auréola sobre a cabeca dele "
						   + "(os 15 s de `Alem.MsNoChao` sao o cadaver, e a auréola so vem na viagem)");
					ONaoAmareloNaFoto("com o corpo assentado e o vivo a dois passos");
				}
				// A VIAGEM acontece aos 15 s (`Alem.MsNoChao`). Peco o `admin_ir` depois disso, e
				// insisto: se eu pedir antes, eu vou parar no berco, do lado do cadaver.
				if (_relogio >= _morreuEm + 18) { _fase = Fase.Seguindo; _pediuIr = false; }
				break;

			case Fase.Seguindo:
				if (Alem.EhOAlem(_minhaZona.Name)) { ChegueiNoAlem(cli); break; }
				_proximoSoco -= delta;
				if (_proximoSoco <= 0)
				{
					_proximoSoco = 3;
					// ============================ O VERB QUER O ID, E NAO O NOME ============================
					// `GameServer.PorNome` e `int.TryParse` puro (`GameServer.Admin.cs:586`) -- o nome dele
					// mente. No jogo isso nunca aparece porque quem digita o verb ja marcou alguem com
					// duplo clique, e o menu manda o id do alvo. Uma bancada que mandasse o nome levaria
					// "marque alguem antes" em silencio e ficaria eternamente no berco, do lado do lugar
					// onde o corpo esteve.
					cli.SendVerbo("admin_ir", _idDoOutro.ToString(CultureInfo.InvariantCulture));
					Anotar($"t={_relogio:0.0}s  pedi `admin_ir {_idDoOutro}` ({Alvo}) -- atras do morto");
				}
				break;

			case Fase.NoAlem:
				PassoNoAlem(cli, delta);
				break;

			case Fase.Revivendo:
				if (_reviveiEm > 0 && _relogio >= _reviveiEm + 2.5 && !_fotoDoRevivido)
				{
					_fotoDoRevivido = true;
					// ---------- FOTO 5: O REVIVIDO ----------
					// Mesmo enquadramento da foto 3, um bit de diferenca. `admin_reviver` chama
					// `Restaurar`, que NAO teleporta -- o corpo fica exatamente onde estava.
					Fotografar("user://morte-5-revivido.png", "5/5 -- REVIVIDO: a auréola sumiu",
							   PontosDosDois());
					ARevivendoApaga(cli);
			Conferir(_amarelosNaFoto == 0,
					 $"e a foto do REVIVIDO nao tem um pixel do amarelo da folha ({_amarelosNaFoto}) -- "
				   + "mesmo enquadramento da anterior, que tinha 80");
					_fase = Fase.Fim;
				}
				break;

			case Fase.Fim:
				Parar();
				if (!_julguei) { Julgar(cli); Fechar(); }
				break;
		}
	}

	private int _fotosDoCaido;
	private bool _fotoDoRevivido, _julguei, _fotoDeControle;

	private void ComecarAPancada(GameClient cli)
	{
		_fase = Fase.Batendo;
		// LETAL LIGADO -- e o pedido, literal: "mate no combate, com o letal ligado". Sem ele o
		// `Body.Ferir` respeita o piso de 19,8% por membro e ninguem morre nunca.
		cli.SendLethal(true);
		cli.SendAim(ZonaMirada);
		Anotar("LETAL LIGADO, mira na cabeca -- comeco a bater pra valer");
	}

	/// <summary>
	/// ELE CAIU. As MEDIDAS acontecem aqui, no quadro exato em que o pacote chegou; a FOTO sai meio
	/// segundo depois, no <see cref="PassoDoOlhador"/> -- e a separacao entre as duas e o achado da
	/// bancada, nao um detalhe de roteiro. Ver `ContarAmarelo`.
	/// </summary>
	private void EleCaiu(GameClient cli)
	{
		_fase = Fase.Caido;
		Parar();
		cli.SendLethal(false);   // ele ja morreu; nao ha nada a ganhar em continuar batendo num corpo
		_fotosDoCaido = 0;
		OCadaverNaoTemAureola(cli);
	}

	/// <summary>
	/// A AUREOLA SAIU NO PIXEL desta foto -- a linha que a arvore de nodes nao responde.
	/// </summary>
	private void AAureolaSaiuNoPixel(string quando) =>
		Conferir(_amarelosNaFoto >= 20,
				 $"e ela saiu NA FOTO {quando}: {_amarelosNaFoto} pixels do amarelo da folha "
			   + "(a camada existir na arvore nao e a mesma coisa que ela ter sido desenhada)");

	/// <summary>
	/// A CONTRAPROVA NO PIXEL: nesta foto NAO pode haver auréola nenhuma. Irma da de cima, e a que
	/// mede o cadaver -- a camada nao existir na arvore tambem nao e a mesma coisa que a tela estar
	/// limpa (uma folha desenhada por outro caminho apareceria aqui e em lugar nenhum).
	/// </summary>
	private void ONaoAmareloNaFoto(string quando) =>
		Conferir(_amarelosNaFoto == 0,
				 $"e NAO ha um pixel do amarelo da folha na foto {quando} ({_amarelosNaFoto})");

	private void ChegueiNoAlem(GameClient cli)
	{
		_fase = Fase.NoAlem;
		_cheguelNoAlem = _relogio;
		Anotar($"t={_relogio:0.0}s  CHEGUEI no '{_minhaZona.Name}' atras dele");
	}

	/// <summary>
	/// ============================ O `admin_ir` POUSA OS DOIS NO MESMO PIXEL ============================
	/// `AdminIr` faz `MoveToZone(adm.Id, p.Zone, p.Pos)` -- a posicao DELE, exata. Dois bonecos
	/// empilhados no mesmo tile nao sao uma foto: o de cima cobre o de baixo, e a auréola do morto
	/// apareceria sobre a cabeca do vivo. Entao o olhador anda uns passos ANTES de fotografar, e a
	/// foto vira a que o dono pediu -- um morto e um vivo, lado a lado, no mesmo quadro.
	/// ============================================================================================
	/// </summary>
	private const double PassosPraSair = 2.2;

	private void PassoNoAlem(GameClient cli, double delta)
	{
		double desde = _relogio - _cheguelNoAlem;

		if (desde < PassosPraSair) { Segurar("move_left", true); Segurar("move_up", true); return; }
		Parar();

		// ---------- FOTO 3: O OUTRO MUNDO ----------
		if (!_fotoDoAlem && desde >= PassosPraSair + 0.8)
		{
			_fotoDoAlem = true;
			Fotografar($"user://morte-3-no-alem.png",
					   $"3/5 -- O OUTRO MUNDO ('{_minhaZona.Name}'): ele morto e de pe, com auréola; eu vivo, sem",
					   PontosDosDois());
			AAureolaSobreviveuAViagem(cli);
			AAureolaSaiuNoPixel("no Outro Mundo");
		}

		// ---------- FOTO 4: O MORTO VOANDO ----------
		// O A decola sozinho (ver `EsperaAntesDeVoar`). Eu so vigio o corpo dele -- amarrar isto ao
		// MEU relogio acoplaria dois processos que nao compartilham um.
		//
		// ============================ O BIT CHEGA ANTES DO BONECO SUBIR ============================
		// A primeira rodada fotografou no QUADRO em que `Voando` virou verdadeiro, e a foto saiu com
		// o corpo ainda no chao -- identica a anterior pro olho, e a bancada fechou VERDE dizendo "a
		// auréola continua desenhada". Estava certa e nao provava nada: a altura sobe ao longo de
		// quase um segundo (e o corpo remoto ainda desenha 66 ms do passado, `RemotePlayer.Atraso`).
		//
		// Entao agora sao duas coisas: ESPERAR o boneco subir, e MEDIR que ele subiu -- a posicao
		// DESENHADA (`World.PosicaoDesenhadaDe`, que ja soma o deslocamento de altura do visual) tem
		// que estar mais alta do que a do mesmo corpo no chao. E como a auréola e camada do proprio
		// visual, aquela subida e literalmente a subida dela.
		if (!_outroVoando && CorpoAlheio() != null
			&& World.Instancia?.PosicaoDesenhadaDe(_idDoOutro) is { } noChao)
			_yNoChao = noChao.Y;

		if (_outroVoando && _decolouEm <= 0) { _decolouEm = _relogio; _viOMortoVoar = true; }

		if (!_fotoDoVoo && _decolouEm > 0 && _relogio >= _decolouEm + 2.5)
		{
			_fotoDoVoo = true;
			Fotografar("user://morte-4-morto-voando.png",
					   "4/5 -- O MORTO VOANDO: a auréola acompanha o corpo no ar", PontosDosDois());
			AAureolaAcompanhaOVoo(cli);
			AAureolaSaiuNoPixel("com ele no ar");
		}

		// ---------- e entao o revive ----------
		// Aos 60 s no alem o `Renascer` levaria ele embora sozinho (`Alem.MsNoAlem`), e a foto do
		// "sem auréola" sairia de um berco vazio. O `admin_reviver` desfaz a morte NO LUGAR.
		// ESPERA ELE POUSAR (`!_outroVoando`): o revive tem que ser o PAR EXATO da foto 3 -- mesma
		// pose, mesmo lugar, um bit de diferenca. Revivendo no meio do voo, a ultima foto mudaria
		// duas coisas de uma vez e nao provaria qual delas apagou a auréola.
		if (!_pediuRevive && ((_fotoDoVoo && !_outroVoando && desde > 12) || desde > 34))
		{
			_pediuRevive = true;
			_reviveiEm = _relogio;
			// ID, e nao nome -- ver o bloco do `admin_ir` no `PassoDoOlhador`.
			cli.SendVerbo("admin_reviver", _idDoOutro.ToString(CultureInfo.InvariantCulture));
			Anotar($"t={_relogio:0.0}s  pedi `admin_reviver {_idDoOutro}` ({Alvo}) -- a auréola tem que sumir "
				 + "SEM ele sair do lugar (`Restaurar` nao teleporta)");
			_fase = Fase.Revivendo;
		}
	}

	private bool _fotoDoAlem, _fotoDoVoo, _viOMortoVoar;
	private double _decolouEm = -1;

	/// <summary>Onde o corpo dele era DESENHADO com os pes no chao. Ver o bloco do voo.</summary>
	private float _yNoChao = float.NaN;

	// =====================================================================
	// ANDAR E BATER
	// =====================================================================
	private void AndarAte(Vector2? destino)
	{
		if (World.Instancia?.PosicaoLocal is not { } eu || destino is not { } la) { Parar(); return; }
		Vector2 d = la - eu;
		if (d.Length() <= Perto) { Parar(); return; }
		// ANDA COM AS TECLAS DE VERDADE, como o `RoboDeSoco`: o passo passa pelo mesmo caminho do
		// jogador (movimento local, concessao do servidor) em vez de teleportar o corpo.
		Segurar("move_right", d.X > 8f);
		Segurar("move_left", d.X < -8f);
		Segurar("move_down", d.Y > 8f);
		Segurar("move_up", d.Y < -8f);
	}

	private bool Encostado() =>
		World.Instancia?.PosicaoLocal is { } eu && _posDoOutro is { } la && (la - eu).Length() <= Perto + 8;

	private void AndarEBater(GameClient cli, double delta)
	{
		AndarAte(_posDoOutro);

		// A MIRA E A LETALIDADE SAO REAFIRMADAS: os dois sao estado do servidor, e um pacote perdido
		// no comeco deixaria a rodada inteira batendo nao-letal -- o que nao da erro nenhum, so uma
		// morte que nunca chega.
		_reafirmar -= delta;
		if (_reafirmar <= 0) { _reafirmar = 3; cli.SendLethal(true); cli.SendAim(ZonaMirada); }

		_proximoSoco -= delta;
		if (_proximoSoco > 0) return;
		_proximoSoco = Cadencia;

		// SOCAR SEM FOLEGO NAO E SOCAR: abaixo de um quarto do tanque o servidor recusa e a bancada
		// mediria o cansaco do robo. Ver o mesmo bloco no `RoboDeSigiloDeVida`.
		if (cli.Sheet.VigorMax > 0 && cli.Sheet.Vigor < cli.Sheet.VigorMax * 0.25) return;

		_golpes++;
		cli.SendAction();
	}

	private static void Segurar(string acao, bool sim)
	{
		if (sim) Godot.Input.ActionPress(acao);
		else Godot.Input.ActionRelease(acao);
	}

	private static void Parar()
	{
		foreach (string a in new[] { "move_right", "move_left", "move_up", "move_down" })
			Godot.Input.ActionRelease(a);
	}

	// =====================================================================
	// O CORPO ALHEIO
	// =====================================================================
	private RemotePlayer? CorpoAlheio()
	{
		if (_corpoAlheio != null && GodotObject.IsInstanceValid(_corpoAlheio)
			&& _corpoAlheio.IsInsideTree()) return _corpoAlheio;
		if (_relogio < _proximaBusca || _idDoOutro == 0) return null;
		_proximaBusca = _relogio + 0.5;
		// PELA MESMA BUSCA QUE O JOGO USA (`World.CorpoDeTeste` -> o mapa `_remotos`), e nao por
		// `FindChild`: achar o node por nome passaria mesmo se o corpo nunca tivesse entrado no mapa
		// de onde a fala, o alvo e a auréola tiram o corpo de alguem.
		_corpoAlheio = World.Instancia?.CorpoDeTeste(_idDoOutro) as RemotePlayer;
		return _corpoAlheio;
	}

	private CharacterVisual? VisualAlheio() =>
		CorpoAlheio() is { } c && GodotObject.IsInstanceValid(c) ? c.GetNodeOrNull<CharacterVisual>("Visual") : null;

	private CharacterVisual? MeuVisual() =>
		GameClient.Instance is { } cli && World.Instancia?.CorpoDeTeste(cli.LocalId) is { } c
			? c.GetNodeOrNull<CharacterVisual>("Visual") : null;

	// =====================================================================
	// AS MEDIDAS (o que a foto sozinha nao afirma)
	// =====================================================================
	/// <summary>
	/// VIVO NAO TEM AUREOLA -- nos dois corpos, e pelos DOIS caminhos (o conjunto do fio e a camada
	/// desenhada). Sem este contra-exemplo, "tem auréola" ficaria verde num jogo em que todo mundo
	/// tem uma o tempo todo.
	/// </summary>
	private void OsDoisSemAureola(GameClient cli)
	{
		Conferir(!cli.ComAureola.Contains(_idDoOutro),
				 "VIVO: o fio nao anuncia auréola nenhuma pro corpo alheio");
		Conferir(VisualAlheio() is { } v && !v.AureolaVisivelDeTeste,
				 "VIVO: nao ha camada de auréola desenhada no corpo alheio");
		Conferir(MeuVisual() is { } m && !m.AureolaVisivelDeTeste,
				 "VIVO: nem no meu proprio corpo");
	}

	/// <summary>
	/// ============================ ELE MORREU E **NAO** ACENDEU -- O CADAVER E O CORPO EXATO ============================
	/// Este metodo se chamava `AAureolaAcendeuNoCorpoAlheio` e cobrava o contrario. Ele media o crivo
	/// que existia (`TemAureola(morto) => morto`) em vez de medir o original, e por isso fechou verde
	/// por cima da tela de que o dono reclamou.
	///
	/// O DM resolve por ORDEM: `GenerateCorpse()` e o passo 5 (`Death.dm:64-67`) e copia os overlays
	/// daquele instante; `overlayList += 'Halo.dmi'` so vem no passo 10 (`:106-108`), no mob que viaja
	/// na linha seguinte. O cadaver do BYOND nunca ve a auréola.
	///
	/// ============================ E O CONTRASTE COM O NOCAUTE MUDOU DE LUGAR, NAO SUMIU ============================
	/// Aqui morava a linha do `_viOKoSemAureola` -- "KO e morte sao coisas diferentes e o desenho
	/// separa as duas". **Neste instante ele nao separa mais, e isso e o desenho e nao um buraco**: um
	/// cadaver e um nocauteado sao IGUAIS na tela pelos 15 s, exatamente como no original (la o corpo
	/// caido nem e mais o mob). Quem separa os dois e o tempo: o nocauteado levanta, o cadaver sobe.
	/// A prova do contraste foi pro <see cref="AAureolaSobreviveuAViagem"/>, que e onde ela existe.
	/// ==========================================================================================================
	/// </summary>
	private void OCadaverNaoTemAureola(GameClient cli)
	{
		Conferir(!cli.ComAureola.Contains(_idDoOutro),
				 "MORTO no combate: o fio NAO anuncia auréola no cadaver (`Death.dm:64-67` fotografa o "
			   + "corpo antes de ela existir)");
		Conferir(VisualAlheio() is { } v && !v.AureolaVisivelDeTeste,
				 "MORTO no combate: nao ha camada de auréola desenhada sobre a cabeca dele -- o corpo no "
			   + "mapa dos vivos e o exato de quem caiu");
		Conferir(MeuVisual() is { } m && !m.AureolaVisivelDeTeste,
				 "e eu, vivo, tambem nao tenho uma NO MESMO QUADRO");
	}

	private void AAureolaSobreviveuAViagem(GameClient cli)
	{
		Conferir(Alem.EhOAlem(_minhaZona.Name),
				 $"a viagem levou pra uma zona do alem ('{_minhaZona.Name}')");
		Conferir(cli.ComAureola.Contains(_idDoOutro),
				 "A AUREOLA ACENDEU NA VIAGEM, e nao na morte: o fio a anuncia no Outro Mundo "
			   + "(`overlayList += 'Halo.dmi'`, `Death.dm:106-108`, na linha de cima do `loc =`)");
		Conferir(VisualAlheio() is { } v && v.AureolaVisivelDeTeste,
				 "no Outro Mundo a auréola esta desenhada num corpo que nasceu de novo na MINHA tela");
		Conferir(MeuVisual() is { } m && !m.AureolaVisivelDeTeste,
				 "e eu, vivo, continuo sem auréola NO MESMO QUADRO -- e o contraste que da sentido a ela");

		// ============================ A LINHA QUE VEIO DO OUTRO LADO ============================
		// Ela morava no instante da morte, e la ela nao prova mais nada (cadaver e nocauteado sao
		// iguais na tela por 15 s, por desenho). AQUI ela prova o que sempre quis provar: existe UM
		// desenho neste jogo que so um morto-que-ja-viajou tem, e nenhum nocauteado teve.
		// ====================================================================================
		Conferir(_viOKoSemAureola,
				 "e o corpo dele passou por NOCAUTE antes de tudo isso, sem auréola nenhuma -- a "
			   + "auréola marca a MORTE consumada, e nao um corpo no chao");

		// ============================ E ELA ACENDEU *DEPOIS*, NO RELOGIO ============================
		// As duas linhas de cima medem LUGAR ("no alem tem", "no berco nao tinha"). Esta mede TEMPO, e
		// e ela que descreve o conserto: o crivo e `morto && jaViajou`, entao o pacote da auréola so
		// pode sair depois da janela do cadaver. A folga de 2 s cobre o tique de 5 Hz e a rede local;
		// o que estava errado antes valia ZERO (a auréola saia no mesmo quadro da morte).
		//
		// Sem esta linha, um crivo que voltasse a ser `=> morto` ainda fecharia esta familia inteira em
		// verde: o corpo TAMBEM tem auréola no alem quando ela acende cedo demais. O que separa os dois
		// mundos nao e a presenca dela la -- e a AUSENCIA dela aqui, no relogio.
		// ==========================================================================================
		Conferir(_aureolaAcendeuEm > 0 && _morreuEm > 0
				 && _aureolaAcendeuEm >= _morreuEm + (Alem.MsNoChao / 1000.0) - 2.0,
				 $"e ela acendeu SO NA VIAGEM: morreu em t={_morreuEm:0.0}s e a auréola so veio em "
			   + $"t={_aureolaAcendeuEm:0.0}s -- os {Alem.MsNoChao / 1000.0:0} s de cadaver "
			   + "(`Alem.MsNoChao`) passaram inteiros sem ela");
	}

	private void AAureolaAcompanhaOVoo(GameClient cli)
	{
		Conferir(VisualAlheio() is { } v && v.AureolaVisivelDeTeste,
				 "VOANDO: a auréola continua desenhada -- ela sobe junto com o boneco, sem `ISobeComOCorpo`");

		// E O BONECO SUBIU DE VERDADE. Sem esta linha, a de cima fecha verde com o corpo parado no
		// chao -- foi exatamente o que aconteceu na primeira rodada.
		//
		// ============================ O QUANTO E 12 px, E O NUMERO E DO JOGO ============================
		// A primeira versao cobrava 20 px "porque parecia pouco", e reprovou uma decolagem correta
		// (mediu 12). Quem manda aqui sao duas constantes que ja existem: quem paira sobe
		// `Voo.AlturaDePairar` (1,5 tile = 48 px de MUNDO) e o desenho comprime isso por
		// `Voo.EscalaNaTela` (0,25) -- 48 x 0,25 = **12 px**. Numero inventado em bancada e o jeito
		// mais limpo de reprovar o jogo certo.
		//
		// E ISTO EXPLICA UMA COISA QUE A FOTO MOSTRA: um morto voando parece quase um morto de pe.
		// O que separa os dois na tela nao e a altura -- e a SOMBRA no chao e a pose deitada.
		// ==========================================================================================
		const float esperado = Voo.AlturaDePairar * Voo.EscalaNaTela;
		float? agora = World.Instancia?.PosicaoDesenhadaDe(_idDoOutro)?.Y;
		bool subiu = !float.IsNaN(_yNoChao) && agora is { } y && y <= _yNoChao - (esperado - 3f);
		Conferir(subiu,
				 $"VOANDO: o corpo esta DESENHADO mais alto do que estava no chao "
			   + $"(y {_yNoChao:0} -> {agora?.ToString("0") ?? "?"}, esperado ~{esperado:0} px) -- a "
			   + "auréola subiu com ele, e nao ficou plantada onde os pes estavam");
	}

	private void ARevivendoApaga(GameClient cli)
	{
		Conferir(!cli.ComAureola.Contains(_idDoOutro),
				 "REVIVIDO: o fio apagou a auréola dele");
		Conferir(VisualAlheio() is { } v && !v.AureolaVisivelDeTeste,
				 "REVIVIDO: a camada sumiu da tela");
	}

	// =====================================================================
	// A FOTO
	// =====================================================================
	/// <summary>
	/// Salva a tela inteira MAIS um recorte ampliado que cobre os corpos pedidos.
	///
	/// A tela cheia prova o LUGAR (o berco, a mesa do Enma, quem mais esta na cena); o recorte prova
	/// a CABECA -- no zoom do jogo o boneco tem 32 px numa tela de mais de mil, e a auréola tem
	/// QUATRO linhas de pixel dentro dele. Nenhuma das duas responde as duas coisas.
	/// </summary>
	private void Fotografar(string destino, string rotulo, Vector2[] pontosDeMundo)
	{
		_amarelosNaFoto = -1;
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty())
		{
			Anotar($"{rotulo}: SEM FOTO (headless nao renderiza -- rode o papel B com janela)");
			return;
		}
		try
		{
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			_fotos++;
			Anotar($"FOTO {rotulo} -> {caminho} ({img.GetWidth()}x{img.GetHeight()})");
			if (Recorte(img, pontosDeMundo) is { } lupa)
			{
				string zoom = caminho.Replace(".png", "-zoom.png");
				lupa.SavePng(zoom);
				Anotar($"     recorte ampliado -> {zoom}");
			}
		}
		catch (Exception e) { Anotar($"{rotulo}: sem foto: {e.Message}"); }
	}

	private int _fotos;

	/// <summary>Os dois corpos em coordenada de MUNDO -- o meu e o dele, quando existirem.</summary>
	private Vector2[] PontosDosDois()
	{
		var l = new List<Vector2>();
		if (GameClient.Instance is { } cli)
		{
			if (World.Instancia?.PosicaoDesenhadaDe(cli.LocalId) is { } eu) l.Add(eu);
			if (World.Instancia?.PosicaoDesenhadaDe(_idDoOutro) is { } ele) l.Add(ele);
		}
		return l.ToArray();
	}

	/// <summary>
	/// ============================ O AMARELO NA FOTO, E POR QUE ELE E A MEDIDA QUE FALTAVA ============================
	/// Todas as outras linhas desta bancada perguntam a ARVORE DE NODES ("a camada existe? esta
	/// `Visible`?"). Este projeto ja tem uma licao inteira escrita sobre isso -- *uniform escrito nao
	/// e pixel desenhado* -- e ela cobrou o preco aqui na primeira rodada boa: a foto do INSTANTE da
	/// morte saiu com **zero** pixel de auréola, enquanto a linha do `AureolaVisivelDeTeste` fechava
	/// verde no mesmo quadro. Nenhum dos dois estava mentindo: `GetViewport().GetTexture()` devolve o
	/// ULTIMO QUADRO JA DESENHADO, que e o de antes da camada nascer.
	///
	/// Entao agora a foto tambem e medida. Duas coisas de uma vez: a auréola chega mesmo ate o PIXEL,
	/// e a foto que leva o nome dela nao sai vazia -- um arquivo chamado "no-alem" sem auréola
	/// nenhuma vira, meses depois, uma prova que ninguem deu.
	///
	/// **E O CONTRARIO TAMBEM E MEDIDO** (<see cref="ONaoAmareloNaFoto"/>): as fotos do CADAVER tem
	/// que sair com ZERO. A da foto de controle (os dois vivos, no mesmo berco, mesma luz) ja provava
	/// que zero e alcancavel ali -- e e por isso que o mesmo crivo serve pras duas pontas.
	///
	/// O CRIVO E ESTREITO de proposito (o amarelo da folha e quase `#ffe000`): a areia e o mato seco
	/// do berco passam dos 170 no verde mas nunca ficam abaixo de 100 no azul.
	/// ============================================================================================================
	/// </summary>
	private static int ContarAmarelo(Image corte)
	{
		int n = 0;
		for (int y = 0; y < corte.GetHeight(); y++)
			for (int x = 0; x < corte.GetWidth(); x++)
			{
				Color c = corte.GetPixel(x, y);
				if (c.R8 > 190 && c.G8 > 170 && c.B8 < 100) n++;
			}
		return n;
	}

	/// <summary>Quantos pixels de auréola havia no recorte da ULTIMA foto. -1 = nao houve foto.</summary>
	private int _amarelosNaFoto = -1;

	private Image? Recorte(Image cheia, Vector2[] pontosDeMundo)
	{
		if (pontosDeMundo.Length == 0) return null;
		Transform2D t = GetViewport()?.CanvasTransform ?? Transform2D.Identity;
		Vector2 min = t * pontosDeMundo[0], max = min;
		foreach (Vector2 p in pontosDeMundo)
		{
			Vector2 s = t * p;
			min = new Vector2(Math.Min(min.X, s.X), Math.Min(min.Y, s.Y));
			max = new Vector2(Math.Max(max.X, s.X), Math.Max(max.Y, s.Y));
		}
		// A MARGEM E MAIOR EM CIMA. O que se quer ver e o que fica ACIMA da cabeca -- a auréola mora
		// nas quatro primeiras linhas do tile, e um recorte centrado no corpo corta justamente ela.
		var r = new Rect2I((int)min.X - 96, (int)min.Y - 112, (int)(max.X - min.X) + 192, (int)(max.Y - min.Y) + 176);
		r = r.Intersection(new Rect2I(0, 0, cheia.GetWidth(), cheia.GetHeight()));
		if (r.Size.X < 32 || r.Size.Y < 32) return null;
		Image corte = cheia.GetRegion(r);
		_amarelosNaFoto = ContarAmarelo(corte);
		// NEAREST, e nao suavizado: o que esta sendo julgado e PIXEL de sprite. Interpolar borraria
		// exatamente o detalhe que a foto existe pra mostrar.
		int fator = corte.GetWidth() > 700 ? 2 : 4;
		corte.Resize(corte.GetWidth() * fator, corte.GetHeight() * fator, Image.Interpolation.Nearest);
		return corte;
	}

	// =====================================================================
	// O VEREDITO
	// =====================================================================
	private void Julgar(GameClient cli)
	{
		_julguei = true;
		if (Papel == "a")
		{
			Despejar(cli);
			GD.Print($"\n===== [morte-a] o morto encerrou (morri em {_morriEm:0.0}s, zona '{_zonaDoAlem}') =====");
			return;
		}

		Dictionary<string, string> morto = LerOMorto();

		// ============================ UMA BANCADA QUE NAO FOTOGRAFOU NADA REPROVA ============================
		// E o modo de falha mais perigoso de uma bancada visual: o outro processo nao subiu, a briga
		// nao aconteceu, o `GetImage` voltou vazio -- e o placar sai "0 falhas", que se le exatamente
		// igual a "tudo certo".
		// ==================================================================================================
		Conferir(_fotos >= 4, $"a bancada tirou as fotos ({_fotos} de 5) -- sem elas nao ha veredito nenhum");
		Conferir(_morreuEm > 0, "houve uma morte de verdade, vista do outro lado do soquete");
		Conferir(_golpes > 0 && _acertos > 0,
				 $"a morte veio de SOCO LETAL e nao de bandeira ({_golpes} golpes, {_acertos} acertos, "
			   + $"{_danoDado:0} de dano dado)");

		// O QUE SO O MORTO SABE, lido do arquivo dele.
		//
		// ============================ E O NOME E CONFERIDO ============================
		// O `user://` e um so por projeto. Uma rodada que fechou sem morte nenhuma ja saiu com TRES
		// linhas verdes ("a morte o levou pra 'Afterlife'", "morreu em 'Earth'") lidas do despejo de um
		// morto de meia hora antes, de outro par de contas. Um arquivo de outra briga nao e prova
		// desta.
		// ==========================================================================
		Conferir(morto.TryGetValue("nome", out string? quem) && quem == Alvo,
				 $"o outro processo deixou o que mediu no `user://morte-do-morto.txt`, e o despejo e "
			   + $"DELE mesmo (esperava '{Alvo}', achei '{(morto.TryGetValue("nome", out string? q2) ? q2 : "nada")}')");
		if (!morto.TryGetValue("nome", out string? dono) || dono != Alvo) morto = [];
		if (morto.TryGetValue("zona_do_alem", out string? za))
			Conferir(za.Length > 0 && Alem.EhOAlem(za),
					 $"o MORTO diz, do lado dele, que a morte o levou pra '{za}'");
		if (morto.TryGetValue("zona_ao_morrer", out string? zm) && morto.TryGetValue("zona_do_alem", out string? za2))
			Conferir(zm.Length > 0 && !string.Equals(zm, za2, StringComparison.OrdinalIgnoreCase),
					 $"e que ele NAO morreu la: morreu em '{zm}' e acordou em '{za2}'");
		Conferir(_viOMortoVoar || (morto.TryGetValue("voou_alguma_vez", out string? v) && v == "1"),
				 "o morto conseguiu VOAR no Outro Mundo (e a folha de `Icons/Misc` tem a pose pra isso)");

		GD.Print($"\n===== [morte-b] {_oks} OK, {_falhas.Count} FALHA -- {_fotos} fotos =====");
		foreach (string f in _falhas) GD.PrintErr("  FALHA  " + f);

		// O DIARIO VAI PRO DISCO: a janela do papel B fecha sozinha, e o console dela some junto.
		var sb = new StringBuilder();
		sb.AppendLine($"===== bancada da morte vista de fora -- {_oks} OK, {_falhas.Count} FALHA, {_fotos} fotos =====");
		foreach (string l in _diario) sb.AppendLine(l);
		using Godot.FileAccess? f2 = Godot.FileAccess.Open("user://morte-diario.txt", Godot.FileAccess.ModeFlags.Write);
		f2?.StoreString(sb.ToString());
	}

	private void Fechar()
	{
		if (_fechou) return;
		_fechou = true;
		Parar();
		GetTree().Quit();
	}
}
