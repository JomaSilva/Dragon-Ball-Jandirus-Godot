using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DO CLIMA (`--diagclima`).
///
/// ============================ O QUE SÓ O TESTE RESPONDE ============================
/// Uma foto de um dia chuvoso prova que existe chuva na tela. Não prova nada disto:
///   * o clima é o MESMO nas duas pontas? (ele não viaja pelo fio -- se as contas divergirem,
///     um jogador está na tempestade e o outro no sol, e ninguém percebe até brigarem sobre isso)
///   * cada planeta sorteia da LISTA DELE? (chuva de sangue é de Vegeta; areia é de Vampa)
///   * o custo é FIXO? (o pool de partículas não pode crescer com a força da chuva)
///   * a transição é suave, ou o céu troca de estado num quadro?
///   * o gancho das transformações realmente força o céu, e ele volta sozinho depois?
///   * a nuvem apaga a lua? (senão a noite de tempestade fica clareada por uma lua invisível)
/// ==================================================================================
///
/// COMO RODAR:
///     Godot --path . --host --climateste Tempestade --diagclima --nome Chuva --conta clima
///
/// `--climateste &lt;tipo&gt;` trava o céu -- sem ele o ciclo natural corre em blocos de seis minutos
/// e sortear a nevasca de Icer pra conferir o desenho dela pode levar meia hora.
/// </summary>
public partial class RoboDeClima : Node
{
	private const double Paciencia = 15;

	/// <summary>Quanto tempo medir o custo e a suavidade, em segundos.</summary>
	private const double Vigia = 12;

	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	private bool _acabou;
	private int _fase;
	private double _t, _espera;

	private int _pingoMaximo;
	private double _forcaAnterior = -1;
	private double _maiorRitmo;
	private int _amostras;

	private Vector2? _andouDe;
	private Vector2 _emissorDe, _campoDe;

	private static GameClient? C => GameClient.Instance;

	/// <summary>Quantos raios o SERVIDOR anunciou, e a que distância caiu o último.</summary>
	private int _raios;
	private float _distanciaDoRaio = -1;

	public override void _Ready()
	{
		if (C is not { } cli) return;

		// O RAIO VEM DO SERVIDOR AGORA. Ele era sorteado dentro de cada cliente, e por isso dois
		// jogadores lado a lado não viam a mesma tempestade. Contar os pacotes é a única prova de
		// que o caminho novo existe: o desenho continuaria compilando e rodando se ninguém
		// mandasse nada, só que sem raio nenhum jamais cair.
		cli.RaioCaiu += (onde, _) =>
		{
			_raios++;
			if (World.Instancia?.PosicaoLocal is { } eu)
				_distanciaDoRaio = new Vector2(onde.X, onde.Y).DistanceTo(eu);
		};
	}

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	/// <summary>
	/// A ORDEM DE DESENHO DO MAPA: o corpo do jogador fica ACIMA do chão e da decoração, e ABAIXO
	/// dos objetos.
	///
	/// ============================ POR QUE ISTO PRECISA DE GUARDA ============================
	/// Decoração e objetos dividiam o z 0 com os atores, e as três camadas ordenavam por Y entre
	/// si. Funcionava pras árvores (é o que põe o jogador atrás de uma quando ele está acima dela)
	/// e falhava pra tudo que é rasteiro: qualquer tufo de grama com Y maior que o do jogador
	/// desenhava por cima dele, e o boneco passava por baixo do chão.
	///
	/// Faltava um degrau: não existe inteiro entre -1 e 0. O chão desceu pra -2 e a decoração
	/// ocupou o -1. É o tipo de coisa que se conserta uma vez e volta na próxima mexida no
	/// conversor, porque o sintoma é sutil e específico de algumas células.
	/// ========================================================================================
	/// </summary>
	private void ConferirCamadasDoMapa(World mundo)
	{
		TileMapLayer? chao = null, decor = null, objetos = null;

		void Varrer(Node n)
		{
			foreach (Node f in n.GetChildren())
			{
				if (f is TileMapLayer c)
					switch (f.Name.ToString())
					{
						case "Chao": chao ??= c; break;
						case "Decor": decor ??= c; break;
						case "Objetos": objetos ??= c; break;
					}
				Varrer(f);
			}
		}
		Varrer(mundo);

		if (chao == null || decor == null || objetos == null)
		{
			_passos.Add($"         (mapa sem as tres camadas: chao={chao != null}, "
						+ $"decor={decor != null}, objetos={objetos != null} -- zona pre-feita?)");
			return;
		}

		// O ATOR DESENHA EM z 0. Quem tem que ficar embaixo precisa de z NEGATIVO -- ordenar por Y
		// no mesmo z nao resolve, porque metade das celulas tem Y maior que o do jogador.
		Conferir(decor.ZIndex < 0,
			$"a decoracao fica ABAIXO do jogador (z {decor.ZIndex}, ator em 0)");
		Conferir(chao.ZIndex < decor.ZIndex,
			$"o chao fica abaixo da decoracao (z {chao.ZIndex} contra {decor.ZIndex})");

		// OS OBJETOS CONTINUAM EM z 0 E ORDENANDO POR Y: e o que poe o jogador ATRAS da arvore
		// quando ele esta acima dela. Baixar esta camada junto com a decoracao mataria isso.
		Conferir(objetos.ZIndex == 0 && objetos.YSortEnabled,
			$"os objetos ordenam por Y junto do jogador (z {objetos.ZIndex}, "
			+ $"ysort {objetos.YSortEnabled}) -- e o que poe o corpo atras da arvore");
	}

	public override void _Process(double delta)
	{
		if (_acabou || C is not { Connected: true } cli || World.Instancia is not { } mundo) return;

		_t += delta;
		if (_espera > 0) { _espera -= delta; return; }

		switch (_fase)
		{
			case 0:
				if (!cli.TempoChegou)
				{
					if (_t < Paciencia) return;
					Conferir(false, $"o servidor NAO mandou a hora do mundo em {Paciencia:0}s");
					_fase = 90;
					return;
				}
				_fase = 1;
				break;

			case 1:
			{
				// AS LISTAS SÃO AS DO DM. Vale a pena conferir uma por uma: o extrator anda numa
				// árvore por indentação com herança, e um erro lá dá um planeta com o clima de
				// outro -- que na tela parece perfeitamente normal.
				var esperado = new (string Zona, TipoDeClima Tipo, bool Deve)[]
				{
					("Earth", TipoDeClima.Chuva, true),
					("Earth", TipoDeClima.ChuvaDeSangue, false),
					("Vegeta", TipoDeClima.ChuvaDeSangue, true),
					("Vegeta", TipoDeClima.Chuva, false),
					("Namek", TipoDeClima.ChuvaDeNamek, true),
					("Desert", TipoDeClima.Areia, true),
					("Icer", TipoDeClima.Nevasca, true),
				};

				foreach ((string zona, TipoDeClima tipo, bool deve) in esperado)
				{
					ClimaDoPlaneta c = Planetas.Clima(ZoneKey.Premade(zona));
					bool tem = Array.IndexOf(c.Permitidos, tipo) >= 0;
					Conferir(tem == deve,
						$"{zona} {(deve ? "PODE" : "nao pode")} ter {Clima.Nome(tipo)}"
						+ (tem == deve ? "" : $" (achei: {string.Join(", ", c.Permitidos.Select(Clima.Nome))})"));
				}

				Conferir(!Planetas.Clima(Espaco.Zona(1)).Existe, "o espaco nao tem clima");
				Conferir(!Planetas.Clima(ZoneKey.Interior("Nave", 1)).Existe, "interior nao tem clima");

				ConferirCamadasDoMapa(mundo);

				// O NUBLADO É NOSSO, e tem que estar em todo planeta que já tem clima -- é o
				// degrau leve que o DM não tinha.
				Conferir(Array.IndexOf(Planetas.Clima(ZoneKey.Premade("Earth")).Permitidos,
									   TipoDeClima.Nublado) >= 0,
					"o nublado (adicao nossa) entrou nos planetas que tem clima");

				_fase = 2;
				break;
			}

			case 2:
			{
				// AS DUAS PONTAS TÊM QUE CHEGAR AO MESMO CÉU. Como o clima natural não viaja, é
				// esta igualdade que o substitui -- e ela vale pra QUALQUER instante, então dá
				// pra conferir o futuro inteiro de uma vez em vez de esperar seis minutos.
				ClimaDoPlaneta terra = Planetas.Clima(ZoneKey.Premade("Earth"));
				ulong sal = Clima.SalDaZona(ZoneKey.Premade("Earth"));
				var vistos = new HashSet<TipoDeClima>();
				int blocosComClima = 0;
				const int amostras = 240;   // 24 horas reais de céu

				for (int n = 0; n < amostras; n++)
				{
					double t = cli.TempoDoMundo + n * Clima.SegundosPorBloco;
					EstadoDoClima e = Clima.Natural(terra, t, sal);
					if (e.Tipo == TipoDeClima.Limpo) continue;
					blocosComClima++;
					vistos.Add(e.Tipo);
					// o cliente e o servidor rodam ESTA função; se ela não for pura, nada abaixo vale
					if (Clima.Natural(terra, t, sal).Tipo != e.Tipo)
					{
						Conferir(false, "a mesma função deu climas diferentes pro mesmo instante");
						break;
					}
				}

				Conferir(vistos.Count >= 3,
					$"a Terra sorteia variedade em {amostras} blocos: {vistos.Count} tipos "
					+ $"({string.Join(", ", vistos.Select(Clima.Nome))})");

				double fracao = blocosComClima / (double)amostras;
				Conferir(fracao is > 0.25 and < 0.75,
					$"o ceu fica limpo boa parte do tempo ({1 - fracao:P0} limpo, {fracao:P0} com clima)");

				// VEGETA NÃO PODE TER O MESMO CÉU DA TERRA no mesmo minuto -- é o sal por zona.
				ulong salV = Clima.SalDaZona(ZoneKey.Premade("Vegeta"));
				int iguais = 0;
				for (int n = 0; n < amostras; n++)
				{
					double t = cli.TempoDoMundo + n * Clima.SegundosPorBloco;
					if (Clima.Natural(terra, t, sal).Tipo == Clima.Natural(
							Planetas.Clima(ZoneKey.Premade("Vegeta")), t, salV).Tipo) iguais++;
				}
				Conferir(iguais < amostras * 0.7,
					$"Terra e Vegeta nao correm o mesmo ceu ({iguais}/{amostras} blocos coincidem)");

				_t = 0;
				_fase = 3;
				// DEIXA A CENA ASSENTAR ANTES DE MEDIR RITMO. A carga da zona instancia um mapa
				// de 250 mil tiles e produz quadros de meio segundo; medir velocidade de rampa em
				// cima disso mede o carregador, nao o clima.
				_espera = 2;
				break;
			}

			case 3:
			{
				// CUSTO FIXO E TRANSIÇÃO SUAVE, medidos ao vivo.
				//
				// O RITMO, E NÃO O SALTO ENTRE QUADROS. A rampa do clima é por TEMPO, então um
				// quadro longo avança muito e isso está certo -- a primeira versão desta bancada
				// mediu 0,421 de salto, que é exatamente meio segundo de rampa: o quadro em que a
				// cena da zona termina de carregar. Um pulo durante uma tela travada não é um
				// pulo que alguém vê. O que de fato importa é a VELOCIDADE da rampa, e ela tem
				// que caber no que o `Clima` promete: 1/1,2 s no forçado, 1/45 s no natural.
				if (mundo.TempoQueFaz is { } tq && delta > 0)
				{
					if (_forcaAnterior >= 0)
						_maiorRitmo = Math.Max(_maiorRitmo, Math.Abs(tq.Forca - _forcaAnterior) / delta);
					_forcaAnterior = tq.Forca;
					_amostras++;
				}

				if (mundo.GetNodeOrNull("Iluminacao/Clima") is ClimaNaTela c)
					_pingoMaximo = Math.Max(_pingoMaximo, c.PingosVivos);

				if (_t < Vigia) return;

				EstadoDoClima agoraCusto = mundo.TempoQueFaz ?? default;
				_passos.Add($"         zona {cli.Zone.Name} | {Clima.Nome(agoraCusto.Tipo)} "
							+ $"forca {agoraCusto.Forca:0.00} | cinza {agoraCusto.Cinza:0.00} "
							+ $"| encobre {agoraCusto.Encobre:0.00}{(agoraCusto.Forcado ? " | FORCADO" : "")}");
				_passos.Add($"         {_amostras} amostras em {Vigia:0}s | pico de pingos: {_pingoMaximo}");

				Conferir(_pingoMaximo <= 900,
					$"o custo NAO cresce com a chuva: pico de {_pingoMaximo} pingos no pool fixo de 900");

				// O TETO É O DA ENTRADA RÁPIDA DO CLIMA FORÇADO. Passar disso significa que
				// alguém escreveu a força direto em vez de deixar a rampa fazer o trabalho -- e
				// aí o céu troca de estado num estalo, que é o defeito que a rampa evita.
				double teto = 1 / 1.2 * 1.35;   // a folga cobre a variação de quadro
				Conferir(_maiorRitmo <= teto,
					$"a forca do clima sobe no ritmo da rampa e nao de estalo "
					+ $"({_maiorRitmo:0.000}/s, teto {teto:0.000}/s)");

				// O RAIO É DO SERVIDOR, E CAI NUM PONTO. Numa tempestade cheia ele sai a cada
				// 2,7-9,9 s, então a vigia de 12 s tem que pegar pelo menos um. Se não pegar, o
				// caminho novo não existe e a tempestade voltou a ser um efeito de tela.
				if (Clima.TemRaio(agoraCusto.Tipo) && agoraCusto.Forca > 0.45)
				{
					Conferir(_raios > 0,
						$"o SERVIDOR anuncia os raios da tempestade ({_raios} em {Vigia:0}s)");
					Conferir(_distanciaDoRaio > 0,
						$"o raio cai a uma distancia do jogador ({_distanciaDoRaio:0} px) -- "
						+ "e o que decide entre ver o risco e so ouvir o trovao");
				}
				else _passos.Add($"         ({Clima.Nome(agoraCusto.Tipo)} nao solta raio)");

				_fase = 4;
				break;
			}

			case 4:
			{
				// A NUVEM APAGA A LUA. Sem isto, uma noite de tempestade ficaria clareada por uma
				// lua cheia que ninguem consegue ver -- e, com o Oozaru pendurado nela, um
				// Saiyajin viraria macaco olhando pra um céu fechado.
				var noite = new EstadoDoCeu { Hora = 0, Fase = 5, Altura = 1, Aceso = 1 };
				var temporal = new EstadoDoClima { Tipo = TipoDeClima.Tempestade, Forca = 1 };
				Color limpa = Iluminacao.CorDoCeu(noite, default);
				Color fechada = Iluminacao.CorDoCeu(noite, temporal);

				Conferir(temporal.Encobre > 0.9,
					$"a tempestade fecha o ceu ({temporal.Encobre:P0} de cobertura)");
				Conferir(fechada.Luminance < limpa.Luminance,
					$"noite de lua cheia COM temporal e mais escura que sem ({fechada.Luminance:0.000} "
					+ $"contra {limpa.Luminance:0.000})");

				// O CLIMA TEM QUE MUDAR A LUZ DE DIA TAMBEM. Era aqui que estava o buraco: a
				// versao anterior "dessaturava mantendo o brilho", e como o tom do meio-dia ja e
				// quase cinza, isso era operacao nula -- nublado, neblina e nevasca nao mudavam
				// nada em pleno 100%. O teste roda ao MEIO-DIA de proposito.
				var meioDia = new EstadoDoCeu { Hora = 0.5, Fase = 0, Altura = -1 };
				Color limpo = Iluminacao.CorDoCeu(meioDia, default);
				foreach (TipoDeClima t in new[] { TipoDeClima.Nublado, TipoDeClima.Neblina,
												  TipoDeClima.Nevasca, TipoDeClima.Areia,
												  TipoDeClima.Tempestade })
				{
					Color com = Iluminacao.CorDoCeu(meioDia, new EstadoDoClima { Tipo = t, Forca = 1 });

					// POR CANAL, e nao por luminancia. A areia CLAREIA o vermelho e ESCURECE o
					// azul: em luminancia os dois quase se cancelam (9%) e o teste reprovaria um
					// ocre forte que muda a cena inteira. O que se quer medir e "mudou o ar", e
					// tingir e mudar tanto quanto escurecer.
					float muda = Mathf.Max(Mathf.Abs(com.R - limpo.R),
										   Mathf.Max(Mathf.Abs(com.G - limpo.G), Mathf.Abs(com.B - limpo.B)))
							   / Mathf.Max(limpo.Luminance, 0.001f);
					Conferir(muda > 0.12f,
						$"{Clima.Nome(t)} muda o ar do MEIO-DIA em {muda:P0} "
						+ $"({limpo.Luminance:0.00} -> {com.Luminance:0.00} de brilho)");
				}
				Conferir(Clima.TemRaio(TipoDeClima.Tempestade) && !Clima.TemRaio(TipoDeClima.Chuva),
					"so a tempestade solta raio (a chuva comum nao)");

				_fase = 5;
				break;
			}

			case 5:
			{
				// ANDAR TEM QUE MOSTRAR CHUVA NOVA, NAO A MESMA CHUVA.
				//
				// Este e o defeito que o dono viu: com tudo em espaco de tela, o efeito viajava
				// colado na camera e lia como sujeira na lente. A prova de que ficou no lugar tem
				// duas metades, e as duas precisam valer:
				//   * o EMISSOR segue a camera (senao so choveria onde se entrou no planeta);
				//   * a gota JA SOLTA nao segue (`LocalCoords = false`) -- e o que a deixa pra tras.
				// Uma sem a outra devolve o problema por um caminho diferente.
				if (mundo.GetNodeOrNull("Iluminacao/Clima") is not ClimaNaTela c)
				{
					Conferir(false, "achei o node do clima pra medir a ancoragem");
					_fase = 90;
					break;
				}

				// UMA VEZ SO, na entrada da fase. Este ramo roda a cada quadro enquanto a bancada
				// anda -- conferir aqui dentro imprimia a mesma linha trezentas vezes e afogava o
				// relatorio inteiro num "ok" repetido.
				if (_andouDe is null)
				{
					Conferir(c.PrendeNoMundo,
						"a gota ja solta vive no MUNDO e nao no emissor (LocalCoords = false)");
					Conferir(c.RaioNoMundo,
						"o raio cai num ponto do MAPA (nao esta colado na camera)");

					// NEVE E CHUVA NAO PODEM DIVIDIR A FORMA. Dividiam: a neve usava a textura de
					// risco da chuva e, como o floco GIRA, a tela virava um campo de arranhoes
					// brancos apontando pra todo lado. Nenhum ajuste de cor ou velocidade
					// conserta forma errada, entao a guarda e sobre a forma.
					Texture2D chuva = ClimaNaTela.FormaDe(TipoDeClima.Chuva);
					Texture2D neve = ClimaNaTela.FormaDe(TipoDeClima.Neve);
					Conferir(chuva != neve, "neve e chuva NAO compartilham a mesma forma");
					Conferir(neve.GetWidth() == neve.GetHeight(),
						$"o floco e redondo ({neve.GetWidth()}x{neve.GetHeight()})");
					Conferir(chuva.GetHeight() > chuva.GetWidth() * 3,
						$"o pingo e um risco ({chuva.GetWidth()}x{chuva.GetHeight()})");

					// A CELULA DO RUIDO TEM QUE CABER NA TELA VARIAS VEZES. Com celulas maiores
					// que o quadro, o veu vira uma cor chapada e andar nao muda nada -- foi o que
					// fez a neblina parecer presa na camera mesmo ja sendo amostrada no mundo.
					//
					// Contra a CONSTANTE de tela de jogo, e nao contra o viewport: em `--headless`
					// a janela tem 64 px, e comparar com ela reprovaria qualquer valor.
					Conferir(ClimaNaTela.MaiorCelula < ClimaNaTela.MundoVisivelTipico * 0.5f,
						$"cabem 2+ celulas de ruido na tela (maior celula {ClimaNaTela.MaiorCelula:0} px, "
						+ $"tela {ClimaNaTela.MundoVisivelTipico:0} px) -- senao nao ha estrutura nem paralaxe");

					// O VEU NAO PODE ENGOLIR O PERSONAGEM. Ele cobre a tela inteira, e opacidade
					// alta baixa o contraste de TUDO por igual -- o boneco fica parecendo meio
					// transparente. Quem carrega o tempo fechado e o ambiente, que multiplica.
					Conferir(ClimaNaTela.MaiorDensidade <= ClimaNaTela.DensidadeMaxima,
						$"o veu nao lava o personagem (maior densidade {ClimaNaTela.MaiorDensidade:0.00}, "
						+ $"teto {ClimaNaTela.DensidadeMaxima:0.00})");

					// NUVEM E SOMBRA, E SOMBRA E COR ESCURA. Misturar a cena com cinza CLARO
					// comprime tudo pro meio da escala: o preto sobe, o branco desce, e o
					// personagem perde contraste contra o chao -- e a aparencia de "meio
					// transparente" que o dono reportou duas vezes. Com cor quase preta a mistura
					// vira uma multiplicacao, que e literalmente o que uma nuvem faz: tirar luz.
					//
					// As leitosas (neblina, nevasca, areia) sao a excecao legitima: elas ESPALHAM
					// luz, entao lavar e o efeito certo -- e por isso a checagem e por familia.
					float veuMaisClaro = 0;
					foreach (TipoDeClima t in Enum.GetValues<TipoDeClima>())
						if (t != TipoDeClima.Limpo)
							veuMaisClaro = Math.Max(veuMaisClaro, ClimaNaTela.ClaridadeDaMassa(t));
					Conferir(veuMaisClaro <= ClimaNaTela.LimiteDeSombra,
						$"NENHUM veu e claro (o mais claro tem {veuMaisClaro:0.00}, teto "
						+ $"{ClimaNaTela.LimiteDeSombra:0.00}) -- veu claro tinge o personagem");

					// O CLIMA CLARO EXISTE, mas na COR DO AR, que multiplica o ambiente e por isso
					// nao mexe na opacidade de nada. Se ninguem passar de 1, um whiteout de nevasca
					// seria impossivel e so restariam climas escuros.
					(double br, _, _) = Clima.CorDoAr(TipoDeClima.Neblina);
					Conferir(br > 1, $"a neblina CLAREIA o ar (fator {br:0.00}) -- nevoa espalha o sol");
					(double sr, _, _) = Clima.CorDoAr(TipoDeClima.Tempestade);
					Conferir(sr < 0.5, $"a tempestade ESCURECE o ar (fator {sr:0.00})");

					// A ESCALA NAO PODE INVERTER. Nevasca e o temporal da familia da neve: ela tem
					// que ser pelo menos tao pesada quanto a neve simples, e pesada como a
					// tempestade. Ja esteve mais CLARA que a neve, e o pior tempo do planeta era o
					// mais alegre da tela.
					(double nvr, _, _) = Clima.CorDoAr(TipoDeClima.Nevasca);
					(double nr, _, _) = Clima.CorDoAr(TipoDeClima.Neve);
					Conferir(nvr < nr, $"a nevasca e mais pesada que a neve ({nvr:0.00} contra {nr:0.00})");
					Conferir(Math.Abs(nvr - sr) < 0.15,
						$"a nevasca escurece como a tempestade ({nvr:0.00} contra {sr:0.00})");

					// TURBULENCIA NAO. Num corpo lento como o floco de neve ela deixa de ser um
					// empurraozinho e vira quem manda: os flocos ficavam presos numa faixa no topo
					// da tela em vez de atravessar, enquanto a chuva (rapida) descia inteira.
					Conferir(!c.UsaTurbulencia,
						"nenhum clima usa turbulencia (era ela que prendia a neve no topo da tela)");

					// E TODO MUNDO TEM QUE ATRAVESSAR A TELA. Vida curta demais mata a particula
					// no ar, e o que se ve e uma faixa de chuva pendurada no alto.
					foreach (TipoDeClima t in Enum.GetValues<TipoDeClima>())
					{
						float queda = ClimaNaTela.QuedaDe(t);
						if (queda == float.MaxValue) continue;   // este clima nao tem precipitacao
						Conferir(queda > ClimaNaTela.MundoVisivelTipico * 0.567f * 1.3f,
							$"{Clima.Nome(t)}: o que cai percorre {queda:0} px, mais que a tela "
							+ $"({ClimaNaTela.MundoVisivelTipico * 0.567f:0} px)");
					}
					_andouDe = mundo.PosicaoLocal;
					_emissorDe = c.EmissorEm;
					_campoDe = c.OrigemDoCampo;
					_t = 0;
				}

				// anda pro leste com o passo do jogador -- nao teleporta: o que se mede e a camera
				mundo.AndarDeTeste(new Vector2(1, 0));
				if (_t < 2.0) return;
				mundo.PararDeTeste();

				float andou = (mundo.PosicaoLocal ?? Vector2.Zero).X - (_andouDe?.X ?? 0);
				float emissor = c.EmissorEm.X - _emissorDe.X;
				float campo = c.OrigemDoCampo.X - _campoDe.X;

				_passos.Add($"         andei {andou:0} px | emissor andou {emissor:0} px "
							+ $"| campo do veu andou {campo:0} px");

				Conferir(Math.Abs(andou) > 60, $"a bancada de fato andou ({andou:0} px)");

				// SO QUANDO HA O QUE CAIR. Neblina, fumaça e nublado não têm precipitação -- o
				// emissor fica desligado e parado, e cobrar que ele siga a câmera reprovaria um
				// comportamento correto. (Foi o que aconteceu rodando com `--climateste Neblina`.)
				TipoDeClima caindo = (mundo.TempoQueFaz ?? default).Tipo;
				if (ClimaNaTela.Precipita(caindo))
					Conferir(Math.Abs(emissor - andou) < Math.Abs(andou) * 0.35f + 40,
						"o emissor ACOMPANHA a camera (senao so choveria no ponto de entrada)");
				else
					_passos.Add($"         ({Clima.Nome(caindo)} nao tem precipitacao: emissor desligado)");
				Conferir(Math.Abs(campo - andou) < Math.Abs(andou) * 0.35f + 40,
					"o campo do veu e amostrado em coordenada de MUNDO (a nuvem fica sobre o mapa)");

				_fase = 90;
				break;
			}

			default:
				_acabou = true;
				GD.Print("\n[clima] ===== BANCADA DO CLIMA =====");
				foreach (string l in _passos) GD.Print("[clima] " + l);
				GD.Print(_falhas.Count == 0
					? "[clima] ===== TUDO OK ====="
					: $"[clima] ===== {_falhas.Count} FALHA(S) =====\n[clima]   " + string.Join("\n[clima]   ", _falhas));
				break;
		}
	}
}
