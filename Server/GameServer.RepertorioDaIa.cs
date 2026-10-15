using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O REPERTORIO DA IA, DO LADO DO SERVIDOR (dono, 2026-10-13: *"IAs q controlam o personagem ... deveriam usar todas as
/// skills disponiveis do jogador descontrolado, se a IA n sabe usar, melhore ela pra deixa-la mais inteligente"*).
///
/// O cerebro decide (`Core/Ai/Cerebro.Repertorio.cs`) e o verb recusa, como sempre. O que mora aqui e o que so o
/// servidor pode responder ou fazer:
///
///   * O QUE ESTE CORPO SABE das tecnicas que nao viajam (<see cref="ArsenalDeLuta"/>), no mesmo relogio de 1 Hz e
///     pela mesma pergunta do jogador (`SabeTecnica`) -- com o preco resolvido pra ficha dele e, pros reforcos, se
///     estao de pe agora;
///   * O SEGUNDO APERTO das tecnicas que se seguram (<see cref="TickDoSegundoApertoDaIa"/>): o cerebro so sabe dar
///     um pulso, e a barragem ligada, a esfera que carrega e o rugido terminam com outro aperto do mesmo verb. E o
///     desenho do prazo do raio (`TickDoPrazoDeRaioDaIa`): o pulso abre, um relogio fecha;
///   * O SILENCIO de quem e dirigido (<see cref="_iaApertando"/>): a frase de um verb que a IA apertou nao vai pro
///     dono do corpo -- ele nao apertou nada;
///   * HA TIRO VINDO? (<see cref="HaTiroVindo"/>), a pergunta de quem sabe rebater.
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// O QUE O CORPO SABE
	// =====================================================================

	/// <summary>
	/// O REPERTORIO DE LUTA DESTE CORPO -- as linhas de `TecnicasDeLuta` que ele SABE, com o preco nas duas moedas
	/// resolvido pra ficha dele. Mesma disciplina do <see cref="ArsenalDeLonge"/>: duas passadas (a primeira so anota
	/// quem se sabe, na pilha), a mesma pergunta do jogador, e o array e o DO CORPO -- reescrito no lugar a cada
	/// leitura, entao um corpo que sabe quarenta tecnicas nao deixa quarenta `Tiro` de lixo por segundo.
	/// </summary>
	private Arsenal ArsenalDeLuta(ServerPlayer pl, out bool temInvestida, out bool sabeRebater)
	{
		temInvestida = sabeRebater = false;
		if (_skills == null) return Arsenal.Vazio;

		ReadOnlySpan<TecnicasDeLuta.Linha> linhas = TecnicasDeLuta.Todas;
		Span<bool> sabe = stackalloc bool[linhas.Length];
		int quantas = 0;
		for (int i = 0; i < linhas.Length; i++)
		{
			sabe[i] = SabeATecnicaDeLuta(pl, linhas[i].Gate);
			if (sabe[i]) quantas++;
		}
		if (quantas == 0) return Arsenal.Vazio;

		Tiro[] meus = pl.RepertorioDaIa is { } guardado && guardado.Length == quantas
			? guardado
			: (pl.RepertorioDaIa = new Tiro[quantas]);

		Fighter f = pl.Ficha;
		int k = 0;
		for (int i = 0; i < linhas.Length; i++)
		{
			if (!sabe[i]) continue;
			TecnicasDeLuta.Linha l = linhas[i];
			temInvestida |= l.Uso == Uso.Investida;
			sabeRebater |= (l.Exige & Exige.TiroVindo) != 0;
			meus[k++] = new Tiro
			{
				Id = l.Id,
				Uso = l.Uso,
				AlcanceMin = l.AlcanceMinTiles * ZoneCollision.TileSize,
				AlcanceMax = l.AlcanceMaxTiles * ZoneCollision.TileSize,
				// O PRECO SAI DA LINHA, que espelha a conta do verb -- ver o cabecalho de `TecnicasDeLuta`.
				CustoDeKi = l.CustoDeKi(f),
				CustoDeFolego = l.CustoDeFolego?.Invoke(f) ?? 0,
				Exige = l.Exige,
				Descanso = l.Descanso,
				Grupo = l.Grupo,
				IdDeDesligar = l.Desliga,
				Ligada = l.Uso == Uso.Reforco && ReforcoDePe(pl, l.Gate),
				Precisao = 1,
			};
		}
		return new Arsenal(meus);
	}

	/// <summary>
	/// ELE SABE ESTA TECNICA? A pergunta do jogador (`SabeTecnica`) -- menos pro Regenerar, que tem duas portas: a
	/// skill, e o corpo de Namekuseijin. Quem responde por ele e o <see cref="PodeRegenerar"/>, o mesmo do verb.
	/// </summary>
	private bool SabeATecnicaDeLuta(ServerPlayer pl, string gate) =>
		gate == "Regenerate" ? PodeRegenerar(pl) : SabeTecnica(pl, gate);

	/// <summary>
	/// ESTE REFORCO ESTA DE PE? Quase todos moram no registro de buffs com o id do proprio verb; tres tem casa
	/// propria (o Ki Shield e anterior ao registro; as miragens e a copia nao sao buff).
	/// </summary>
	private bool ReforcoDePe(ServerPlayer pl, string gate) => gate switch
	{
		"Ki_Shield" => EscudoDePeG3(pl),
		"Zanzoken_Afterimage" => _miragensG14.ContainsKey(pl.Id),
		"SplitForm" => TemCopiaDePeG12(pl.Id),
		// (sem o `BuffsDe`: ele CRIA o dicionario de quem nao tem, e esta pergunta roda pra todo corpo dirigido)
		_ => _buffs.TryGetValue(pl.Id, out Dictionary<string, BuffAtivo>? meus) && meus.ContainsKey(gate),
	};

	private bool TemCopiaDePeG12(int dono)
	{
		foreach (SplitformG12 s in _splitformsG12.Values)
			if (s.Master == dono) return true;
		return false;
	}

	/// <summary>Este corpo e uma copia da Divisao do Corpo DESTE dono? (A fera nao caca as proprias copias -- `PresaDaFera`.)</summary>
	private bool EhCopiaDeG12(int corpo, int dono) =>
		_splitformsG12.Count > 0 && _splitformsG12.TryGetValue(corpo, out SplitformG12? s) && s.Master == dono;

	// =====================================================================
	// HA TIRO VINDO?
	// =====================================================================

	/// <summary>A distancia em que o corpo dirigido percebe o tiro que vem: quatro tiles -- o tempo de um gesto.</summary>
	private const float FaroDeTiroDaIa = 4 * ZoneCollision.TileSize;

	/// <summary>
	/// HA KI ALHEIO VINDO PRA CIMA DESTE CORPO? E o `Percepcao.HaTiroVindo`, perguntado a 30 Hz e SO pra quem sabe
	/// rebater (`Capacidades.SabeRebater`). Sem criar lista, pela razao do `HaKiPraAnular`: o `ProjeteisDaZona`
	/// nasce uma lista vazia pra zona que nao tem tiro nenhum. O criterio de rumo e o mesmo de la.
	/// </summary>
	private bool HaTiroVindo(ServerPlayer pl)
	{
		if (!_projeteis.TryGetValue(pl.Zone.Hash, out List<Projetil>? daZona) || daZona.Count == 0) return false;

		foreach (Projetil p in daZona)
		{
			if (!p.Vivo || p.Dono == pl.Id) continue;
			Vec2 ateMim = pl.Pos - p.Pos;
			float dist = ateMim.Length, passo = p.Rumo.Length;
			if (dist > FaroDeTiroDaIa || passo < 1e-3f) continue;   // longe demais, ou parado no ar (uma mina)
			if (dist < 1e-3f) return true;
			if ((p.Rumo.X * ateMim.X + p.Rumo.Y * ateMim.Y) / (dist * passo) >= RumoQueVemPraCa) return true;
		}
		return false;
	}

	// =====================================================================
	// O APERTO DA IA: O SILENCIO E O SEGUNDO APERTO
	// =====================================================================

	/// <summary>
	/// O CORPO CUJO VERB A IA ESTA APERTANDO AGORA -- nulo fora do aperto. O <see cref="Avisar"/> nao manda a frase
	/// pra ele.
	///
	/// ============================ POR QUE O DONO DO CORPO NAO OUVE ============================
	/// Todo verb fala com quem o apertou: *"isso pede 300 de energia"*, *"seus golpes especiais ainda se
	/// recompoem"*, *"voce encaixa um jab e um cruzado"*. Com quatro tiros na mao da IA isso era raro; com o livro
	/// inteiro, o jogador possuido receberia uma linha de sistema a cada segundo e meio sobre teclas que ele nao
	/// apertou -- e metade delas seriam recusas, porque o cerebro pede e quem sabe se da e o verb. O que a fera FAZ
	/// ele ve na tela (o golpe, o tiro, o reforco aceso); a conversa do verb com quem aperta nao e pra ele.
	///
	/// O que o verb diz ao ALVO (*"a onda de choque de Fulano joga voce longe"*) continua saindo: so a frase pro
	/// proprio corpo dirigido e calada. NPC nao tem a quem dizer (`Peer` nulo) -- pra ele isto nao muda nada.
	/// ========================================================================================
	/// </summary>
	private ServerPlayer? _iaApertando;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia U): a frase do verb que a IA apertou vai pro dono do corpo, como na v0.2.0.</summary>
	internal static bool IaFalaComODonoDeTeste;

	/// <summary>Quantas frases o silencio de cima segurou desde o boot. So a bancada le (um `++` por frase calada).</summary>
	private int _frasesCaladasDaIa;

	/// <summary>A BANCADA ESCUTA AQUI: cada verb que um cerebro apertou, na ordem. Nula em jogo.</summary>
	internal List<(int Quem, string Tecnica)>? EscutaDeTecnicasDaIa;

	private sealed class ApertoQueFalta
	{
		public string Tecnica = "";
		public long Em;
		public int Feitos;
	}

	/// <summary>Quem esta com uma tecnica que se SEGURA, apertada pela IA, esperando o segundo aperto.</summary>
	private readonly Dictionary<int, ApertoQueFalta> _segundoApertoDaIa = [];

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia U): ninguem da o segundo aperto -- a barragem que a IA liga fica ligada.</summary>
	internal static bool SemSegundoApertoDeTeste;

	/// <summary>Entre o segundo aperto e os seguintes, se a tecnica ainda nao acabou (a Death Ball tem tres fases).</summary>
	private const long MsEntreApertosDaIa = 3000;

	/// <summary>
	/// O CORPO ESTA COM AS MAOS OCUPADAS POR UM ATAQUE DE KI? O raio canalizado, a disputa de feixes, uma tecnica que se
	/// segura a espera do segundo aperto, ou (v0.3.0) a SUPERNOVA do ultimato erguida sobre a cabeca. O cerebro NAO SABE
	/// disso -- ele continua mandando --, e quem recusa por ele e o atuador (`AplicarComando`, onde esta contado por que).
	///
	/// A SUPERNOVA PRECISAVA DESTA PORTA, e nao so da cena: a cena prende o PASSO (`PodeMexerOCorpo`), mas o soco tem a
	/// investida dele (`Atacar` -> `Aproximar`) e a tecnica nao passa pelo passo. Medido na `--diagv030`: o Freeza erguendo
	/// o sol nocauteou o jogador a cinco tiles.
	/// </summary>
	private bool MaosOcupadasDaIa(ServerPlayer npc) =>
		EnraizadoPorKi(npc.Id) || _emEmbateDeKi.ContainsKey(npc.Id) || _segundoApertoDaIa.ContainsKey(npc.Id)
		|| (npc.SupernovaDesde != 0 && !SupernovaComAsMaosLivresDeTeste);

	/// <summary>DEFEITO INJETADO (bancada `--v030teste`, familia U): a Supernova nao ocupa as maos -- o chefe investe, soca e atira com o sol erguido.</summary>
	internal static bool SupernovaComAsMaosLivresDeTeste;

	/// <summary>Esta tecnica e das que se seguram, e o corpo a esta segurando agora?</summary>
	private bool AindaSeSegura(ServerPlayer pl) => BlastingG12(pl.Id) || _rugindo.ContainsKey(pl.Id);

	/// <summary>
	/// DEPOIS DE UM APERTO DA IA: se a tecnica e das que pedem um segundo aperto (`SegundoAperto` na linha dela) e o
	/// verb ACEITOU (o corpo esta segurando alguma coisa), o relogio e armado. Recusada, nao ha o que soltar.
	/// </summary>
	private void DepoisDoApertoDaIa(ServerPlayer npc, string tecnica)
	{
		npc.Cerebro?.Apertou(tecnica);   // (o rodizio do cerebro conta o que SAIU -- ver `Cerebro.Apertou`)
		EscutaDeTecnicasDaIa?.Add((npc.Id, tecnica));

		// A COPIA QUE A IA FAZ JA NASCE COM ORDEM. No DM (e pro jogador, aqui) ela nasce PARADA ate o dono clicar nela
		// e escolher (`Split Forms.dm:28-68`); o corpo dirigido nao clica em nada, e uma copia parada ao lado dele e so
		// um boneco. A ordem e a de sempre, pelo verb de sempre: atacar quem estiver mais perto.
		if (tecnica == "SplitForm" && TemCopiaDePeG12(npc.Id))
		{
			_iaApertando = npc;
			try { OrdenarSplitformsG12(npc, FuncaoDeSplitformG12.AtacarPerto); }
			finally { _iaApertando = null; }
		}

		double segundos = TecnicasDeLonge.Get(tecnica)?.SegundoAperto ?? TecnicasDeLuta.Get(tecnica)?.SegundoAperto ?? 0;
		if (segundos <= 0 || SemSegundoApertoDeTeste || !AindaSeSegura(npc)) return;
		_segundoApertoDaIa[npc.Id] = new ApertoQueFalta { Tecnica = tecnica, Em = NowMs() + (long)(segundos * 1000) };
	}

	/// <summary>
	/// O SEGUNDO APERTO DE QUEM NAO TEM TECLADO. Vencido o prazo da linha, o MESMO verb e apertado de novo pelo cano
	/// do jogador -- a barragem para, o rugido estoura, a Genkidama sai, a Death Ball larga a guia. Se a tecnica
	/// ainda se segura depois dele (a Death Ball que so parou de carregar), vem outro, ate quatro.
	///
	/// QUEM VOLTOU A DIRIGIR APERTA SOZINHO: se a posse acabou no meio (a lua saiu, a furia passou), o corpo tem dono
	/// de novo e o relogio some -- ele ve a tecnica ligada e a tecla e dele.
	/// </summary>
	private void TickDoSegundoApertoDaIa()
	{
		if (_segundoApertoDaIa.Count == 0) return;

		long agora = NowMs();
		foreach ((int id, ApertoQueFalta a) in _segundoApertoDaIa.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Cerebro == null || !AindaSeSegura(pl))
			{
				_segundoApertoDaIa.Remove(id);
				continue;
			}
			if (agora < a.Em) continue;

			_iaApertando = pl;
			try { UsarHabilidade(pl, a.Tecnica); }
			finally { _iaApertando = null; }
			EscutaDeTecnicasDaIa?.Add((pl.Id, a.Tecnica));

			if (++a.Feitos >= 4 || !AindaSeSegura(pl)) _segundoApertoDaIa.Remove(id);
			else a.Em = agora + MsEntreApertosDaIa;
		}
	}

	/// <summary>Quem saiu leva o relogio junto (id de jogador se reusa) -- inscrito no `EsquecerTecnicas`.</summary>
	private void EsquecerRepertorioDaIa(int id) => _segundoApertoDaIa.Remove(id);
}
