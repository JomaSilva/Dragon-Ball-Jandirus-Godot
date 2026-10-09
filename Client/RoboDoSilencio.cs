using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O SILENCIO DO ESPACO, ANDADO (`--diagsilencio`) -- o corpo MUDA DE ZONA e o ouvido acompanha.
///
/// ============================ O PEDIDO DO DONO, LITERAL ============================
/// *"no espaco o jogo n tem som, SOMENTE A MUSICA de combate. efeitos sonoros de ki, soco etc N
/// EXISTEM, a nao ser q estejam DENTRO DE UMA NAVE como a capital ship. mas no espaco em si, usando
/// roupa espacial ou sem ela, N TEM SOM, somente a OST"*.
/// ==================================================================================
///
/// ============================ POR QUE ELA EXISTE, SE A `--diagtrilha` JA MEDE O VACUO ============================
/// A `RoboDeTrilha.OSilencioDoEspaco` liga e desliga o vacuo **na mao** (`AudioDirector.Vacuo(true)`)
/// e mede o barramento. Isso responde uma pergunta e so uma: *"o corte, quando pedido, funciona?"*.
///
/// A pergunta que o dono faz e outra: *"ao ENTRAR no espaco, ele e pedido?"*. E essa nao se responde
/// chamando o corte -- ela se responde ANDANDO ate la. Entre as duas moram o `World.CarregarZona` (que
/// tem cinco caminhos terminando em `return`, e o ramo do espaco e um deles), o `MoveToZone`, o pacote
/// de troca de zona e a ordem em que tudo isso acontece. Uma bancada que chama `Vacuo(true)` fica
/// VERDE num jogo em que ninguem nunca chama `Vacuo` coisa nenhuma.
///
/// Esta bancada faz o servidor mudar o corpo de zona -- planeta, espaco, dentro da nave-capital,
/// planeta de novo -- e pergunta ao `AudioServer` em cada parada.
/// ================================================================================================================
///
/// ============================ O PAR QUE SE SEGURA, E ELE E O CORACAO DISTO ============================
/// *"o soco nao faz som"* sozinho fica verde com o jogo INTEIRO mudo -- e o jogo inteiro mudo e o
/// conserto preguicoso (mutar o `Master`). Entao toda parada no vacuo mede as duas coisas juntas:
///
///   * o EFEITO cala **e** o efeito continua sendo PEDIDO (o `AudioDirector.Espiao` dispara). Um
///     soco que nunca foi tocado tambem "nao faz som", e nao e isso que o dono pediu;
///   * a MUSICA de combate TOCA, de verdade -- `TocandoDeTeste` le os dois `AudioStreamPlayer` da
///     trilha, e nao um campo de intencao.
/// ======================================================================================================
///
/// ============================ E A REGRESSAO MAIS PROVAVEL TEM PARADA PROPRIA ============================
/// *"fora do espaco tudo soa normal"*. O defeito que este sistema pode ter e ficar LIGADO: o jogador
/// pousa e continua sem ouvir soco nenhum, para sempre, e nada aponta pro espaco. Por isso a rodada
/// termina de volta num planeta, e nao no vacuo -- a ultima parada e a que ninguem lembra de testar.
/// ========================================================================================================
///
/// ============================ O PALCO DO ESPACO E O PONTO DE DECOLAGEM ============================
/// A rota manda o corpo pro espaco com o `MoveToZone` do servidor, e o ONDE importa: la em cima
/// encostar num disco e pousar (`GameServer.TickDoEspaco`). A chegada e a de toda subida de producao
/// -- `Espaco.PontoDeDecolagem` do planeta de casa, 90 px fora da borda do disco, o ponto do
/// `GameServer.Decolar`. Ver <see cref="IrProEspaco"/>, que conta o que acontecia fora dele.
///
/// CONTRA-EXEMPLO: `--palconodisco` poe a chegada no CENTRO do disco (<see cref="PalcoNoDiscoDeTeste"/>,
/// o palco de antes) e a rodada fecha em SEM MEDIDA dizendo a rota que o servidor mandou.
/// ==================================================================================================
///
/// Roda com `--host` e **serve no `--headless`**: aqui nao ha pixel, ha barramento.
///
///     Godot --headless --path . --host --rede 7904 --diagsilencio \
///           --raca Human --conta bancada_silencio --nome Silencio
/// </summary>
public partial class RoboDoSilencio : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S =>
		Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	private readonly List<string> _passos = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _semMedida = [];
	private int _verdes;

	private bool _acabou;
	private double _t, _vida;
	private int _passo;

	private const double Paciencia = 180;

	/// <summary>Onde o corpo comecou. A ultima parada volta pra ca -- ver o cabecalho.</summary>
	private ZoneKey _casa;
	private Vec2 _ondeEmCasa;
	private bool _temCasa;

	/// <summary>Quantos efeitos POSICIONADOS foram pedidos desde o ultimo zero. Ver o `Espiao`.</summary>
	private int _pedidos;

	/// <summary>
	/// DEFEITO INJETADO (bancada, `--palconodisco`): o palco de ANTES -- a chegada no espaco cai no CENTRO
	/// do disco do planeta de casa, em vez de no ponto de decolagem dele. Era o `new Vec2(0, 0)` que esta
	/// bancada mandava, e `(0, 0)` e o centro da Terra. Sempre falso sem a chave. Ver <see cref="IrProEspaco"/>.
	/// </summary>
	private static readonly bool PalcoNoDiscoDeTeste =
		Array.IndexOf(OS.GetCmdlineArgs(), "--palconodisco") >= 0;

	/// <summary>Onde o corpo foi posto no espaco. Ver <see cref="IrProEspaco"/>.</summary>
	private Vec2 _chegada;

	/// <summary>
	/// O QUE O SERVIDOR DISSE DA MINHA ZONA DESDE O PEDIDO DO ESPACO: um nome por `ZoneChanged`, na
	/// ordem. E a palavra dele, pacote a pacote -- olhar `cli.Zone` uma vez por quadro perderia a ida e
	/// a volta que chegam na mesma leva de rede.
	/// </summary>
	private readonly List<string> _rota = [];

	/// <summary>De quem a rota esta sendo ouvida agora (nulo = de ninguem). Ver <see cref="OuvirARota"/>.</summary>
	private GameClient? _ouvido;

	// (metodo NOMEADO e `-=`, nunca lambda: lambda nao se cancela)
	private void AnotarARota(ZoneKey z, Vec2 onde) => _rota.Add(z.Name);

	/// <summary>Passa a ouvir os `ZoneChanged` deste cliente -- ou para de ouvir, com nulo.</summary>
	private void OuvirARota(GameClient? cli)
	{
		if (_ouvido != null && IsInstanceValid(_ouvido)) _ouvido.ZoneChanged -= AnotarARota;
		_ouvido = cli;
		if (cli != null) cli.ZoneChanged += AnotarARota;
	}

	public override void _ExitTree() => OuvirARota(null);

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (ok) _verdes++; else _falhas.Add(oque);
	}

	private void NaoMediu(string oque)
	{
		_passos.Add("  --      SEM MEDIDA  " + oque);
		_semMedida.Add(oque);
	}

	private void Nota(string oque) => _passos.Add("  --     " + oque);

	public override void _Ready()
	{
		// ESTE MUNDO E MEU? Mesma recusa do `RoboDeTrilha`, e aqui ela pesa mais: esta bancada MOVE
		// o corpo pro espaco (onde se sufoca) e pra dentro de uma nave. Fazer isso com o personagem
		// de outra sessao seria estragar o jogo de alguem pra medir o meu.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0
			&& Jandirus.Server.GameServer.Instance is { Running: false })
		{
			_acabou = true;
			GD.PrintErr("[silencio] RECUSADO: subi com `--host` mas a porta ja estava tomada -- este "
					  + "mundo e de outra sessao. Nada foi forcado. Suba com `--rede <outra porta>`.");
			return;
		}

		// O ESPIAO E A UNICA PROVA DE QUE O SOM FOI **PEDIDO**. Sem ele, "nao ouvi nada" seria
		// indistinguivel de "nada foi tocado" -- e o segundo nao e o pedido do dono.
		AudioDirector.Espiao += (_, _) => _pedidos++;
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli) return;
		if (S is not { } srv) { Nota("sem servidor no processo (`--diagsilencio` precisa de `--host`)"); Fechar(); return; }
		if (AudioDirector.Instance is not { } audio) return;

		_vida += delta;
		if (_vida > Paciencia) { Nota($"acabou a paciencia ({Paciencia:0} s)"); Fechar(); return; }

		_t += delta;

		switch (_passo)
		{
			case 0: Assentar(cli); break;
			case 1: EmTerraFirme(cli, audio); break;
			case 2: IrProEspaco(cli, srv); break;
			case 3: NoVacuo(cli, audio); break;
			case 4: EntrarNaNave(cli, srv); break;
			case 5: DentroDaNave(cli, audio); break;
			case 6: VoltarProPlaneta(cli, srv); break;
			case 7: DeVoltaEmTerraFirme(cli, audio); break;
			default: Fechar(); break;
		}
	}

	private void Virar(int proximo) { _passo = proximo; _t = 0; }

	// =====================================================================
	// 0) ASSENTAR -- e guardar de onde se saiu
	// =====================================================================
	private void Assentar(GameClient cli)
	{
		if (_t < 3) return;
		if (World.Instancia?.PosicaoLocal is not { } pe) return;

		// O PONTO E O DO CORPO, e nao `(0, 0)`: a volta pra casa pousa onde o jogo o pos ao nascer (chao
		// que o berco ja conferiu), e nao na quina do mapa. Ver `IrProEspaco`, onde o outro `(0, 0)`
		// desta bancada custou a rodada inteira.
		_casa = cli.Zone;
		_ondeEmCasa = new Vec2(pe.X, pe.Y);
		_temCasa = true;

		// A CASA NAO PODE SER O ESPACO -- se o berco nascesse no vacuo, a primeira parada (que afirma
		// "em terra firme os efeitos tocam") mediria o vacuo e ficaria vermelha por enquadramento.
		if (Espaco.EhEspaco(_casa))
		{
			NaoMediu($"o berco nasceu NO ESPACO (`{_casa.Name}`) -- nao ha terra firme pra comparar");
			Fechar();
			return;
		}

		Nota($"de pe em `{_casa.Name}`. A rota: planeta -> espaco -> dentro da nave -> planeta.");
		Virar(1);
	}

	// =====================================================================
	// 1) EM TERRA FIRME O SOM E NORMAL
	// =====================================================================
	private void EmTerraFirme(GameClient cli, AudioDirector audio)
	{
		if (_t < 0.5) return;

		// O ESTADO DE PARTIDA IMPORTA: com o controle deslizante em zero os efeitos ja estariam
		// mudos, e TODA linha abaixo passaria sem que o vacuo fizesse nada. Volume cheio antes.
		audio.AplicarVolumes(new Settings { VolumeGeral = 1f, VolumeMusica = 1f, VolumeEfeitos = 1f,
											VolumeAmbiente = 1f, VolumeVoz = 1f });

		Conferir(!AudioDirector.EfeitosMudosDeTeste,
				 $"1. em `{_casa.Name}` (terra firme) os EFEITOS tocam");
		Conferir(!AudioDirector.Instance!.NoVacuoDeTeste,
				 "1. e o jogo nao se acha no vacuo num planeta");

		Socar(cli);
		Conferir(_pedidos > 0, $"1. o soco foi PEDIDO pela via de producao ({_pedidos} efeito(s))");

		Virar(2);
	}

	// =====================================================================
	// 2) IR PRO ESPACO -- pelo servidor, e nao chamando `Vacuo`
	// =====================================================================
	/// <summary>
	/// ============================ O PONTO DE CHEGADA E O DA DECOLAGEM, E NAO `(0, 0)` ============================
	/// Esta parada mandava o corpo pra `new Vec2(0, 0)`, e `(0, 0)` do espaco nao e um ponto qualquer do
	/// vazio: e o CENTRO DO DISCO DA TERRA (`Espaco.PreFeitos`, raio 220). La em cima encostar num disco
	/// e pousar -- `GameServer.TickDoEspaco` pergunta `Espaco.PlanetaSob` a 30 Hz --, entao o servidor
	/// descia o corpo de volta pra `Earth` no tique seguinte. MEDIDO em 2026-10-09 (duas rodadas): o log
	/// do servidor diz `Silencio pousou em Earth` antes de o cliente terminar de carregar o espaco, e a
	/// rodada fechava com 3 das 17 provas e "o cliente nunca chegou no espaco".
	///
	/// O jogo nao poe ninguem ali. Toda subida de producao chega em `Espaco.PontoDeDecolagem` -- o
	/// `GameServer.Decolar` (romper a atmosfera voando, a habilidade `decolar`, a nave com piloto), a nave
	/// que sobe sozinha e a evacuacao de um mundo que morre --, e ele fica 90 px ACIMA da borda do disco,
	/// *"fora do raio de proposito"*: o comentario de la descreve este mesmo defeito. A bancada chega pelo
	/// mesmo ponto, com o mesmo `MoveToZone`.
	///
	/// ============================ E SE O SERVIDOR ME TIRAR DE LA, ELA DIZ ============================
	/// "Nunca chegou" era falso: o corpo chegava e era tirado. A parada escuta os `ZoneChanged` desde o
	/// pedido (<see cref="_rota"/>) e, se a ultima palavra do servidor nao for o espaco, fecha na hora com
	/// a rota que ele mandou e com a resposta do Core pra "esse ponto esta em cima de um disco?" -- a
	/// MESMA pergunta do tique. O contra-exemplo e <see cref="PalcoNoDiscoDeTeste"/>.
	/// ===============================================================================================
	/// </summary>
	private void IrProEspaco(GameClient cli, Jandirus.Server.GameServer srv)
	{
		if (!_pediuEspaco)
		{
			_pediuEspaco = true;

			if (srv.CorpoDaZona(_casa) is not { } planeta)
			{
				NaoMediu($"`{_casa.Name}` nao esta na carta estelar -- nao ha ponto de decolagem pra chegar no espaco");
				Fechar();
				return;
			}

			_chegada = PalcoNoDiscoDeTeste ? planeta.Pos : Espaco.PontoDeDecolagem(planeta);
			string onde = PalcoNoDiscoDeTeste
				? "(defeito injetado: palco em cima do disco) no CENTRO do disco"
				: "no ponto de decolagem";

			OuvirARota(cli);
			srv.MoveToZone(cli.LocalId, srv.ZonaDoEspaco, _chegada);
			Nota($"o SERVIDOR me mandou pro espaco (`MoveToZone`), {onde} de `{planeta.Nome}` "
			   + $"({_chegada.X:0}; {_chegada.Y:0}) -- ninguem chamou `Vacuo`");
			return;
		}

		if (Espaco.EhEspaco(cli.Zone))
		{
			if (_t < 1) return;   // um quadro de folga pro `CarregarZona` terminar
			OuvirARota(null);
			Virar(3);
			return;
		}

		// O SERVIDOR JA FALOU DE ZONA DEPOIS DO PEDIDO, E A ULTIMA PALAVRA NAO E O ESPACO: ele me tirou
		// de la. Nao ha o que esperar -- e os 15 s de antes terminavam sem dizer o porque.
		if (_rota.Count > 0)
		{
			string porque = Espaco.PlanetaSob(srv.SeedDoUniverso, _chegada) is { } disco
				? $"o ponto de chegada ({_chegada.X:0}; {_chegada.Y:0}) esta DENTRO do disco de `{disco.Nome}` "
				  + $"(raio {disco.Raio:0}), e la em cima encostar e pousar (`TickDoEspaco`)"
				: $"o ponto de chegada ({_chegada.X:0}; {_chegada.Y:0}) e espaco aberto -- quem tirou o corpo "
				  + "de la foi outra regra do servidor (veja o log dele)";
			NaoMediu($"o servidor me tirou do espaco em {_t:0.00} s (rota: {string.Join(" -> ", _rota)}): {porque}");
			Fechar();
			return;
		}

		if (_t > 15)
		{
			NaoMediu($"o `ZoneChanged` do espaco nunca chegou -- o cliente segue em `{cli.Zone.Name}`");
			Fechar();
		}
	}

	private bool _pediuEspaco;

	// =====================================================================
	// 3) NO VACUO: o soco cala, a OST toca
	// =====================================================================
	private void NoVacuo(GameClient cli, AudioDirector audio)
	{
		Conferir(AudioDirector.EfeitosMudosDeTeste,
				 "2. no ESPACO os efeitos calam -- e ninguem chamou `Vacuo` na mao");

		// ---------- o par que se segura: o soco FOI pedido, e mesmo assim nao sai ----------
		_pedidos = 0;
		Socar(cli);
		Conferir(_pedidos > 0,
				 $"2. o soco continua sendo PEDIDO no vacuo ({_pedidos}) -- o silencio e do "
			   + "BARRAMENTO, e nao de o jogo ter parado de tocar");
		Conferir(AudioDirector.EfeitosMudosDeTeste,
				 "2. ...e mesmo assim ele nao sai (o barramento de Efeitos esta mudo)");

		// ---------- a OST: a UNICA coisa que o dono quer ouvir la ----------
		string faixa = Trilha.Combate();
		if (faixa.Length == 0) NaoMediu("a pasta `battle ost` esta vazia -- sem OST pra provar");
		else
		{
			audio.Musica(faixa, AudioDirector.Camada.Combate, "bancada do silencio");
			int iMus = AudioServer.GetBusIndex(AudioDirector.BusMusica);
			int iMaster = AudioServer.GetBusIndex("Master");

			Conferir(iMus >= 0 && !AudioServer.IsBusMute(iMus),
					 "2. a MUSICA continua audivel no vacuo (o barramento dela nao foi tocado)");
			Conferir(iMaster >= 0 && !AudioServer.IsBusMute(iMaster),
					 "2. o corte NAO foi no `Master` (que levaria a OST junto)");
			Conferir(audio.TocandoDeTeste,
					 $"2. e a OST de combate esta TOCANDO mesmo ({faixa.GetFile()})");
		}

		// ---------- a roupa espacial nao devolve o som ----------
		// Ela nao tem uma linha sequer no caminho do audio, e essa AUSENCIA e a regra: a conta so
		// pergunta pela ZONA. Escrito como prova pra que "acrescentar o traje na conta" reprove aqui.
		Conferir(Espaco.EhEspaco(cli.Zone) && AudioDirector.EfeitosMudosDeTeste,
				 "2. com traje ou sem, o silencio e o mesmo -- a conta so pergunta pela ZONA");

		// ---------- e mexer no volume LA DENTRO nao ressuscita nada ----------
		audio.AplicarVolumes(new Settings { VolumeGeral = 1f, VolumeMusica = 1f, VolumeEfeitos = 0.9f,
											VolumeAmbiente = 1f, VolumeVoz = 1f });
		Conferir(AudioDirector.EfeitosMudosDeTeste,
				 "2. mexer no controle de EFEITOS dentro do vacuo NAO devolve o som");

		Virar(4);
	}

	// =====================================================================
	// 4-5) DENTRO DA NAVE-CAPITAL TEM SOM
	// =====================================================================
	/// <summary>
	/// *"a nao ser q estejam DENTRO DE UMA NAVE como a capital ship"*.
	///
	/// ============================ NAO HA UMA LINHA DE EXCECAO, E E ISSO QUE SE MEDE ============================
	/// O interior e `ZoneKey.Interior("Nave", id)` -- `Kind` de interior e nome `"Nave"` --, entao
	/// `Espaco.EhEspaco` responde FALSO e o vacuo desliga sozinho ao entrar. Nenhum `if` de nave foi
	/// escrito no audio.
	///
	/// A prova disso nao pode ser "olhei o codigo e nao tem `if`": tem que ser o corpo LA DENTRO com o
	/// barramento audivel. Se um dia alguem alargar a pergunta de zona (o defeito classico: o interior
	/// virar espaco), esta linha fica vermelha antes de o dono descobrir que a nave dele emudeceu.
	/// ==========================================================================================================
	/// </summary>
	private void EntrarNaNave(GameClient cli, Jandirus.Server.GameServer srv)
	{
		if (!_pediuNave)
		{
			_pediuNave = true;
			ZoneKey dentro = Jandirus.Core.Tech.NaveGrande.ZonaDoInterior(1);
			srv.MoveToZone(cli.LocalId, dentro,
						   Jandirus.Core.Tech.NaveGrande.PixelDe(Jandirus.Core.Tech.NaveGrande.CelDaChegada));
			Nota("o servidor me pos DENTRO da nave-capital (interior, zona propria)");
			return;
		}

		if (cli.Zone.Kind != ZoneKey.KindInterior)
		{
			if (_t > 15) { NaoMediu("o cliente nunca chegou no interior da nave"); Virar(6); }
			return;
		}

		if (_t < 1) return;
		Virar(5);
	}

	private bool _pediuNave;

	private void DentroDaNave(GameClient cli, AudioDirector audio)
	{
		Conferir(!Espaco.EhEspaco(cli.Zone),
				 $"3. o interior da nave NAO e espaco (`{cli.Zone}`) -- por isso nao precisa de excecao");
		Conferir(!AudioDirector.EfeitosMudosDeTeste,
				 "3. DENTRO da nave-capital os efeitos VOLTAM a tocar");

		_pedidos = 0;
		Socar(cli);
		Conferir(_pedidos > 0 && !AudioDirector.EfeitosMudosDeTeste,
				 $"3. e o soco soa la dentro ({_pedidos} efeito(s) pedidos, barramento audivel)");

		Virar(6);
	}

	// =====================================================================
	// 6-7) DE VOLTA AO PLANETA -- a regressao mais provavel
	// =====================================================================
	private void VoltarProPlaneta(GameClient cli, Jandirus.Server.GameServer srv)
	{
		if (!_pediuVolta)
		{
			_pediuVolta = true;
			if (!_temCasa) { NaoMediu("nao guardei a zona de casa"); Fechar(); return; }
			srv.MoveToZone(cli.LocalId, _casa, _ondeEmCasa);
			Nota($"de volta pra `{_casa.Name}`");
			return;
		}

		if (cli.Zone.Hash != _casa.Hash)
		{
			if (_t > 15) { NaoMediu("o cliente nunca voltou pro planeta"); Fechar(); }
			return;
		}

		if (_t < 1) return;
		Virar(7);
	}

	private bool _pediuVolta;

	private void DeVoltaEmTerraFirme(GameClient cli, AudioDirector audio)
	{
		Conferir(!AudioDirector.EfeitosMudosDeTeste,
				 $"4. de volta em `{cli.Zone.Name}` os efeitos tocam -- o vacuo NAO ficou ligado");

		_pedidos = 0;
		Socar(cli);
		Conferir(_pedidos > 0 && !AudioDirector.EfeitosMudosDeTeste,
				 $"4. e o soco soa de novo ({_pedidos} efeito(s) pedidos, barramento audivel)");

		// ============================ E O CONTRARIO TAMBEM ============================
		// Sair do vacuo nao pode devolver som pra quem ZEROU o proprio controle. Sem esta linha,
		// `AplicarEfeitos` poderia ignorar o volume e olhar so o vacuo -- e o jogador que desligou os
		// efeitos nas opcoes voltaria a ouvi-los ao pousar.
		// ==============================================================================
		audio.AplicarVolumes(new Settings { VolumeGeral = 1f, VolumeMusica = 1f, VolumeEfeitos = 0f,
											VolumeAmbiente = 1f, VolumeVoz = 1f });
		Conferir(AudioDirector.EfeitosMudosDeTeste,
				 "4. quem ZEROU o controle continua mudo fora do vacuo (as duas razoes, uma conta)");
		audio.AplicarVolumes(new Settings { VolumeGeral = 1f, VolumeMusica = 1f, VolumeEfeitos = 1f,
											VolumeAmbiente = 1f, VolumeVoz = 1f });

		Fechar();
	}

	// =====================================================================
	// O SOCO
	// =====================================================================
	/// <summary>
	/// UM BAQUE DE MELEE PELA VIA DE PRODUCAO -- <see cref="AudioDirector.EfeitoNoLugar"/>, o mesmo
	/// caminho que o combate usa, com o mesmo arquivo que a <see cref="Trilha"/> sorteia.
	///
	/// ============================ POR QUE A BANCADA NAO SOCA UM NPC DE VERDADE ============================
	/// Porque isso mediria o COMBATE (alcance, esquiva, cadencia, nocaute) pra responder uma pergunta
	/// de AUDIO, e reprovaria no dia em que a esquiva mudasse. O que esta bancada afirma e mais
	/// estreito e exato: *o som que o soco toca, tocado como o soco o toca, sai ou nao sai nesta zona*.
	///
	/// A prova de que o soco de verdade passa por aqui e do outro lado: `Trilha.Acerto` e o sorteio
	/// que o `RemotePlayer` consome, e `EfeitoNoLugar` e a unica porta posicional do audio.
	/// ======================================================================================================
	/// </summary>
	private void Socar(GameClient cli)
	{
		if (World.Instancia?.CorpoDeTeste(cli.LocalId) is not { } corpo) { NaoMediu("sem corpo pra socar"); return; }
		string som = Trilha.Acerto(1);
		if (som.Length == 0) { NaoMediu("a pasta de socos esta vazia"); return; }
		AudioDirector.EfeitoNoLugar(corpo, som);
	}

	private void Fechar()
	{
		if (_acabou) return;
		_acabou = true;
		OuvirARota(null);

		GD.Print("[silencio] ================ O SILENCIO DO ESPACO, ANDADO ================");
		foreach (string p in _passos) GD.Print("[silencio] " + p);
		GD.Print($"[silencio] ===== FIM: {_verdes} OK, {_falhas.Count} FALHA(S), "
			   + $"{_semMedida.Count} SEM MEDIDA =====");
		foreach (string f in _falhas) GD.PrintErr("[silencio] FALHA: " + f);
		foreach (string s in _semMedida) GD.PrintErr("[silencio] SEM MEDIDA: " + s);
	}
}
