using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DE DOIS PROCESSOS (`--dois a` e `--dois b`). O que ela mede nao cabe num processo so.
///
/// ============================ POR QUE ELA PRECISA EXISTIR ============================
/// Os tres defeitos que o dono relatou olhando duas telas -- a forma que nao aparece pra quem
/// acabou de entrar, o contorno aceso no corpo ALHEIO sem Ki, e a aura solta do corpo -- sao todos
/// do corpo REMOTO. Nenhuma bancada deste port jamais teve DOIS clientes: o `--diagforma` forja
/// fantasmas dentro do proprio processo e chama `AoReceberAparencia`/`AoReceberSnapshot` na mao,
/// o que prova a FUNCAO e nao a CORRIDA. A corrida entre o canal confiavel (o `PeerLook`) e o
/// nao-confiavel (o snapshot) so acontece quando existem dois soquetes de verdade.
///
/// ============================ OS DOIS PAPEIS ============================
/// `--dois a` e quem SE TRANSFORMA. Ele nao mede nada: espera entrar no mundo, conta
/// `--doisatraso` segundos e sobe a escada (tecla C, pelo `SendTransformar`) ou forca a forma que
/// `--doisforma` pedir (o mesmo `admin_forma` do menu P, que e como se chega no Ultra Instinto sem
/// jogar meia hora).
///
/// `--dois b` e quem OLHA. Ele nunca se transforma: varre os corpos remotos, escreve o que cada
/// camada REALMENTE tem no material (nao o que se pediu) e tira foto. A foto e o unico juiz de
/// posicao -- "a aura esta torta" nao se prova com numero de camada nenhum.
///
/// A ORDEM DE ENTRADA E QUEM ROTEIA O TESTE, e ela mora fora daqui, na linha de comando: subir o
/// A primeiro e o B depois mede "nao sincroniza na ENTRADA"; subir os dois e transformar depois
/// mede "nao sincroniza NUNCA". Sao os dois lados do mesmo relato, e so o relogio os separa.
/// ====================================================================================
/// </summary>
public partial class RoboDeDoisCorpos : Node
{
	/// <summary>`a` transforma, `b` olha. Vem do `--dois`.</summary>
	public string Papel = "b";

	/// <summary>Segundos, contados de quando o corpo entra no mundo, ate o A se transformar.</summary>
	public double Atraso = 12;

	/// <summary>Id de forma pro `admin_forma`. Vazio = sobe a escada com a tecla C.</summary>
	public string Forma = "";

	/// <summary>Segundo em que o A comeca a SEGURAR o C (carregar Ki). 0 = nunca carrega.</summary>
	public double Carga;

	/// <summary>A conta do A, pro `admin_promover` -- ver o cabecalho de <see cref="Agir"/>.</summary>
	public string Conta = "";

	/// <summary>Rotulo do arquivo de foto -- duas instancias escrevem no MESMO `user://`.</summary>
	public string Rotulo = "x";

	private bool _dentro;
	private double _relogio;
	private bool _agiu;
	private bool _carregando;
	private bool _promoveu;
	private bool _voou;

	/// <summary>Segundo em que o A decola. 0 = fica no chao. Exige `--vooteste` no servidor.</summary>
	public double Voar;

	/// <summary>Segundo em que o A SOLTA o C. 0 = segura ate o fim. Ver o bloco em <see cref="Agir"/>.</summary>
	public double Soltar;
	private bool _soltou;

	/// <summary>
	/// ============================ O SEGUNDO EM QUE A FICHA VOLTA A PASSAR ============================
	/// Vale PROS DOIS PAPEIS, e com sentidos diferentes -- o `--doisficha` tem que ir na linha de
	/// comando dos dois processos:
	///
	///   * no A e a HORA de pedir `admin_ir` (ver o bloco em <see cref="Agir"/>), que e o que faz o
	///     servidor reemitir o `PeerLook` dele;
	///   * no B e a EXPECTATIVA. Sem ela, uma rodada em que a ficha nunca chegou fecharia com o mesmo
	///     placar de uma em que ela chegou e nao despiu nada -- ver a linha no <see cref="Fechar"/>.
	///
	/// 0 = nao mexe com ficha, e a bancada roda como antes.
	/// ============================================================================================
	/// </summary>
	public double Ficha;
	private bool _pediuFicha;

	/// <summary>Quantas vezes um `PeerLook` do OUTRO caiu num corpo que ja estava na minha tela.</summary>
	private int _fichasEmCorpoVivo;

	/// <summary>O id do outro jogador, colhido do `PeerLook`. E o alvo do `admin_ir`.</summary>
	private int _idDoOutro;

	/// <summary>
	/// A FICHA DO OUTRO, como ela chegou pela rede. E o unico jeito de o B saber a cor de olho
	/// ESPERADA na base -- ver <see cref="TintaEsperadaDoAlheio"/>.
	/// </summary>
	private Jandirus.Core.Appearance.Appearance? _fichaDoOutro;

	private int _fotos;
	private double _proximoDump;
	private readonly List<string> _linhas = [];

	public override void _Ready()
	{
		if (GameClient.Instance is not { } cli) return;
		cli.Joined += AoEntrar;
		cli.FormaMudou += AoMudarForma;
		cli.OozaruMudou += (quem, f, l, d) => Anotar($"servidor: oozaru de {quem} -> {f} (ligado {l})");
		cli.PeerLooked += AoReceberFicha;
		cli.PeerLeft += quem => Anotar($"servidor: {quem} saiu");
		GD.Print($"[dois-{Papel}] no ar (atraso {Atraso}s, forma '{Forma}', rotulo {Rotulo})");

		// A ANCORA DA AURA NAO PRECISA DE MUNDO, DE REDE NEM DE SEGUNDO CORPO -- e por isso ela roda
		// ja, antes de qualquer coisa poder dar errado. Mesmo motivo do bloco puro do
		// `RoboDeNebulosa`: uma rodada que morre por porta ocupada ainda entrega esta parte do veredito.
		if (Papel == "b") AAncoraSaiDaFolha();

		// ============================ O `Joined` JA PASSOU ============================
		// Este no e criado pelo `Boot.EntrarNoJogo`, que e justamente o que roda NA resposta do
		// `Joined` -- assinar o evento aqui e chegar depois da festa, e o relogio nunca comecava.
		// Custou uma rodada inteira em silencio: os dois processos subiram, se viram no
		// `[world] entrou no meu campo de visao` e nao mediram nada.
		if (cli.LocalId != 0) AoEntrar(cli.LocalId, cli.ZonaDeTeste, default, "(ja estava dentro)");
	}

	private void AoEntrar(int id, ZoneKey z, Vec2 spawn, string nome)
	{
		_dentro = true;
		_relogio = 0;
		_proximoDump = 3;
		GD.Print($"[dois-{Papel}] ENTREI: id {id} nome '{nome}' zona {z} em ({spawn.X:0},{spawn.Y:0})");
	}

	// O `dominada` E O BIT NOVO DO `S2C.Forma` -- ver `Catalogo.DominouOSuperSaiyajin`. Ele entra no
	// relatorio porque e ELE quem decide o cabelo `SSjFP` e o nome "Grade 4": um dump que mostrasse o
	// corpo do outro com cabelo de Full Power sem dizer que o bit chegou nao explicaria nada.
	private void AoMudarForma(int quem, int de, int para, Jandirus.Core.Forms.DegrauDeCena degrau,
							  bool dominada)
	{
		string nde = Jandirus.Core.Forms.Catalogo.PorRede((ushort)de)?.Id ?? de.ToString();
		string npara = Jandirus.Core.Forms.Catalogo.PorRede((ushort)para)?.Id ?? para.ToString();
		bool meu = GameClient.Instance is { } c && c.LocalId == quem;
		Anotar($"servidor: FORMA de {quem}{(meu ? " (EU)" : " (OUTRO)")}: {nde} -> {npara} "
			 + $"(cena {degrau}{(dominada ? ", DOMINADA" : "")})");

		// QUEM ESTA EM QUE FORMA e a metade do carimbo que casa os dois retratos (a outra e o Ki).
		// O corpo do A e o corpo remoto do B tem que ser comparados NO MESMO ESTADO -- comparar um
		// SSJ1 com uma base daria uma lista de cem diferencas verdadeiras e inuteis.
		if (meu) _minhaForma = npara; else _formaDoOutro = npara;
		// UM DUMP LOGO DEPOIS DA MUDANCA, e nao no mesmo quadro: a cena do outro corpo leva alguns
		// quadros pra vestir, e fotografar no instante do pacote fotografaria o corpo no meio da troca.
		if (Papel == "b" && !meu) _proximoDump = Math.Min(_proximoDump, _relogio + 2.5);
	}

	/// <summary>
	/// ============================ A FICHA CAINDO NUM CORPO QUE JA EXISTE ============================
	/// Era uma lambda de log. Virou metodo porque este pacote e o SEGUNDO caminho que pode despir uma
	/// transformacao, e ele nao tinha juiz nenhum: `CharacterVisual.Vestir` remonta as camadas do
	/// zero, reescreve o penteado base e retinge o olho com a cor da FICHA. Se nada repuser a forma
	/// depois disso, o rabo e o olho de quem esta transformado voltam ao normal na tela dos OUTROS --
	/// e some do jeito mais barato de todos: sem erro, sem log, so a cor errada.
	///
	/// ============================ E O CORPO PRECISA JA EXISTIR PRA VALER ============================
	/// A guarda `_corpoAlheio` valido e a pergunta inteira. Ficha que chega ANTES do corpo e o caminho
	/// de NASCIMENTO (o `World.VestirCorpoInteiro` monta tudo de uma vez, e a `AChegadaNaZona` do
	/// `--diagforma` ja cobre isso); ficha que chega DEPOIS e a corrida que so dois soquetes produzem,
	/// e e ela que este contador conta. Se o node tiver sido liberado e renascido, `IsInstanceValid`
	/// devolve falso e o pacote nao e contado -- de proposito: aquilo seria nascimento de novo.
	///
	/// REABRIR A JANELA E O QUE FAZ O JUIZ OLHAR DUAS VEZES. O `_jaJulguei` casa `travessia|estado`, e
	/// a ficha nao muda nem a forma nem o Ki -- ou seja, sem contar uma travessia nova, tudo o que
	/// viesse depois dela cairia numa chave ja julgada e seria descartado em silencio.
	/// ============================================================================================
	/// </summary>
	private void AoReceberFicha(int quem, string nome, string raca, string genero,
								Jandirus.Core.Appearance.Appearance ap)
	{
		bool meu = GameClient.Instance is { } c && c.LocalId == quem;
		Anotar($"servidor: PeerLook de {quem}{(meu ? " (EU)" : " (OUTRO)")} ({nome}, {raca}) -- "
			 + $"cabelo da ficha '{ap.Cabelo}', olho da ficha {ap.CorOlho}, "
			 + $"AURA da ficha {ap.CorAura?.ToString() ?? "nula (deriva)"}");
		if (meu) return;

		_idDoOutro = quem;
		_fichaDoOutro = ap;

		if (Papel != "b" || _corpoAlheio == null || !GodotObject.IsInstanceValid(_corpoAlheio)) return;
		_fichasEmCorpoVivo++;
		AbrirJanelaDoAlheio(_estadoDoRetrato,
							$"o `PeerLook` #{_fichasEmCorpoVivo} caiu num corpo que ja estava na tela");
	}

	private void Anotar(string s)
	{
		_linhas.Add(s);
		GD.Print($"[dois-{Papel}] {s}");
	}

	public override void _Process(double delta)
	{
		if (!_dentro) return;
		_relogio += delta;

		// ============================ O B SAI DE CIMA DO A ============================
		// Os dois nascem no MESMO ponto de spawn, e na primeira rodada isso quase custou um falso
		// positivo: o cabelo preto que aparecia atras do dourado na foto era o cabelo do PROPRIO
		// observador, desenhado no mesmo pixel. Corpos empilhados nao se leem -- nem na foto, nem
		// na conta de posicao da aura.
		if (Papel == "b")
		{
			bool andando = _relogio < 2.0;
			if (andando != _andando)
			{
				_andando = andando;
				if (andando) Godot.Input.ActionPress("move_right");
				else Godot.Input.ActionRelease("move_right");
			}
		}

		if (Papel == "a") Agir();

		// ============================ A MEDICAO CORRE POR QUADRO, O JULGAMENTO POR JANELA ============================
		// Ver `RetratoDeCorpo`: metade do que se ve neste jogo PULSA, e o que se compara e a FAIXA
		// que cada valor percorre -- entao a colheita tem que ser continua e o veredito so pode sair
		// depois de a janela cobrir o maior ciclo (a cor alternada do contorno, 4,0 s).
		if (Papel == "a") ColherOMeuCorpo(delta);
		else JulgarOCorpoAlheio();

		if (_relogio < _proximoDump) return;
		_proximoDump = _relogio + 6;
		Olhar();
	}

	private bool _andando;

	// =====================================================================
	// O PAPEL A -- SO SE TRANSFORMA
	// =====================================================================
	private void Agir()
	{
		// ============================ O A CRAVA O ADMIN NO DISCO ANTES DO B CHEGAR ============================
		// NAO e enfeite de bancada: o servidor DESLIGA o admin-por-endereco assim que duas contas
		// chegam de 127.0.0.1 (`GameServer.Admin.cs:157`) -- e com razao, o endereco deixou de
		// identificar a maquina do dono. So que os dois clientes desta bancada moram na mesma
		// maquina por construcao, entao a rodada "o B entra primeiro e o A se transforma depois"
		// perdia o `admin_forma` em silencio: o pedido saia, ninguem respondia, e o log parecia
		// dizer que a transformacao nao SINCRONIZA quando na verdade ela nunca ACONTECEU.
		//
		// `admin_promover` grava a marca no arquivo da conta, e conta marcada nao depende de
		// endereco. Roda no primeiro segundo, enquanto o A ainda e o unico local.
		if (!_promoveu && _relogio >= 1 && Conta.Length > 0)
		{
			_promoveu = true;
			GameClient.Instance?.SendVerbo("admin_promover", Conta);
		}

		// ============================ O KI, QUE E O SEGUNDO DEFEITO ============================
		// Segurar C carrega Ki, e passar dos 100% acende o bit `Sobrecarregado` no snapshot -- que e
		// exatamente o que passou a mandar no contorno do corpo ALHEIO. Sem isto a bancada so provaria
		// metade da regra (contorno APAGADO sem Ki); com isto ela prova a outra (acende com Ki), que e
		// a que diferencia "consertado" de "desligado".
		// ============================ E DECOLAR, QUE E O CASO QUE NUNCA FOI OLHADO ============================
		// A nebulosa e os raios acabaram de entrar nas listas de deslocamento por altitude, e nenhuma
		// bancada olha nuvem com altura != 0 -- que e justamente o buraco por onde "a aura do UI ta
		// torta" passou: 160 px de vao entre o corpo desenhado no ar e a nuvem parada no chao nao se
		// le como "aura baixa", se le como um borrao solto. Precisa de `--vooteste` no servidor.
		if (!_voou && Voar > 0 && _relogio >= Voar)
		{
			_voou = true;
			GameClient.Instance?.SendHabilidade("voar");
			GD.Print("[dois-a] decolando (SendHabilidade voar)");
		}

		// ============================ E O C PODE VIR ANTES DA FORMA ============================
		// Isto era `if (_agiu && ...)`, ou seja o C so descia DEPOIS da transformacao -- e com essa
		// amarra o estado `base|ki>100%` era INALCANCAVEL por qualquer roteiro. Justo esse: a regra do
		// dono diz que *"na base nao importa a % do Ki, a unica coisa que deve ficar ativa e o node de
		// carga"*, e o ramo que a cobra (o `naBase` do `OContornoAlheio`) ficava sem nenhuma rodada que
		// o alcancasse. Um ramo que nenhum roteiro atinge e um ramo que nao esta sendo testado.
		//
		// SEGURAR O C NAO TRANSFORMA -- e por isso tirar a amarra e seguro. A escada so sobe no TOQUE
		// DUPLO (`LocalPlayer.LerTeclaC`: `IsActionJustPressed` duas vezes dentro de 1 s); um unico
		// press seguido de hold so manda `SendCarregar(true)`. E quem transforma nesta bancada e o
		// verbo `admin_forma`, que nao passa pelo teclado.
		//
		// A ORDEM VIRA PARAMETRO, entao: `--doiscarga` antes de `--doisatraso` mede a base
		// sobrecarregada; depois, mede o que ela media antes. As duas rodadas cabem no mesmo binario.
		// ====================================================================================
		if (!_carregando && Carga > 0 && _relogio >= Carga)
		{
			_carregando = true;
			Godot.Input.ActionPress("transformar");
			GD.Print("[dois-a] segurando C -- carregando Ki (o bit de sobrecarga)");
		}

		// ============================ E SOLTA O C, QUE E A VOLTA DA REGRA ============================
		// Sem soltar, a bancada mede o contorno acendendo e nunca o mede APAGANDO com a forma ainda no
		// corpo -- e "apagou" e exatamente a metade que o dono relatou faltando ("fica sempre com a
		// outline mesmo sem ativar a aura"). Um teste que so acende prova que existe interruptor, nao
		// que ele desliga.
		if (_carregando && !_soltou && Soltar > 0 && _relogio >= Soltar)
		{
			_soltou = true;
			Godot.Input.ActionRelease("transformar");
			GD.Print("[dois-a] SOLTEI o C -- o Ki volta pra baixo dos 100%");
		}

		// ============================ E A FICHA PASSANDO POR CIMA DA FORMA ============================
		// `admin_ir` cai no `MoveToZone`, e o `MoveToZone` chama `TrocarAparencias(pl)` SEM perguntar se
		// a zona mudou (`GameServer.cs`, o bloco "A APARENCIA E AS FERIDAS TAMBEM SAO POR ZONA"). Com o
		// alvo na MESMA zona, isso e um `PeerLook` meu reemitido pra quem ja esta olhando pra mim --
		// exatamente a corrida do jogo em que a ficha chega depois do corpo, so que na hora que eu
		// escolho em vez de quando o roteador quiser.
		//
		// E NAO E SO DO OUTRO LADO: o `TrocarAparencias` manda a minha ficha pra mim tambem (a guarda
		// `outro != novo` so protege o segundo envio), e o meu cliente ainda recebe um `ZoneChanged` e
		// remonta o proprio corpo. Um pedido, os dois corpos medidos.
		//
		// O PRECO E A FOTO: `admin_ir` me poe EM CIMA do outro, e corpos empilhados nao se leem na
		// imagem (ver o bloco "O B SAI DE CIMA DO A"). As fotos daqui pra frente sao lixo de proposito;
		// o que se mede depois deste ponto e numero.
		if (!_pediuFicha && Ficha > 0 && _relogio >= Ficha && _idDoOutro != 0)
		{
			_pediuFicha = true;
			GameClient.Instance?.SendVerbo("admin_ir", _idDoOutro.ToString());
			GD.Print($"[dois-a] `admin_ir {_idDoOutro}` -- reemitindo a MINHA ficha num corpo ja transformado");

			// A MINHA JANELA TAMBEM RECOMECA. Sem isto o retrato do dono atravessaria a remontagem do
			// proprio corpo (o `ZoneChanged` reconstroi o `LocalPlayer`) e a regua que o B usa seria uma
			// media entre antes e depois -- que e o erro de medir a TRANSICAO, ja pago uma vez aqui.
			_janelaAberta = _relogio + AssentarKi;
			_retrato = new RetratoDeCorpo { Forma = _minhaForma, Estado = _estadoDoRetrato };
		}

		if (_agiu || _relogio < Atraso) return;
		_agiu = true;
		if (GameClient.Instance is not { } cli) return;

		if (Forma.Length > 0)
		{
			// O MESMO CAMINHO DO BOTAO DA ABA DE ADMIN (`MenuJogo.PainelDeFormas`): "alvo|forma", e o
			// alvo 0 cai em quem clicou. Sem isto o Ultra Instinto exigiria a escada divina inteira.
			cli.SendVerbo("admin_forma", $"0|{Forma}");
			GD.Print($"[dois-a] FORCEI a forma '{Forma}' em mim (admin_forma)");
		}
		else
		{
			cli.SendTransformar(true);
			GD.Print("[dois-a] apertei C (SendTransformar) -- subindo a escada");
		}
	}

	// =====================================================================
	// O PAPEL B -- OLHA OS CORPOS ALHEIOS
	// =====================================================================
	private void Olhar()
	{
		Node? mundoN = GetTree().Root.FindChild("World", true, false);
		if (mundoN is not Node2D mundo) { GD.Print($"[dois-{Papel}] ainda nao ha World"); return; }

		var corpos = new List<Node2D>();
		// ============================ O CORPO LOCAL ENTRA NA MESMA VARREDURA ============================
		// Sem ele nao ha REGUA: "o contorno do outro esta certo" so quer dizer alguma coisa contra o que
		// o dono da tela ve no proprio corpo, na mesma forma. Os dois tem os mesmos nomes de filho
		// (`Visual`, `Aura`, `Carga`, `Raios`, `Nebulosa`) porque nascem do mesmo bloco no `World` --
		// entao uma rotina so le os dois, e e por isso que ela nao pergunta de quem e o corpo.
		if (GetTree().Root.FindChild("LocalPlayer", true, false) is Node2D meu) corpos.Add(meu);
		int locais = corpos.Count;
		Rastelar(mundo, corpos);

		var sb = new StringBuilder();
		sb.AppendLine($"===== [dois-{Papel}] OLHADA {_fotos} (t={_relogio:0.0}s, relogio de parede "
					  + $"{DateTime.Now:HH:mm:ss}) -- {corpos.Count - locais} corpo(s) remoto(s) =====");

		foreach (Node2D r in corpos)
		{
			bool ehMeu = r is LocalPlayer;
			sb.AppendLine($"  -- {(ehMeu ? "MEU CORPO" : "REMOTO")} {r.Name} em {r.GlobalPosition}"
						  + (!ehMeu && corpos[0] is LocalPlayer eu
							 ? $"  (distancia {eu.GlobalPosition.DistanceTo(r.GlobalPosition):0} px)" : ""));
			if (r is RemotePlayer rp) sb.AppendLine($"     altura {rp.AlturaDeTeste:0.0} | vida {rp.VidaDeTeste} | olhar {rp.OlharDeTeste}");

			if (r.GetNodeOrNull<CharacterVisual>("Visual") is { } v)
			{
				(Color cc, float cf, int ccam) = v.ContornoNoMaterialDeTeste();
				sb.AppendLine($"     CORPO   anim '{v.AnimacaoDeTeste}' | corpo base visivel {v.CorpoBaseVisivelDeTeste}"
							  + $" | corpo da FORMA {v.CorpoDaFormaVisivelDeTeste} '{v.FolhaDoCorpoDaFormaDeTeste}'");
				sb.AppendLine($"     CABELO  '{v.CabeloDeTeste}' visivel {v.TemCabeloVisivelDeTeste}"
							  + $" | tinta {Tinta(v.TintaDoCabeloDeTeste?.Tinta)} modo {v.TintaDoCabeloDeTeste?.Modo}");
				sb.AppendLine($"     OLHO    tem {v.TemOlhosDeTeste} | tinta {Tinta(v.TintaDoOlhoDeTeste)}");
				sb.AppendLine($"     RABO    visivel {v.RaboVisivelDeTeste} | tinta {Tinta(v.TintaDoRaboDeTeste)}");
				sb.AppendLine($"     COLADAS {v.ColadasDeTeste} {string.Join(", ", v.PosesDasColadasDeTeste)}");
				sb.AppendLine($"     CONTORNO cor {cc.ToHtml(false)} forca {cf:0.000} em {ccam} camada(s)"
							  + $"  [pedido {v.AuraDaFormaDeTeste:0.000}]");
			}
			else sb.AppendLine("     (sem CharacterVisual)");

			if (r.GetNodeOrNull<Aura>("Aura") is { } a)
			{
				SpriteDeAura d = a.DesenhoDeTeste;
				sb.AppendLine($"     AURA    acesa {a.AcesaDeTeste} | pessoal {a.CorPessoal.ToHtml(false)}"
							  + $" | luz {a.EnergiaDeTeste:0.00} {a.CorDaLuzDeTeste.ToHtml(false)}"
							  + $" | folha '{System.IO.Path.GetFileName(d.FolhaDeTeste)}' largura {d.LarguraDeTeste:0}");
				sb.AppendLine($"     AURA-POS base(local) {d.BaseDeTeste:0.0} (pes = {SpriteDeAura.LinhaDosPes})"
							  + $" | quad global {d.GlobalPosition} vs corpo {r.GlobalPosition}"
							  + $" | delta {(d.GlobalPosition - r.GlobalPosition)}");
			}

			// ============================ OS DOIS BITS DA CARGA ENTRAM NO DESPEJO ============================
			// A linha so dizia FOLHA e POSICAO, e com isso ela nao respondia a metade da regra da base:
			// *"na base nao importa a % do Ki, a unica coisa que deve ficar ativa e o node de carga"*. O
			// contorno apagado prova a primeira metade ("nao acende"); sem o `desenhando`, "nao acende"
			// se le igual a "a carga inteira morreu", que e o defeito irmao e o mais provavel aqui.
			//
			// SAO OS DOIS BITS DO MESMO PAR (`CargaVisual.Definir(carregando, excesso)`), e o `excesso` e
			// a SEGUNDA leitura do mesmo `EntityState.Sobrecarregado` que manda no contorno -- ou seja ele
			// e a testemunha independente de que o bit CHEGOU neste corpo. Sem ele, "contorno em 0" no
			// corpo alheio nao distingue "a regra da base funcionou" de "o bit nunca veio".
			// ==========================================================================================
			if (r.GetNodeOrNull<CargaVisual>("Carga") is { } cg)
				sb.AppendLine($"     CARGA   folha '{System.IO.Path.GetFileName(cg.DesenhoDeTeste.FolhaDeTeste)}'"
							  + $" | quad global {cg.DesenhoDeTeste.GlobalPosition}"
							  + $" | carregando {cg.CarregandoDeTeste} excesso {cg.ExcessoDeTeste}"
							  + $" | desenhando {cg.DesenhoDeTeste.Visible}");

			if (r.GetNodeOrNull<RaiosDaForma>("Raios") is { } ra)
				sb.AppendLine($"     RAIOS   emitindo {ra.EmitindoDeTeste} | intensidade {ra.IntensidadeDeTeste}"
							  + $" | cor {ra.CorDeTeste.ToHtml(false)} | node global {ra.GlobalPosition}");

			if (r.GetNodeOrNull<NebulosaDaForma>("Nebulosa") is { } ne)
				sb.AppendLine($"     NEBULOSA forca {ne.ForcaDeTeste:0.000} lado {ne.LadoDeTeste:0}"
							  + $" | centro do quad {ne.PosicaoDoQuadDeTeste} vs corpo {r.GlobalPosition}"
							  + $" | delta {(ne.PosicaoDoQuadDeTeste - r.GlobalPosition)}");
		}

		GD.Print(sb.ToString());
		Fotografar($"user://dois-{Rotulo}-{_fotos:00}.png");
		_fotos++;

		// SAIR SOZINHA, e nao esperar o `Stop-Process` do roteiro: matar o processo a forca deixa a
		// saida padrao sem descarregar, e a bancada "termina" com metade das olhadas no arquivo --
		// o que se le exatamente igual a uma bancada que TRAVOU. Uma delas ja me custou uma rodada.
		if (Fim > 0 && _fotos >= Fim)
		{
			GD.Print($"[dois-{Papel}] FIM ({_fotos} olhadas) -- saindo");
			Fechar();
		}
	}

	/// <summary>Quantas olhadas antes de o processo sair sozinho. 0 = so sai quando matarem.</summary>
	public int Fim = 14;

	private void Rastelar(Node n, List<Node2D> achados)
	{
		foreach (Node f in n.GetChildren())
		{
			if (f is RemotePlayer r2) achados.Add(r2);
			Rastelar(f, achados);
		}
	}

	private static string Tinta(Vector3? t) =>
		t is { } v ? $"({v.X:0.00},{v.Y:0.00},{v.Z:0.00})" : "nenhuma";

	// =====================================================================
	// O PLACAR
	// =====================================================================
	private int _oks;
	private readonly List<string> _falhas = [];

	private void Conferir(bool ok, string oque)
	{
		if (ok) _oks++; else _falhas.Add(oque);
		GD.Print($"[dois-{Papel}] " + (ok ? "  ok   " : "  FALHA") + "  " + oque);
	}

	// =====================================================================
	// FAMILIA 1 e 2 -- O RETRATO, DE UM LADO E DO OUTRO
	// =====================================================================
	/// <summary>
	/// ONDE O CORPO DO DONO POUSA PRA SER LIDO PELO JUIZ. Os dois processos desta bancada moram na
	/// mesma maquina, entao o `user://` e o mesmo -- e nao ha outro caminho: a regua de "o corpo
	/// alheio esta certo" e o que o DONO daquele corpo ve, e isso esta do outro lado do soquete.
	/// </summary>
	private const string ArquivoDoDono = "user://replica-dono.txt";

	/// <summary>
	/// A JANELA DE MEDICAO, em segundos. Tem que ser maior que o maior ciclo visual do corpo -- a cor
	/// alternada do contorno (`Catalogo.SegundosDoCicloDoContorno` = 4,0 s). Com janela menor, a
	/// faixa colhida dependeria de ONDE o pulso estava quando a janela abriu.
	/// </summary>
	private const double Janela = 5.5;

	/// <summary>Piso de amostras. Uma janela com tres quadros nao mede faixa nenhuma.</summary>
	private const int MinimoDeAmostras = 60;

	/// <summary>
	/// ============================ O CORPO PRECISA ASSENTAR ANTES DE SER MEDIDO ============================
	/// Achado na primeira rodada verde-com-ruido: a janela abria no MESMO instante em que o carimbo
	/// mudava, e o carimbo muda no PACOTE -- enquanto o corpo leva quadros (e uma cinematica) pra
	/// vestir. O resultado foi um punhado de diferencas verdadeiras e inuteis: a folha do cabelo com
	/// DUAS entradas do lado do dono (`Hair_Goku` e depois `Hair_GokuSSj`), a tinta do olho indo de
	/// 0 a 0,251 no meio da janela, e um `AudioStreamPlayer2D` que era o SOM DA CINEMATICA pendurado
	/// no corpo (`Transformacao.cs:1833`) -- coisas que o corpo alheio nunca teve porque a cena dele
	/// nao roda (quem entra depois recebe `DegrauDeCena.Nenhuma`).
	///
	/// Medir a TRANSICAO em vez do ESTADO e o erro classico deste tipo de bancada.
	///
	/// ============================ E A ESTREIA E LONGA -- MEDIDO, NAO CHUTADO ============================
	/// Seis segundos resolveram o cabelo e o olho e NAO resolveram o som: a cinematica de estreia do
	/// SSJ1 dura ~29 s (medido no log do papel A: o retrato do dono teve 660 campos ate t=33 s e 629
	/// dai em diante -- os 31 campos a mais eram o `AudioStreamPlayer2D` da cena). O corpo alheio nao
	/// tem cena nenhuma nesse periodo: quem entra depois recebe `DegrauDeCena.Nenhuma`, de proposito.
	///
	/// Entao a espera e do TIPO de mudanca, e nao do papel: trocar de FORMA roda cinematica, trocar de
	/// KI nao. Fazer disso uma diferenca entre o papel A e o papel B seria mais rapido e seria errado
	/// -- a espera e uma propriedade do ESTADO, e um dia o corpo alheio TAMBEM vai rodar a cena.
	/// ==================================================================================================
	/// </summary>
	private const double AssentarKi = 6.0;

	/// <inheritdoc cref="AssentarKi"/>
	private const double AssentarForma = 36.0;

	/// <summary>Quanto esperar antes de abrir a janela: a FORMA roda cena, o KI nao. Ver <see cref="AssentarKi"/>.</summary>
	private double Espera(string novo) =>
		Carimbo(novo.Split('|')[0], false) == Carimbo(_estadoDoRetrato.Split('|')[0], false)
			? AssentarKi : AssentarForma;

	private string _minhaForma = "base", _formaDoOutro = "base";
	private RetratoDeCorpo _retrato = new();
	private string _estadoDoRetrato = "";
	private double _janelaAberta;
	private readonly HashSet<string> _jaJulguei = [];
	private int _comparacoes;

	/// <summary>
	/// O CARIMBO DO ESTADO. Duas coisas, e as duas mudam o que se ve: a FORMA (cabelo, olho, corpo,
	/// aura, coladas) e o KI acima dos 100% (o contorno). Retratos de estados diferentes nao se
	/// comparam -- e o carimbo e o que impede a bancada de fazer isso e chamar de defeito.
	/// </summary>
	private static string Carimbo(string forma, bool excesso) =>
		forma + (excesso ? "|ki>100%" : "|ki<=100%");

	// ============================ OS DOIS CORPOS FICAM GUARDADOS ============================
	// `FindChild(recursivo)` e `Rastelar` desciam a arvore INTEIRA -- que inclui a cena da zona, com
	// dezenas de milhares de nodes de TileMap. Rodando por quadro, isso derrubou o B pra menos de um
	// quadro por segundo na primeira rodada, e o efeito se le exatamente como bancada travada: tres
	// olhadas no arquivo e depois silencio.
	//
	// A busca fica, mas UMA VEZ POR SEGUNDO e so enquanto o corpo guardado nao serve. `IsInstanceValid`
	// pega o corpo que saiu do campo de visao (o node e liberado) sem custo nenhum.
	// ========================================================================================
	private Node2D? _meuCorpo;
	private RemotePlayer? _corpoAlheio;
	private double _proximaBusca;

	private Node2D? MeuCorpo()
	{
		if (_meuCorpo != null && GodotObject.IsInstanceValid(_meuCorpo)) return _meuCorpo;
		if (_relogio < _proximaBusca) return null;
		_proximaBusca = _relogio + 1;
		_meuCorpo = GetTree().Root.FindChild("LocalPlayer", true, false) as Node2D;
		return _meuCorpo;
	}

	/// <summary>O papel A: colhe o proprio corpo e despeja a janela fechada no disco.</summary>
	private void ColherOMeuCorpo(double delta)
	{
		if (MeuCorpo() is not Node2D meu) return;

		bool excesso = meu.GetNodeOrNull<CargaVisual>("Carga")?.ExcessoDeTeste ?? false;
		string estado = Carimbo(_minhaForma, excesso);

		// ESTADO NOVO REABRE A JANELA. Sem isto, a faixa misturaria "sem contorno" com "com
		// contorno" e daria um intervalo que descreve o corpo em nenhum dos dois momentos --
		// e o juiz do outro lado compararia contra essa mistura e acharia tudo certo.
		if (estado != _estadoDoRetrato)
		{
			_janelaAberta = _relogio + Espera(estado);
			_estadoDoRetrato = estado;
			_retrato = new RetratoDeCorpo { Forma = _minhaForma, Estado = estado };
		}

		if (_relogio < _janelaAberta) return;   // assentando -- ver `Assentar`
		_retrato.Amostrar(meu);

		if (_relogio - _janelaAberta < Janela || _retrato.Amostras < MinimoDeAmostras) return;

		// JANELA CHEIA: escreve e ABRE OUTRA. A que fica no disco e sempre uma janela COMPLETA do
		// estado corrente -- nunca um pedaco, nunca uma janela que atravessou uma mudanca.
		using (Godot.FileAccess? f = Godot.FileAccess.Open(ArquivoDoDono, Godot.FileAccess.ModeFlags.Write))
			f?.StoreString(_retrato.Serializar());

		GD.Print($"[dois-a] retrato despejado: {_retrato.Campos} campos, {_retrato.Amostras} amostras, "
			   + $"estado '{estado}'");
		_retrato = new RetratoDeCorpo { Forma = _minhaForma, Estado = estado };
		_janelaAberta = _relogio;
	}

	/// <summary>
	/// NODES QUE SO EXISTEM NO CORPO DO DONO -- e os dois motivos estao aqui, porque cada nome nesta
	/// lista e um pedaco do corpo que a bancada deixa de comparar.
	///
	///   * `Camera2D`  -- e o que EU enxergo, nao algo que os outros veem (`World.cs`, o bloco do
	///                    `AoEntrar`). Um corpo alheio com camera seria o defeito.
	///   * `SombraDeVoo` -- nasce SOB DEMANDA no corpo alheio (`RemotePlayer.CriarSombra`: "corpo
	///                    remoto e o que mais se instancia no jogo e a maioria nunca sai do chao").
	///                    Fica anotado como buraco: no chao ela nao e comparada.
	/// </summary>
	private static readonly string[] SoNoDono = ["Camera2D", "SombraDeVoo"];

	/// <summary>
	/// COMECA UMA JANELA DE MEDICAO NOVA no corpo alheio, e zera TUDO o que se acumula por quadro.
	///
	/// A TRAVESSIA E CONTADA, e nao so o estado: o corpo passa por `ki&lt;=100%` DUAS vezes -- antes de
	/// o A segurar o C e depois de ele soltar --, e sao dois momentos diferentes da mesma regra.
	/// Guardando so o nome do estado, a segunda passagem (o contorno APAGANDO com a forma ainda no
	/// corpo) seria descartada como repetida, e ela e a metade que faltava.
	///
	/// EXISTE COMO METODO porque ha DOIS motivos pra recomecar, e o segundo nao muda o estado: a ficha
	/// atrasada (ver <see cref="AoReceberFicha"/>). Deixar as duas listas de zeragem em dois lugares e
	/// como um acumulador novo nasce esquecido em um deles -- e um `&amp;&amp;` que nao foi zerado carrega
	/// pra dentro da janela nova o veredito da anterior.
	/// </summary>
	private void AbrirJanelaDoAlheio(string estado, string porque)
	{
		_travessia++;
		_janelaAberta = _relogio + Espera(estado);   // le o `_estadoDoRetrato` VELHO -- tem que vir antes
		_estadoDoRetrato = estado;
		_retrato = new RetratoDeCorpo { Forma = _formaDoOutro, Estado = estado };
		_contornoMin = float.MaxValue;
		_contornoMax = float.MinValue;
		_cargaDesenhouSempre = true;
		_raboMedidoSempre = true;
		_raboDaFormaSempre = true;
		_olhoDaFormaSempre = true;
		_chamaPessoalSempre = true;
		if (porque.Length > 0) Anotar($"janela REABERTA em '{estado}': {porque}");
	}

	/// <summary>O papel B: colhe o corpo alheio, e quando a janela fecha, JULGA.</summary>
	private void JulgarOCorpoAlheio()
	{
		if (CorpoAlheio() is not RemotePlayer outro) return;

		bool excesso = outro.GetNodeOrNull<CargaVisual>("Carga")?.ExcessoDeTeste ?? false;
		string estado = Carimbo(_formaDoOutro, excesso);

		if (estado != _estadoDoRetrato) AbrirJanelaDoAlheio(estado, "");

		if (_relogio < _janelaAberta) return;   // assentando -- ver `Assentar`
		_retrato.Amostrar(outro);

		// O CONTORNO COLHIDO A PARTE, e nao lido do retrato: o retrato compara o corpo alheio com o
		// corpo do dono ("os dois veem a mesma coisa"), e isso continuaria verde se os DOIS
		// estivessem errados do mesmo jeito -- que e exatamente o estado anterior deste projeto
		// (contorno aceso pela forma nas duas telas seria "igual"). A regra absoluta precisa de uma
		// medida absoluta.
		if (outro.GetNodeOrNull<CharacterVisual>("Visual") is { } vis)
		{
			(_, float forca, _) = vis.ContornoNoMaterialDeTeste();
			_contornoMin = Math.Min(_contornoMin, forca);
			_contornoMax = Math.Max(_contornoMax, forca);

			// ============================ O RABO E O OLHO, CONTRA O CORE E NAO CONTRA O OUTRO ============================
			// Pelo mesmo motivo do contorno acima: `OsDoisCorpos` pergunta "os dois veem a mesma coisa",
			// e isso continua VERDE se as duas telas estiverem erradas do mesmo jeito -- e e assim que
			// este defeito se apresenta. Se o `VestirCabeloDaForma` parasse de tingir, o rabo e o olho
			// sairiam crus nos DOIS corpos e a comparacao campo a campo nao teria uma palavra a dizer.
			// Regra absoluta precisa de medida absoluta.
			//
			// COLHIDO POR QUADRO E COM `&&`: a pergunta e "carregou a cor A JANELA INTEIRA", e nao "estava
			// certo no instante em que o juiz olhou". Uma ficha que despe a forma por meio segundo e
			// depois e reposta e exatamente o defeito que uma leitura pontual perde.
			// ==========================================================================================================
			(Vector3 eRabo, Vector3 eOlho, bool cobraORabo) = TintaEsperadaDoAlheio();
			_ultimoRabo = vis.TintaDoRaboDeTeste;
			_ultimoOlho = vis.TintaDoOlhoDeTeste;
			if (cobraORabo)
			{
				// `TintaDoRaboDeTeste` e nula quando NAO HA camada de rabo -- e por isso ela serve de
				// prova de existencia. `RaboVisivelDeTeste` vem junto porque uma camada escondida ainda
				// responde a tinta, e tinta certa num rabo invisivel nao e o rabo dourado do dono.
				_raboMedidoSempre &= _ultimoRabo is not null && vis.RaboVisivelDeTeste;
				_raboDaFormaSempre &= _ultimoRabo is { } tr && tr.IsEqualApprox(eRabo);
			}
			_olhoDaFormaSempre &= _ultimoOlho is { } to && to.IsEqualApprox(eOlho);
		}

		// A CHAMA DA CARGA, NO MESMO PASSO E PELA MESMA JANELA -- ver `OContornoAlheio`. Um quadro que
		// nao desenhou derruba o `&&`: aqui a pergunta e "ficou ativa o tempo TODO", e nao "estava
		// ativa no instante em que o juiz olhou".
		if (outro.GetNodeOrNull<CargaVisual>("Carga") is { } cga)
			_cargaDesenhouSempre &= cga.DesenhoDeTeste.Visible;

		// A COR PESSOAL DO CORPO ALHEIO, no mesmo passo e pela mesma janela. Ver
		// <see cref="AChamaPessoalAlheia"/>: esta e a unica bancada do projeto com DOIS corpos de
		// personagens diferentes na mesma tela, e portanto a unica que pode pegar uma cor que chega
		// certa num corpo e errada no outro.
		if (outro.GetNodeOrNull<Aura>("Aura") is { } aoutro)
		{
			_ultimaChamaPessoal = aoutro.CorPessoal;
			if (_fichaDoOutro is { } fo)
				_chamaPessoalSempre &= aoutro.CorPessoal.IsEqualApprox(Aura.CorPessoalDe(fo));
		}

		if (_relogio - _janelaAberta < Janela || _retrato.Amostras < MinimoDeAmostras) return;
		if (!_jaJulguei.Add($"{_travessia}|{estado}")) { _janelaAberta = _relogio; return; }

		OContornoAlheio(estado, excesso);
		ORaboEOOlhoAlheios(estado);
		AChamaPessoalAlheia(estado);
		OsDoisCorpos(estado);
		_janelaAberta = _relogio;
	}

	private float _contornoMin = float.MaxValue, _contornoMax = float.MinValue;
	private int _travessia;

	/// <summary>O rabo teve camada VISIVEL em todo quadro da janela -- o controle de "mediu alguma coisa".</summary>
	private bool _raboMedidoSempre = true;

	/// <summary>A tinta do rabo / do olho bateu com a do Core em todo quadro da janela.</summary>
	private bool _raboDaFormaSempre = true, _olhoDaFormaSempre = true;

	/// <summary>O ultimo valor lido de cada uma -- so pra a mensagem dizer o que foi medido.</summary>
	private Vector3? _ultimoRabo, _ultimoOlho;

	/// <summary>
	/// ============================ O QUE O RABO E O OLHO DO CORPO ALHEIO DEVEM TER ============================
	/// As duas respostas saem do CORE (<see cref="Jandirus.Core.Forms.Catalogo.CorDoRabo"/> e
	/// <see cref="Jandirus.Core.Forms.Catalogo.CorDoOlho"/>) e da FICHA que chegou pela rede -- nenhum
	/// hexa escrito aqui. Se o dono trocar o dourado do rabo amanha, esta bancada acompanha em vez de
	/// virar o unico lugar do projeto que ainda acha que SSJ1 e `dada26`.
	///
	/// ============================ AS TRES DERIVACOES, E POR QUE CADA UMA ============================
	///   * RABO SEM FORMA -> a ARTE CRUA (zero em soma), e nao a cor da ficha: a ficha nao tem cor de
	///     rabo e o `CharacterVisual.Vestir` nunca o tinge (ver `BaseDaFicha`). Vale pra base e pras
	///     formas que nao mexem no rabo (Mistico, Beast, Oozaru).
	///   * OLHO SEM FORMA -> a cor da FICHA, que o `PeerLook` trouxe. E o que torna a checagem da base
	///     uma medida e nao uma formalidade: sem a ficha o esperado seria zero, que e o mesmo valor de
	///     "ninguem pintou nada".
	///   * FORMA CUJA FOLHA JA TRAZ O RABO (o SSJ4 e o corpo musculoso) -> o rabo NAO E COBRADO. O
	///     `CharacterVisual.PintarRabo` sai antes de escrever nesses casos, entao o material guarda o
	///     que estivesse armado de antes -- um valor que nao aparece na tela e sobre o qual nao ha
	///     regra. Cobrar zero ali seria inventar uma.
	///
	/// UMA COISA FICA DE FORA E ELA ESTA ANOTADA: o `_semRedeas` (a furia dirigindo o corpo) troca o
	/// olho das duas linhas Legendary pelo branco sem iris. Esta bancada nao possui ninguem, entao
	/// `CorDoOlho(d)` basta -- o dia em que um roteiro daqui enlouquecer o A, o esperado passa a ter
	/// que perguntar a posse ao corpo, e nao ao catalogo.
	/// ======================================================================================================
	/// </summary>
	private (Vector3 Rabo, Vector3 Olho, bool CobraORabo) TintaEsperadaDoAlheio()
	{
		static Vector3 Do(string hexa) { var c = new Color(hexa); return new Vector3(c.R, c.G, c.B); }

		Jandirus.Core.Forms.FormaDef? def = Jandirus.Core.Forms.Catalogo.Def(_formaDoOutro);

		Vector3 rabo = Jandirus.Core.Forms.Catalogo.CorDoRabo(def) is { } cr ? Do(cr) : Vector3.Zero;
		bool cobra = def == null
			|| !Jandirus.Core.Forms.Catalogo.FolhaTrazORabo(def.Corpo);

		Vector3 olho = Jandirus.Core.Forms.Catalogo.CorDoOlho(def) is { } co ? Do(co)
			: _fichaDoOutro?.CorOlho is { } fo
				? new Vector3(fo.R / 255f, fo.G / 255f, fo.B / 255f)
				: Vector3.Zero;

		return (rabo, olho, cobra);
	}

	/// <summary>
	/// ============================ FAMILIA 4 -- O RABO E O OLHO DO CORPO ALHEIO ============================
	/// O relato do dono foi *"quebrou a mudanca de cor do rabo e dos olhos"*, e ate aqui nenhuma
	/// bancada de dois processos tinha uma palavra a dizer sobre isso: o `--diagforma` chama
	/// `VestirCabeloDaForma` DIRETO num boneco e le o uniform em seguida -- o que prova a FUNCAO --, e
	/// o `OsDoisCorpos` compara as duas telas entre si -- o que nao distingue "certo" de "errado igual".
	///
	/// Isto mede o CAMINHO: o pacote de forma chega pela rede, o `World.AoMudarForma` veste, e a cor
	/// medida no material do corpo remoto e conferida contra a tabela do Core.
	///
	/// E ELE COBRE OS DOIS SENTIDOS, como o contorno: na BASE o rabo tem que estar CRU e o olho tem que
	/// ter a cor da ficha. Um teste que so olhasse a forma daria o mesmo verde pra um
	/// `VestirCabeloDaForma(null)` que nao desfizesse nada.
	/// ======================================================================================================
	/// </summary>
	private void ORaboEOOlhoAlheios(string estado)
	{
		bool naBase = _formaDoOutro == Jandirus.Core.Forms.Catalogo.IdBase;
		(Vector3 eRabo, Vector3 eOlho, bool cobraORabo) = TintaEsperadaDoAlheio();

		if (cobraORabo)
		{
			// O CONTROLE, e ele vem primeiro pelo mesmo motivo do `_cargaDesenhouSempre`: um corpo SEM
			// camada de rabo passaria a linha de baixo sem ter olhado nada (a tinta nula nunca e
			// comparada), e "nao ha rabo na tela" e um defeito maior que "o rabo esta da cor errada".
			Conferir(_raboMedidoSempre,
					 $"[{estado}] o corpo ALHEIO tem RABO desenhado em todo quadro da janela -- e o que "
				   + "impede a linha de baixo de ficar verde sem ter medido cor nenhuma");
			Conferir(_raboDaFormaSempre,
					 $"[{estado}] o RABO do corpo ALHEIO carrega a tinta que o CORE manda pra "
				   + $"`{_formaDoOutro}` (esperado {eRabo}{(naBase ? ", a arte crua" : "")}, "
				   + $"ultimo medido {Tinta(_ultimoRabo)}), em TODO quadro da janela");
		}

		// SEM FICHA A LINHA DO OLHO NA BASE NAO VALE NADA -- ver `TintaEsperadaDoAlheio`. Esta e a
		// afirmacao que impede a bancada de se dar verde comparando zero com zero.
		if (naBase)
		{
			Conferir(_fichaDoOutro is not null,
					 $"[{estado}] a FICHA do corpo alheio chegou (`PeerLook`) -- sem ela a cor de olho "
				   + "esperada na base seria zero, que e o mesmo valor de 'ninguem pintou'");

			// ============================ E O QUE ESTA MEDIDA **NAO** PROVA ============================
			// Uma ficha sem cor de olho (`CorOlho` nula -- e e o caso das contas criadas pelo modo
			// automatico) faz o esperado ser ZERO, que e exatamente o valor de "sem tinta". A linha do
			// olho continua valendo no que importa aqui -- ela reprova tinta de FORMA deixada pra tras
			// no corpo alheio --, mas ela nao distingue "devolveu a cor da ficha" de "nao devolveu
			// nada". Quem quiser a outra metade precisa de uma conta com olho colorido.
			//
			// ANOTADO EM VEZ DE REPROVADO de proposito: nao ha defeito nenhum numa ficha sem cor de
			// olho, e bancada que fica vermelha por escolha legitima do jogador ensina a ignorar
			// vermelho. Mas medida que se cala sobre o proprio alcance vira, no relatorio de alguem,
			// uma prova que ela nunca deu.
			// ==========================================================================================
			if (_fichaDoOutro?.CorOlho is null)
				Anotar("AVISO: a ficha do corpo alheio nao tem cor de olho -- o esperado na base e ZERO, "
					 + "o mesmo valor de 'sem tinta'. A linha do olho abaixo reprova tinta de FORMA "
					 + "esquecida no corpo, e nao prova que a cor da ficha foi devolvida.");
		}

		Conferir(_olhoDaFormaSempre,
				 $"[{estado}] o OLHO do corpo ALHEIO carrega a tinta {(naBase ? "da FICHA" : "da FORMA")} "
			   + $"(esperado {eOlho}, ultimo medido {Tinta(_ultimoOlho)}), em TODO quadro da janela");
	}

	/// <summary>
	/// ============================ A COR PESSOAL CHEGA NO CORPO **DO OUTRO** ============================
	/// A chama da base (e a do Mistico) deixou de ser uma constante compartilhada: cada personagem
	/// sorteia a propria no nascimento (`Appearance.CorAura`, o `rand(0,255)` do
	/// `CharacterCreation.dm:25-27`) e ela viaja no `PeerLook`.
	///
	/// E ESTA E A UNICA BANCADA QUE PODE COBRAR ISSO. O `--diagforma` mede funcoes puras e UM corpo:
	/// com um sujeito so, "a cor pessoal chegou" e indistinguivel de "todo mundo usa a mesma cor" --
	/// que e exatamente o estado anterior do port. Aqui ha dois personagens de contas diferentes, cada
	/// um com o proprio sorteio, e o que se afirma e que o corpo ALHEIO carrega a cor da ficha DELE.
	///
	/// CONTRA A FICHA RECEBIDA, e nao contra um hexa escrito: o hexa seria a cor de um personagem
	/// especifico e a bancada nasceria presa a uma conta. A ficha e a mesma que o servidor mandou, e
	/// comparar as duas e perguntar "o que chegou pela rede virou estado neste corpo".
	///
	/// POR QUADRO E COM `&amp;&amp;`, como o rabo e o olho: a pergunta e sobre a JANELA. Um `Vestir` que
	/// remontasse o corpo e reescrevesse a cor pelo fallback por meio segundo e o defeito que uma
	/// leitura pontual perde.
	///
	/// O QUE ELA **NAO** PROVA: que as duas cores sao DIFERENTES entre si. Metade dos sorteios da
	/// branco puro (ver `CorDeAura`), entao dois personagens quaisquer batem em ~24% das vezes e
	/// exigir diferenca faria esta linha piscar vermelha sozinha uma vez a cada quatro rodadas. O que
	/// se mede e a correspondencia corpo a corpo, que e a pergunta certa.
	/// ==============================================================================================
	/// </summary>
	private void AChamaPessoalAlheia(string estado)
	{
		if (_fichaDoOutro is not { } fo)
		{
			// Sem ficha nao ha esperado, e comparar com o fallback seria comparar zero com zero. A
			// falta da ficha ja e reprovada no ramo da base do `ORaboEOOlhoAlheios`.
			Anotar($"[{estado}] a cor pessoal do corpo alheio nao pode ser julgada: a ficha dele nao chegou");
			return;
		}

		Color esperada = Aura.CorPessoalDe(fo);
		Conferir(_chamaPessoalSempre,
				 $"[{estado}] o corpo ALHEIO carrega a COR DE AURA da ficha dele "
			   + $"({esperada.ToHtml(false)}, ultima medida {_ultimaChamaPessoal?.ToHtml(false) ?? "-"}), "
			   + "em TODO quadro da janela");

		// E ELA NAO E O FALLBACK. Sem esta linha, tudo acima ficaria verde num jogo em que a cor
		// sorteada nunca saisse do save: as duas pontas seriam o `Aura.CorDoKiCru` e concordariam.
		Conferir(fo.CorAura is not null && !esperada.IsEqualApprox(Aura.CorDoKiCru),
				 $"-- e ela e uma cor SORTEADA de verdade ({fo.CorAura?.ToString() ?? "nula"}), e nao o "
			   + $"#{Aura.CorDoKiCru.ToHtml(false)} de quem nao tem ficha");
	}

	/// <summary>Ver <see cref="AChamaPessoalAlheia"/>. Como o rabo e o olho: um `&amp;&amp;` por quadro.</summary>
	private bool _chamaPessoalSempre = true;

	/// <summary>A ultima cor pessoal lida no corpo alheio. So pra o log reprovar dizendo o valor.</summary>
	private Color? _ultimaChamaPessoal;

	/// <summary>
	/// A CHAMA DA CARGA DESENHOU EM TODO QUADRO da janela corrente. E o CONTROLE da regra da base --
	/// ver o ramo `naBase` de <see cref="OContornoAlheio"/>: sem ele, "o contorno ficou em zero"
	/// passaria verde com o corpo alheio inteiro apagado, que e o defeito irmao e o mais barato de
	/// cometer. Um `&amp;&amp;` por quadro, e nao uma leitura no fim, porque a pergunta e sobre a JANELA.
	/// </summary>
	private bool _cargaDesenhouSempre = true;

	/// <summary>
	/// ============================ FAMILIA 2 -- O CONTORNO DO CORPO ALHEIO, NOS DOIS SENTIDOS ============================
	/// A regra, escrita pelo dono e implementada em `World.AplicarContorno`: **a FORMA e dona da COR,
	/// o KI e dono do ACENDER**, nos dois corpos. O defeito que ela consertou era o contorno alheio
	/// aceso pela forma, o tempo todo, com a conta velha `0,35 + Intensidade*0,13`.
	///
	/// SAO OS DOIS SENTIDOS OU NENHUM: "consertado" e "desligado" dao o mesmo verde num teste que so
	/// olha o lado apagado. Por isso a bancada exige as duas travessias -- e a segunda so acontece
	/// porque o A segura o C de verdade, pela tecla, com o servidor decidindo.
	///
	/// OS NUMEROS SAEM DO CORE (`Catalogo.ForcaDoContorno` e `PisoDoPulsoDoContorno`), nunca escritos
	/// aqui: se o dono pedir um contorno mais forte amanha, esta bancada acompanha em vez de virar o
	/// unico lugar do projeto que ainda acha que sao 0,70.
	/// ============================================================================================================
	/// </summary>
	private void OContornoAlheio(string estado, bool excesso)
	{
		float topo = Jandirus.Core.Forms.Catalogo.ForcaDoContorno;
		float piso = topo * (float)Jandirus.Core.Forms.Catalogo.PisoDoPulsoDoContorno;
		bool naBase = _formaDoOutro == Jandirus.Core.Forms.Catalogo.IdBase;

		if (!excesso)
		{
			Conferir(_contornoMax <= 0.001f,
					 $"[{estado}] o contorno do corpo ALHEIO fica APAGADO com o Ki abaixo dos 100% "
				   + $"(faixa medida {_contornoMin:0.###}..{_contornoMax:0.###})");
			return;
		}

		if (naBase)
		{
			// NA BASE, KI NENHUM ACENDE CONTORNO -- nao ha forma, logo nao ha cor, logo forca 0 (ver
			// `_contornoDaForma`, cujo `default` e forca zero). E o corolario que o dono avisou.
			Conferir(_contornoMax <= 0.001f,
					 $"[{estado}] na BASE, nem o Ki acima dos 100% acende contorno "
				   + $"(faixa {_contornoMin:0.###}..{_contornoMax:0.###})");

			// ============================ E A OUTRA METADE DA MESMA FRASE ============================
			// A regra do dono nao e "na base nao acende nada", e sim *"na base ... a unica coisa que deve
			// ficar ativa e o node de carga"*. A linha de cima sozinha cobra o "nao acende" e da o MESMO
			// verde pro corpo alheio inteiro apagado -- que e o outro jeito de errar isto, e o mais
			// barato: basta a chama da carga deixar de ser desenhada no corpo remoto e ninguem repara,
			// porque o que o dono relatou olhando a tela e sempre o que FALTA no outro.
			//
			// E ela e a testemunha de que o bit CHEGOU: a `CargaVisual` e escrita pelo mesmo
			// `EntityState.Sobrecarregado` que o contorno le (`World.AoReceberSnapshot`), por um caminho
			// separado. Sem esta linha, "contorno em zero" no corpo alheio nao distingue "a regra da base
			// funcionou" de "o snapshot nunca disse nada a este corpo".
			// ====================================================================================
			Conferir(_cargaDesenhouSempre,
					 $"[{estado}] -- e o node de CARGA fica desenhando o tempo todo, que e a unica "
				   + "coisa que a base tem (senao 'nao acende' passaria com o corpo alheio apagado)");
			return;
		}

		Conferir(_contornoMax >= topo - 0.02f && _contornoMin >= piso - 0.02f
				 && _contornoMax <= topo + 0.02f,
				 $"[{estado}] o contorno do corpo ALHEIO ACENDE com o Ki acima dos 100%, na forca do "
			   + $"Core (esperado {piso:0.###}..{topo:0.###}, medido {_contornoMin:0.###}..{_contornoMax:0.###})");
	}

	/// <summary>
	/// ============================ FAMILIA 1 -- O SEGUNDO CORPO QUE ENTROU DEPOIS ============================
	/// O `--dois b` sobe DEPOIS do A ja transformado (e o roteiro que garante isso), entao o corpo que
	/// ele esta comparando nasceu de um `PeerLook` e de um snapshot que chegaram com a forma ja
	/// acontecida -- o caminho de entrada, que e onde o defeito relatado morava.
	///
	/// E A COMPARACAO E DE CONJUNTO. Ver `RetratoDeCorpo`: nao ha lista de campos, ha varredura de
	/// nodes, propriedades e uniformes de shader. Um campo visual novo entra sozinho; foi o rabo
	/// dourado que so a comparacao achou (e ele estava numa lista escrita a mao -- o proximo nao vai
	/// estar).
	/// ==========================================================================================================
	/// </summary>
	private void OsDoisCorpos(string estado)
	{
		RetratoDeCorpo? dono = RetratoDeCorpo.Ler(ArquivoDoDono);
		if (dono == null)
		{
			Conferir(false, $"[{estado}] o retrato do corpo do DONO nao chegou ({ArquivoDoDono}) -- "
						  + "sem ele nao ha regua, e uma bancada sem regua nao e uma bancada verde");
			return;
		}

		// RETRATO VELHO E RETRATO DE OUTRO MOMENTO. Sem esta guarda, uma rodada anterior deixada no
		// disco seria comparada com este corpo e a bancada julgaria dois mundos diferentes.
		if (dono.Idade > TimeSpan.FromSeconds(20))
		{
			GD.Print($"[dois-b] retrato do dono velho demais ({dono.Idade.TotalSeconds:0}s) -- nao julguei");
			_jaJulguei.Remove($"{_travessia}|{estado}");
			return;
		}

		if (dono.Estado != estado)
		{
			GD.Print($"[dois-b] o dono esta em '{dono.Estado}' e eu vejo '{estado}' -- espero casar");
			_jaJulguei.Remove($"{_travessia}|{estado}");
			return;
		}

		List<string> fora = RetratoDeCorpo.Comparar(dono, _retrato, SoNoDono);
		_comparacoes++;

		// A CONTAGEM DE CAMPOS E UMA CHECAGEM, e nao enfeite: uma varredura que quebrasse (tipo sem
		// propriedades, material nulo, node que nao nasceu) devolveria "zero diferencas" e passaria
		// como perfeita. Uma bancada que pode ficar verde por nao ter olhado nada e pior que nenhuma.
		Conferir(dono.Campos > 200 && _retrato.Campos > 200,
				 $"[{estado}] a varredura colheu corpo de verdade nos dois lados "
			   + $"(dono {dono.Campos} campos / alheio {_retrato.Campos})");

		foreach (string d in fora) GD.Print($"[dois-b]        > {d}");
		Conferir(fora.Count == 0,
				 $"[{estado}] **o corpo alheio e igual ao corpo do dono, campo por campo** "
			   + $"({fora.Count} divergencia(s) em {dono.Campos} campos)");
	}

	private RemotePlayer? CorpoAlheio()
	{
		if (_corpoAlheio != null && GodotObject.IsInstanceValid(_corpoAlheio)) return _corpoAlheio;
		if (_relogio < _proximaBusca) return null;
		_proximaBusca = _relogio + 1;
		if (GetTree().Root.FindChild("World", true, false) is not Node mundo) return null;
		var achados = new List<Node2D>();
		Rastelar(mundo, achados);
		_corpoAlheio = achados.OfType<RemotePlayer>().FirstOrDefault();
		return _corpoAlheio;
	}

	// =====================================================================
	// FAMILIA 3 -- A ANCORA DA AURA SAI DA FOLHA
	// =====================================================================
	/// <summary>
	/// ============================ ELA NAO PODE SER UM NUMERO ============================
	/// "A aura do UI ta torta" foi julgada por foto tres vezes e errada tres vezes. A regra que
	/// resolveu esta escrita em `SpriteDeAura.AncoraPara`: com o sprite `Centered`, a base do quadro
	/// cai em `+altura/2`, os pes do corpo em `LinhaDosPes`, logo `Offset.Y = LinhaDosPes -
	/// altura/2`. E a regra do original (`AuraObject.dm:61-62` manda todo icone que nao seja 32x32
	/// pra `center-bottom`).
	///
	/// O `--diagforma` ja confere a FUNCAO (`AncoraPara(96) == -32`). Isso nao basta: um `Offset`
	/// cravado dentro do `Montar` deixaria a funcao inteira certa e o desenho inteiro errado, e foi
	/// assim que a chama grande afundou uma vez.
	///
	/// ENTAO O QUE SE MEDE AQUI E O NODE VIVO, COM AS FOLHAS DE VERDADE -- e sao TRES ALTURAS
	/// diferentes no disco (96 na `colorablebigaura`/`AuraSSjBig`/`AuraLSSjBig`, 64 nas duas divinas,
	/// 101 na Rose). Um numero cravado acerta uma delas e erra as outras duas **por construcao**:
	/// nao ha literal que faca as tres baterem. E e por isso que a bancada exige as tres alturas
	/// distintas em vez de so passar folha por folha.
	/// ====================================================================================
	/// </summary>
	private void AAncoraSaiDaFolha()
	{
		string[] folhas =
		[
			SpriteDeAura.FolhaBase, SpriteDeAura.FolhaLssj, SpriteDeAura.FolhaSsj,
			SpriteDeAura.FolhaDeusQuente, SpriteDeAura.FolhaDeusFrio, SpriteDeAura.FolhaDeusRosa,
		];

		var alturas = new HashSet<float>();
		foreach (string caminho in folhas)
		{
			var frames = ResourceLoader.Load<SpriteFrames>(caminho);
			string anim = frames?.HasAnimation("default") == true ? "default"
						: frames?.GetAnimationNames() is { Length: > 0 } n ? n[0] : "";
			float altura = frames?.GetFrameTexture(anim, 0)?.GetHeight() ?? 0;
			if (altura <= 0) { Conferir(false, $"a folha {caminho.GetFile()} nao carregou"); continue; }
			alturas.Add(altura);

			var d = new SpriteDeAura { Name = "AncoraDeTeste" };
			AddChild(d);
			d.DefinirFolha(caminho);
			d.Definir(true, Colors.White);

			Conferir(Mathf.Abs(d.BaseDeTeste - SpriteDeAura.LinhaDosPes) <= 0.01f,
					 $"a base da folha {caminho.GetFile()} (quadro de {altura:0} px) cai na linha dos "
				   + $"pes: medido {d.BaseDeTeste:0.###}, pe {SpriteDeAura.LinhaDosPes}");
			d.QueueFree();
		}

		Conferir(alturas.Count >= 3,
				 $"as folhas exercitadas tem {alturas.Count} ALTURAS distintas ({string.Join("/", alturas.OrderBy(a => a))}) "
			   + "-- e e isso que torna impossivel um Offset cravado passar");

		// ============================ E O ULTRA INSTINTO NAO TEM ANCORA PORQUE NAO TEM FOLHA ============================
		// Estas linhas mediam a ancora da aura das duas formas de UI, e o comentario delas dizia o
		// diagnostico inteiro sem saber: *"as duas formas de UI nao declaram `Folha`, entao caem no
		// fallback `FolhaDeAura.Base`"*. Era o defeito, escrito como se fosse a regra -- a chama
		// `colorablebigaura` acendia por cima da nebulosa, que e a queixa do dono.
		//
		// Hoje o Core devolve `FolhaDeAura.Nebulosa` pras duas, e a `CaminhoDa` responde NULO: "a minha
		// nao e folha". Medir a ancora de uma folha que nao existe deixou de fazer sentido, entao o que
		// esta bancada cobra e o fato que substituiu aquele -- e ela continua perguntando ao CORE, que
		// era o unico ponto bom do bloco antigo.
		// ==========================================================================================================
		foreach (string id in new[] { "ui_sign", "ui_perfected" })
		{
			Jandirus.Core.Forms.FormaDef? def = Jandirus.Core.Forms.Catalogo.Def(id);
			var simbolo = Jandirus.Core.Forms.Catalogo.Folha(def);

			Conferir(simbolo == Jandirus.Core.Forms.FolhaDeAura.Nebulosa
					 && SpriteDeAura.CaminhoDa(simbolo) == null,
					 $"`{id}` nao tem folha de aura: o Core devolve {simbolo} e a traducao devolve nulo "
				   + "-- o desenho dele e a nuvem, e nenhuma chama pode acender por cima dela");

			// E O NODE OBEDECE, que e a outra metade: o simbolo certo nao vale nada se o `SpriteDeAura`
			// ainda montasse alguma coisa. Um sprite proprio, sem corpo nenhum em volta.
			var d = new SpriteDeAura { Name = "SemFolhaDeTeste" };
			AddChild(d);
			d.DefinirFolha(simbolo);
			d.Definir(true, Colors.White);
			Conferir(d.SemFolha && !d.Visible,
					 $"e mandar a folha de `{id}` num desenho e ACENDE-LO nao desenha nada "
				   + $"(sem folha {d.SemFolha}, visivel {d.Visible})");
			d.QueueFree();
		}
	}

	// =====================================================================
	// O VEREDITO
	// =====================================================================
	/// <summary>
	/// FECHA A BANCADA. So o papel B tem placar -- o A nao mede nada, ele e o corpo medido.
	///
	/// **UMA BANCADA QUE NAO COMPAROU NADA REPROVA.** Este e o modo de falha mais perigoso de uma
	/// bancada de dois processos: o outro lado nao subiu, a forma nao pegou, o corpo nunca entrou no
	/// campo de visao -- e o placar sai "0 falhas", que se le exatamente igual a "tudo certo".
	/// </summary>
	private void Fechar()
	{
		if (Papel == "b")
		{
			Conferir(_comparacoes >= 2,
					 $"a bancada comparou os dois corpos em pelo menos DOIS estados ({_comparacoes} "
				   + "comparacao(oes)) -- com Ki normal e com Ki acima dos 100%");

			// MESMA REGRA DA LINHA DE CIMA, pro roteiro da ficha: se o `--doisficha` foi pedido e nenhum
			// `PeerLook` caiu num corpo vivo, entao a rodada mediu o caminho de NASCIMENTO de novo e o
			// placar verde estaria falando de outra coisa. Sem o `--doisficha` a linha nem existe.
			if (Ficha > 0)
				Conferir(_fichasEmCorpoVivo > 0,
						 "a ficha (`PeerLook`) chegou num corpo alheio que JA existia "
					   + $"({_fichasEmCorpoVivo} vez(es)) -- e o caminho em que o `CharacterVisual.Vestir` "
					   + "remonta as camadas do zero por cima de uma transformacao");

			GD.Print($"\n===== [dois-b] {_oks} OK, {_falhas.Count} FALHA =====");
			foreach (string f in _falhas) GD.PrintErr("  FALHA  " + f);
		}
		GetTree().Quit();
	}

	/// <summary>Salva a tela. No headless o `GetImage` volta vazio -- esta bancada quer janela.</summary>
	private void Fotografar(string destino)
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) { GD.Print($"[dois-{Papel}] SEM FOTO (headless nao renderiza)"); return; }
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			GD.Print($"[dois-{Papel}] foto: " + caminho);
		}
		catch (Exception e) { GD.Print($"[dois-{Papel}] sem foto: " + e.Message); }
	}
}
