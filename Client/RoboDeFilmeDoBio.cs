using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ A CINEMATICA **QUADRO A QUADRO** (`--diagfilme`) ============================
/// Sobe junto do `--biofilme` do servidor (`GameServer.BioFilme.cs`), que planta quatro corpos em volta
/// de quem esta olhando e roda o roteiro pelas portas de producao.
///
/// O dono, com foto: *"o bio androide ta MUDANDO O CORPO ANTES DA CINEMATICA ACABAR ai ta ficando
/// BUGADO como pode ver"* -- corpo meio trocado, dois desenhos empilhados. E ele ja tinha reclamado do
/// mesmo FORMATO antes: *"tem transformacao q estao criando a CRATERA NO MEIO da cinematica (deveria ser
/// sempre no FINAL)"*. **Efeito que pertence ao fim acontecendo no comeco.**
///
/// ============================ POR QUE UMA FOTO NAO RESPONDE ISSO ============================
/// "Antes do fim" e uma afirmacao sobre ORDEM, e ordem nao cabe num quadro. Uma foto tirada no meio da
/// cena mostra um corpo -- e nao ha como saber, olhando so pra ela, se aquele e o corpo velho (certo) ou
/// o novo que chegou cedo (o defeito). As tres bancadas de bio que ja existiam tiram uma foto por
/// estado, e por isso nenhuma delas jamais poderia ter pego isto.
///
/// Entao aqui a unidade nao e a foto, e o **FILME**: ~30 amostras por cena, uma por segundo, do segundo
/// zero ao fim. O veredito nao le nenhuma delas isoladamente -- ele procura o INSTANTE DA TROCA na
/// sequencia e pergunta tres coisas sobre ele:
///
///   1. ele acontece **na virada**, e nao antes (a conta e contra o beat `Efeito.Assumir` do roteiro,
///      lido do Core -- nunca contra um numero digitado aqui);
///   2. ele acontece **entre dois quadros**, e nao ao longo de varios: o desenho do corpo troca UMA vez
///      no filme inteiro. Duas trocas, ou uma troca que se arrasta, e o "meio trocado" da foto;
///   3. e no bio, o quadro que tem o corpo NOVO **nao tem mais a silhueta de luz da cena**. Essa e a
///      forma exata do defeito que o dono fotografou: a silhueta e dimensionada pro corpo VELHO, entao
///      ela sobre o corpo novo sao duas silhuetas de tamanhos diferentes empilhadas.
///
/// ============================ O QUE E "O DESENHO DO CORPO", E POR QUE SAO DUAS CAMADAS ============================
/// <see cref="AssinaturaDoCorpo"/> le as DUAS folhas que podem desenhar um corpo neste jogo: a base
/// (`FolhaDoCorpoDeTeste`) e a da forma (`FolhaDoCorpoDaFormaDeTeste`, so quando visivel). O bio troca a
/// primeira -- ele vem da FICHA (`Appearance.Corpo` -> `S2C.PeerLook`); o Oozaru troca a segunda -- ela
/// vem do CATALOGO (`FormaDef.Corpo`, que so o cliente le, dentro do `Vestir`). Ler so uma delas daria
/// uma bancada que nao ve metade das trocas de corpo do jogo.
///
/// ============================ E A PROVA DE QUE NAO HOUVE MISTURA E O **SPRITE**, NAO A TELA ============================
/// O recorte de tela nao serve pra "o corpo trocou aos poucos": a cena inteira e feita de coisas se
/// mexendo (feixes, anel, tremor de camera, particulas), entao dois quadros consecutivos JA diferem em
/// centenas de pixels sem que corpo nenhum tenha mudado. A casa ja gastou uma bancada inteira nesse
/// engano (`RoboDeOlharDoBio.Espalhamento`).
///
/// Entao a medida de pixel e outra: <see cref="SpriteNaTela"/>, que sao os pixels CRUS do quadro zero
/// da folha do corpo que esta desenhando -- sem cenario, sem cinematica, sem luz do dia e sem a nuvem
/// de poeira da cratera, que e o que torna as fotos do proprio instante quase ilegiveis. Byte a byte
/// contra o primeiro quadro do filme, ele responde exatamente a pergunta que o dono fez: o sprite e o
/// mesmo, ou virou outro? Nao ha meio termo possivel -- e e por isso que "meio trocado" so pode
/// significar duas CAMADAS discordando, que e a linha 3 la de cima.
/// ==============================================================================================================
///
/// ============================ E ELA SABE FICAR VERMELHA ============================
/// O quarto corpo do palco roda a MESMA cena com `World.VestirNaHoraDeTeste` ligado -- que e, letra por
/// letra, o codigo de antes do conserto. As linhas acima TEM que reprovar nele (medidas: cinco das seis
/// reprovam, e a do quadro misturado sai com 28 de 32). Sem essa rodada, esta bancada seria uma lista de
/// afirmacoes que ninguem provou serem capazes de reprovar -- ver <see cref="_injetando"/>, que e como
/// aquelas vermelhas deixam de contar no placar sem deixar de contar como sinal.
/// ==================================================================
/// </summary>
public partial class RoboDeFilmeDoBio : Node
{
	private const string Pasta = "user://";

	/// <summary>
	/// O RECORTE. Alto de proposito: o Oozaru e um macaco de dez metros e o corpo do bio semi-perfeito
	/// e mais alto que o do imperfeito -- um recorte justo cortaria justamente a parte que cresceu, que
	/// e a que o dono viu empilhada.
	/// </summary>
	private const int Largura = 96, Altura = 128;

	/// <summary>Quanto ampliar a foto que vai pro disco. A MEDIDA e feita no recorte cru.</summary>
	private const int Ampliacao = 3;

	/// <summary>
	/// QUANTOS QUADROS ESPERAR entre pedir a foto e le-la. TRES, pelo motivo do `--diagolhar`: o
	/// `GetImage` devolve o quadro que a GPU JA desenhou, ou seja ele atrasa.
	/// </summary>
	private const int QuadrosDeEspera = 3;

	private int _f, _m, _k, _d;
	private int _passo = -1;
	private bool _fechou, _ocupado;
	private double _relogio;

	/// <summary>Segundos totais antes de fechar sozinha, mesmo com passos faltando.</summary>
	public double Fim = 320;

	private int _oks;
	private readonly List<string> _falhas = [];
	private readonly List<string> _linhas = [];

	/// <summary>O veredito de cada filme, guardado pra o fechamento poder cruza-los.</summary>
	private readonly Dictionary<string, Veredito> _filmes = [];

	/// <summary>
	/// ============================ ENQUANTO ISTO ESTA LIGADO, VERMELHO E O RESULTADO ESPERADO ============================
	/// O filme do corpo D roda com o defeito posto a mao, e ali as linhas do veredito **tem** que
	/// reprovar. Contadas no mesmo placar das outras, elas deixariam esta bancada impossivel de ficar
	/// verde -- e uma bancada que nunca fecha limpa e uma bancada que ninguem le.
	///
	/// Entao elas vao pra uma segunda lista, com marcador proprio, e quem as julga e o
	/// <see cref="OVereditoDaInjecao"/>: la a AUSENCIA delas e que vira falha. O sinal nao se perde, ele
	/// so troca de lado -- que e a diferenca entre ignorar um resultado e interpreta-lo.
	/// ================================================================================================================
	/// </summary>
	private bool _injetando;

	/// <inheritdoc cref="_injetando"/>
	private readonly List<string> _esperadas = [];

	private void Conferir(bool ok, string oque)
	{
		if (_injetando)
		{
			if (!ok) _esperadas.Add(oque);
			GD.Print("[filme] " + (ok ? "  (verde) " : "  (VERMELHA -- e e o que se quer) ") + oque);
			return;
		}
		if (ok) _oks++; else _falhas.Add(oque);
		GD.Print("[filme] " + (ok ? "  ok   " : "  FALHA") + "  " + oque);
	}

	private void Anotar(string s) { _linhas.Add(s); GD.Print("[filme] " + s); }

	public override void _Ready()
	{
		if (GameClient.Instance is not { } cli) return;
		cli.Falou += AoOuvir;
		OQueOOlhoVaiJulgar();
		GD.Print("[filme] no ar -- esperando o elenco do `--biofilme`");
	}

	/// <summary>
	/// O ENUNCIADO, ESCRITO ANTES DE QUALQUER FOTO SAIR.
	///
	/// Copiado do `RoboDeCena.OQueOOlhoVaiJulgar` e pelo motivo escrito la: foto nao tem legenda, e
	/// escrever a expectativa DEPOIS do resultado deixa qualquer imagem ser lida como confirmacao do que
	/// quer que ela mostre.
	/// </summary>
	private static void OQueOOlhoVaiJulgar()
	{
		GD.Print("[filme]   --   A TIRA `TIRA-filme-*.png` e a sequencia inteira, da esquerda pra");
		GD.Print("[filme]   --   direita, um quadro por segundo. O que se procura nela:");
		GD.Print("[filme]   --   1. EM NENHUM quadro pode haver corpo MEIO TROCADO -- dois desenhos de");
		GD.Print("[filme]   --      tamanhos diferentes empilhados, que e a foto do dono. Procure de");
		GD.Print("[filme]   --      proposito: e uma AUSENCIA, e ausencia e o que passa batido;");
		GD.Print("[filme]   --   2. o corpo NOVO so aparece no FIM. A tira `TIRA-troca-*.png` isola os");
		GD.Print("[filme]   --      tres quadros do instante: o anterior (corpo VELHO), o da troca e o");
		GD.Print("[filme]   --      seguinte (corpo NOVO). Tem que ser um corte seco entre dois;");
		GD.Print("[filme]   --   3. no quadro do corpo novo NAO pode haver silhueta de luz por cima.");
		GD.Print("");
	}

	public override void _Process(double delta)
	{
		_relogio += delta;
		if (!_fechou && _relogio >= Fim) Fechar();
	}

	private void AoOuvir(Jandirus.Net.Protocol.Fala tipo, string quem, string texto)
	{
		string elenco = Jandirus.Server.GameServer.MarcaDoElencoDoFilme;
		int i = texto.IndexOf(elenco, StringComparison.Ordinal);
		if (i >= 0)
		{
			string[] ids = texto[(i + elenco.Length)..].Trim().Split(',');
			if (ids.Length == 4 && int.TryParse(ids[0], out _f) && int.TryParse(ids[1], out _m)
				&& int.TryParse(ids[2], out _k) && int.TryParse(ids[3], out _d))
				Anotar($"elenco: F={_f} (o filme) | M={_m} (Oozaru) | K={_k} (nocaute) | D={_d} (defeito)");
			return;
		}

		string marca = Jandirus.Server.GameServer.MarcaDoFilmeDoBio;
		i = texto.IndexOf(marca, StringComparison.Ordinal);
		if (i < 0 || !int.TryParse(texto[(i + marca.Length)..].Trim(), out int passo)) return;
		if (passo == _passo) return;

		_passo = passo;

		// ============================ O INSTANTE DO GOLPE, MARCADO NA HORA EM QUE ELE E ANUNCIADO ============================
		// O passo 5 chega COM O FILME DO K RODANDO, e a rotina dele e recusada pelo `_ocupado` -- de
		// proposito (ver o bloco do `case 4`). Mas o instante nao pode se perder junto: sem ele, "que
		// corpo estava na tela quando o golpe caiu" viraria uma conta a partir das duracoes escritas no
		// palco, ou seja a bancada confirmaria o proprio roteiro em vez de medir o mundo.
		//
		// Este `if` roda no `AoOuvir`, ANTES de qualquer recusa por ocupacao, e guarda so um numero.
		// ==================================================================================================================
		if (passo == 5) _nocauteEm = _relogio;

		Anotar($"o palco anunciou o passo {passo}");
		_ = Roteiro(passo);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async Task Roteiro(int passo)
	{
		if (_ocupado) { Anotar($"     passo {passo} chegou com a bancada ocupada -- pulado"); return; }
		_ocupado = true;
		try
		{
			switch (passo)
			{
				// O CONTROLE: sem cena nenhuma, o corpo tem que estar PARADO no desenho. Sem esta
				// linha, "o corpo so troca na virada" ficaria verde num cliente que nunca trocasse
				// corpo nenhum -- que e o defeito oposto e igualmente feio.
				case 0: await Esperar(2.5); await OControle(); break;

				case 1: break;   // o nascimento e o rompimento assentam sozinhos

				// ============================ **O FILME** ============================
				case 2:
					await Filmar(_f, "bio", Jandirus.Core.Forms.Cinematicas.BioSemiPerfeito,
								 segundos: 44.0, intervalo: 1.0);
					break;

				// O OOZARU -- 6,2 s de cena, entao a amostragem tem que ser mais densa: a 1 Hz o filme
				// teria seis quadros e a virada (4,0 s) cairia entre dois deles com um segundo inteiro
				// de incerteza, que e mais do que a folga que a linha 1 do veredito tolera.
				case 3:
					await Filmar(_m, "oozaru", Jandirus.Core.Forms.Cinematicas.Oozaru,
								 segundos: 9.0, intervalo: 0.25);
					break;

				// ============================ A CENA INTERROMPIDA, E ELA E **UM** FILME E NAO DOIS RETRATOS ============================
				// A primeira versao tirava dois retratos avulsos no passo 5 (o do nocaute e o de depois
				// da virada), e a rodada voltou com uma linha so: *"passo 5 chegou com a bancada ocupada
				// -- pulado"*. O filme do K ainda estava rodando, o `_ocupado` recusou o passo, e a
				// pergunta do dono ("que corpo ficou?") ficou **sem medida nenhuma** -- calada, sem
				// falha, com o placar limpo.
				//
				// A correcao nao foi destravar o `_ocupado` (duas rotinas fotografando o mesmo corpo ao
				// mesmo tempo e pior): foi parar de pedir a mesma coisa duas vezes. O filme JA amostra a
				// pose a cada segundo, entao o nocaute aparece nele como um quadro em que `pose` vira
				// `ko` -- e ai o "antes" e o "depois" sao dois quadros do MESMO filme, na mesma regua,
				// sem depender de nenhum passo do palco chegar na hora.
				//
				// QUARENTA E DOIS SEGUNDOS pra o filme passar da virada (28,0 s) E da poeira da cratera; o golpe cai
				// aos 14 s, que e o passo 5 do palco.
				// ======================================================================================================================
				case 4:
					await Filmar(_k, "nocaute", Jandirus.Core.Forms.Cinematicas.BioSemiPerfeito,
								 segundos: 42.0, intervalo: 1.0);
					OVereditoDoNocaute();

					// ============================ E SO AGORA O DEFEITO E LIGADO ============================
					// A virada do K cai aos 28,0 s, DENTRO do filme acima. Ligar o interruptor antes
					// disso mudaria a entrega dele tambem, e o corpo do nocaute deixaria de ser o
					// controle limpo que ele existe pra ser. O passo 6 do palco so chega ~11 s depois
					// daqui.
					// ==================================================================================
					World.VestirNaHoraDeTeste = true;
					Anotar("     >>> DEFEITO INJETADO: `World.VestirNaHoraDeTeste = true` -- daqui pra "
						 + "frente a aparencia que chegar no meio de uma cena entra NA HORA, que e "
						 + "letra por letra o codigo de antes do conserto");
					break;

				case 5: break;   // o nocaute; quem o mede e o filme do passo 4

				case 6:
					try
					{
						_injetando = true;
						await Filmar(_d, "defeito", Jandirus.Core.Forms.Cinematicas.BioSemiPerfeito,
									 segundos: 44.0, intervalo: 1.0);
					}
					// DEVOLVE SEMPRE OS DOIS, mesmo se a medida tropecar: o estatico ligado contaminaria
					// todo corpo que trocasse de ficha dali pra frente, e o `_injetando` ligado engoliria
					// silenciosamente as falhas do fechamento.
					finally { World.VestirNaHoraDeTeste = false; _injetando = false; }
					OVereditoDaInjecao();
					break;

				case 7: Fechar(); break;
			}
		}
		catch (Exception e) { Anotar($"     a bancada tropecou no passo {passo}: {e.Message}"); }
		finally { _ocupado = false; }
	}

	private async Task Esperar(double s)
	{
		double ate = _relogio + s;
		while (_relogio < ate && !_fechou)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Task Quadros(int n)
	{
		for (int q = 0; q < n && !_fechou; q++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	// =====================================================================
	// O CONTROLE -- sem cena, o desenho fica parado
	// =====================================================================
	/// <summary>
	/// Trinta quadros do corpo do F antes de qualquer cena. A assinatura tem que ser a MESMA nos trinta.
	///
	/// Sem isto, todas as linhas de "so trocou uma vez" seriam verdes de graca num cliente que trocasse
	/// a folha do corpo o tempo todo por outro motivo (pose, direcao, animacao) -- e a assinatura que
	/// esta bancada le e a FOLHA, que nao muda com nada disso. E uma afirmacao que precisa ser
	/// verificada uma vez, e nao presumida trinta.
	/// </summary>
	private async Task OControle()
	{
		var vistas = new HashSet<string>();
		for (int q = 0; q < 30; q++)
		{
			if (Visual(_f) is { } v) vistas.Add(AssinaturaDoCorpo(v));
			await Quadros(1);
		}

		Conferir(vistas.Count == 1 && !vistas.Contains(""),
				 $"[controle] SEM cena, o desenho do corpo fica PARADO em 30 quadros -- "
			   + $"assinaturas vistas: {vistas.Count} ({string.Join(" / ", vistas.Select(Curto))})");
	}

	// =====================================================================
	// O FILME
	// =====================================================================
	/// <summary>Uma amostra do filme. Ver <see cref="Filmar"/>.</summary>
	private sealed record Amostra(int N, double T, double Relogio, string Corpo, string Silhueta,
								  bool CenaViva, string Pose, bool PoseTravada, Foto? Recorte,
								  Image? SpriteImg)
	{
		/// <summary>Os bytes do <see cref="SpriteImg"/> -- a regua da linha do sprite no veredito.</summary>
		public byte[]? Sprite { get; } = SpriteImg?.GetData();
	}

	/// <summary>O que um filme concluiu. Ver <see cref="OVereditoDoFilme"/>.</summary>
	private sealed record Veredito(int Quadros, int Trocas, int Troca, double TDaTroca,
								   int Misturados, bool SpriteInteiro, bool NaVirada);

	/// <summary>
	/// ============================ FILMA UMA CENA, DO SEGUNDO ZERO AO FIM ============================
	/// Ele NAO dispara a cena: quem dispara e o servidor, pelas portas de producao. O robo so acompanha
	/// -- e comeca a filmar no quadro seguinte ao anuncio, que e o mais perto do segundo zero que da pra
	/// chegar sem inventar um relogio proprio.
	///
	/// O RELOGIO E O DA PROPRIA CENA (`Transformacao.TempoDeTeste`) enquanto ela existe, e o do robo
	/// depois que ela morre. Os dois numeros aparecem na anotacao de cada quadro justamente pra a
	/// diferenca entre eles ser visivel se um dia houver uma.
	/// ==========================================================================================
	/// </summary>
	private async Task Filmar(int id, string rotulo, Jandirus.Core.Forms.Cinematica roteiro,
							  double segundos, double intervalo)
	{
		if (id == 0) { Conferir(false, $"[{rotulo}] a bancada recebeu o id do corpo pra filmar"); return; }

		// ============================ O INSTANTE DA VIRADA E LIDO DO ROTEIRO, NUNCA DIGITADO ============================
		// Copiado do `RoboDeCena.Comecar` e pelo motivo escrito la: um numero cravado aqui mediria o
		// instante errado no dia em que um prazo do DM fosse recontado, e ninguem saberia. `Cinematica.Beats`
		// passa pelo funil no `init`, entao o beat que carrega `Efeito.Assumir` E a virada, por construcao.
		// ==========================================================================================================
		double vira = roteiro.Segundos;
		foreach (Jandirus.Core.Forms.Beat b in roteiro.Beats)
			if (b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Assumir)) { vira = b.Em; break; }

		Anotar("");
		Anotar($"===== FILME `{rotulo}` (corpo {id}) -- cena de {roteiro.Segundos:0.0}s, "
			 + $"virada aos {vira:0.0}s, amostra a cada {intervalo:0.00}s =====");

		var filme = new List<Amostra>();
		double comecou = _relogio, proxima = 0;
		int n = 0;

		while (_relogio - comecou < segundos && !_fechou)
		{
			if (_relogio - comecou < proxima)
			{ await Quadros(1); continue; }
			proxima += intervalo;

			if (Visual(id) is not { } v) { await Quadros(1); continue; }
			Transformacao? cena = CenaDe(id);

			await Quadros(QuadrosDeEspera);

			double t = cena is { } c && IsInstanceValid(c) ? c.TempoDeTeste : _relogio - comecou;
			var a = new Amostra(
				n++, t, _relogio,
				AssinaturaDoCorpo(v),
				v.SilhuetaDeCenaDeTeste ?? "",
				cena is { } viva && IsInstanceValid(viva) && viva.Rodando,
				v.PoseDeTeste, v.PoseTravadaDeTeste,
				Recortar(id),
				SpriteNaTela(v));
			filme.Add(a);

			Anotar($"     q{a.N:00} t={a.T,5:0.0}s  cena={(a.CenaViva ? "viva" : "  - ")}  "
				 + $"corpo={Curto(a.Corpo)}  silhueta={(a.Silhueta.Length == 0 ? "-" : Nome(a.Silhueta))}"
				 + $"  pose={a.Pose}{(a.PoseTravada ? " (travada)" : "")}");
		}

		_crus[rotulo] = filme;
		_filmes[rotulo] = OVereditoDoFilme(rotulo, filme, vira);
		SalvarOFilme(rotulo, filme, _filmes[rotulo]);
	}

	/// <summary>
	/// ============================ O VEREDITO, E ELE E SOBRE A SEQUENCIA E NAO SOBRE UM QUADRO ============================
	/// As cinco linhas se sustentam umas nas outras e nenhuma serve sozinha:
	///
	///   0. O FILME EXISTE. Uma bancada visual que nao filmou nada fecha com "0 falhas" -- e o modo de
	///      falha mais perigoso desta familia inteira (esta escrito em tres robos desta casa);
	///   1. o corpo MUDOU em algum ponto. Sem ela, todas as outras passam de graca num cliente que
	///      nunca trocasse corpo nenhum;
	///   2. mudou UMA VEZ SO. Duas trocas no mesmo filme sao o "meio trocado" na sua forma mais crua:
	///      alguma coisa vestiu no meio e alguma outra corrigiu depois;
	///   3. mudou **NA VIRADA**. Esta e a linha do dono. A folga e de um intervalo de amostragem, que e
	///      a incerteza da propria medida -- nao um afrouxamento;
	///   4. no quadro do corpo NOVO nao ha mais silhueta de cena. E a forma exata da foto dele;
	///   5. e o SPRITE do corpo e integro dos dois lados da troca: identico ao primeiro quadro do filme
	///      ate a troca, e diferente dele dai em diante. Nenhum quadro intermediario.
	/// ==================================================================================================================
	/// </summary>
	private Veredito OVereditoDoFilme(string rotulo, IReadOnlyList<Amostra> filme, double vira)
	{
		if (filme.Count < 5)
		{
			Conferir(false, $"[{rotulo}] o filme tem quadros ({filme.Count}) -- sem isto todas as "
						  + "linhas abaixo sao afirmacoes sobre o nada");
			return new Veredito(filme.Count, 0, -1, 0, 0, false, false);
		}

		// AS TROCAS, uma por uma. `Where` sobre pares consecutivos: nao ha "primeira troca" aqui de
		// proposito -- a bancada quer saber QUANTAS houve, e ficar com a primeira esconderia a segunda.
		var trocas = new List<int>();
		for (int i = 1; i < filme.Count; i++)
			if (filme[i].Corpo != filme[i - 1].Corpo) trocas.Add(i);

		int troca = trocas.Count > 0 ? trocas[0] : -1;
		double tTroca = troca > 0 ? filme[troca].T : -1;
		double intervalo = filme.Count > 1 ? Math.Max(0.05, filme[1].T - filme[0].T) : 1.0;

		Conferir(filme.Count >= 5,
				 $"[{rotulo}] o filme rendeu {filme.Count} quadros da cena inteira");

		Conferir(trocas.Count > 0,
				 $"[{rotulo}] o desenho do corpo MUDOU em algum ponto do filme -- sem isto, "
			   + "\"so troca no fim\" ficaria verde num corpo que nunca trocasse "
			   + $"(primeiro \"{Curto(filme[0].Corpo)}\", ultimo \"{Curto(filme[^1].Corpo)}\")");

		Conferir(trocas.Count == 1,
				 $"[{rotulo}] e mudou **UMA VEZ SO** no filme inteiro -- {trocas.Count} troca(s) "
			   + $"nos quadros [{string.Join(", ", trocas.Select(i => $"q{i:00}"))}]. Duas trocas sao "
			   + "o corpo meio trocado na forma mais crua: alguem vestiu no meio e alguem corrigiu");

		// ============================ A LINHA DO DONO ============================
		// A TOLERANCIA E UM INTERVALO DE AMOSTRAGEM, e ela e a incerteza da medida e nao folga: o quadro
		// que flagra a troca e o PRIMEIRO depois dela, entao ele cai em algum lugar entre `vira` e
		// `vira + intervalo`. Exigir `T >= vira` cravado reprovaria por arredondamento.
		//
		// O QUE ELA **NAO** TOLERA e o outro lado: um quadro com o corpo novo ANTES de `vira` reprova
		// sem folga nenhuma, porque e exatamente isso que a foto do dono mostra.
		// ========================================================================
		bool naVirada = troca > 0 && tTroca >= vira - 0.05 && tTroca <= vira + intervalo + 0.35;
		Conferir(naVirada,
				 $"[{rotulo}] a troca caiu **NA VIRADA** ({tTroca:0.0}s, e a virada da cena e "
			   + $"{vira:0.0}s; a amostra e de {intervalo:0.00}s) -- o pedido do dono e literalmente "
			   + "este numero: nada de corpo novo antes do fim");

		// ============================ **O QUADRO MISTURADO** -- A FOTO DO DONO, CONTADA ============================
		// A definicao e o corpo FINAL do filme com a silhueta da cena ACESA por cima. E ela e contada no
		// filme INTEIRO e nao no instante da troca, e isso foi corrigido POR UMA RODADA VERMELHA: na
		// primeira versao a conta pendurava no indice da troca, e no filme com o defeito injetado **nao
		// houve troca nenhuma** -- o corpo ja nasceu novo no quadro 0. A linha que devia acusar o defeito
		// ficou verde justamente na rodada em que o defeito estava presente, que e o pior jeito de uma
		// bancada errar.
		//
		// Contando sobre todos os quadros ela responde nos dois casos: no filme limpo da ZERO (o corpo
		// novo so aparece depois de a silhueta sair) e no filme com defeito da 28 -- vinte e oito
		// segundos de duas figuras de tamanhos diferentes empilhadas, que e a imagem que o dono mandou.
		//
		// Nas cenas SEM silhueta (o Oozaru) ela e vacuosamente zero, e a linha diz isso em vez de deixar
		// o verde passar por prova do que ele nao e.
		// ========================================================================================================
		bool temSilhueta = filme.Any(a => a.Silhueta.Length > 0);
		int misturados = filme.Count(a => a.Silhueta.Length > 0 && a.Corpo == filme[^1].Corpo);
		Conferir(misturados == 0,
				 $"[{rotulo}] em NENHUM quadro ha o corpo NOVO com a silhueta de cena acesa por cima -- "
			   + $"{misturados} quadro(s) misturado(s) de {filme.Count} "
			   + $"({(temSilhueta ? "e esta cena TEM silhueta -- a do bio" : "esta cena nao usa silhueta")}). "
			   + "A silhueta e dimensionada pro corpo VELHO: ela sobre o novo sao as duas figuras "
			   + "empilhadas da foto do dono");

		// ============================ O SPRITE, QUE E A PROVA DE QUE NAO HOUVE MEIO TERMO ============================
		// Byte a byte contra o PRIMEIRO quadro do filme. Antes da troca tem que ser igual a ele; depois,
		// diferente. Um unico quadro que fosse "nem um nem outro" apareceria aqui como um terceiro
		// valor -- e e a unica medida desta bancada que nao pode ser enganada por cenario, luz ou efeito.
		// ========================================================================================================
		bool spriteInteiro = troca > 0 && filme[0].Sprite is { } zero
			&& filme.Take(troca).All(a => a.Sprite != null && a.Sprite.SequenceEqual(zero))
			&& filme.Skip(troca).All(a => a.Sprite != null && !a.Sprite.SequenceEqual(zero));
		Conferir(spriteInteiro,
				 $"[{rotulo}] o SPRITE CRU do corpo e o mesmo do quadro 0 ate q{Math.Max(troca - 1, 0):00} "
			   + $"e outro de q{Math.Max(troca, 0):00} em diante -- corte seco entre dois quadros, "
			   + "sem quadro intermediario");

		return new Veredito(filme.Count, trocas.Count, troca, tTroca, misturados, spriteInteiro, naVirada);
	}

	// =====================================================================
	// O NOCAUTE NO MEIO DA CENA
	// =====================================================================
	/// <summary>Os filmes crus, guardados pra as perguntas que precisam dos quadros e nao do veredito.</summary>
	private readonly Dictionary<string, List<Amostra>> _crus = [];

	/// <summary>Em que instante do relogio do robo o palco anunciou o golpe. -1 = ainda nao caiu.</summary>
	private double _nocauteEm = -1;

	/// <summary>
	/// ============================ A CENA INTERROMPIDA -- "QUE CORPO FICOU?" ============================
	/// A pergunta e do dono e a resposta desta bancada nao e uma preferencia: e o que ela mediu, e os
	/// dois quadros que a sustentam sao do MESMO filme, na mesma regua.
	///
	/// ============================ E O QUE ELA MEDIU CONTRADIZ UM COMENTARIO DA CASA ============================
	/// Havia uma frase em `World._pendentes` que prometia mais do que o codigo entrega: *"uma cena de
	/// 28 s cortada aos 3 s por um nocaute entrega a aparencia nova aos 3 s"*. **Um nocaute nao corta
	/// cena nenhuma**: o `Transformacao._Process` so encerra pelo teto, pelo alvo deixar de EXISTIR e
	/// pelo `_ExitTree`, e um corpo nocauteado continua existindo. Ele leva o golpe, a cena segue, e o
	/// corpo novo entra na virada como em qualquer outra. (A frase la ja foi corrigida com esta medida
	/// como referencia.)
	///
	/// ============================ E A POSE NEM CAI, PORQUE A CENA A PRENDE ============================
	/// A primeira versao desta medida procurava a pose `ko` no filme e nao achava nenhuma -- com o
	/// servidor logando `KO=True` no mesmo instante. Nao e defeito e nao e contradicao: a cinematica
	/// TRANCA a pose (`CharacterVisual.PoseTravadaDeTeste`), que e a regra do dono de que o corpo fica
	/// preso a cena inteira. Quem esta virando nao cai no chao no meio da propria metamorfose.
	///
	/// Entao o golpe nao se procura no desenho: ele se marca no INSTANTE em que o palco o anuncia
	/// (<see cref="_nocauteEm"/>), e o que se cobra do desenho e o que o desenho tem a dizer -- a cena
	/// viva, a pose travada e o corpo ainda velho naquele quadro.
	/// ================================================================================================
	/// </summary>
	private void OVereditoDoNocaute()
	{
		if (!_crus.TryGetValue("nocaute", out List<Amostra>? filme) || filme.Count < 5)
		{ Conferir(false, "[nocaute] o filme da cena interrompida rendeu quadros"); return; }

		int caiu = _nocauteEm < 0 ? -1 : filme.FindIndex(a => a.Relogio >= _nocauteEm);
		Conferir(caiu > 0 && caiu < filme.Count - 1,
				 $"[nocaute] o palco anunciou o golpe DENTRO do filme -- primeiro quadro depois dele e "
			   + $"q{caiu:00} (t={(caiu >= 0 ? filme[caiu].T : -1):0.0}s de uma cena de 28,0s). Sem esta "
			   + "linha as tres abaixo falariam de um golpe que caiu fora da janela medida");
		if (caiu <= 0 || caiu >= filme.Count - 1) return;

		Conferir(filme[caiu].CenaViva,
				 $"[nocaute] **o nocaute NAO interrompe a cinematica** -- no quadro q{caiu:00} ela "
			   + "continua rodando. Cena so acaba pelo teto, pelo corpo deixar de existir e pela troca "
			   + "de zona; um corpo nocauteado continua existindo");

		Conferir(filme[caiu].PoseTravada && filme[caiu].Corpo == filme[0].Corpo,
				 $"[nocaute] ...e no instante do golpe o corpo ainda e o VELHO "
			   + $"({Curto(filme[caiu].Corpo)}), com a pose TRAVADA pela cena (pose "
			   + $"'{filme[caiu].Pose}') -- levar porrada no meio da metamorfose nao adianta a "
			   + "aparencia nova nem derruba o boneco antes da hora");

		Conferir(filme[^1].Corpo != filme[0].Corpo,
				 $"[nocaute] **e o corpo que ficou foi o NOVO** ({Curto(filme[^1].Corpo)}), entregue "
			   + "na virada como em qualquer outra cena. Ninguem fica com a aparencia velha pra sempre "
			   + "por ter levado um golpe no meio");
	}

	// =====================================================================
	// O DEFEITO INJETADO
	// =====================================================================
	/// <summary>
	/// ============================ A RODADA QUE **TEM** QUE FICAR VERMELHA ============================
	/// O corpo D roda a MESMA cena do corpo F, no mesmo palco, pela mesma porta de producao. A unica
	/// diferenca esta do outro lado do fio: `World.VestirNaHoraDeTeste` ligado, que faz o
	/// `AoReceberAparencia` voltar a ser o codigo de antes do conserto.
	///
	/// Entao o veredito do `defeito` tem que ser o NEGATIVO do veredito do `bio`. Uma bancada que
	/// aprovasse os dois estaria medindo outra coisa -- e este projeto ja documenta duas bancadas que
	/// ficaram verdes tres rodadas seguidas por sorte, e so a rodada do defeito injetado as derrubou.
	/// ==============================================================================================
	/// </summary>
	private void OVereditoDaInjecao()
	{
		if (!_filmes.TryGetValue("bio", out Veredito? bom) || !_filmes.TryGetValue("defeito", out Veredito? mau))
		{ Conferir(false, "[injecao] os dois filmes (limpo e com defeito) foram rodados"); return; }

		Conferir(_esperadas.Count > 0,
				 $"[injecao] **com o defeito injetado a bancada REPROVA** -- {_esperadas.Count} linha(s) "
			   + "do veredito ficaram vermelhas naquele filme. Sem isto, todas as verdes acima seriam "
			   + "afirmacoes que ninguem provou serem capazes de ficar vermelhas");

		Conferir(!mau.NaVirada,
				 $"[injecao] ...e a linha da VIRADA e uma delas: com o defeito o corpo ja e o novo no "
			   + $"quadro 0 (nem chega a haver troca -- {mau.Trocas} no filme inteiro), enquanto no "
			   + $"filme limpo ela cai aos {bom.TDaTroca:0.0}s");

		Conferir(mau.Misturados > 0,
				 $"[injecao] ...e o QUADRO MISTURADO aparece: {mau.Misturados} quadro(s) com o corpo "
			   + "NOVO e a silhueta de luz acesa por cima -- que e, medida, exatamente a foto que o "
			   + "dono mandou: duas figuras de tamanhos diferentes empilhadas no mesmo instante");

		Conferir(bom.NaVirada && bom.Misturados == 0,
				 $"[injecao] ...e o filme LIMPO, medido pelas MESMAS duas linhas no mesmo palco, passa "
			   + $"nas duas ({bom.Misturados} quadro misturado, troca aos {bom.TDaTroca:0.0}s). E o par "
			   + "que faz da medida uma medida");
	}

	// =====================================================================
	// LEITURA DO CORPO
	// =====================================================================
	/// <summary>
	/// ============================ "QUAL CORPO ESTA DESENHADO", E SAO DUAS CAMADAS ============================
	/// A base (`_corpo`) e a da forma (`_corpoDaForma`), e as duas trocam de folha por caminhos
	/// diferentes: o bio troca a primeira pela FICHA que chega na rede, o Oozaru troca a segunda pelo
	/// CATALOGO que so o cliente le. Uma assinatura com uma so nao veria metade das trocas do jogo.
	///
	/// A VISIBILIDADE ENTRA NA ASSINATURA de proposito: a camada da forma existe apagada em varios
	/// estados, e "existe" nao e "esta na tela" -- a mesma distincao que o `CorpoDaFormaVisivelDeTeste`
	/// existe pra fazer.
	/// ======================================================================================================
	/// </summary>
	private static string AssinaturaDoCorpo(CharacterVisual v) =>
		$"base:{Nome(v.FolhaDoCorpoDeTeste)}"
		+ (v.CorpoBaseVisivelDeTeste ? "" : "(oculto)")
		+ $"|forma:{(v.CorpoDaFormaVisivelDeTeste ? Nome(v.FolhaDoCorpoDaFormaDeTeste) : "-")}";

	/// <summary>
	/// ============================ OS PIXELS DO CORPO QUE ESTA **NA TELA**, E SAO DUAS CAMADAS ============================
	/// A primeira rodada desta bancada leu so `QuadroDoCorpoDeTeste` (o corpo BASE) e reprovou o filme do
	/// Oozaru com todas as outras cinco linhas verdes -- o sprite base dele nao muda em momento nenhum
	/// (continua `NewPaleMale` do primeiro ao ultimo quadro), porque a fera nao troca a folha da base:
	/// ela APAGA a base e desenha `_corpoDaForma` por cima (`Oozaru.dm:123-125` -- o macaco nao veste
	/// nada, ele SUBSTITUI o mob).
	///
	/// Ou seja a bancada estava certa em reprovar e estava medindo a coisa errada: um sprite que a tela
	/// nao mostra. Aqui a leitura segue a MESMA regra da <see cref="AssinaturaDoCorpo"/> -- a camada de
	/// forma quando ela esta visivel, a base quando nao esta --, e as duas passam a responder sobre o
	/// mesmo objeto. Duas reguas diferentes pra "o corpo" era o defeito.
	/// ================================================================================================================
	/// </summary>
	private static Image? SpriteNaTela(CharacterVisual v) =>
		v.CorpoDaFormaVisivelDeTeste
			? v.QuadroDoCorpoDaFormaDeTeste(primeiro: true)
			: v.QuadroDoCorpoDeTeste(primeiro: true);

	private static CharacterVisual? Visual(int id) =>
		id == 0 ? null : World.Instancia?.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual");

	/// <summary>
	/// A CINEMATICA QUE ESTA RODANDO NESTE CORPO -- perguntada a ARVORE, do mesmo jeito que o
	/// `World.CenaEmCurso` faz, e pelo mesmo motivo escrito la: um mapa `id -> cena` mantido pela
	/// bancada envelheceria calado no dia em que nascesse a quinta cena.
	/// </summary>
	private static Transformacao? CenaDe(int id)
	{
		if (World.Instancia?.CorpoDeTeste(id) is not { } corpo) return null;
		if (corpo.GetParent() is not { } pai) return null;
		Transformacao? qualquer = null;
		foreach (Node n in pai.GetChildren())
			if (n is Transformacao t && t.AlvoDaCena == corpo)
			{ if (t.Rodando) return t; qualquer ??= t; }
		return qualquer;
	}

	// =====================================================================
	// FOTO
	// =====================================================================
	/// <summary>Um recorte E DE ONDE ELE VEIO -- ver o mesmo par no `RoboDeOlharDoBio`.</summary>
	private sealed record Foto(Image Img, Rect2I Caixa);

	private Foto? Recortar(int id)
	{
		Image? tela = GetViewport()?.GetTexture()?.GetImage();
		if (tela == null || tela.IsEmpty()) return null;
		if (World.Instancia?.PosicaoDesenhadaDe(id) is not { } mundo) return null;

		Vector2 tel = (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * mundo;
		var caixa = new Rect2I((int)tel.X - Largura / 2, (int)tel.Y - Altura * 2 / 3, Largura, Altura);
		caixa = caixa.Intersection(new Rect2I(0, 0, tela.GetWidth(), tela.GetHeight()));
		if (caixa.Size.X < Largura || caixa.Size.Y < Altura) return null;

		Image corte = tela.GetRegion(caixa);
		corte.Convert(Image.Format.Rgba8);
		return new Foto(corte, caixa);
	}

	private void Salvar(Foto foto, string nome)
	{
		Image copia = (Image)foto.Img.Duplicate();
		copia.Resize(copia.GetWidth() * Ampliacao, copia.GetHeight() * Ampliacao, Image.Interpolation.Nearest);
		string caminho = ProjectSettings.GlobalizePath($"{Pasta}{nome}.png");
		copia.SavePng(caminho);
		Anotar($"     -> {caminho}");
	}

	/// <summary>O sprite CRU guardado naquele quadro do filme -- ver o bloco no <see cref="SalvarOFilme"/>.</summary>
	private void SalvarSprite(Amostra a, string nome)
	{
		if (a.SpriteImg is not { } q) return;
		var copia = (Image)q.Duplicate();
		copia.Resize(copia.GetWidth() * 6, copia.GetHeight() * 6, Image.Interpolation.Nearest);
		string caminho = ProjectSettings.GlobalizePath($"{Pasta}{nome}.png");
		copia.SavePng(caminho);
		Anotar($"     -> {caminho}");
	}

	/// <summary>
	/// ============================ AS DUAS TIRAS, E ELAS NAO DIZEM A MESMA COISA ============================
	///   `TIRA-filme-<rotulo>.png` -- a cena INTEIRA, um quadro por amostra, da esquerda pra direita. E
	///        nela que se procura o corpo meio trocado: um defeito de ordem se ve na sequencia;
	///   `TIRA-troca-<rotulo>.png` -- so os tres quadros do instante (anterior / troca / seguinte). Ela
	///        existe porque a tira longa tem trinta imagens pequenas e o corte seco entre duas delas e
	///        justamente o que se perde quando ha trinta.
	///
	/// O QUADRO DA TROCA VEM MARCADO com uma faixa clara embaixo: numa tira de trinta recortes iguais,
	/// "o de numero dezenove" nao se acha a olho -- e a tira existe pra ser olhada a olho.
	/// ====================================================================================================
	/// </summary>
	private void SalvarOFilme(string rotulo, IReadOnlyList<Amostra> filme, Veredito v)
	{
		var comFoto = filme.Where(a => a.Recorte != null).ToList();
		if (comFoto.Count == 0)
		{
			Conferir(false, $"[{rotulo}] as fotos do filme renderam -- no headless o `GetImage` volta "
						  + "vazio e nao ha veredito visual nenhum; aqui a foto E o teste");
			return;
		}

		Tira($"filme-{rotulo}", comFoto, v.Troca);

		if (v.Troca > 0)
		{
			// ============================ E O ULTIMO QUADRO ENTRA NA TIRA DA TROCA, POR CAUSA DA POEIRA ============================
			// A primeira rodada boa desta bancada saiu com a tira da troca ILEGIVEL: a virada e o mesmo
			// beat que abre a CRATERA, e a nuvem de poeira dela cobre o boneco por varios segundos. O
			// veredito estava certo (o sprite trocou entre dois quadros, medido) e a IMAGEM nao mostrava
			// nada -- que numa bancada visual e meio caminho pra ninguem olhar.
			//
			// Nao ha o que consertar na cena: a poeira e do DM e o dono a pediu. O que se conserta e a
			// tira -- ela ganha o ultimo quadro do filme, quando a poeira ja baixou e o corpo novo esta
			// limpo na tela. Por isso os filmes do bio duram bem mais que a cena.
			// ==================================================================================================================
			var quadros = new List<Amostra>();
			for (int i = v.Troca - 1; i <= v.Troca + 1 && i < filme.Count; i++)
				if (i >= 0 && filme[i].Recorte != null) quadros.Add(filme[i]);
			if (filme[^1].Recorte != null && filme[^1].N != filme[Math.Min(v.Troca + 1, filme.Count - 1)].N)
				quadros.Add(filme[^1]);
			Tira($"troca-{rotulo}", quadros, 1);
			Anotar($"     a tira da troca: q{v.Troca - 1:00} (corpo VELHO), q{v.Troca:00} (a troca), "
				 + $"q{v.Troca + 1:00} e q{filme[^1].N:00} (o fim, com a poeira ja baixada)");

			// E OS DOIS LADOS DA TROCA TAMBEM SOLTOS, em tamanho cheio.
			if (filme[v.Troca - 1].Recorte is { } antes) Salvar(antes, $"troca-{rotulo}-1-ANTES-corpo-velho");
			if (filme[^1].Recorte is { } depois) Salvar(depois, $"troca-{rotulo}-2-DEPOIS-corpo-novo");

			// ============================ E O PAR DE SPRITES, QUE E O QUE A POEIRA NAO ALCANCA ============================
			// Sao os MESMOS pixels que a linha do sprite compara byte a byte -- o desenho cru do corpo,
			// sem cenario, sem cinematica, sem luz do dia e sem nuvem. Duas imagens: o quadro anterior a
			// troca e o da troca. E a unica prova de "velho / novo" desta bancada que nao depende de o
			// enquadramento estar limpo.
			// ========================================================================================================
			SalvarSprite(filme[v.Troca - 1], $"troca-{rotulo}-3-SPRITE-antes");
			SalvarSprite(filme[v.Troca], $"troca-{rotulo}-4-SPRITE-depois");
		}
	}

	private void Tira(string nome, IReadOnlyList<Amostra> fotos, int marcar)
	{
		if (fotos.Count == 0) return;
		const int folga = 4, faixa = 6;
		int w = fotos.Sum(f => f.Recorte!.Img.GetWidth()) + folga * (fotos.Count - 1);
		int h = fotos.Max(f => f.Recorte!.Img.GetHeight()) + faixa;

		Image tira = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		tira.Fill(new Color(0, 0, 0, 1));
		int x = 0;
		foreach (Amostra f in fotos)
		{
			Image img = f.Recorte!.Img;
			tira.BlitRect(img, new Rect2I(0, 0, img.GetWidth(), img.GetHeight()), new Vector2I(x, 0));

			// A FAIXA EMBAIXO: clara no quadro da troca, escura nos outros. Ver o cabecalho do
			// `SalvarOFilme` pra por que ela existe.
			bool eu = marcar >= 0 && f.N == marcar;
			for (int px = x; px < x + img.GetWidth(); px++)
				for (int py = img.GetHeight(); py < h; py++)
					tira.SetPixel(px, py, eu ? new Color(1, 1, 1, 1) : new Color(0.15f, 0.15f, 0.15f, 1));

			x += img.GetWidth() + folga;
		}

		tira.Resize(tira.GetWidth() * Ampliacao, tira.GetHeight() * Ampliacao, Image.Interpolation.Nearest);
		string caminho = ProjectSettings.GlobalizePath($"{Pasta}TIRA-{nome}.png");
		tira.SavePng(caminho);
		Anotar($"     -> {caminho}   <<< a tira");
	}

	private static string Nome(string caminho) =>
		caminho.Length == 0 ? "-" : System.IO.Path.GetFileNameWithoutExtension(caminho);

	/// <summary>A assinatura encurtada, pra a anotacao caber numa linha de terminal.</summary>
	private static string Curto(string assinatura) => assinatura.Replace("base:", "").Replace("|forma:", " + ");

	// =====================================================================
	// O VEREDITO
	// =====================================================================
	private void Fechar()
	{
		if (_fechou) return;
		_fechou = true;

		// DEVOLVE O INTERRUPTOR, sempre. Se a bancada fechar pelo teto (`Fim`) no meio do passo 6, o
		// `finally` de la nao roda -- e um estatico ligado sobreviveria ao fechamento.
		World.VestirNaHoraDeTeste = false;

		GD.Print("\n[filme] ===== A CINEMATICA QUADRO A QUADRO =====");

		// UMA BANCADA VISUAL QUE NAO FILMOU NADA REPROVA -- o modo de falha mais perigoso desta familia
		// e fechar com "0 falhas" porque o palco nunca subiu.
		Conferir(_f != 0 && _m != 0 && _k != 0 && _d != 0, "a bancada recebeu o elenco (quatro corpos)");
		Conferir(_passo >= 7, $"o roteiro chegou ao fim (parou no passo {_passo} de 7)");

		// ============================ E A REGRA E **GENERICA**, QUE E A PERGUNTA DE FUNDO ============================
		// O dono cobrou o bio. Mas a queixa dele tem irma (*"a cratera no meio da cinematica"*), e as
		// duas sao a mesma coisa: efeito de fim acontecendo no comeco. Consertar so o bio seria trocar
		// um `if` por outro. Esta linha cobra que o instante seja O MESMO em duas racas cujos corpos
		// trocam por caminhos de codigo DIFERENTES -- a ficha (rede) e o catalogo (cliente).
		// ========================================================================================================
		Conferir(_filmes.TryGetValue("bio", out Veredito? b) && b.NaVirada
				 && _filmes.TryGetValue("oozaru", out Veredito? o) && o.NaVirada,
				 "[regra] as DUAS racas trocam o corpo na virada -- o bio (que troca pela FICHA, "
			   + "`Appearance.Corpo` -> `S2C.PeerLook`) e o Oozaru (que troca pelo CATALOGO, "
			   + "`FormaDef.Corpo`, lido so pelo cliente). Um `if` de bio passaria na primeira e "
			   + "reprovaria nesta");

		GD.Print(_falhas.Count == 0
			? $"[filme] ===== {_oks} ok, 0 falha(s) ====="
			: $"[filme] ===== {_oks} ok, {_falhas.Count} FALHA(S) =====\n[filme]   "
			  + string.Join("\n[filme]   ", _falhas));

		GetTree().Quit();
	}
}
