using System.Collections;
using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA INVASAO (`--diaginvasao`, o pedido 227, 2026-11-02): os viloes soltos atras do povo dos mundos com civilizacao, na tela. As
/// regras sao das bancadas (`--soltosteste`, `--booteste`); aqui se fotografa o que o jogador ve, nos dois primeiros mundos com cidade da
/// seed de fabrica (o mesmo par em toda rodada). O jogador nao se fere (o palco): a cena e pra ser vista.
///   N1. O KID SE ENTEDIA NO CEU: na frente do jogador, o clarao do IT e a frase -- foto `invasao-1-o-kid-some-do-ceu`;
///   N2. O BOO INVADE A CIDADE e caca o POVO -- foto `invasao-2-o-boo-ataca-o-povo`; N2b. (o pedido 228a) o corpo dele na tela e o do
///       KID BOO (a folha da Forma Pura), e nao um corpo generico;
///   N3. no fim dos dois dias (encurtados), a ESFERA ROSA sobre a cidade -- foto `invasao-3-a-esfera-rosa`; e o planeta entra na
///       destruicao -- foto `invasao-4-o-planeta-explodindo`;
///   N4. O CELL NO OUTRO MUNDO: o KAMEHAMEHA da Terra -- foto `invasao-5-o-kamehameha-do-cell`; e o planeta entra na destruicao.
///
///     Godot --path . --janela --resolution 1280x720 --host --rede 12991 --horateste 0.5 --semfauna --diaginvasao --raca Human --conta bancada_invasao --nome Testemunha
/// </summary>
public partial class RoboDaInvasao : Node
{
	private int _ok, _falhou;
	private IEnumerator? _roteiro;
	private double _espera = 3.0;
	private bool _acabou;
	private ZoneKey _primeiro, _segundo;

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[dinvasao]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; GD.PrintErr($"[dinvasao]   FALHA {nome}   [{detalhe}]"); }
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (GameClient.Instance is not { Connected: true } || World.Instancia?.PosicaoLocal == null) return;
		_espera -= delta;
		if (_espera > 0) return;
		_roteiro ??= Roteiro().GetEnumerator();
		if (!_roteiro.MoveNext())
		{
			_acabou = true;
			if (GameServer.Instance is { } srv && GameClient.Instance is { } cli) srv.PalcoInv_Limpar(cli.LocalId, _primeiro, _segundo);
			GD.Print(_falhou == 0 ? $"\n[dinvasao] ===== TUDO OK ===== ({_ok} OK)" : $"\n[dinvasao] ===== {_falhou} FALHA(S) ===== ({_ok} OK)");
			GD.Print("[dinvasao] ===== FIM =====");
			return;
		}
		_espera = _roteiro.Current is double d ? d : 0;
	}

	private void Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { GD.Print("[dinvasao] sem foto (headless nao renderiza): rode com janela"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		GD.Print($"[dinvasao] foto {caminho}");
	}

	/// <summary>Pousa no mundo, de dia e de ceu limpo, e vai pra praca da cidade. Devolve se chegou.</summary>
	private IEnumerable Pousar(GameServer srv, GameClient cli, PlanetaNoEspaco p, CidadeProcedural cidade, ZoneKey zona)
	{
		for (int i = 0; i < 60 && !srv.ProceduralDeFoto_Pousar(cli.LocalId, p.Nome, p.Seed); i++) yield return 0.25;
		for (int i = 0; i < 40 && !cli.Zone.Equals(zona); i++) yield return 0.25;
		yield return 3.0;
		srv.EmbateDeFoto_CeuLimpo(cli.LocalId);
		for (int i = 0; i < 20; i++)
		{
			double daqui = World.Instancia?.Ceu is { } ceu ? ceu.Hora - Math.Floor(ceu.Hora) : -1;
			if (daqui is >= 0.42 and <= 0.6) break;
			srv.CeuDaTerraDeTeste(i / 20.0);
			yield return 0.35;
		}
		srv.ProceduralDeFoto_Levar(cli.LocalId, cidade.CentroX, cidade.CentroY + 1);
		yield return 3.0;
	}

	private IEnumerable Roteiro()
	{
		GameClient cli = GameClient.Instance!;
		if (GameServer.Instance is not { } srv) { Afirmar("(montagem) o robo roda no modo host", false); yield break; }
		if (GameServer.MundoCivilizadoDeTeste() is not { } um || GameServer.MundoCivilizadoDeTeste(pular: 1) is not { } dois)
		{
			Afirmar("(montagem) ha dois mundos com cidade na seed de fabrica", false);
			yield break;
		}
		_primeiro = ZoneKey.Procedural(um.Planeta.Nome, um.Planeta.Seed);
		_segundo = ZoneKey.Procedural(dois.Planeta.Nome, dois.Planeta.Seed);

		// ---- N1. O KID SE ENTEDIA NO CEU ----
		bool kid = srv.PalcoInv_KidEntediado(cli.LocalId);
		for (int i = 0; i < 40 && cli.Zone.Name != Jandirus.Core.Npc.SagaDoBuu.ZonaDoKid; i++) yield return 0.25;
		GD.Print($"[dinvasao] o jogador esta em {cli.Zone.Name} pra ver o Kid se entediar");
		yield return 3.0;
		bool entediou = srv.PalcoInv_Entediar();
		yield return 0.15;
		Fotografar("invasao-1-o-kid-some-do-ceu");
		yield return 1.0;
		var boo = srv.PalcoInv_Solto("boo");
		Afirmar("N1. **O KID SE ENTEDIA NO CEU**: na frente do jogador ele some pelo IT, e vira um vilao solto SUMIDO",
				kid && entediou && boo.Corpo == 0 && boo.Sumido, $"kid {kid}, entediou {entediou}, sumido {boo.Sumido}");

		// ---- N2. O BOO INVADE A CIDADE E CACA O POVO ----
		foreach (object passo in Pousar(srv, cli, um.Planeta, um.Terreno.Cidade!, _primeiro)) yield return passo;
		GameServer.SoltoSoCacaOPovoDeTeste = true;
		srv.PalcoInv_Soltar(cli.LocalId, "boo", _primeiro, Jandirus.Core.Npc.SagaDoBuu.DegrauKid, 22);
		(int Corpo, int Presa, bool PresaDoPovo, bool Sumido, bool EmCena) b = default;
		for (int i = 0; i < 40; i++)
		{
			yield return 0.25;
			b = srv.PalcoInv_Solto("boo");
			if (b.Corpo != 0 && b.PresaDoPovo) break;
		}
		yield return 4.0;
		Fotografar("invasao-2-o-boo-ataca-o-povo");
		Afirmar($"N2. **O BOO INVADE {um.Planeta.Nome} E CACA O POVO DA CIDADE**: o corpo nasce com o jogador la, e a presa dele e um habitante",
				b.Corpo != 0 && b.PresaDoPovo, $"corpo #{b.Corpo}, presa #{b.Presa} (do povo {b.PresaDoPovo})");
		// (2026-11-02, o pedido 228a) NA MINHA TELA o corpo e o do Kid -- na r2 dos invasores ele chegou com um corpo generico de jogador
		string folhaDoKid = SagaDoBuu.FolhaDoCorpo(SagaDoBuu.CorpoDoKidBoo) ?? "?";
		string folhaNaTela = World.Instancia?.CorpoDeTeste(b.Corpo)?.GetNodeOrNull<CharacterVisual>("Visual")?.FolhaDoCorpoDeTeste ?? "";
		Afirmar("N2b. (o pedido 228a) **O BOO QUE INVADE E O KID BOO NA TELA**: a folha do corpo dele e a da Forma Pura, e nao um corpo generico",
				folhaNaTela == folhaDoKid, $"na tela '{folhaNaTela}', a do Kid '{folhaDoKid}'");

		// ---- N3. A ESFERA ROSA E A EXPLOSAO ----
		// (2026-11-02, a r1 das fotos) o fotografo nao conta como luta (senao o prazo do vilao se estende), e vai pra perto do Boo quando a
		// esfera sobe: a cena e em cima do corpo dele, e a praca pode estar longe
		for (int i = 0; i < 160 && !srv.PalcoInv_Solto("boo").EmCena; i++)
		{
			srv.PalcoInv_SemBriga(cli.LocalId);
			yield return 0.25;
		}
		// (a r2 das fotos) TRES TILES AO NORTE dele, e nao seis ao sul: a esfera sobe ACIMA da cabeca (seis tiles de diametro no fim da
		// carga), e a r1 fotografou so o chao -- o Boo e a esfera ficaram acima do quadro
		bool pertoDoBoo = srv.PalcoInv_PertoDoSolto(cli.LocalId, "boo", -3);
		for (int i = 0; i < 40 && EsferaRosa() == null; i++) yield return 0.25;
		yield return 6.0;   // a carga, como na foto da Terra: grande, e ainda sem cair
		Fotografar("invasao-3-a-esfera-rosa");
		(bool esferaNoQuadro, string ondeAEsfera) = EsferaRosa() is { } esfera ? EsferaNoQuadro(esfera) : (false, "sem esfera na tela");
		GD.Print($"[dinvasao] perto do Boo pra foto da esfera: {pertoDoBoo}; a esfera: {ondeAEsfera}");
		bool cena = srv.PalcoInv_Solto("boo").EmCena;
		for (int i = 0; i < 80 && !srv.PalcoInv_Explodindo(_primeiro); i++) yield return 0.25;
		yield return 3.0;
		Fotografar("invasao-4-o-planeta-explodindo");
		var depois = srv.PalcoInv_Solto("boo");
		Afirmar($"N3. **NO FIM DOS DOIS DIAS, A ESFERA ROSA** sobre a cidade, no quadro da foto -- e {um.Planeta.Nome} entra na destruicao, e o Boo some",
				cena && esferaNoQuadro && srv.PalcoInv_Explodindo(_primeiro) && depois.Sumido,
				$"cena {cena}, esfera no quadro {esferaNoQuadro} ({ondeAEsfera}), explodindo {srv.PalcoInv_Explodindo(_primeiro)}, sumido {depois.Sumido}");
		GameServer.SoltoSoCacaOPovoDeTeste = false;

		// ---- N4. O KAMEHAMEHA DO CELL NO OUTRO MUNDO ----
		foreach (object passo in Pousar(srv, cli, dois.Planeta, dois.Terreno.Cidade!, _segundo)) yield return passo;
		srv.PalcoInv_Soltar(cli.LocalId, "cell", _segundo, 1, 12);
		for (int i = 0; i < 160 && !srv.PalcoInv_Solto("cell").EmCena; i++)
		{
			srv.PalcoInv_SemBriga(cli.LocalId);
			yield return 0.25;
		}
		bool pertoDoCell = srv.PalcoInv_PertoDoSolto(cli.LocalId, "cell", 6);
		yield return 6.5;   // a carga e de 6 s: o raio ja saiu
		Fotografar("invasao-5-o-kamehameha-do-cell");
		GD.Print($"[dinvasao] perto do Cell pra foto do Kamehameha: {pertoDoCell}");
		bool doCell = srv.PalcoInv_Solto("cell").EmCena;
		for (int i = 0; i < 120 && !srv.PalcoInv_Explodindo(_segundo); i++) yield return 0.25;
		yield return 2.0;
		Fotografar("invasao-6-o-outro-planeta-explodindo");
		Afirmar($"N4. **O CELL SOLTA O KAMEHAMEHA DA TERRA** em {dois.Planeta.Nome} no fim dos dois dias -- e o planeta entra na destruicao",
				doCell && srv.PalcoInv_Explodindo(_segundo), $"cena {doCell}, explodindo {srv.PalcoInv_Explodindo(_segundo)}");
	}

	/// <summary>(a r2 das fotos) A esfera rosa desenhada na minha tela (a `SupernovaDesenhada` do ataque rosa), ou nulo.</summary>
	private static SupernovaDesenhada? EsferaRosa() => World.Instancia is { } w ? AcharEsfera(w) : null;

	private static SupernovaDesenhada? AcharEsfera(Node raiz)
	{
		if (raiz is SupernovaDesenhada s && !s.IsQueuedForDeletion() && s.Ataque == AtaqueDoUltimato.EsferaRosa) return s;
		foreach (Node filho in raiz.GetChildren())
			if (AcharEsfera(filho) is { } achada) return achada;
		return null;
	}

	/// <summary>
	/// (a r2 das fotos) O CENTRO DA ESFERA DENTRO DO QUADRO da foto (a regua do robo da saga do Buu, pelo centro: aqui o fotografo e posto
	/// ao norte do Boo, e a esfera inteira cabe).
	/// </summary>
	private static (bool Dentro, string Onde) EsferaNoQuadro(SupernovaDesenhada s)
	{
		if (s.GetViewport() is not { } v) return (false, "sem viewport");
		Transform2D t = s.GetGlobalTransformWithCanvas();
		Vector2 centro = t * s.CentroDeTeste;
		float raio = s.DiametroDeTeste / 2f * t.Scale.X;
		Rect2 quadro = v.GetVisibleRect();
		return (raio > 0 && quadro.HasPoint(centro), $"centro px {centro.X:0},{centro.Y:0}, raio {raio:0} px, quadro {quadro.Size.X:0}x{quadro.Size.Y:0}");
	}
}
