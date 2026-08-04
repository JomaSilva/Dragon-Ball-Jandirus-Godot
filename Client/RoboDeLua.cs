using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DO CÉU (`--diagceu`).
///
/// ============================ O QUE SÓ O TESTE RESPONDE ============================
/// Um ciclo de lua de oito noites de 24 minutos leva MAIS DE TRÊS HORAS pra fechar. Olhando não
/// se responde nada disso:
///   * a fase da lua vira no ANOITECER, ou vira à meia-noite no meio da cara de quem olha?
///   * o relógio do cliente bate com o do servidor, ou cada um tem o seu de novo?
///   * cada planeta corre mesmo o próprio dia, ou todos amanhecem juntos como no BYOND?
///   * Namek continua sem anoitecer (`HasNight=0`) e sem lua (`HasMoon=0`)?
///   * a lua está DESENHADA quando o estado diz que ela está no céu?
///   * a noite de lua cheia é visivelmente mais clara que a de lua nova?
/// ==================================================================================
///
/// COMO RODAR (a flag do servidor é o que torna isto possível em segundos):
///     Godot --path . --host --luateste --diagceu --nome Lua --conta lua
///
/// `--luateste [1-8]` põe a TERRA na fase pedida (5 = cheia) com a lua no alto. `--horateste
/// 0.78` põe o mundo pouco antes do anoitecer, que é onde a virada de fase acontece.
/// </summary>
public partial class RoboDeLua : Node
{
	/// <summary>Quanto esperar o primeiro pacote de hora antes de desistir.</summary>
	private const double Paciencia = 15;

	/// <summary>
	/// Quanto tempo VIGIAR a virada de fase, em segundos reais.
	///
	/// Uma noite da Terra dura 0,46 de um dia de 24 min = 11 min. Vigiar tudo isso num teste
	/// automático não vale: o que se quer provar é que a fase NÃO troca no meio da noite, e pra
	/// isso basta cobrir a meia-noite -- que é justamente onde a implementação ingênua erra.
	/// </summary>
	private const double Vigia = 40;

	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	private bool _acabou;
	private int _fase;
	private double _t, _espera;

	private int _faseInicial;
	private double _horaInicial;
	private bool _cruzouMeiaNoite;
	private int _viradasNaNoite;

	/// <summary>
	/// O QUE O SERVIDOR ANUNCIOU SOBRE A LUA.
	///
	/// É o único jeito de provar o GANCHO DO OOZARU de fora: o aviso de lua cheia nasce no
	/// `GameServer.LuaCheiaNasceu`, que é exatamente o ponto onde a transformação vai entrar. Se
	/// a frase chega, o gancho está pendurado no lugar certo e disparando na hora certa; se não
	/// chega, o Oozaru também não dispararia -- e nada mais neste teste acusaria isso.
	/// </summary>
	private readonly List<string> _avisos = [];

	private static GameClient? C => GameClient.Instance;

	public override void _Ready()
	{
		if (C is { } cli)
			cli.Falou += (canal, _, texto) =>
			{
				if (canal != Jandirus.Net.Protocol.Fala.Sistema) return;
				if (texto.Contains("lua", StringComparison.OrdinalIgnoreCase)
					|| texto.Contains("rabo", StringComparison.OrdinalIgnoreCase))
					_avisos.Add(texto);
			};
	}

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Process(double delta)
	{
		if (_acabou || C is not { Connected: true } cli || World.Instancia is not { } mundo) return;

		_t += delta;
		if (_espera > 0) { _espera -= delta; return; }

		switch (_fase)
		{
			case 0:
				// O RELÓGIO TEM QUE VIR DO SERVIDOR. Antes deste port ele não vinha: o cliente
				// contava sozinho a partir de 0,42 e ninguém nunca o corrigia.
				if (!cli.TempoChegou)
				{
					if (_t < Paciencia) return;
					Conferir(false, $"o servidor NAO mandou a hora do mundo em {Paciencia:0}s");
					_fase = 90;
					return;
				}
				Conferir(true, $"o servidor mandou a hora do mundo ({cli.TempoDoMundo:0} s)");
				Conferir(Math.Abs(mundo.TempoDoMundo - cli.TempoDoMundo) < 2,
					$"o relogio da luz bate com o do pacote (dif {mundo.TempoDoMundo - cli.TempoDoMundo:0.00} s)");
				_fase = 1;
				break;

			case 1:
			{
				RelogioDoPlaneta r = mundo.RelogioDoLugar;
				EstadoDoCeu ceu = mundo.Ceu ?? default;

				Conferir(r.SegundosPorDia > 0, $"o planeta tem rotacao propria ({r.MinutosPorDia:0.#} min por dia)");
				_passos.Add($"         zona {cli.Zone.Name} | {Ceu.NomeDoCiclo(r)} | "
							+ $"{Ceu.NomeDaHora(ceu.Hora)} {ceu.Hora * 24:00.0}h | {Ceu.NomeDaFase(ceu.Fase)} "
							+ $"| altura {ceu.Altura:0.00} | luar {ceu.Luar:0.00}");

				// O CÉU DE CADA MUNDO, CALCULADO DAQUI. É o que prova que a hora é POR PLANETA e
				// que o cliente consegue responder por qualquer um deles sem pedir nada.
				var mundos = new[] { "Earth", "Vegeta", "Namek", "Arlia", "Icer" };
				var horas = new List<double>();
				foreach (string nome in mundos)
				{
					RelogioDoPlaneta rr = Planetas.Relogio(ZoneKey.Premade(nome));
					EstadoDoCeu cc = Ceu.De(rr, cli.TempoDoMundo);
					horas.Add(cc.Hora);
					_passos.Add($"         {nome,-8} {Ceu.NomeDoCiclo(rr),-16} {Ceu.NomeDaHora(cc.Hora),-10} "
								+ $"{cc.Hora * 24:00.0}h  {Ceu.NomeDaFase(cc.Fase)}");
				}

				// SE TODO MUNDO MARCASSE A MESMA HORA, o horário por planeta não existiria -- foi
				// exatamente esse o defeito do BYOND, onde o `WorldClock` era um só pra tudo.
				double espalhamento = horas.Max() - horas.Min();
				Conferir(espalhamento > 0.1,
					$"os planetas NAO marcam a mesma hora (espalhamento {espalhamento:0.00} do dia)");

				// NAMEK: os três sóis. `HasNight=0` e `HasMoon=0` no `Areas.dm`.
				RelogioDoPlaneta namek = Planetas.Relogio(ZoneKey.Premade("Namek"));
				Conferir(!namek.TemNoite, "Namek nao anoitece (HasNight=0 no DM -- os tres sois)");
				Conferir(!namek.Lua.Existe, "Namek nao tem lua (HasMoon=0 no DM)");

				// TERRA E VEGETA: os dois mundos com lua que o jogador de fato pisa.
				Conferir(Planetas.Relogio(ZoneKey.Premade("Earth")).Lua.Existe, "a Terra tem lua");
				Conferir(Planetas.Relogio(ZoneKey.Premade("Vegeta")).Lua.Existe, "Vegeta tem lua");

				// O ESPAÇO NÃO TEM CÉU NENHUM, e um interior também não: é de lá que sai a regra
				// do DM de que não se olha pra lua de dentro de casa.
				Conferir(!Planetas.Relogio(Espaco.Zona(1)).Lua.Existe, "o espaco nao tem lua");
				Conferir(!Planetas.Relogio(ZoneKey.Interior("Nave", 1)).Lua.Existe,
					"interior nao tem lua (nao se olha pra lua de dentro de casa)");

				_fase = 2;
				break;
			}

			case 2:
			{
				// AS OITO FASES EXISTEM E SÃO DISTINTAS, e a 5 é a cheia. Conferido pela conta
				// pura, sem esperar oito noites.
				RelogioDoPlaneta terra = Planetas.Relogio(ZoneKey.Premade("Earth"));
				var vistas = new HashSet<int>();
				double aceso5 = 0, aceso1 = 1;
				for (int n = 0; n < Ceu.Fases; n++)
				{
					double t = cli.TempoDoMundo + n * terra.SegundosPorDia;
					int f = Ceu.FaseEm(terra, terra.DiaLocal(t));
					vistas.Add(f);
					if (f == Ceu.Cheia) aceso5 = Ceu.AcesoNaFase(f);
					if (f == 1) aceso1 = Ceu.AcesoNaFase(f);
				}
				Conferir(vistas.Count == Ceu.Fases, $"as {Ceu.Fases} fases aparecem em {Ceu.Fases} noites ({vistas.Count})");
				Conferir(Math.Abs(aceso5 - 1) < 1e-9, $"a fase 5 e a CHEIA (disco {aceso5:P0} aceso)");
				Conferir(aceso1 < 1e-9, $"a fase 1 e a NOVA (disco {aceso1:P0} aceso)");

				// A NOITE DE LUA CHEIA TEM QUE SER VISIVELMENTE MAIS CLARA. Sem isso o gatilho do
				// Oozaru seria uma linha de chat sobre um céu que não mudou.
				Color nova = Iluminacao.CorDoCeu(new EstadoDoCeu { Hora = 0, Fase = 1, Altura = 1, Aceso = 0 }, default);
				Color cheia = Iluminacao.CorDoCeu(new EstadoDoCeu { Hora = 0, Fase = 5, Altura = 1, Aceso = 1 }, default);
				float ganho = cheia.Luminance - nova.Luminance;
				Conferir(ganho > 0.05f,
					$"a noite de lua CHEIA e mais clara que a de lua nova (+{ganho:0.000} de luminancia)");

				_faseInicial = (mundo.Ceu ?? default).Fase;
				_horaInicial = (mundo.Ceu ?? default).Hora;
				_t = 0;
				_fase = 3;
				break;
			}

			case 3:
			{
				// A VIRADA DE FASE ACONTECE NO ANOITECER, NUNCA À MEIA-NOITE.
				//
				// É o defeito que a implementação óbvia produz: contar a lua por DIA de
				// calendário (`floor(tempo)`) troca a fase no meio da noite -- o jogador está
				// olhando pra lua cheia e ela vira minguante na frente dele. Aqui a bancada
				// atravessa a meia-noite de propósito e exige que nada mude.
				EstadoDoCeu ceu = mundo.Ceu ?? default;
				if (ceu.Hora < _horaInicial) _cruzouMeiaNoite = true;   // o relógio deu a volta
				if (ceu.Fase != _faseInicial && ceu.Noite && _cruzouMeiaNoite) _viradasNaNoite++;
				_horaInicial = ceu.Hora;

				if (_t < Vigia) return;

				Conferir(_cruzouMeiaNoite || !ceu.Noite,
					_cruzouMeiaNoite ? "o relogio atravessou a meia-noite durante a vigia"
									 : "a vigia nao caiu numa noite (rode com --luateste pra forcar)");
				Conferir(_viradasNaNoite == 0,
					$"a fase NAO virou no meio da noite ({_viradasNaNoite} virada(s) depois da meia-noite)");

				_fase = 4;
				break;
			}

			case 4:
			{
				// O MOSTRADOR TEM QUE CONCORDAR COM O ESTADO. Um `LuaNoCeu` invisível numa noite
				// de lua cheia é o modo de falha que nenhuma conta pega: os números ficam certos e
				// a tela fica vazia.
				//
				// ELE MORA NO HUD, e não mais no céu: a lua saiu do meio da tela porque num jogo
				// de câmera de cima ela passava por cima do combate, e porque fase da lua é
				// informação (o gatilho do Oozaru) e não cenário.
				EstadoDoCeu ceu = mundo.Ceu ?? default;
				LuaNoCeu? lua = Hud.Instancia?.Lua;
				Conferir(lua != null, "o mostrador da lua existe no HUD");
				if (lua != null)
					Conferir(lua.Visible == ceu.LuaNoCeu,
						$"o mostrador concorda com o estado (visivel={lua.Visible}, no ceu={ceu.LuaNoCeu})");

				// O GANCHO DO OOZARU DISPAROU? O servidor só anuncia na TRANSIÇÃO (a lua subindo),
				// e é nesse mesmo ponto que a transformação vai entrar. Conferir a frase é
				// conferir o gancho -- sem isto, um gancho que nunca dispara passaria no teste
				// porque todas as contas continuariam certas.
				foreach (string a in _avisos) _passos.Add("         chat: \"" + a + "\"");
				if (ceu.Cheia)
				{
					Conferir(_avisos.Exists(a => a.Contains("lua cheia")),
						"o servidor ANUNCIOU a lua cheia (e o ponto em que o Oozaru se pendura)");

					// QUEM TEM RABO RECEBE A LINHA A MAIS -- e É ELA que vira a chamada do
					// Oozaru. O servidor decide isso pelo membro "Rabo" do corpo; aqui o teste
					// confere pelo bit que o snapshot já carrega, que é outra fonte. As duas
					// discordarem seria o gancho apontando pra pessoa errada.
					if (cli.Sheet.Rabo)
						Conferir(_avisos.Exists(a => a.Contains("rabo")),
							"quem tem RABO sentiu a lua (a linha que vira `Apeshit()` no port do Oozaru)");
					else
						_passos.Add("         (personagem sem rabo: a linha do Oozaru nao se aplica)");
				}
				else _passos.Add("         (a vigia nao pegou uma lua cheia nascendo -- rode com --luateste)");

				_fase = 90;
				break;
			}

			default:
				_acabou = true;
				GD.Print("\n[ceu] ===== BANCADA DO CEU E DA LUA =====");
				foreach (string l in _passos) GD.Print("[ceu] " + l);
				GD.Print(_falhas.Count == 0
					? "[ceu] ===== TUDO OK ====="
					: $"[ceu] ===== {_falhas.Count} FALHA(S) =====\n[ceu]   " + string.Join("\n[ceu]   ", _falhas));
				break;
		}
	}
}
