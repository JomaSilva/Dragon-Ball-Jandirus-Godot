using Godot;
using Jandirus.Core.Magic;

namespace Jandirus.Server;

/// <summary>
/// ============================ AS ESFERAS POR JOGADOR (dono, 2026-10-31, o pedido 195) ============================
/// *"faça na aba de adm tem um botao q faz esferas de namek e de jogadores se tornarem client side assim cada um tendo seu set, ou voltando ao
/// padrao (1 set de cada no server) , esferas como black star e super esfera nunca podem ser client side, sempre server"* -- e *"Deixando
/// dragon balls normais e de jogadores sendo locais e nao server coisas como forçar esferas reativarem mais cedo n vai mais funcionar/ n faz
/// sentido, logo quando essa opção do server estiver ligada, essa opção vai ser desativada"*.
///
/// O MODO e uma chave do servidor (<see cref="_esferasPorJogador"/>, no `esferas.json`), ligada e desligada pelo admin
/// (`admin_esferas_jogador`, o cartao "Esferas do Dragão" da aba Admin). Ligado:
///   * CADA JOGADOR TEM A SUA COPIA de cada set comum -- o Porunga eterno de Namek e os sets erguidos por jogadores (`SetDeEsferas.Base` e
///     `SoDe`): um set inteiro, com as sete espalhadas pelo planeta num sorteio so dele, a mesma estatua, o mesmo dragao, o mesmo poder, os
///     mesmos pedidos e a mesma espera. A copia nasce quando ele pisa no planeta do set (o tique das esferas) e fica no disco: o que ele
///     juntou continua juntado.
///   * ELE SO VE E SO TOCA AS SUAS (<see cref="VeOSet"/>): o pacote das esferas e de cada um (`OQueAZonaMostra`), e o radar, o pegar, o
///     invocar e o `db_ver` so enxergam a copia dele. Dois jogadores no mesmo planeta cacam cada um as suas sete, e cada um invoca o seu
///     dragao -- que so ele ve.
///   * O SET DO SERVIDOR DORME POR BAIXO: ninguem o ve nem o toca, e as estatuas continuam no chao (sao dele). Quem carregava esferas do
///     servidor quando o modo ligou as larga ali mesmo, e o dragao do servidor de pe vai embora.
///   * REFAZER O SET (o `db_refazer`: reconfigurar a estatua e espalhar as sete de novo -- o "forcar as esferas a reativarem" do dono) FICA
///     DESLIGADO: cada um tem as proprias sete, e refazer o set do servidor nao mexeria em nenhuma delas. O resto da estatua continua --
///     reviver (as copias seguem a inercia dela) e derrubar (as copias caem junto).
///   * AS BLACK STAR E AS SUPER ESFERAS NUNCA: sao sistemas a parte (`_setNegro`, `_supers`), fora do `_sets`, e nada aqui as copia.
/// Desligado (o padrao): as copias somem todas -- com as esferas e o dragao de pe delas -- e o set do servidor volta a ser o de todo mundo.
/// ===============================================================================================================================
/// Bancada: `--esferateste`, familia 15.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O modo: falso = um set de cada no servidor (o padrao); verdadeiro = cada jogador com a sua copia. Vai pro `esferas.json`.</summary>
	private bool _esferasPorJogador;

	/// <summary>O primeiro id de copia: fora dos sets de jogador (1..999), das Black Star (1000) e do Super Shenlong (2000).</summary>
	private const int PrimeiroIdDeCopia = 100_000;

	/// <summary>A frase de quem tenta refazer a estatua com o modo ligado.</summary>
	private const string FraseDoRefazerDesligado =
		"com as Esferas POR JOGADOR (uma opção do servidor), a estátua não se refaz: cada jogador tem as próprias sete, e refazer o set do "
		+ "servidor não mexeria em nenhuma delas.";

	/// <summary>DEFEITO INJETADO (`--esferateste`, familia 15): todo mundo ve todas as copias -- o "por jogador" de nome so. Lido por <see cref="VeOSet"/>. Falso em jogo, sempre.</summary>
	internal static bool CopiasVisiveisATodosDeTeste;

	/// <summary>DEFEITO INJETADO (`--esferateste`, familia 15): refazer a estatua continua valendo com o modo ligado. Lido pelo `RefazerOSet`. Falso em jogo, sempre.</summary>
	internal static bool RefazerComAsCopiasDeTeste;

	/// <summary>(bancada) A copia nasce tambem pra corpo sem tela -- os corpos forjados da `--esferateste`. Falso em jogo, sempre.</summary>
	internal static bool CopiaAteDeCorpoSemTelaDeTeste;

	/// <summary>O SET QUE ESTE JOGADOR ENXERGA E TOCA: sem o modo, os do servidor; com ele, so as copias DELE.</summary>
	internal bool VeOSet(ServerPlayer pl, SetDeEsferas s) =>
		!_esferasPorJogador
			? s.Base == 0
			: s.Base != 0 && (CopiasVisiveisATodosDeTeste || string.Equals(s.SoDe, pl.Assinatura, StringComparison.Ordinal));

	/// <summary>A esfera e de um set que este jogador enxerga (a de set desconhecido, como sempre, aparece).</summary>
	internal bool VeAEsfera(ServerPlayer pl, Esfera e) => SetPorId(e.Set) is not { } s || VeOSet(pl, s);

	/// <summary>O set e de jogador -- o dele, ou o do servidor de que ele e copia (a forca dividida, a frase do `db_ver`).</summary>
	internal static bool EhSetDeJogador(SetDeEsferas s) => Esferas.SetDeJogador(s.Id) || (s.Base != 0 && Esferas.SetDeJogador(s.Base));

	/// <summary>A copia que este jogador tem deste set do servidor (nula se ainda nao nasceu).</summary>
	internal SetDeEsferas? CopiaDe(ServerPlayer pl, SetDeEsferas b) =>
		_sets.Find(s => s.Base == b.Id && string.Equals(s.SoDe, pl.Assinatura, StringComparison.Ordinal));

	private int ProximoIdDeCopia() => Math.Max(PrimeiroIdDeCopia, _sets.Count == 0 ? 0 : _sets.Max(s => s.Id) + 1);

	/// <summary>
	/// AS COPIAS NASCEM E SEGUEM O SET DO SERVIDOR -- chamado do tique das esferas. A copia sem o set dela (derrubado, enterrado com o mundo)
	/// some; a que sobra acompanha a inercia dele (o reviver e a morte do criador sao da estatua); e, com o modo ligado, quem esta num planeta
	/// com estatua ganha a sua.
	/// </summary>
	private void ManterAsCopiasDasEsferas()
	{
		foreach (SetDeEsferas orfa in _sets.Where(s => s.Base != 0 && !_sets.Any(b => b.Id == s.Base && b.Base == 0)).ToList())
			ApagarACopia(orfa);
		foreach (SetDeEsferas c in _sets.Where(s => s.Base != 0))
			if (_sets.Find(b => b.Id == c.Base) is { } dela) c.Inerte = dela.Inerte;

		if (!_esferasPorJogador) return;
		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			if ((pl.Peer == null && !CopiaAteDeCorpoSemTelaDeTeste) || !EhPessoa(pl) || pl.Assinatura.Length == 0) continue;
			foreach (SetDeEsferas b in _sets.Where(s => s.Base == 0 && s.Zona.Hash == pl.Zone.Hash).ToList())
				if (CopiaDe(pl, b) == null) CriarACopia(pl, b);
		}
	}

	/// <summary>
	/// A COPIA DE UM JOGADOR: o set do servidor inteiro -- a estatua, o dragao, o poder, os pedidos, a espera e a inercia --, com as sete dela
	/// espalhadas pelo planeta num sorteio proprio (outro sal, outras vagas).
	/// </summary>
	private SetDeEsferas CriarACopia(ServerPlayer pl, SetDeEsferas b)
	{
		var c = new SetDeEsferas
		{
			Id = ProximoIdDeCopia(), Base = b.Id, SoDe = pl.Assinatura, Eterno = b.Eterno,
			Ex = b.Ex, Ey = b.Ey, CriadorSig = b.CriadorSig, CriadorConta = b.CriadorConta, CriadorNome = b.CriadorNome,
			Poder = b.Poder, Desejos = b.Desejos, OffTime = b.OffTime, AtivoEm = b.AtivoEm, Inerte = b.Inerte, TemSupremo = b.TemSupremo,
			NomeDoDragao = b.NomeDoDragao, TituloDoDragao = b.TituloDoDragao, Dragao = b.Dragao, Folha = b.Folha,
			Desligados = [.. b.Desligados],
		};
		c.PorZona(b.Zona);
		_sets.Add(c);
		for (int n = 1; n <= Esferas.Total; n++)
		{
			var e = new Esfera { Set = c.Id, Numero = n };
			e.PorZona(c.Zona);
			_esferas.Add(e);
		}
		EspalharOSet(c);
		GD.Print($"[server] esferas por jogador: {pl.Name} ganhou a copia #{c.Id} do set {b.Etiqueta}");
		return c;
	}

	/// <summary>A copia some -- com as esferas dela e o dragao de pe.</summary>
	private void ApagarACopia(SetDeEsferas c)
	{
		if (_invocacoes.ContainsKey(c.Id)) FecharAInvocacao(c.Id, "");
		_esferas.RemoveAll(e => e.Set == c.Id);
		_sets.Remove(c);
	}

	/// <summary>
	/// O ADMIN LIGA OU DESLIGA. Ligando: quem carrega esferas do servidor as larga ali mesmo, e o dragao do servidor de pe vai embora (ninguem
	/// mais toca naquele set); desligando: as copias somem todas. O mundo fica sabendo, e as telas de todo planeta com gente se refazem.
	/// </summary>
	internal void TrocarAsEsferasPorJogador(bool ligar)
	{
		if (ligar == _esferasPorJogador) return;
		_esferasPorJogador = ligar;
		if (ligar)
		{
			foreach (Esfera e in _esferas.Where(x => x.Portador != 0 && SetPorId(x.Set) is { Base: 0 }))
				if (_players.TryGetValue(e.Portador, out ServerPlayer? quem))
				{
					e.Portador = 0;
					e.PorZona(quem.Zone);
					e.X = quem.Pos.X;
					e.Y = quem.Pos.Y;
				}
			foreach (Invocacao i in _invocacoes.Values.ToList())
				if (SetPorId(i.Set) is { Base: 0 } s && s.Id != EsferasNegras.IdDoSet)
					FecharAInvocacao(s.Id, $"{s.NomeDoDragao} some no céu: as Esferas do Dragão agora são de cada um.");
		}
		else
			foreach (SetDeEsferas c in _sets.Where(s => s.Base != 0).ToList()) ApagarACopia(c);

		ManterAsCopiasDasEsferas();
		SalvarEsferas();
		AnunciarEsferas(ligar
			? "As Esferas do Dragão agora são POR JOGADOR: cada um caça as próprias sete (as de Namek e as das estátuas de jogadores). "
			  + "As Black Star e as Super Esferas continuam as mesmas pra todo mundo."
			: "As Esferas do Dragão voltaram a ser UM SET DE CADA pra todo mundo.");
		foreach (ulong zona in _players.Values.Select(p => p.Zone.Hash).Distinct().ToList()) MandarEsferasPorHash(zona);
		SincronizarOsMenusDoDragao();
	}

	/// <summary>(robo `--diagesferaslocais`) O modo de agora, e quantas copias de jogador ha no mundo.</summary>
	internal (bool Ligado, int Copias) EsferasPorJogadorDeTeste => (_esferasPorJogador, _sets.Count(s => s.Base != 0));

	/// <summary>
	/// O VERBO DO CARTAO, num lugar so (o botao, o `switch` do admin e a bancada leem daqui). CABE NAS 24 LETRAS do fio: o servidor le o nome
	/// do verbo com `GetString(24)`, e o LiteNetLib devolve VAZIO o que passa disso -- o primeiro nome (`admin_esferas_por_jogador`, 25) chegava
	/// em branco, e o botao nao fazia nada.
	/// </summary>
	internal const string VerboDasEsferasPorJogador = "admin_esferas_jogador";

	/// <summary>`admin_esferas_jogador [ligar|desligar]` -- o cartao "Esferas do Dragão" da aba Admin. Sem argumento, diz o modo de agora.</summary>
	private void AdminEsferasPorJogador(ServerPlayer adm, string arg)
	{
		string a = arg.Trim().ToLowerInvariant();
		if (a is "ligar" or "desligar") TrocarAsEsferasPorJogador(a == "ligar");
		Avisar(adm, _esferasPorJogador
			? "ESFERAS POR JOGADOR: LIGADO -- cada jogador tem o seu set das Esferas de Namek e dos sets de jogador; as Black Star e as Super "
			  + "Esferas continuam do servidor. Refazer a estátua fica desligado."
			: "ESFERAS POR JOGADOR: DESLIGADO -- um set de cada no servidor, pra todo mundo (o padrão).");
	}
}
