using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O REGENERADOR E A MAQUINA DE GRAVIDADE NA TELA (`--diagmaquinas`).
///
/// ============================ OS PEDIDOS DO DONO (2026-10-09) ============================
/// *"as maquinas de regeneraçao nao estao funcionando (veja no DM como elas funcionavam) e verifique se a
/// maquina de gravidade tb esta funcionando"*, e as duas regras que vieram em seguida:
///
///   * *"pra ligar vc tem q apertar E e abrir o menu de interaçoes e apertar pra ligar, curando todos q
///     estiverem no mesmo tile da maquina"*;
///   * *"a area da gravidade deve se adaptar ao local q ta a maquina ... igual um liquido"*.
///
/// As contas inteiras ja sao cobradas sem janela pela `--maquinasteste`. O que so existe AQUI e o que o
/// jogador ve e aperta: o menu da tecla E com os botoes novos, o teclado numerico da gravidade, a campanula
/// do tanque ligado, a vida subindo na ficha, e o filtro vermelho do campo com a forma da sala.
/// =========================================================================================
///
/// ============================ DOIS ATOS, NUMA CORRIDA SO ============================
///   1. O TANQUE. Assentado ao lado, aparafusado PELO MENU; eu, ferido, subo nele: desligado, nada acontece.
///      Ligo PELO MENU: a campanula aparece por cima de mim, a vida sobe ate encher, a bateria gasta. Desligo
///      pelo menu e me firo de novo: a vida para -- a contraprova das mesmas reguas.
///   2. A GRAVIDADE. Maquina assentada, aparafusada pelo menu (o botao que faltava), alcance no teto e uma
///      sala 4x4 erguida em volta. Ajusto pra 10x no TECLADO: ela espera os cinco segundos, o campo enche a
///      sala e so a sala -- e a sala fica VERMELHA, chao, maquina e eu (o filtro e medido no pixel, dentro e
///      fora da parede). Saio ANDANDO pela porta e a gravidade me larga. O defeito injetado
///      (`CampoDeGravidade.AtravessaParedeDeTeste`) avermelha o lado de fora e me esmaga la -- e as mesmas
///      reguas reprovam.
/// ====================================================================================
///
/// COMO RODAR -- um processo so, com JANELA (no headless o `GetImage` volta vazio):
///
///     Godot --path . --host --rede 7993 --horateste 0.5 --campoteste 9 --diagmaquinas \
///           --position 1920,0 --resolution 1600x900 --raca Human --conta bancada_dmaquinas --nome Paciente
///
/// As fotos saem em `user://dmaquinas-*.png`. Comecar por `dmaquinas-0-historia.png`.
/// </summary>
public partial class RoboDasMaquinas : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;
	private static MenuDeInteracao? E => MenuDeInteracao.Instancia;

	private const int T = ZoneCollision.TileSize;

	/// <summary>O 0,12 das outras bancadas de foto: abaixo disso e ruido do viewport.</summary>
	private const float Epsilon = 0.12f;

	/// <summary>
	/// O LIMIAR DO FILTRO DO CAMPO. Ele tem 23% de opacidade (o icone do original), e sobre um pixel que ja
	/// era avermelhado a diferenca fica abaixo do 0,12 das outras reguas. As tres fotos sao do MESMO quadro
	/// parado (a arvore esta pausada), entao o ruido entre elas e zero e o limiar pode descer.
	/// </summary>
	private const float EpsilonDaTinta = 0.04f;

	private const double Paciencia = 200;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<TiraDeFotos.Quadro> _historia = [];
	private bool _acabou;
	private double _t, _vida;
	private int _passo, _sub;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok     " : "  FALHA  ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --     " + oque);

	public override void _Ready()
	{
		// A ARVORE E PAUSADA PRA FOTOGRAFAR (ver `Obturador`); sem isto a bancada congelaria junto.
		ProcessMode = ProcessModeEnum.Always;
	}

	public override void _ExitTree() => CampoDeGravidade.AtravessaParedeDeTeste = false;

	private const int PAssentar = 0,
					  // o ato do TANQUE
					  TIrAoLado = 1, TPlantar = 2, TParafuso = 3, TSubir = 4, TDesligado = 5, TLigar = 6, TLigado = 7,
					  TCurar = 8, TContraprova = 9, TVoltar = 10,
					  // o ato da GRAVIDADE
					  GPlantar = 20, GParafuso = 21, GSala = 22, GTeclado = 23, GEspera = 24, GCampo = 25, GSair = 26,
					  GDefeito = 27;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (S is not { } srv) { Nota("sem servidor no processo (`--diagmaquinas` precisa de `--host`)"); Fechar(); return; }

		_vida += delta;
		if (_vida > Paciencia) { Conferir(false, $"a bancada coube na paciencia ({Paciencia:0} s; parou no passo {_passo}.{_sub})"); Fechar(); return; }
		_t += delta;

		// O PRIMEIRO PULSO DEPOIS DE LIGAR, lido a cada quadro e nao no passo em que a bancada estiver: a foto do
		// tanque ligado leva meio segundo, e o pulso nao espera por ela.
		if (_ligouEm >= 0 && _subiuEm < 0 && cli.Sheet.HP > _vidaAoLigar + SubidaDeUmPulso / 2) _subiuEm = _vida - _ligouEm;

		switch (_passo)
		{
			case PAssentar: Assentar(srv, cli); break;

			case TIrAoLado: IrAoLadoDoTanque(mundo, srv, cli); break;
			case TPlantar: PlantarOTanque(mundo, srv, cli); break;
			case TParafuso: AparafusarOTanque(mundo, srv); break;
			case TSubir: SubirNoTanque(mundo, srv, cli); break;
			case TDesligado: Desligado(mundo, srv, cli); break;
			case TLigar: LigarPeloMenu(mundo, srv); break;
			case TLigado: Ligado(mundo, srv, cli); break;
			case TCurar: Curar(mundo, srv, cli); break;
			case TContraprova: Contraprova(mundo, srv, cli); break;
			case TVoltar: VoltarProCentro(mundo, srv, cli); break;

			case GPlantar: PlantarAMaquina(mundo, srv, cli); break;
			case GParafuso: AparafusarAMaquina(mundo, srv); break;
			case GSala: ErguerASala(mundo, srv, cli); break;
			case GTeclado: AjustarNoTeclado(mundo, srv, cli); break;
			case GEspera: OsCincoSegundos(srv, cli); break;
			case GCampo: OCampoNaSala(mundo, srv, cli); break;
			case GSair: SairPelaPorta(mundo, srv, cli); break;
			case GDefeito: ODefeito(mundo, srv, cli); break;

			default: Fechar(); break;
		}
	}

	private void Ir(int proximo) { _passo = proximo; _sub = 0; _t = 0; }

	private void Sub(int proximo) { _sub = proximo; _t = 0; }

	// =====================================================================
	// 0) O BERCO ASSENTA
	// =====================================================================
	/// <summary>A celula do centro da cena: onde o corpo assentou. O tanque fica a oeste dela, a sala em volta.</summary>
	private (int X, int Y) _c;

	private static Vec2 No(int cx, int cy) => new(cx * T + T / 2f, cy * T + T / 2f - MoveRules.FeetOffsetY);

	private static (int X, int Y) CelulaDe(Vec2 corpo) => CatalogoDeObras.Celula(corpo.X, corpo.Y);

	private void Assentar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_sub == 0)
		{
			if (_t < 3) return;

			// UM CORPO RECEM-CRIADO ENTRA NOCAUTEADO POR UM INSTANTE (a `--diagvariedade` perdeu o primeiro tiro assim).
			(bool ko, bool morto, _, _, _) = srv.EstadoDaVariedade(cli.LocalId);
			if ((ko || morto) && _t < 30) return;
			Conferir(!ko && !morto, "o corpo entrou em pe e acordado");

			bool praca = srv.AssentarNaPracaDasMaquinas(cli.LocalId, 6);
			Conferir(praca, "achei um QUADRADO de chao comum (seis tiles livres pra cada lado)");
			if (!praca) { Fechar(); return; }

			// MEIO-DIA E CEU LIMPO: as fotos saem todas com a mesma luz de cena.
			srv.CravarMeioDiaDaVariedade(cli.LocalId);
			// ...e um corpo que ja treinou em gravidade: sem isto os 10x o prenderiam no chao da sala.
			srv.MaestriaDeGravidadeNaFotoDasMaquinas(cli.LocalId, 50);
			Sub(1);
			return;
		}
		if (_t < 1.0) return;   // a correcao de posicao precisa atravessar o fio

		_c = CelulaDe(srv.CorpoDaBoca(cli.LocalId).Pos);
		_gravidadeDeFora = cli.Atributos.Gravidade;
		Nota($"a cena: centro na celula ({_c.X},{_c.Y}); gravidade do lugar {_gravidadeDeFora:0.##}x");
		Ir(TIrAoLado);
	}

	// =====================================================================
	// ATO DO TANQUE
	// =====================================================================
	private int _tanque;
	private (int X, int Y) _celulaDoTanque;
	private double _vidaFerido;
	private int _corpoVisivel;
	private float _bateriaAntes;

	/// <summary>
	/// Quanto UM pulso do tanque da cena sobe a vida da ficha: +15 em cada um dos quatro membros feridos
	/// (velocidade 10), e a vida e a media das catorze partes do corpo.
	/// </summary>
	private const double SubidaDeUmPulso = 15 * 4 / 14.0;

	/// <summary>Quando o tanque ligou (no relogio da bancada), e a vida da ficha naquele instante.</summary>
	private double _ligouEm = -1, _vidaAoLigar;

	/// <summary>O centro da CAMPANULA no mundo: a folha tem 58x131, encosta no pe do tile, e a campanula ocupa de 19 a 50 px acima dele.</summary>
	private Vector2 CentroDaCampanula => new(_celulaDoTanque.X * T + T / 2f, (_celulaDoTanque.Y + 1) * T - 34.5f);

	private Vector2 CentroDoTileDoTanque => new(_celulaDoTanque.X * T + T / 2f, _celulaDoTanque.Y * T + T / 2f);

	private void IrAoLadoDoTanque(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		// O TANQUE FICA QUATRO TILES A OESTE DO CENTRO (fora da sala do segundo ato, e fora do alcance do menu
		// de la). Eu ando ate o tile ao lado dele: assentar e um gesto de quem esta perto.
		_celulaDoTanque = (_c.X - 4, _c.Y);
		bool cheguei = AndarAte(mundo, No(_c.X - 3, _c.Y));
		if (!cheguei && _t < 10) return;
		PararDeAndar(mundo);
		(int X, int Y) onde = CelulaDe(srv.CorpoDaBoca(cli.LocalId).Pos);
		Conferir(cheguei && onde == (_c.X - 3, _c.Y),
			$"andei ate o tile ao lado de onde o tanque vai ficar -- celula ({onde.X},{onde.Y})");
		Ir(TPlantar);
	}

	private void PlantarOTanque(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_sub == 0)
		{
			if (_t < 0.6) return;
			_tanque = srv.MaquinaDaFoto(cli.LocalId, Regenerador.Tipo, -1, 0);
			Conferir(_tanque != 0, "um REGENERADOR foi assentado um tile a oeste (mochila + `posicionar`, o funil de producao)");
			if (_tanque == 0) { Fechar(); return; }
			srv.TurbinarOTanqueDaFoto(_tanque);
			Sub(1);
			return;
		}

		ObraDesenhada? o = mundo.ObraDeTeste(_celulaDoTanque.X, _celulaDoTanque.Y);
		if (o == null && _t < 4) return;
		Conferir(o is { Tipo: Regenerador.Tipo, Aparafusada: false },
			"...e esta desenhado na minha tela, SOLTO (o contorno que pisca)");
		Conferir(o is { Estado: Regenerador.ArteDesligado, CamadasDeTeste: 1, ZIndex: -1 },
			$"desligado ele e so a BASE, no plano do chao -- quem pisa nele fica por cima (estado `{o?.Estado}`, "
			+ $"{o?.CamadasDeTeste} camada(s), z {o?.ZIndex})");
		Ir(TParafuso);
	}

	private void AparafusarOTanque(World mundo, Jandirus.Server.GameServer srv)
	{
		switch (_sub)
		{
			case 0:
				ApertarE();
				Sub(1);
				return;

			case 1:
			{
				if (E is not { NaTela: true } && _t < 2) return;
				List<string> botoes = E?.BotoesDesenhados() ?? [];
				Conferir(E is { NaTela: true } && botoes.SequenceEqual(["Ligar / desligar", "Melhorar...", "Ver estado", "Aparafusar / soltar"]),
					$"a tecla E abre o menu do tanque com os botoes novos: {string.Join(" | ", botoes)}");
				Conferir(Apertar("Aparafusar / soltar"), "apertei \"Aparafusar / soltar\"");
				Sub(2);
				return;
			}

			default:
			{
				bool noServidor = srv.TanqueNaFoto(_tanque).Aparafusado;
				bool naTela = mundo.ObraDeTeste(_celulaDoTanque.X, _celulaDoTanque.Y) is { Aparafusada: true };
				if (!(noServidor && naTela) && _t < 3) return;
				Conferir(noServidor && naTela, "o tanque ficou APARAFUSADO, no servidor e no desenho");
				Ir(TSubir);
				return;
			}
		}
	}

	private void SubirNoTanque(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		switch (_sub)
		{
			case 0:
				Conferir(srv.FerirNaFotoDasMaquinas(cli.LocalId), "fui FERIDO: os dois bracos e as duas pernas a 30%");
				Sub(1);
				return;

			case 1:
			{
				// ATE O MEIO DO TILE, e nao so ate a beirada dele: a foto quer o corpo em cima do disco.
				bool cheguei = AndarAte(mundo, No(_celulaDoTanque.X, _celulaDoTanque.Y));
				if (!cheguei && _t < 8) return;
				PararDeAndar(mundo);
				bool emCima = CelulaDe(srv.CorpoDaBoca(cli.LocalId).Pos) == _celulaDoTanque;
				Conferir(cheguei && emCima, "subi no tanque andando (ele nao barra: e um disco no chao)");
				Sub(2);
				return;
			}

			default:
				if (cli.Sheet.HP > 99 && _t < 3) return;   // a ficha anda a 5 Hz
				_vidaFerido = cli.Sheet.HP;
				Conferir(_vidaFerido < 90, $"...e a minha ficha mostra a vida ferida ({_vidaFerido:0.#})");
				Ir(TDesligado);
				return;
		}
	}

	private void Desligado(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_sub == 0)
		{
			if (_t < 4.6) return;   // dois pulsos de dois segundos, com folga
			// O CORPO SE COSTURA SOZINHO, devagar (a passiva: uns 0,05 de vida por segundo neste corpo). Um pulso
			// do tanque da cena vale +4,3 de uma vez -- a regua separa as duas coisas com folga dos dois lados.
			Conferir(cli.Sheet.HP - _vidaFerido < SubidaDeUmPulso / 2 && !srv.TanqueNaFoto(_tanque).Ligado,
				$"em cima do tanque DESLIGADO, em 4,6 s (dois pulsos) a vida so andou o que o corpo cura sozinho: "
				+ $"{_vidaFerido:0.##} -> {cli.Sheet.HP:0.##} (um pulso do tanque seria +{SubidaDeUmPulso:0.#})");
			Mirar(MeuDesenho(mundo, cli));
			Sub(1);
			return;
		}

		if (!Obturador(out Fotos f)) return;
		if (f.Falhou) { Nota("sem foto (headless nao renderiza): as reguas de pixel do tanque ficam pro olho do dono"); Ir(TLigar); return; }

		_corpoVisivel = Tinta(f, CentroDoTileDoTanque, 26, Epsilon).Tinta;
		Color campanula = Media(f.Com, CentroDaCampanula, 6);
		Conferir(_corpoVisivel > 150, $"o meu corpo aparece EM CIMA do disco desligado ({_corpoVisivel} px de tinta do corpo)");
		Conferir(!Azul(campanula), $"...e nao ha campanula: a cor ali nao e o azul dela (R {campanula.R:0.00} B {campanula.B:0.00})");
		Guardar("dmaquinas-1-desligado.png", "1. desligado: ferido, nada acontece", Recortar(f.Com, CentroDoTileDoTanque, 130, 100));
		Ir(TLigar);
	}

	private void LigarPeloMenu(World mundo, Jandirus.Server.GameServer srv)
	{
		switch (_sub)
		{
			case 0:
				ApertarE();
				Sub(1);
				return;

			case 1:
				if (E is not { NaTela: true } && _t < 2) return;
				if (_t < 0.4) return;   // o menu desenhado, pra foto
				Conferir(E is { NaTela: true } && (E.TituloDesenhado ?? "").Length > 0,
					$"de cima do tanque, a tecla E abre o menu dele (titulo \"{E?.TituloDesenhado}\")");
				GuardarATela("dmaquinas-2-menu.png", "2. a tecla E: o menu do tanque");
				Conferir(Apertar("Ligar / desligar"), "apertei \"Ligar / desligar\"");
				Sub(2);
				return;

			default:
			{
				ObraDesenhada? o = mundo.ObraDeTeste(_celulaDoTanque.X, _celulaDoTanque.Y);
				bool ligou = srv.TanqueNaFoto(_tanque).Ligado && o is { Estado: Regenerador.ArteLigado };
				if (!ligou && _t < 3) return;
				Conferir(srv.TanqueNaFoto(_tanque).Ligado, "o tanque LIGOU no servidor");
				Conferir(o is { Estado: Regenerador.ArteLigado, CamadasDeTeste: 2, ZIndex: 1 },
					$"...e o desenho ganhou a CAMPANULA, no plano de cima -- o tanque fechado (estado `{o?.Estado}`, "
					+ $"{o?.CamadasDeTeste} camada(s), z {o?.ZIndex})");
				_bateriaAntes = (float)srv.TanqueNaFoto(_tanque).Energia;
				_ligouEm = _vida;
				_vidaAoLigar = C?.Sheet.HP ?? 0;
				_subiuEm = -1;
				Ir(TLigado);
				return;
			}
		}
	}

	private void Ligado(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_sub == 0)
		{
			if (_t < 0.3) return;
			Mirar(MeuDesenho(mundo, cli));
			Sub(1);
			return;
		}

		if (!Obturador(out Fotos f)) return;
		if (!f.Falhou)
		{
			int agora = Tinta(f, CentroDoTileDoTanque, 26, Epsilon).Tinta;
			Color campanula = Media(f.Com, CentroDaCampanula, 6);
			Conferir(Azul(campanula), $"NA FOTO, a campanula azul esta la (R {campanula.R:0.00} G {campanula.G:0.00} B {campanula.B:0.00})");
			Conferir(agora < _corpoVisivel * 0.35f,
				$"...e o tanque fechado ESCONDE o paciente, como no original (`plane = 6`): {_corpoVisivel} px de corpo a mostra antes, {agora} agora");
			Guardar("dmaquinas-3-ligado.png", "3. LIGADO pelo menu: a campanula fecha", Recortar(f.Com, CentroDoTileDoTanque, 130, 100));
		}
		Ir(TCurar);
	}

	private double _subiuEm = -1;

	private void Curar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (cli.Sheet.HP < 99.9 && _vida - _ligouEm < 30) return;

		Conferir(_subiuEm >= 0 && _subiuEm <= 2.8,
			$"LIGADO, a vida subiu no PRIMEIRO pulso: +{SubidaDeUmPulso:0.#} de uma vez, "
			+ $"{(_subiuEm < 0 ? "nunca" : $"{_subiuEm:0.0} s")} depois de ligar (o pulso e de dois em dois segundos)");
		Conferir(cli.Sheet.HP >= 99.9, $"...e encheu: {_vidaAoLigar:0.#} -> {cli.Sheet.HP:0.#} em {_vida - _ligouEm:0.0} s");
		float bateria = (float)srv.TanqueNaFoto(_tanque).Energia;
		Conferir(bateria < _bateriaAntes, $"...gastando bateria: {_bateriaAntes * 100:0.#} -> {bateria * 100:0.#}");
		Ir(TContraprova);
	}

	private void Contraprova(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		switch (_sub)
		{
			case 0:
				ApertarE();
				Sub(1);
				return;

			case 1:
				if (E is not { NaTela: true } && _t < 2) return;
				Conferir(Apertar("Ligar / desligar"), "apertei \"Ligar / desligar\" de novo");
				Sub(2);
				return;

			case 2:
			{
				ObraDesenhada? o = mundo.ObraDeTeste(_celulaDoTanque.X, _celulaDoTanque.Y);
				bool desligou = !srv.TanqueNaFoto(_tanque).Ligado && o is { Estado: Regenerador.ArteDesligado };
				if (!desligou && _t < 3) return;
				Conferir(desligou && o is { CamadasDeTeste: 1, ZIndex: -1 }, "o mesmo botao DESLIGA: a campanula some e o disco volta pro chao");
				_ligouEm = -1;
				srv.FerirNaFotoDasMaquinas(cli.LocalId);
				Sub(3);
				return;
			}

			case 3:
				if (cli.Sheet.HP > 99 && _t < 3) return;
				_vidaFerido = cli.Sheet.HP;
				Sub(4);
				return;

			default:
				if (_t < 4.6) return;
				Conferir(_vidaFerido < 90 && cli.Sheet.HP - _vidaFerido < SubidaDeUmPulso / 2,
					$"CONTRAPROVA: desligado de novo e ferido de novo, a mesma regua da vida nao ve pulso nenhum ({_vidaFerido:0.##} -> {cli.Sheet.HP:0.##})");
				Ir(TVoltar);
				return;
		}
	}

	private void VoltarProCentro(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		bool cheguei = AndarAte(mundo, No(_c.X, _c.Y));
		if (!cheguei && _t < 12) return;
		PararDeAndar(mundo);
		(int X, int Y) onde = CelulaDe(srv.CorpoDaBoca(cli.LocalId).Pos);
		Conferir(cheguei && onde == _c, $"voltei andando pro centro da cena -- celula ({onde.X},{onde.Y})");
		Ir(GPlantar);
	}

	// =====================================================================
	// ATO DA GRAVIDADE
	// =====================================================================
	private int _maquina;
	private (int X, int Y) _celulaDaMaquina;
	private float _gravidadeDeFora;

	/// <summary>A sala: o anel de parede de (c-2, c-2) a (c+3, c+3), 4x4 por dentro, e a porta no meio da parede leste.</summary>
	private (int X0, int Y0, int X1, int Y1) Sala => (_c.X - 2, _c.Y - 2, _c.X + 3, _c.Y + 3);
	private (int X, int Y) Porta => (_c.X + 3, _c.Y);

	/// <summary>O meio do 4x4 de dentro, no mundo.</summary>
	private Vector2 MeioDaSala => new((_c.X + 1) * T, (_c.Y + 1) * T);

	/// <summary>O meio do tile logo depois da porta, do lado de fora.</summary>
	private Vector2 ForaDaPorta => new((_c.X + 4) * T + T / 2f, _c.Y * T + T / 2f);

	private void PlantarAMaquina(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		_celulaDaMaquina = (_c.X + 1, _c.Y - 1);
		if (_sub == 0)
		{
			if (_t < 0.6) return;
			_maquina = srv.MaquinaDaFoto(cli.LocalId, "Gravity", 1, -1);
			Conferir(_maquina != 0, "uma MAQUINA DE GRAVIDADE foi assentada na diagonal (o funil de producao)");
			if (_maquina == 0) { Fechar(); return; }
			Sub(1);
			return;
		}

		ObraDesenhada? o = mundo.ObraDeTeste(_celulaDaMaquina.X, _celulaDaMaquina.Y);
		if (o == null && _t < 4) return;
		Conferir(o is { Tipo: "Gravity", Aparafusada: false }, "...e esta desenhada na minha tela, solta");
		Ir(GParafuso);
	}

	private void AparafusarAMaquina(World mundo, Jandirus.Server.GameServer srv)
	{
		switch (_sub)
		{
			case 0:
				ApertarE();
				Sub(1);
				return;

			case 1:
			{
				if (E is not { NaTela: true } && _t < 2) return;
				List<string> botoes = E?.BotoesDesenhados() ?? [];
				Conferir(botoes.Contains("Aparafusar / soltar") && botoes.Contains("Ajustar gravidade"),
					$"o menu da maquina tem o PARAFUSO (era o botao que faltava): {string.Join(" | ", botoes)}");
				Conferir(Apertar("Aparafusar / soltar"), "apertei \"Aparafusar / soltar\"");
				Sub(2);
				return;
			}

			default:
			{
				bool ok = srv.CampoNaFoto(_maquina).Aparafusada && mundo.ObraDeTeste(_celulaDaMaquina.X, _celulaDaMaquina.Y) is { Aparafusada: true };
				if (!ok && _t < 3) return;
				Conferir(ok, "a maquina ficou APARAFUSADA pelo menu dela");
				Ir(GSala);
				return;
			}
		}
	}

	private void ErguerASala(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_sub == 0)
		{
			int alcance = srv.AlcanceNaFotoDasMaquinas(cli.LocalId, _maquina, 10);
			Conferir(alcance == 10, $"o alcance foi comprado ate o teto pelo verbo de producao (10: um quadrado de 11x11) -- ficou em {alcance}");

			(int x0, int y0, int x1, int y1) = Sala;
			int blocos = srv.SalaNaFotoDasMaquinas(cli.LocalId, x0, y0, x1, y1, Porta.X, Porta.Y);
			Conferir(blocos == 20, $"a SALA subiu em volta de mim e da maquina (19 paredes e 1 porta, 4x4 por dentro) -- {blocos} blocos");
			Sub(1);
			return;
		}

		(int sx0, int sy0, int sx1, int sy1) = Sala;
		bool desenhada = mundo.BlocoDesenhadoDeTeste(sx0, sy0) != null && mundo.BlocoDesenhadoDeTeste(sx1, sy1) != null
						 && mundo.PortaErguidaDeTeste(Porta.X, Porta.Y) != null;
		if (!desenhada && _t < 4) return;
		Conferir(desenhada, "...e esta desenhada na minha tela: as quinas de parede e a porta");
		Ir(GTeclado);
	}

	private double _pediuEm;

	private void AjustarNoTeclado(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		switch (_sub)
		{
			case 0:
				ApertarE();
				Sub(1);
				return;

			case 1:
				if (E is not { NaTela: true } && _t < 2) return;
				Conferir(Apertar("Ajustar gravidade"), "apertei \"Ajustar gravidade\"");
				Sub(2);
				return;

			case 2:
			{
				if (E is not { TecladoNaTela: true } && _t < 2) return;
				string visor = E?.DigitarNoTeclado("10", confirmar: false) ?? "";
				Conferir(E is { TecladoNaTela: true } && visor == "10", $"o TECLADO NUMERICO abriu, e eu digitei 10 (o visor mostra \"{visor}\")");
				Sub(3);
				return;
			}

			case 3:
				if (_t < 0.4) return;   // o visor desenhado, pra foto
				GuardarATela("dmaquinas-4-teclado.png", "4. ajustar gravidade: o teclado");
				E?.DigitarNoTeclado("", confirmar: true);
				_pediuEm = _vida;
				Sub(4);
				return;

			default:
			{
				var campo = srv.CampoNaFoto(_maquina);
				if (!campo.Mudando && _t < 2) return;
				Conferir(campo is { Mudando: true, Grav: 0, Pedida: 10 },
					$"o pedido NAO muda na hora: a maquina fica em espera (pedida {campo.Pedida:0}x, ligada {campo.Grav:0}x)");
				Conferir(cli.CamposDeGravidade.Count == 0, "...e nao ha campo nenhum na tela enquanto ela espera");
				Ir(GEspera);
				return;
			}
		}
	}

	private void OsCincoSegundos(Jandirus.Server.GameServer srv, GameClient cli)
	{
		var campo = srv.CampoNaFoto(_maquina);
		if (campo.Grav <= 0 && _t < 9) return;

		double espera = _vida - _pediuEm;
		Conferir(campo.Grav == 10 && espera is >= 4.6 and <= 6.5,
			$"CINCO SEGUNDOS depois do pedido a gravidade ligou em 10x (esperou {espera:0.0} s)");
		Ir(GCampo);
	}

	private void OCampoNaSala(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_sub == 0)
		{
			bool chegou = cli.CamposDeGravidade.Count == 1 && mundo.CamposDeTeste.LadrilhosDeTeste > 0
						  && cli.Atributos.Gravidade > _gravidadeDeFora + 5;
			if (!chegou && _t < 3) return;

			int tiles = cli.CamposDeGravidade.Count == 1 ? cli.CamposDeGravidade[0].Tiles.Length : -1;
			Conferir(tiles == 16 && srv.CampoNaFoto(_maquina).Tiles == 16,
				$"com o alcance no teto (11x11) o campo enche so a SALA: 16 tiles no servidor, {tiles} na lista que chegou ao cliente");
			Conferir(mundo.CamposDeTeste.LadrilhosDeTeste == 16 && mundo.CamposDeTeste.ZIndex == CamposDeGravidadeNaTela.Plano,
				$"...e 16 ladrilhos de filtro, no plano de cima dos corpos ({mundo.CamposDeTeste.LadrilhosDeTeste} ladrilhos, z {mundo.CamposDeTeste.ZIndex})");

			var eu = srv.CorpoNaFotoDasMaquinas(cli.LocalId);
			Conferir(eu.Campo == 10, $"eu, dentro da sala, peso os 10x da maquina no servidor ({eu.Campo:0.#})");
			Conferir(Math.Abs(cli.Atributos.Gravidade - (_gravidadeDeFora + 10)) < 0.05,
				$"...e a minha ficha mostra a gravidade do lugar MAIS os 10x: {_gravidadeDeFora:0.##} -> {cli.Atributos.Gravidade:0.##}");

			Mirar([mundo.CamposDeTeste]);
			Sub(1);
			return;
		}

		if (!Obturador(out Fotos f)) return;
		if (f.Falhou) Nota("sem foto (headless nao renderiza): a tinta do campo fica pro olho do dono");
		else
		{
			// SO AS REGUAS DE DENTRO, daqui: a parede e a porta fechada escondem o lado de fora de quem esta na sala,
			// e uma regua de "fora sem filtro" medida no escuro passaria com qualquer coisa. A de fora e tirada la
			// de fora, no passo seguinte.
			float dentro = Fracao(f, MeioDaSala, 58);
			Conferir(dentro >= 0.95f, $"NA FOTO, a sala INTEIRA esta sob o filtro do campo ({dentro:P0} da janela de dentro)");

			// "EM CIMA DE TUDO": o filtro cobre o MEU CORPO e a MAQUINA, e nao so o chao em volta deles.
			float emMim = Fracao(f, mundo.PosicaoDesenhadaDe(cli.LocalId) ?? MeioDaSala, 7);
			float naMaquina = Fracao(f, new Vector2(_celulaDaMaquina.X * T + T / 2f, _celulaDaMaquina.Y * T + T / 2f), 10);
			Conferir(emMim >= 0.95f && naMaquina >= 0.95f,
				$"...por cima de TUDO o que esta nela: o meu corpo ({emMim:P0}) e a maquina ({naMaquina:P0}) tambem mudam de cor");

			// E E VERMELHO: com o filtro, o vermelho da janela sobe EM RELACAO ao verde (num chao de grama o verde
			// mandava com folga; sob o filtro os dois empatam).
			Color com = Media(f.Com, MeioDaSala, 58), sem = Media(f.Sem, MeioDaSala, 58);
			Conferir(com.R > sem.R + 0.08f && com.R - com.G > sem.R - sem.G + 0.12f,
				$"...e o filtro e VERMELHO, como o `Gravity Field` do original: a cor media da sala vai de "
				+ $"({sem.R:0.00}, {sem.G:0.00}, {sem.B:0.00}) pra ({com.R:0.00}, {com.G:0.00}, {com.B:0.00})");
			Guardar("dmaquinas-5-a-sala.png", "5. 10x: o campo enche a sala, e so ela", Recortar(f.Com, MeioDaSala, 170, 140));
		}
		Ir(GSair);
	}

	private void SairPelaPorta(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		var eu = srv.CorpoNaFotoDasMaquinas(cli.LocalId);
		switch (_sub)
		{
			case 0:
			{
				// DOIS TILES ALEM DA PORTA: ela abre por encostar, e o corpo segue andando.
				bool cheguei = AndarAte(mundo, No(_c.X + 5, _c.Y));
				if (!cheguei && _t < 16) return;
				PararDeAndar(mundo);
				(int X, int Y) onde = CelulaDe(eu.Pos);
				Conferir(cheguei && onde == (_c.X + 5, _c.Y),
					$"sai da sala ANDANDO pela porta (ela abre por encostar) -- estou na celula ({onde.X},{onde.Y})");
				Sub(1);
				return;
			}

			case 1:
			{
				bool leve = eu.Campo == 0 && Math.Abs(cli.Atributos.Gravidade - _gravidadeDeFora) < 0.05;
				if (!leve && _t < 2.5) return;
				Conferir(eu.Campo == 0, $"do lado de fora o campo me LARGOU sem ninguem mexer na maquina (era o defeito: quem saia levava a gravidade junto) -- {eu.Campo:0.#}x");
				Conferir(Math.Abs(cli.Atributos.Gravidade - _gravidadeDeFora) < 0.05,
					$"...e a minha ficha voltou pra gravidade do lugar ({cli.Atributos.Gravidade:0.##})");
				Conferir(srv.CampoNaFoto(_maquina).Tiles == 16, "a porta que a minha passagem abriu NAO derramou o campo: continuam 16 tiles");
				Mirar([mundo.CamposDeTeste]);
				Sub(2);
				return;
			}

			default:
			{
				if (!Obturador(out Fotos f)) return;
				if (!f.Falhou)
				{
					float fora = Fracao(f, ForaDaPorta, 13);
					Conferir(fora <= 0.03f, $"NA FOTO, tirada daqui de fora: o tile logo depois da porta nao tem filtro nenhum ({fora:P1})");
					Guardar("dmaquinas-6-fora.png", "6. fora da sala: leve de novo", Recortar(f.Com, ForaDaPorta, 170, 140));
				}
				Ir(GDefeito);
				return;
			}
		}
	}

	private void ODefeito(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		switch (_sub)
		{
			case 0:
				CampoDeGravidade.AtravessaParedeDeTeste = true;
				Sub(1);
				return;

			case 1:
			{
				bool vazou = cli.CamposDeGravidade.Count == 1 && cli.CamposDeGravidade[0].Tiles.Length > 16
							 && srv.CorpoNaFotoDasMaquinas(cli.LocalId).Campo > 0;
				if (!vazou && _t < 4) return;
				Conferir(vazou && srv.CorpoNaFotoDasMaquinas(cli.LocalId).Campo == 10,
					$"(defeito injetado: o campo atravessa parede) a regua do corpo REPROVA -- do lado de fora, sou esmagado pelos 10x "
					+ $"({(cli.CamposDeGravidade.Count == 1 ? cli.CamposDeGravidade[0].Tiles.Length : 0)} tiles na lista)");
				Mirar([mundo.CamposDeTeste]);
				Sub(2);
				return;
			}

			case 2:
			{
				if (!Obturador(out Fotos f)) return;
				if (!f.Falhou)
				{
					float fora = Fracao(f, ForaDaPorta, 13);
					Conferir(fora > 0.9f, $"(defeito injetado) ...e a regua do filtro REPROVA: o lado de fora da porta esta vermelho ({fora:P0})");
					Guardar("dmaquinas-7-defeito.png", "7. DEFEITO INJETADO: o campo vaza", Recortar(f.Com, ForaDaPorta, 170, 140));
				}
				CampoDeGravidade.AtravessaParedeDeTeste = false;
				Sub(3);
				return;
			}

			default:
			{
				bool voltou = cli.CamposDeGravidade.Count == 1 && cli.CamposDeGravidade[0].Tiles.Length == 16
							  && srv.CorpoNaFotoDasMaquinas(cli.LocalId).Campo == 0;
				if (!voltou && _t < 4) return;
				Conferir(voltou, "sem o defeito, o campo volta pra dentro da sala e me larga de novo");
				Ir(-1);
				return;
			}
		}
	}

	// =====================================================================
	// ANDAR ATE UM PONTO EXATO
	// =====================================================================
	private bool _andando;
	private Vector2 _rumoDaAndada;

	/// <summary>
	/// ANDA EM LINHA RETA ATE UM PONTO, e para NELE. Devolve verdadeiro no quadro em que chegou.
	///
	/// ============================ POR QUE NAO E SO O `IrAteDeTeste` ============================
	/// O piloto automatico foi feito pra viajar ate um planeta: ele da "voce chegou" a 64 px do alvo
	/// (`LocalPlayer`, `rumo.LengthSquared &lt; 64 * 64`). A primeira corrida desta bancada mandou o corpo "ate o
	/// tile ao lado do tanque" e ele parou DOIS tiles antes -- e dali pra frente toda celula da cena estava
	/// errada (o tanque nasceu noutro lugar, a sala subiu pela metade, o menu aberto era o da maquina errada).
	///
	/// Entao o alvo do piloto vai 200 px ALEM do ponto, no mesmo rumo, e quem para o corpo e a bancada, no
	/// quadro em que ele cruza o ponto. O passo continua sendo o do jogo: `MoveRules`, conferido pelo servidor.
	/// ===========================================================================================
	/// </summary>
	private bool AndarAte(World mundo, Vec2 ponto)
	{
		if (mundo.PosicaoLocalDeTeste is not { } eu) return false;
		Vector2 falta = new Vector2(ponto.X, ponto.Y) - eu;

		if (!_andando)
		{
			if (falta.Length() < 3f) return true;
			_rumoDaAndada = falta.Normalized();
			mundo.IrAteDeTeste(new Vec2(ponto.X + _rumoDaAndada.X * 200f, ponto.Y + _rumoDaAndada.Y * 200f));
			_andando = true;
			return false;
		}

		if (falta.Dot(_rumoDaAndada) > 1.5f) return false;   // ainda nao cruzou o ponto
		PararDeAndar(mundo);
		return true;
	}

	private void PararDeAndar(World mundo)
	{
		mundo.PararDeTeste();
		_andando = false;
	}

	// =====================================================================
	// AS TECLAS E O MENU
	// =====================================================================
	/// <summary>A TECLA E DE VERDADE: um evento de teclado empurrado no viewport (o caminho do `_UnhandledInput`).</summary>
	private void ApertarE()
	{
		var ev = new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = true };
		GetViewport().PushInput(ev);
	}

	/// <summary>Aperta um botao do menu aberto pelo rotulo -- o mesmo sinal que o dedo emite. Falso se ele nao estava la.</summary>
	private bool Apertar(string rotulo)
	{
		if (E?.ApertarDesenhado(rotulo) == true) return true;
		Nota(E is { NaTela: true } ? $"(\"{rotulo}\" nao esta no menu, que tem: {string.Join(" | ", E.BotoesDesenhados())})"
								   : $"(\"{rotulo}\": o menu nem estava aberto)");
		return false;
	}

	// =====================================================================
	// O OBTURADOR: tres fotos do mesmo instante, com os alvos escondidos nas duas de fora
	// =====================================================================
	private struct Fotos
	{
		public Image Com, Sem, Sem2;
		public bool Falhou;
	}

	private int _obtFase, _obtQuadros;
	private Image? _obtCom, _obtSem;
	private readonly List<CanvasItem> _obtAlvos = [];
	private readonly List<bool> _obtEram = [];

	private void Mirar(IEnumerable<CanvasItem?> alvos)
	{
		_obtAlvos.Clear();
		foreach (CanvasItem? a in alvos)
			if (a != null) _obtAlvos.Add(a);
	}

	/// <summary>O desenho do MEU corpo -- o alvo da regua "o tanque fechado esconde o paciente".</summary>
	private static IEnumerable<CanvasItem?> MeuDesenho(World mundo, GameClient cli) => [mundo.CorpoDeTeste(cli.LocalId)];

	/// <summary>
	/// ESCONDE, FOTOGRAFA, MOSTRA, FOTOGRAFA, ESCONDE, FOTOGRAFA -- com a arvore pausada, e dois quadros de
	/// folga a cada troca (`GetImage` devolve o ULTIMO quadro renderizado). Devolve falso enquanto nao acabou.
	/// A mesma regra das tres fotos da `--diagboca` e da `--diagmajin`.
	/// </summary>
	private bool Obturador(out Fotos f)
	{
		f = default;
		switch (_obtFase)
		{
			case 0:
				GetTree().Paused = true;
				_obtEram.Clear();
				foreach (CanvasItem c in _obtAlvos) _obtEram.Add(IsInstanceValid(c) && c.Visible);
				Mostrar(false);
				_obtFase = 1; _obtQuadros = 0;
				return false;

			case 1:
				if (_obtQuadros++ < 2) return false;
				_obtSem = Tela();
				Mostrar(true);
				_obtFase = 2; _obtQuadros = 0;
				return false;

			case 2:
				if (_obtQuadros++ < 2) return false;
				_obtCom = Tela();
				Mostrar(false);
				_obtFase = 3; _obtQuadros = 0;
				return false;

			default:
			{
				if (_obtQuadros++ < 2) return false;
				Image? sem2 = Tela();
				Mostrar(true);

				if (_obtCom == null || _obtSem == null || sem2 == null) f.Falhou = true;
				else { f.Com = _obtCom; f.Sem = _obtSem; f.Sem2 = sem2; }
				_obtFase = 0; _obtQuadros = 0;
				_obtCom = null; _obtSem = null;
				_obtAlvos.Clear();
				_obtEram.Clear();
				if (GetTree() is { Paused: true } t) t.Paused = false;
				return true;
			}
		}
	}

	/// <summary>Mostra (cada um como estava antes) ou esconde os alvos do obturador.</summary>
	private void Mostrar(bool sim)
	{
		for (int i = 0; i < _obtAlvos.Count && i < _obtEram.Count; i++)
			if (IsInstanceValid(_obtAlvos[i])) _obtAlvos[i].Visible = sim && _obtEram[i];
	}

	// =====================================================================
	// A MEDIDA
	// =====================================================================
	private Image? Tela()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return null;
		img.Convert(Image.Format.Rgba8);
		return img;
	}

	/// <summary>De ponto do mundo pra pixel da imagem (a `CanvasTransform` e a razao imagem / viewport).</summary>
	private Vector2 NaImagem(Image img, Vector2 mundo)
	{
		Vector2 v = (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * mundo;
		Vector2 tam = GetViewport()?.GetVisibleRect().Size ?? img.GetSize();
		return new Vector2(v.X * img.GetWidth() / tam.X, v.Y * img.GetHeight() / tam.Y);
	}

	/// <summary>Quantos pixels de imagem vale um pixel de mundo -- medido, e nao lido do `Zoom`.</summary>
	private float EscalaDaImagem(Image img)
	{
		float e = (NaImagem(img, new Vector2(T, 0)) - NaImagem(img, Vector2.Zero)).Length() / T;
		return e > 0.01f ? e : 1f;
	}

	/// <summary>A janela medida: tantos px de mundo pra cada lado do ponto, cortada na borda da imagem.</summary>
	private Rect2I Janela(Image img, Vector2 centroNoMundo, float meiaLargura, float meiaAltura)
	{
		Vector2 c = NaImagem(img, centroNoMundo);
		float e = EscalaDaImagem(img);
		int x0 = Math.Clamp((int)(c.X - meiaLargura * e), 0, img.GetWidth() - 1), x1 = Math.Clamp((int)(c.X + meiaLargura * e), 1, img.GetWidth());
		int y0 = Math.Clamp((int)(c.Y - meiaAltura * e), 0, img.GetHeight() - 1), y1 = Math.Clamp((int)(c.Y + meiaAltura * e), 1, img.GetHeight());
		return new Rect2I(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
	}

	/// <summary>Quantos pixels da janela sao tinta dos alvos, e quantos pixels a janela tem.</summary>
	private (int Tinta, int Total) Tinta(Fotos f, Vector2 centroNoMundo, float meia, float limiar)
	{
		Rect2I j = Janela(f.Com, centroNoMundo, meia, meia);
		int n = 0;
		for (int y = j.Position.Y; y < j.Position.Y + j.Size.Y; y++)
			for (int x = j.Position.X; x < j.Position.X + j.Size.X; x++)
				if (EhTinta(f, x, y, limiar)) n++;
		return (n, j.Size.X * j.Size.Y);
	}

	/// <summary>A fracao da janela que e tinta do CAMPO -- com o limiar fino dela.</summary>
	private float Fracao(Fotos f, Vector2 centroNoMundo, float meia)
	{
		(int tinta, int total) = Tinta(f, centroNoMundo, meia, EpsilonDaTinta);
		return total > 0 ? tinta / (float)total : 0f;
	}

	/// <summary>A regra das tres fotos: difere das DUAS sem o alvo, e as duas concordam entre si ali.</summary>
	private static bool EhTinta(Fotos f, int x, int y, float limiar)
	{
		Color com = f.Com.GetPixel(x, y), sem = f.Sem.GetPixel(x, y), sem2 = f.Sem2.GetPixel(x, y);
		return !Difere(sem, sem2, limiar) && Difere(com, sem, limiar) && Difere(com, sem2, limiar);
	}

	private static bool Difere(Color p, Color q, float limiar)
		=> MathF.Abs(p.R - q.R) > limiar || MathF.Abs(p.G - q.G) > limiar || MathF.Abs(p.B - q.B) > limiar;

	/// <summary>A cor media de uma janelinha em volta de um ponto do mundo.</summary>
	private Color Media(Image img, Vector2 centroNoMundo, float meia)
	{
		Rect2I j = Janela(img, centroNoMundo, meia, meia);
		double r = 0, g = 0, b = 0;
		int n = 0;
		for (int y = j.Position.Y; y < j.Position.Y + j.Size.Y; y++)
			for (int x = j.Position.X; x < j.Position.X + j.Size.X; x++)
			{
				Color p = img.GetPixel(x, y);
				r += p.R; g += p.G; b += p.B; n++;
			}
		return n == 0 ? Colors.Black : new Color((float)(r / n), (float)(g / n), (float)(b / n));
	}

	/// <summary>O AZUL DA CAMPANULA (`regenerator.dmi`, "tank"): claro, e bem mais azul que vermelho.</summary>
	private static bool Azul(Color c) => c.B - c.R > 0.18f && c.B > 0.55f;

	// =====================================================================
	// AS FOTOS
	// =====================================================================
	private Image? Recortar(Image? img, Vector2 centroNoMundo, float meiaLargura, float meiaAltura)
	{
		if (img == null) return null;
		Image r = img.GetRegion(Janela(img, centroNoMundo, meiaLargura, meiaAltura));
		r.Convert(Image.Format.Rgba8);
		return r;
	}

	/// <summary>Grava a foto e a guarda pra tira da historia.</summary>
	private void Guardar(string nome, string legenda, Image? foto)
	{
		if (foto == null) { Nota($"{legenda}: a janela nao devolveu imagem"); return; }
		try
		{
			string caminho = ProjectSettings.GlobalizePath("user://" + nome);
			foto.SavePng(caminho);
			_linhas.Add($"  foto   {legenda}: {caminho}");
			_historia.Add(new TiraDeFotos.Quadro(foto, legenda));
		}
		catch (Exception e) { Nota($"{legenda}: sem foto: {e.Message}"); }
	}

	/// <summary>A TELA INTEIRA pela metade -- pras fotos de interface (o menu e o teclado), que nao moram no mundo.</summary>
	private void GuardarATela(string nome, string legenda)
	{
		if (Tela() is not { } tela) { Nota($"{legenda}: a janela nao devolveu imagem"); return; }
		tela.Resize(tela.GetWidth() / 2, tela.GetHeight() / 2, Image.Interpolation.Bilinear);
		try
		{
			string caminho = ProjectSettings.GlobalizePath("user://" + nome);
			tela.SavePng(caminho);
			_linhas.Add($"  foto   {legenda}: {caminho}");
		}
		catch (Exception e) { Nota($"{legenda}: sem foto: {e.Message}"); }
	}

	private void Fechar()
	{
		if (_acabou) return;
		_acabou = true;

		CampoDeGravidade.AtravessaParedeDeTeste = false;
		if (GetTree() is { } t) t.Paused = false;
		Mostrar(true);
		World.Instancia?.PararDeTeste();
		if (E is { NaTela: true }) ApertarE();

		if (_historia.Count > 0)
		{
			string tira = ProjectSettings.GlobalizePath("user://dmaquinas-0-historia.png");
			double pintada = TiraDeFotos.Montar(_historia, tira);
			Conferir(pintada > 0.5, $"a TIRA da historia saiu ({_historia.Count} quadros, {pintada:P0} de imagem): {tira}");
		}

		S?.LimparAFotoDasMaquinas();

		GD.Print("\n[dmaquinas] ===== O REGENERADOR E A MAQUINA DE GRAVIDADE NA TELA =====");
		foreach (string l in _linhas) GD.Print("[dmaquinas] " + l);
		GD.Print(_falhas.Count == 0
			? "[dmaquinas] ===== TUDO OK ====="
			: $"[dmaquinas] ===== {_falhas.Count} FALHA(S) =====\n[dmaquinas]   " + string.Join("\n[dmaquinas]   ", _falhas));
		GetTree().Quit();
	}
}
