using Jandirus.Core.World;

namespace Jandirus.Core.Ai;

/// <summary>O que um habitante esta fazendo da vida agora.</summary>
public enum Afazer : byte
{
	/// <summary>Parado, esperando o proximo afazer.</summary>
	Ocioso,
	/// <summary>Andando ate um ponto perto.</summary>
	Passeando,
	/// <summary>Treinando sozinho (a pose de treino do jogador; o poder NAO sobe -- ver o servidor).</summary>
	Treinando,
	/// <summary>Conversando com outro habitante (baloes curtos, alternados).</summary>
	Conversando,
	/// <summary>Num spar: o cerebro de combate assume, sem golpe letal, ate a vida de um dos dois cair.</summary>
	Sparring,
	/// <summary>Recuperando o folego depois do spar.</summary>
	Descansando,
}

/// <summary>
/// O QUE A ROTINA VE -- so o que o servidor ja sabe, como a <see cref="Percepcao"/> do combate.
/// O vizinho so e procurado quando <see cref="Rotina.PrecisaDeVizinho"/>: e uma varredura da zona,
/// e ela acontece uma vez por decisao, nao uma vez por tique.
/// </summary>
public readonly struct Arredores
{
	public Vec2 Minha { get; init; }
	public double VidaFrac { get; init; }
	public double MeuPoder { get; init; }
	/// <summary>KO ou morto: a rotina congela e larga o parceiro.</summary>
	public bool Caido { get; init; }
	/// <summary>Quis andar no tique anterior e nao saiu do lugar (parede, agua, alguem).</summary>
	public bool Bloqueado { get; init; }

	/// <summary>Um habitante LIVRE (ocioso ou passeando) ao alcance de conversa; 0 = nenhum.</summary>
	public int VizinhoLivre { get; init; }
	public Vec2 DoVizinho { get; init; }

	// --- o parceiro de agora (conversa ou spar) ---
	public bool ParceiroPresente { get; init; }
	public Vec2 DoParceiro { get; init; }
	public double VidaDoParceiro { get; init; }
	public bool ParceiroCaido { get; init; }
	public Afazer AfazerDoParceiro { get; init; }
	/// <summary>Com quem o parceiro acha que esta -- a simetria e conferida dos dois lados.</summary>
	public int ParceiroDoParceiro { get; init; }
}

/// <summary>
/// A ORDEM DA ROTINA PRO SERVIDOR. Como o <see cref="Comando"/>, so pede; quem executa e o servidor,
/// pelos mesmos funis do jogador (`Falar`, a flag de treino da ficha, `PassoDaIa`).
/// </summary>
public readonly struct Ordem
{
	public Vec2 Rumo { get; init; }
	public Vec2 Olhar { get; init; }
	public bool Treinar { get; init; }
	public string? Falar { get; init; }
	/// <summary>Id do parceiro de spar: o servidor entrega ELE como presa ao cerebro de combate.</summary>
	public int Lutar { get; init; }
	/// <summary>Id de quem eu puxo pra conversa (o servidor chama <see cref="Rotina.Chamado"/> nele).</summary>
	public int Chamar { get; init; }
	/// <summary>Id de quem eu convido pro spar (o servidor pergunta a ele por <see cref="Rotina.Convite"/>).</summary>
	public int Convidar { get; init; }

	public static readonly Ordem Parada = new();
}

/// <summary>
/// A VIDA DIARIA DE UM HABITANTE -- a maquina de estados que o dono pediu: *"ocioso/andar, treinar
/// sozinho, conversar com outro NPC proximo, convidar pra spar (aceitar/recusar), spar nao letal com
/// condicao de termino, e voltar a rotina"*.
///
/// ============================ O QUE ELA E, E O QUE ELA NAO E ============================
/// E Core puro e determinista: cada habitante tem o seu `Random` semeado pela semente do corpo, e
/// de (semente, sequencia de arredores) sai sempre a mesma vida. Ela NAO mexe no mundo -- devolve
/// uma <see cref="Ordem"/> por tique e o servidor a executa pelos funis do jogador. E ela NAO luta:
/// no spar ela so diz "a presa e fulano" e o <see cref="Cerebro"/> de combate faz o resto, com o
/// golpe nao-letal que todo NPC ja tem por padrao (`CombatState.Letal`).
///
/// O original nao tem nada disto: o `idle_wander_loop` (PlanetPopulation.dm:122-131) era um passo
/// aleatorio a cada 4,5-9,5 s. O PASSEIO daqui e o herdeiro dele; o resto e novo, a pedido.
///
/// ============================ O TIQUE E BARATO DE PROPOSITO ============================
/// Fora das decisoes, um tique e meia duzia de comparacoes. A unica coisa cara -- achar um vizinho
/// -- so e pedida ao servidor quando <see cref="PrecisaDeVizinho"/>, ou seja, no instante da decisao.
/// Conversa e spar sao SIMETRICOS por conferencia: cada lado olha o afazer e o parceiro do outro
/// todo tique e, se o outro sumiu ou mudou de ideia, volta a rotina sozinho. Nao ha "sessao" a
/// sanear.
/// ============================================================================================
/// </summary>
public sealed class Rotina
{
	/// <summary>A que distancia (px) do destino o passeio conta como chegado.</summary>
	public const float ChegouEmPx = 12f;

	/// <summary>Quantos segundos depois de um convite sem resposta a conversa se encerra sozinha.</summary>
	public const double PrazoDaDespedida = 2.5;

	/// <summary>Um tique de folga pra simetria: o parceiro pode ser ticado antes ou depois de mim.</summary>
	private const double FolgaDaSimetria = 0.5;

	public Afazer Afazer { get; private set; } = Afazer.Ocioso;
	public int Parceiro { get; private set; }
	/// <summary>Segundos no afazer atual.</summary>
	public double NoAfazer { get; private set; }
	public string Porque { get; private set; } = "nasceu";
	public readonly int MeuId;

	// Contadores -- pra bancada e pro `--diagia`, nunca pra decidir nada.
	public int Conversas, Convites, Aceites, Recusas, Spars;

	private readonly RotinaConfig _cfg;
	private readonly Random _rng;

	/// <summary>
	/// SO OCIO E PASSEIO: e a rotina de quem NAO e habitante (chefe, inimigo) quando esta sem presa.
	/// Eles herdam o passeio que o corpo antigo tinha (o `idle_wander_loop`), e nada mais: um chefe
	/// nao puxa conversa com o povo, nao treina na praca e nao convida ninguem pra spar. A bancada
	/// do povoamento viu Turles conversando e passeando de maos dadas com o habitante que ele
	/// deveria estar cacando.
	/// </summary>
	public readonly bool SoPasseia;

	private double _prazo;
	private Vec2 _destino;
	private double _proximaFala;
	private bool _iniciei, _convidei, _esperandoResposta;
	private string? _fraseAgendada;

	public Rotina(RotinaConfig cfg, ulong semente, int meuId, bool soPasseia = false)
	{
		_cfg = cfg;
		MeuId = meuId;
		SoPasseia = soPasseia;
		_rng = Jandirus.Core.Npc.SorteioDeNpc.Sorteador(semente, "rotina");
		// A PRIMEIRA ESPERA E SORTEADA por corpo: sem isso os quarenta habitantes da Terra decidiriam
		// no mesmo tique, e o que se veria seria a cidade inteira andando em bloco.
		_prazo = Entre(cfg.OciosoMin, cfg.OciosoMax);
	}

	/// <summary>Este tique e o da decisao: o servidor so procura vizinho quando isto e verdade.</summary>
	public bool PrecisaDeVizinho => !SoPasseia && Afazer == Afazer.Ocioso && NoAfazer >= _prazo;

	/// <summary>Quanto a conversa atual vai durar (quem e chamado recebe o mesmo prazo).</summary>
	public double PrazoDaConversa => _prazo;

	public Ordem Tique(double dt, in Arredores a)
	{
		if (a.Caido)
		{
			// No chao a vida para. Conversa ou spar acabam aqui; o outro lado ve e acaba tambem.
			if (Afazer is Afazer.Conversando or Afazer.Sparring) Voltar("caido");
			return Ordem.Parada;
		}

		string? fala = _fraseAgendada;
		_fraseAgendada = null;
		Ordem o = Afazer switch
		{
			Afazer.Ocioso => NoAfazer >= _prazo ? Decidir(a) : Ordem.Parada,
			Afazer.Passeando => Passeio(a),
			Afazer.Treinando => Treino(),
			Afazer.Conversando => Conversa(a, ref fala),
			Afazer.Sparring => Spar(a, ref fala),
			Afazer.Descansando => Descanso(),
			_ => Ordem.Parada,
		};
		NoAfazer += dt;
		return fala != null && o.Falar == null ? o with { Falar = fala } : o;
	}

	// =====================================================================
	// AS CHAMADAS DE FORA -- o servidor as faz em nome do OUTRO habitante
	// =====================================================================

	/// <summary>Alguem puxou conversa comigo. So aceito se estiver livre; a duracao e a dele.</summary>
	public bool Chamado(int quem, double duracao)
	{
		if (SoPasseia || Afazer is not (Afazer.Ocioso or Afazer.Passeando)) return false;
		Afazer = Afazer.Conversando;
		NoAfazer = 0;
		Parceiro = quem;
		_iniciei = false;
		_convidei = false;
		_esperandoResposta = false;
		_prazo = duracao;
		_proximaFala = _cfg.SegundosEntreFalas;   // quem foi chamado fala em segundo
		Conversas++;
		Porque = "chamado pra conversar";
		return true;
	}

	/// <summary>
	/// O convite pro spar chegou. A "inteligencia minima": recusa quem o esmagaria (ou quem ele
	/// esmagaria), e fora isso decide na sorte da config. Aceitar ja entra no spar.
	/// </summary>
	/// <param name="razaoDePoder">poder de quem convida / o meu</param>
	public bool Convite(int quem, double razaoDePoder)
	{
		if (Afazer != Afazer.Conversando || Parceiro != quem)
		{
			Recusas++;
			Porque = "recusa: nao estou nessa conversa";
			return false;
		}
		bool assusta = razaoDePoder > _cfg.RazaoDePoderQueAssusta || razaoDePoder < 1 / _cfg.RazaoDePoderQueAssusta;
		bool aceita = !assusta && _rng.NextDouble() < _cfg.ChanceDeAceitar;
		if (aceita)
		{
			Aceites++;
			_fraseAgendada = Frase(_cfg.Frases.Aceite);
			EntrarNoSpar(quem, "aceitei o spar");
			return true;
		}
		Recusas++;
		_fraseAgendada = Frase(_cfg.Frases.Recusa);
		_prazo = NoAfazer + PrazoDaDespedida;
		Porque = assusta ? $"recusa: poder desigual ({razaoDePoder:0.0}x)" : "recusa: sem vontade";
		return false;
	}

	/// <summary>A resposta ao MEU convite.</summary>
	public void Resposta(bool aceitou)
	{
		_esperandoResposta = false;
		if (aceitou) EntrarNoSpar(Parceiro, "ele aceitou o spar");
		else _prazo = NoAfazer + PrazoDaDespedida;
	}

	/// <summary>Apareceu uma presa de verdade (um jogador provocou): a vida diaria para na hora.</summary>
	public void Interromper(string porque) => Voltar(porque);

	// =====================================================================
	// OS AFAZERES
	// =====================================================================

	private Ordem Decidir(in Arredores a)
	{
		if (!SoPasseia && a.VizinhoLivre != 0 && _rng.NextDouble() < _cfg.ChanceDeConversar)
		{
			Afazer = Afazer.Conversando;
			NoAfazer = 0;
			Parceiro = a.VizinhoLivre;
			_iniciei = true;
			_convidei = false;
			_esperandoResposta = false;
			_prazo = Entre(_cfg.ConversaMin, _cfg.ConversaMax);
			_proximaFala = 0;   // quem puxa fala primeiro
			Conversas++;
			Porque = "puxar conversa";
			return new Ordem { Chamar = Parceiro, Olhar = Rumo(a.Minha, a.DoVizinho) };
		}
		if (!SoPasseia && _rng.NextDouble() < _cfg.ChanceDeTreinar)
		{
			Afazer = Afazer.Treinando;
			NoAfazer = 0;
			_prazo = Entre(_cfg.TreinoMin, _cfg.TreinoMax);
			Porque = "treinar sozinho";
			return new Ordem { Treinar = true, Falar = _rng.NextDouble() < 0.5 ? Frase(_cfg.Frases.Treino) : null };
		}
		Afazer = Afazer.Passeando;
		NoAfazer = 0;
		_prazo = Entre(_cfg.PasseioMin, _cfg.PasseioMax);
		double ang = _rng.NextDouble() * Math.Tau;
		float longe = _cfg.PasseioTiles * ZoneCollision.TileSize * (float)(0.5 + 0.5 * _rng.NextDouble());
		_destino = a.Minha + new Vec2((float)Math.Cos(ang), (float)Math.Sin(ang)) * longe;
		Porque = "passear";
		return Passeio(a);
	}

	private Ordem Passeio(in Arredores a)
	{
		Vec2 d = _destino - a.Minha;
		if (d.Length <= ChegouEmPx || a.Bloqueado || NoAfazer >= _prazo)
		{
			Voltar(a.Bloqueado ? "passeio: bateu em algo" : d.Length <= ChegouEmPx ? "passeio: chegou" : "passeio: desistiu");
			return Ordem.Parada;
		}
		Vec2 rumo = d.Normalized();
		return new Ordem { Rumo = rumo, Olhar = rumo };
	}

	private Ordem Treino()
	{
		if (NoAfazer >= _prazo) { Voltar("treinou o bastante"); return Ordem.Parada; }
		return new Ordem { Treinar = true };
	}

	private Ordem Conversa(in Arredores a, ref string? fala)
	{
		bool sumiu = !a.ParceiroPresente
			|| (NoAfazer > FolgaDaSimetria && !_esperandoResposta
				&& (a.AfazerDoParceiro != Afazer.Conversando || a.ParceiroDoParceiro != MeuId));
		if (sumiu)
		{
			Voltar("conversa: o outro se foi");
			return new Ordem { Falar = fala };
		}
		Vec2 olhar = Rumo(a.Minha, a.DoParceiro);
		if (NoAfazer >= _prazo)
		{
			if (_iniciei && !_convidei && _rng.NextDouble() < _cfg.ChanceDeConvidar)
			{
				_convidei = true;
				_esperandoResposta = true;
				_prazo = NoAfazer + PrazoDaDespedida;
				Convites++;
				Porque = "convidar pro spar";
				return new Ordem { Convidar = Parceiro, Olhar = olhar, Falar = Frase(_cfg.Frases.Convite) };
			}
			Voltar("conversa acabou");
			return new Ordem { Falar = fala };
		}
		if (!_esperandoResposta && NoAfazer >= _proximaFala)
		{
			_proximaFala += 2 * _cfg.SegundosEntreFalas;   // os dois alternam
			fala ??= Frase(_cfg.Frases.Conversa);
		}
		return new Ordem { Olhar = olhar, Falar = fala };
	}

	private Ordem Spar(in Arredores a, ref string? fala)
	{
		bool parceiroParou = !a.ParceiroPresente
			|| (NoAfazer > FolgaDaSimetria && (a.AfazerDoParceiro != Afazer.Sparring || a.ParceiroDoParceiro != MeuId));
		string? fim =
			parceiroParou ? "spar: o outro parou"
			: a.VidaFrac <= _cfg.VidaQueEncerraOSpar ? "spar: chega, estou ferido"
			: a.VidaDoParceiro <= _cfg.VidaQueEncerraOSpar ? "spar: chega, ele esta ferido"
			: a.ParceiroCaido ? "spar: ele caiu"
			: NoAfazer >= _cfg.SparMax ? "spar: deu a hora"
			: null;
		if (fim != null)
		{
			Afazer = Afazer.Descansando;
			NoAfazer = 0;
			Parceiro = 0;
			_prazo = Entre(_cfg.DescansoMin, _cfg.DescansoMax);
			Porque = fim;
			return new Ordem { Falar = fala ?? Frase(_cfg.Frases.FimDoSpar) };
		}
		return new Ordem { Lutar = Parceiro, Falar = fala };
	}

	private Ordem Descanso()
	{
		if (NoAfazer >= _prazo) Voltar("descansou");
		return Ordem.Parada;
	}

	private void EntrarNoSpar(int quem, string porque)
	{
		Afazer = Afazer.Sparring;
		NoAfazer = 0;
		Parceiro = quem;
		_esperandoResposta = false;
		Spars++;
		Porque = porque;
	}

	private void Voltar(string porque)
	{
		Afazer = Afazer.Ocioso;
		NoAfazer = 0;
		Parceiro = 0;
		_esperandoResposta = false;
		_prazo = Entre(_cfg.OciosoMin, _cfg.OciosoMax);
		Porque = porque;
	}

	private double Entre(double min, double max) => min + _rng.NextDouble() * Math.Max(0, max - min);

	private string? Frase(string[] opcoes) => opcoes.Length == 0 ? null : opcoes[_rng.Next(opcoes.Length)];

	private static Vec2 Rumo(Vec2 de, Vec2 para)
	{
		Vec2 d = para - de;
		return d.LengthSquared > 1e-6f ? d.Normalized() : Vec2.Zero;
	}
}
