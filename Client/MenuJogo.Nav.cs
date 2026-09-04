using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Social;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A ABA NAV -- a carta estelar, a tela do sistema e o painel do destino.
///
/// O REDESENHO AQUI FOI SO DE MOLDURA: a barra de zoom mora num cartao com o titulo da carta, e as
/// legendas de baixo moram num cartao "Legenda". O mapa, a tela do sistema e o painel do destino
/// ficam DIRETO na pagina, fora de qualquer cartao -- a `--diagnav` acha o painel do menu subindo do
/// mapa ate o primeiro `PanelContainer` (`PainelGrande`), e um cartao em volta do mapa faria ela
/// esticar o cartao em vez do menu.
/// </summary>
public partial class MenuJogo
{
	// =====================================================================
	// NAV -- o mapa do espaco
	// =====================================================================
	/// <summary>
	/// O MAPA ESTELAR: a galaxia desenhada, com zoom, arrasto e clique.
	///
	/// ============================ ERA UMA LISTA, E LISTA NAO E MAPA ============================
	/// Esta aba mostrava os corpos celestes das tres chunks em volta, em texto, ordenados por
	/// distancia. Isso responde "o que ha por perto" -- e nao responde a pergunta que faz um mapa
	/// estelar existir, que e "PRA ONDE EU VOU". Sem posicao relativa nao da pra escolher rota, nao
	/// da pra saber que Namek fica do lado oposto de Vegeta, e o universo inteiro cabe em cinco
	/// linhas de texto que mudam quando voce anda.
	///
	/// O pedido do dono: "faca um mapa do espaco com todos os planetas que o servidor sabe onde
	/// estao... voce clica nos planetas e seleciona viajar... e voce pode dar zoom e zoom out".
	/// O desenho, o zoom e o clique moram no <see cref="MapaEstelar"/>; esta aba e a moldura.
	/// ===========================================================================================
	///
	/// O TEMPO CONTINUA SENDO O QUE IMPORTA no texto: "1.608.035 px" nao diz nada a ninguem, "7
	/// dias" diz tudo -- e a mesma unidade em que o anime mede a viagem pra Namek. A conta sai do
	/// Core (velocidade de voo x ciclo do dia), entao ela nao pode divergir do que a viagem custa.
	/// </summary>
	private void AbaNav()
	{
		if (GameClient.Instance is not { } cli) return;

		// ============================ A SEGUNDA METADE DO PORTAO ============================
		// A aba so entra na barra com o bit `Poder.Nav` (ver `Abas()`), e o bit so acende com o item na
		// mochila (`GameServer.Sigilo.PoderesVisiveis`). Entao esta recusa e, em jogo normal, inalcancavel
		// -- e ela existe pelo mesmo motivo que existe no original: `ui_tab_nav` (`HtmlUI.dm:336-344`)
		// tambem imprime "O sistema de navegacao esta desligado" mesmo com a barra ja tendo escondido a
		// aba em `HtmlUI.dm:138`. Duas metades, e nenhuma confiando na outra.
		//
		// SEJAMOS HONESTOS SOBRE O QUE ELA VALE: nao e permissao. A carta estelar nao vem por rede
		// nenhuma (`MapaEstelar` e funcao pura da seed do mundo) e o piloto e do cliente
		// (`World.Pilotar` so escreve `LocalPlayer.Destino`, e andar ate la o servidor ja valida como
		// andar). Um cliente mexido continua enxergando a galaxia e continua conseguindo pilotar. O que
		// esta linha entrega e o que ela promete: a aba nao monta sem o item.
		// ==================================================================================================
		if (!_atributos.Tem(Protocol.Poder.Nav))
		{
			VBoxContainer desligado = Cartao("Nav System");
			Nota("O sistema de navegação está desligado: você não tem um Nav System na mochila. "
				 + "Ele se fabrica na bancada de pesquisa (aba Tech).", desligado);
			return;
		}

		bool noEspaco = Jandirus.Core.World.Espaco.EhEspaco(cli.Zone);

		_mapa = new MapaEstelar { Name = "MapaEstelar" };

		// A BARRA VEM ANTES DO MAPA na arvore, e isso e proposital.
		//
		// O mapa e alto (a carta so serve grande) e a aba mora num ScrollContainer: com a barra
		// EMBAIXO ela caia fora da area visivel e so aparecia depois de rolar -- os controles de
		// zoom ficavam escondidos justamente na tela que existe pra dar zoom. Em cima, eles sao a
		// primeira coisa que se ve.
		//
		// O MAPA APARECE EM TERRA FIRME TAMBEM -- so o botao de viajar e que nao. Ele e uma CARTA:
		// consultar antes de decolar e metade do uso de uma carta estelar, e esconder o mapa de quem
		// esta no chao obrigaria a subir pra descobrir aonde subir. O titulo do cartao diz isso.
		//
		// A barra mora no CARTAO da carta (titulo + botoes). Quando a tela do sistema entra, some SO A
		// BARRA -- "+/-/ver tudo" sao da carta --, e o cartao fica, com o titulo: e o que a etiqueta
		// "CARTA ESTELAR" de antes ja fazia, e a `--diagnav` conta com isso -- a familia C dela empurra a
		// roda no topo da pagina, que tem que ser um pedaco FORA dos dois mapas, e ela troca a tela do
		// sistema pela carta escrevendo `Visible` direto (sem passar pelo `PediuVoltar`). Com o cartao
		// inteiro escondido, o mapa subia ate o topo e a familia ficava sem ponto pra medir.
		VBoxContainer cartaoDaCarta = Cartao(noEspaco ? "Carta estelar" : "Carta estelar (em terra: só leitura)");
		var barra = new HBoxContainer();
		barra.AddThemeConstantOverride("separation", 4);
		foreach ((string rotulo, Action acao, string dica) in new (string, Action, string)[]
		{
			("+", () => _mapa.Zoom(1.6f), "aproximar (roda do mouse pra cima)"),
			("-", () => _mapa.Zoom(1f / 1.6f), "afastar (roda do mouse pra baixo)"),
			("centralizar em mim", () => _mapa.VerMim(), "poe voce no meio, num zoom de vizinhanca"),
			("ver tudo", () => _mapa.VerTudo(), "enquadra os mundos com mapa proprio"),
		})
		{
			var b = new Button { Text = rotulo, TooltipText = dica };
			Action fazer = acao;
			b.Pressed += () => fazer();
			barra.AddChild(b);
		}
		cartaoDaCarta.AddChild(barra);
		_conteudo.AddChild(_mapa);

		// ============================ A TELA DO SISTEMA MORA AQUI DO LADO ============================
		// Ela e IRMA do mapa e nasce escondida, em vez de ser criada no primeiro duplo clique. Duas
		// razoes, e as duas ja custaram caro neste projeto:
		//
		//   * criar node dentro do tratador do clique remonta o layout do `ScrollContainer` no meio
		//     do evento, e o mapa perde o zoom e o arrasto -- o mesmo motivo que ja mantem o painel
		//     do destino fora da remontagem da aba;
		//   * as duas telas compartilham o painel do destino embaixo. Trocar VISIBILIDADE deixa o
		//     painel intacto; trocar NODE obrigaria a religar os eventos dele toda vez.
		// ==========================================================================================
		_sistema = new TelaDoSistema { Name = "TelaDoSistema", Visible = false };
		_conteudo.AddChild(_sistema);

		// O PAINEL DO DESTINO fica FORA da remontagem da pagina: ele muda a cada clique no mapa, e
		// remontar a aba inteira a cada clique jogaria fora o zoom e o arrasto que o jogador acabou
		// de ajustar. Por isso o mapa avisa por evento e so este pedaco se refaz.
		var painel = new VBoxContainer();
		_conteudo.AddChild(painel);

		void Repintar() => DesenharDestino(painel, noEspaco);

		_mapa.SelecaoMudou += Repintar;
		_mapa.PediuViagem += p => Viajar(p, noEspaco);
		_mapa.PediuSistema += s =>
		{
			_sistema.Mostrar(s);
			_mapa.Visible = false;
			barra.Visible = false;   // "+/-/ver tudo" sao da carta; a tela do sistema tem os dela
			_sistema.Visible = true;
			Repintar();
		};

		_sistema.SelecaoMudou += Repintar;
		_sistema.PediuViagem += p => Viajar(p, noEspaco);
		_sistema.PediuPorto += p => ViajarAoPonto(p, noEspaco);
		_sistema.PediuVoltar += () =>
		{
			_sistema.Visible = false;
			_mapa.Visible = true;
			barra.Visible = true;
			Repintar();
		};

		Repintar();

		// ============================ AS LEGENDAS, NUM CARTAO ============================
		// As tres frases de como se usa a carta e, embaixo delas, as duas legendas que sobraram do
		// bloco de botoes da nave -- sobraram porque NAO SAO ACOES: sao legenda desta carta, e legenda
		// mora embaixo do mapa.
		//
		// A PRIMEIRA das duas explica por que a bolinha do mapa mudou de dono -- sem ela, quem pediu
		// Observar no console da ponte fica olhando pra um ponto e achando que e ele mesmo. A SEGUNDA e
		// a ESCALA da carta: "1.608.035 px" nao diz nada, "7 dias" diz tudo, e o numero da nave ao lado
		// e o que transforma comprar velocidade numa decisao.
		// ================================================================================
		VBoxContainer legenda = Cartao("Legenda");
		Nota("Cada pontinho e um SISTEMA. Clique pra selecionar, DUPLO CLIQUE pra abrir o mapa do "
			 + "sistema -- estrela no centro, os mundos nos aneis de orbita. Arraste pra mover, roda pra zoom.", legenda);
		Nota("A cor do ponto e a classe da estrela, medida na propria arte dela. Aproximando, os mundos "
			 + "aparecem: o laranja tem mapa proprio, o azul-acinzentado e gerado. Duplo clique num MUNDO viaja.", legenda);
		Nota("A viagem leva o tempo que diz: o piloto anda no passo normal, nao teleporta. "
			 + "Terra a Namek sao 7 dias in-game, como no anime.", legenda);

		if (cli.NaveVista is { } nv)
			Nota($"A carta está centrada na NAVE (em {nv.Zona}, casco {nv.CascoPct:0}%), e não em você. "
				 + "Ela volta a mostrar você quando desembarcar.", legenda)
				.AddThemeColorOverride("font_color", Tema.Destaque);

		double dTN = Jandirus.Core.World.Espaco.DistanciaTerraNamek;
		Nota($"A pé, Terra→Namek leva {Jandirus.Core.World.Espaco.SegundosDeViagem(dTN) / 60:0} min "
			 + $"({Jandirus.Core.World.Espaco.DiasInGame(dTN):0.#} dias in-game). "
			 + $"Numa Spacepod no limite ({Jandirus.Core.Tech.Naves.VelocidadeMaxima}x), "
			 + $"{Jandirus.Core.Tech.Naves.SegundosDeViagem(dTN, Jandirus.Core.Tech.Naves.VelocidadeMaxima) / 60:0.#} min.", legenda);
	}

	// ============================ O BLOCO DE BOTOES DA NAVE FOI DELETADO DAQUI ============================
	// Eram OITO botoes (entrar/sair, lancar, melhorar, ver estado, largar o leme, observar, desembarcar,
	// recondicionar) e os oito eram INTERACAO com um objeto, e nao decisao de menu. O dono disse isso com
	// todas as letras: *"a MAIORIA deles nem eram pra ser verbs do menu, e sim INTERACAO com as naves (ao
	// apertar E perto delas...)"*. Todos os oito ja viviam tambem no `Interacoes`, ou seja: eram a segunda
	// porta pra mesma coisa, que e o defeito que este repo mais paga.
	//
	// ELES ESTAVAM AQUI POR UM MOTIVO REAL, e o motivo foi RESOLVIDO em vez de ignorado: a nave PILOTADA
	// sai da lista de construcoes da zona, entao a tecla E nao tinha alvo nenhum pra oferecer ao piloto --
	// nem pra descer. Hoje o servidor diz qual veiculo esta embaixo de voce (`Protocol.S2C.Veiculo`) e o
	// menu da tecla E o alcanca como alcanca a macieira. Ver `Interacoes.DoVeiculo` e `MenuDeInteracao`.
	//
	// O QUE SOBROU sao as duas LEGENDAS (a nave observada e a escala Terra->Namek): elas nao sao acoes, e
	// por isso desceram pra debaixo da carta, no cartao "Legenda" do fim da `AbaNav`, que e onde legenda
	// de mapa mora.
	// ====================================================================================================

	/// <summary>O mapa da aba Nav. Guardado pra os botoes da barra alcancarem a camera dele.</summary>
	private MapaEstelar _mapa = null!;

	/// <summary>A tela do sistema, irma do mapa. Ver `AbaNav`.</summary>
	private TelaDoSistema _sistema = null!;

	/// <summary>O mapa vivo, ou nulo se a aba Nav ainda nao foi montada. SO PRA BANCADA (`--diagnav`).</summary>
	public MapaEstelar? MapaDeTeste => IsInstanceValid(_mapa) ? _mapa : null;

	/// <summary>A tela do sistema viva, ou nulo. SO PRA BANCADA (`--diagnav`).</summary>
	public TelaDoSistema? SistemaDeTeste => IsInstanceValid(_sistema) ? _sistema : null;

	/// <summary>
	/// O QUE ESTA SELECIONADO: nome, distancia, tempo de viagem, e o botao que liga o piloto.
	///
	/// Refeito sozinho a cada clique no mapa -- e so ele, nao a aba.
	/// </summary>
	private void DesenharDestino(VBoxContainer painel, bool noEspaco)
	{
		foreach (Node n in painel.GetChildren()) { painel.RemoveChild(n); n.QueueFree(); }

		// DE QUAL TELA VEM O DESTINO: a que esta visivel. Ler sempre do mapa faria o painel mostrar
		// o ultimo planeta clicado NA GALAXIA enquanto o jogador clica dentro de um sistema -- e o
		// botao "Viajar" mandaria pro corpo errado, calado.
		bool dentroDoSistema = IsInstanceValid(_sistema) && _sistema.Visible;
		Jandirus.Core.World.PlanetaNoEspaco? alvo =
			dentroDoSistema ? _sistema.Selecionado : _mapa.Selecionado;

		if (alvo is not { } p)
		{
			var vazio = new Label
			{
				Text = dentroDoSistema
					? "clique num mundo do sistema pra ver a ficha dele."
					: _mapa.VendoProcedurais
						? "nenhum destino selecionado."
						: "nenhum destino selecionado. Aproxime pra os mundos gerados aparecerem.",
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			};
			vazio.AddThemeColorOverride("font_color", Tema.TextoFraco);
			vazio.AddThemeFontSizeOverride("font_size", 12);
			painel.AddChild(vazio);
			return;
		}

		// A POSICAO DE GALAXIA, e nao a do corpo -- ver `MapaEstelar.MinhaPosicaoNaGalaxia`.
		// Pousado, a coordenada do corpo e de superficie e o "X dias daqui" sairia de um ponto
		// que nao existe no mapa.
		Vector2? eu = MapaEstelar.MinhaPosicaoNaGalaxia();
		var titulo = new Label { Text = p.Nome };
		titulo.AddThemeColorOverride("font_color", Tema.Destaque);
		painel.AddChild(titulo);

		var info = new Label
		{
			Text = FichaDoPlaneta(p)
				 + (eu is { } meu
					? $"   ·   {Jandirus.Core.World.Espaco.DiasInGame((new Vector2(p.Pos.X, p.Pos.Y) - meu).Length()):0.0} dias in-game"
					+ $" ({Jandirus.Core.World.Espaco.SegundosDeViagem((new Vector2(p.Pos.X, p.Pos.Y) - meu).Length()) / 60:0} min reais)"
					: ""),
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		info.AddThemeColorOverride("font_color", Tema.TextoFraco);
		info.AddThemeFontSizeOverride("font_size", 12);
		painel.AddChild(info);

		var linha = new HBoxContainer();
		var viajar = new Button
		{
			Text = $"Viajar para {p.Nome}",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			// SO NO ESPACO. Viajar e voar entre mundos: em terra firme o piloto automatico andaria
			// contra a parede do planeta. Continua APARECENDO, apagado, porque saber que o destino
			// existe e que falta decolar e informacao -- sumir com o botao seria esconder o jogo.
			Disabled = !noEspaco,
			TooltipText = noEspaco
				? "liga o piloto automatico. Qualquer tecla de movimento desliga."
				: "so no espaco. Use 'Decolar' (aba Other) pra subir.",
		};
		Jandirus.Core.World.PlanetaNoEspaco destino = p;
		viajar.Pressed += () => Viajar(destino, noEspaco);
		linha.AddChild(viajar);

		if (World.Instancia?.DestinoDoPiloto != null)
		{
			var parar = new Button { Text = "Parar", TooltipText = "desliga o piloto automatico" };
			parar.Pressed += () =>
			{
				World.Instancia?.SoltarPiloto();
				Chat.Sistema("piloto automatico desligado.");
			};
			linha.AddChild(parar);
		}
		painel.AddChild(linha);
	}

	/// <summary>
	/// O QUE SE SABE DO MUNDO ANTES DE IR: superficie, bioma e GRAVIDADE.
	///
	/// ============================ A GRAVIDADE NAO E ENFEITE ============================
	/// O `Chronology.dm` e explicito: "O Nav System mostra a gravidade dos planetas antes de
	/// pousar" -- e o motivo e que acima da sua maestria de gravidade o planeta te ESMAGA (anda
	/// devagar, o corpo todo toma dano, muito acima desmaia, absurdamente acima explode). Um
	/// planeta gerado pode ter ate 80x.
	///
	/// Sem este numero na tela, escolher destino no mapa e apostar. Com ele, a carta estelar faz o
	/// que uma carta faz: avisa onde nao se atracar.
	/// ==================================================================================
	///
	/// Os dois lados saem de funcao PURA -- a tabela dos pre-feitos (`planetas.json`, o mesmo
	/// arquivo que o servidor le) e a seed do gerado. Nenhum pacote de rede.
	/// </summary>
	private static string FichaDoPlaneta(Jandirus.Core.World.PlanetaNoEspaco p)
	{
		var zona = p.Premade
			? Jandirus.Core.World.ZoneKey.Premade(p.Nome)
			: Jandirus.Core.World.ZoneKey.Procedural(p.Nome, p.Seed);

		// O CEU DE LA, DAQUI. A hora de cada planeta e funcao pura do relogio do mundo mais a
		// ficha dele, entao a carta consegue dizer "em Namek e dia" sem pedir nada ao servidor --
		// e saber que o destino esta em plena noite de lua cheia e informacao de viagem.
		Jandirus.Core.World.RelogioDoPlaneta r = Planetas.Relogio(zona);
		string ceu = Ceu(r);

		if (!p.Premade)
		{
			Jandirus.Core.World.MundoProcedural m = Jandirus.Core.World.MundoProcedural.DaSeed(p.Seed, p.Nome);
			return $"mundo gerado · {m.Bioma} · gravidade {m.Gravidade:0.##}x · {ceu}";
		}
		double g = Planetas.Catalogo?.De(p.Nome).Gravidade ?? 1;
		return $"mundo com superficie -- da pra pousar · gravidade {g:0.##}x · {ceu}";
	}

	/// <summary>Que horas sao naquele mundo agora, e em que fase a lua dele esta.</summary>
	private static string Ceu(Jandirus.Core.World.RelogioDoPlaneta r)
	{
		if (GameClient.Instance is not { TempoChegou: true } cli)
			return Jandirus.Core.World.Ceu.NomeDoCiclo(r);

		Jandirus.Core.World.EstadoDoCeu e = Jandirus.Core.World.Ceu.De(r, cli.TempoDoMundo);
		string hora = Jandirus.Core.World.Ceu.NomeDaHora(e.Hora);
		return e.LuaNoCeu ? $"{hora}, {Jandirus.Core.World.Ceu.NomeDaFase(e.Fase)}" : hora;
	}

	private void Viajar(Jandirus.Core.World.PlanetaNoEspaco p, bool noEspaco)
	{
		if (!noEspaco) { Chat.Sistema("voce precisa estar no espaco pra viajar."); return; }
		World.Instancia?.Pilotar(new Vector2(p.Pos.X, p.Pos.Y));
		Chat.Sistema($"rumo a {p.Nome}. Qualquer tecla de movimento desliga o piloto.");
		Fechar();
	}

	/// <summary>
	/// VIAJAR PRO PORTO DE UM SISTEMA -- um ponto, e nao um corpo.
	///
	/// O `SistemaSolar.PortoDeEntrada` fica na orbita interna, do lado OPOSTO ao do primeiro mundo,
	/// justamente pra nao coincidir com nada. Ele existe pra quem quer "ir ate ali" antes de
	/// escolher em qual mundo pousar.
	///
	/// AVISO QUE E HONESTO DAR: a chegada e um lugar VAZIO. O corpo mais proximo fica a 900 px ou
	/// mais e a janela de mundo mostra 384x216 px -- quem chegar la vai ver espaco preto e achar
	/// que a viagem falhou. Por isso a mensagem diz o que fazer em seguida.
	/// </summary>
	private void ViajarAoPonto(Vector2 destino, bool noEspaco)
	{
		if (!noEspaco) { Chat.Sistema("voce precisa estar no espaco pra viajar."); return; }
		World.Instancia?.Pilotar(destino);
		Chat.Sistema("rumo ao porto do sistema. A chegada e um ponto vazio -- abra o mapa do sistema "
					 + "(aba Nav) pra escolher o mundo. Qualquer tecla de movimento desliga o piloto.");
		Fechar();
	}

	/// <summary>Distancia em TEMPO, que e como o anime mede: dias in-game e minutos reais.</summary>
	private static string Tempo(double dias, double min) =>
		dias >= 1 ? $"{dias:0.0} dias in-game ({min:0} min reais)"
		: $"{dias * 24:0.0} h in-game ({min:0.0} min reais)";

	/// <summary>
	/// O PEDACO DESTA ABA NA ASSINATURA DE CACHE (ver `MenuJogo.Assinatura`): tudo que ESTA aba
	/// desenha e que a assinatura basica (em MenuJogo.cs) nao cobre entra aqui, nos mesmos
	/// arredondamentos em que e desenhado.
	///
	/// VAZIO DE PROPOSITO: a assinatura da Nav e a mais cara de errar do menu (remontar joga fora o
	/// zoom e o arrasto do mapa), e o redesenho desta rodada foi so de moldura -- nenhum dado novo
	/// entrou na pagina. A legenda da nave observada (`NaveVista`) ja ficava de fora antes, e
	/// continua: ela muda quando se observa ou se desembarca, e a zona (que esta na basica) muda junto.
	/// </summary>
	private string ExtraDaAssinaturaDeNav(SheetState f) => "";
}
