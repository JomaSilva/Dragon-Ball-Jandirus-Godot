using Godot;
using Jandirus.Core.Social;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--penavivoteste` -- A PENA DO INFERNO NAO SEGUE O VIVO, E ESPERA A PROXIMA MORTE (2026-10-19). O defeito, achado lendo o
/// codigo pela sessao do King Yemma: nenhum revive mexia no `hell_lockout_until`, e o tique da pena (`CumprirPenaNoInferno`, o
/// `afterlife_alignment_check()` do DM) nao perguntava `dead` -- quem voltava a vida com a pena correndo era arrastado VIVO de volta ao
/// portao do Inferno no tique seguinte. A regra do dono: *"Vivo, ele fica livre; quando morrer de novo, volta pro Inferno pra cumprir o
/// resto da pena"* (o relogio parado enquanto ele vive). O conserto e o porque estao no proprio `CumprirPenaNoInferno`.
///
/// Um cliente cru entra pela porta: o tique da pena so olha quem `EhJogador` diz que e gente, e isso pede um `Peer` (com um corpo forjado
/// a regua passaria por ausencia). Ele morre de pe na mesa do Enma com o coracao negro e e julgado pela porta de producao (`EnmaOuvir`).
/// Depois, por cada porta de volta a vida, e UM tique do laco de fichas de producao (`TickFichas`):
///   A. A TECNICA DE REVIVER (`RessuscitarG4`), de um vivo forjado ao lado dele no Inferno -- ele acorda em casa, LIVRE; A2 o aviso,
///      A3 o resto guardado (a pena inteira, menos nada), A4 o resto no disco;
///   B. O DESEJO DAS ESFERAS (`Ressuscitar`, o revive dos dois desejos) -- puxado pra Terra;
///   C. O ENMA PAGO (`EnmaReviverPorZeni`), com o karma ja limpo e a pena ainda correndo;
///   D. O VERBO DE ADMIN (`AdminReviver`, que revive no lugar -- dentro do Inferno) e o "mandar pro spawn" (`AdminMandarProSpawn`);
///   E. O SAVE DE ANTES DO CONSERTO: vivo em casa com a pena gravada correndo (o que sobrou e guardado) e, E2, ja vencida (nada a guardar).
/// A regua do vivo: vivo, fora do Inferno, sem pena correndo. E A MORTE SEGUINTE:
///   F0. dez minutos vivo nao mexem no resto (o relogio para);
///   F.  morto pela porta de producao (`Combate.Morrer` + `PassoDaMorte`, a viagem a mesa do Enma), um tique o leva ao portao do Inferno
///       com o resto INTEIRO no relogio; F2 o aviso;
///   G.  cumprido o resto, a alma sai limpa e volta ao checkpoint, como toda pena.
///
/// AS DUAS GUARDAS, pra a regua nao passar por ausencia nem por exagero: K1, o MORTO com pena fora do Inferno continua sendo devolvido ao
/// portao pelo mesmo tique (o tique alcanca este corpo, e o conserto nao soltou os mortos); K2, a volta nao lava o karma.
/// CONTRA-EXEMPLOS, lidos pelas linhas do conserto: `PenaSegueOVivoDeTeste` (o revivido acorda de novo no portao, vivo) e
/// `PenaGuardadaSomeDeTeste` (a morte seguinte o deixa na mesa do Enma, sem pena).
///
/// CADA PORTA COMECA DO MESMO LUGAR (`Condenar`: morto de pe na mesa, julgado de novo): o defeito de uma nao arrasta as seguintes.
///
///     Godot --headless --path . --server --port 9220 --penavivoteste
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--penavivoteste`): a pena do Inferno volta a seguir o VIVO -- o tique da pena deixa de perguntar `dead`,
	/// e quem voltou a vida com ela correndo e arrastado de volta ao portao. Declarado aqui, e nao no `GameServer.Alem.cs` que o le, pra
	/// a volta do ciclo de publicacao (que deixa este arquivo so com um comentario) compilar.
	/// </summary>
	internal bool PenaSegueOVivoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--penavivoteste`): a pena guardada SOME na morte seguinte -- a alma chega ao Outro Mundo e o resto nao
	/// volta ao relogio. Declarado aqui pelo mesmo motivo do de cima.
	/// </summary>
	internal bool PenaGuardadaSomeDeTeste;

	private const string ContaDaPenaDoVivo = "bancada_penavivo";
	private const int IdDoRevivedorDaPenaDeTeste = 987_830;

	/// <summary>O coracao do condenado: -50 da 30 min de pena (`Alem.MsDePenaNoInferno`), longe dos dois lados do piso.</summary>
	private const int KarmaDoCondenadoDeTeste = -50;

	/// <summary>A folga das contas de relogio: entre o julgamento e o tique que guarda passam milissegundos, nao segundos.</summary>
	private const long FolgaDaPenaDeTeste = 5_000;

	private bool _penaVivoJaRodou;
	private int _pvOk, _pvFalhou;

	private void AfirmarPv(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _pvOk++; GD.Print($"[penavivo]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _pvFalhou++; GD.PrintErr($"[penavivo]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDaPenaDoVivo(int porta)
	{
		if (_penaVivoJaRodou) return;
		_penaVivoJaRodou = true;
		_pvOk = _pvFalhou = 0;
		GD.Print("[penavivo] ================ A PENA DO INFERNO NAO SEGUE O VIVO (2026-10-19) ================");

		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		ServerPlayer? revivedor = null;
		long relogioDeAntes = AdiantoDoRelogioDeTeste;
		try
		{
			ServerPlayer? pl = mesa.EntraNoMundo(cli, ContaDaPenaDoVivo, "Condenado");
			AfirmarPv("(montagem) o cliente entra pela porta e e jogador", pl != null && EhJogador(pl) && pl.Combate != null);
			if (pl == null || pl.Combate == null) return;

			// QUEM REVIVE E QUEM DA A ORDEM DE ADMIN: um vivo forjado, sem `Peer` (nada dele vai pro disco).
			revivedor = new ServerPlayer
			{
				Id = IdDoRevivedorDaPenaDeTeste, Peer = null, Name = "Revivedor", Race = "Human", Genero = "Male", Idade = 30,
				Zone = SpawnZone, Pos = PontoDeNascimento(SpawnZone), Conta = "bancada_penavivo_revivedor", Slot = 0,
				Ficha = new Fighter { Race = "Human", BP = 1000 },
				Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			revivedor.Ficha.Class = "Normal";
			PorNoMundo(revivedor);
			revivedor.Ficha.Ki = revivedor.Ficha.MaxKi;

			RoteiroDaPenaDoVivo(pl, revivedor, mesa);
		}
		catch (Exception e)
		{
			AfirmarPv("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			PenaSegueOVivoDeTeste = false;
			PenaGuardadaSomeDeTeste = false;
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			if (revivedor != null && _players.ContainsKey(revivedor.Id)) RemoverNpc(revivedor);
			mesa.Fecha();
			if (_store != null)
				try
				{
					string caminho = System.IO.Path.Combine(_store.Pasta, ContaDaPenaDoVivo + ".json");
					if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
				}
				catch (Exception e) { GD.PushWarning($"[penavivo] nao apaguei '{ContaDaPenaDoVivo}': {e.Message}"); }
			GD.Print($"[penavivo] ================ {_pvOk} OK, {_pvFalhou} FALHA(S) ================");
		}
	}

	private void RoteiroDaPenaDoVivo(ServerPlayer pl, ServerPlayer revivedor, MesaDeClientesCrus mesa)
	{
		const float T = ZoneCollision.TileSize;
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo), inferno = ZoneKey.Premade(Alem.ZonaDoInferno);
		Obra? enma = _noChao.FirstOrDefault(o => o.Tipo == Alem.TipoDoEnma && o.Zona.Equals(alem));
		AfirmarPv("(montagem) o Enma esta na mesa dele", enma != null);
		if (enma == null) return;
		Vec2 naMesa = new(enma.X + T, enma.Y);
		(ZoneKey Zona, Vec2 Pos) casa = DestinoDe(pl);
		Vec2 doPedinte = PontoDeNascimento(SpawnZone);
		string alvo = pl.Id.ToString();
		long penaInteira = Alem.MsDePenaNoInferno(KarmaDoCondenadoDeTeste);

		// MORTO DE PE NA MESA DO ENMA, COM O CORACAO NEGRO, JULGADO PELA PORTA DE PRODUCAO -- o comeco de cada porta.
		bool Condenar()
		{
			pl.Ficha.dead = true; pl.MorteJaViajou = true; pl.RelogioDaMorte = long.MaxValue;
			pl.Ficha.hell_lockout_until = 0;
			pl.Ficha.pena_do_inferno_guardada_ms = 0;
			pl.Karma = KarmaDoCondenadoDeTeste;
			if (!pl.Zone.Equals(alem)) MoveToZone(pl.Id, alem, naMesa);
			else CravarPosicao(pl, naMesa);
			EnmaOuvir(pl);
			return pl.Ficha.dead && pl.Zone.Equals(inferno) && pl.Ficha.hell_lockout_until > NowMs();
		}
		// A MORTE SEGUINTE, pela porta de producao: o funil unico da morte e a viagem a mesa do Enma (o prazo dos 2 s vencido na mao).
		void MorrerDeNovo()
		{
			pl.Combate.Morrer(ignorarSeguro: true);
			PassoDaMorte(pl);
		}
		List<string> Tique() => Ouvir(TickFichas);
		bool Livre() => !pl.Ficha.dead && !pl.Zone.Equals(inferno) && pl.Ficha.hell_lockout_until == 0;
		bool PenaInteiraGuardada() => pl.Ficha.pena_do_inferno_guardada_ms > penaInteira - FolgaDaPenaDeTeste
									  && pl.Ficha.pena_do_inferno_guardada_ms <= penaInteira;
		string Estado() => $"{(pl.Ficha.dead ? "morto" : "vivo")} em {pl.Zone.Name} ({pl.Pos.X:0},{pl.Pos.Y:0}), pena correndo "
						   + (pl.Ficha.hell_lockout_until == 0 ? "nenhuma" : $"{(pl.Ficha.hell_lockout_until - NowMs()) / 1000} s")
						   + $", guardada {pl.Ficha.pena_do_inferno_guardada_ms / 1000} s, karma {pl.Karma}";

		// ---------------------------------------------------------------- K1. o morto continua preso
		AfirmarPv("(montagem) morto de pe na mesa do Enma com karma -50, o Enma o julga: no portao do Inferno, com a pena correndo",
				  Condenar(), Estado());
		MoveToZone(pl.Id, alem, naMesa);
		Tique();
		AfirmarPv("K1. **O MORTO CONTINUA PRESO**: fora do Inferno com a pena correndo, um tique do laco de fichas o devolve ao portao",
				  pl.Ficha.dead && pl.Zone.Equals(inferno) && pl.Ficha.hell_lockout_until > NowMs(), Estado());

		// ---------------------------------------------------------------- A. a tecnica de reviver
		Condenar();
		Vec2 aoLado = pl.Pos + new Vec2(0.75f * T, 0);
		if (!revivedor.Zone.Equals(inferno)) MoveToZone(revivedor.Id, inferno, aoLado);
		else revivedor.Pos = aoLado;
		revivedor.AlvoId = pl.Id;   // a mira nele: o Inferno tem as almas dos viloes, mortas, e uma delas podia estar mais perto
		RessuscitarG4(revivedor);
		bool revivido = !pl.Ficha.dead && pl.Zone.Equals(casa.Zona);
		List<string> falas = Tique();
		AfirmarPv("A. **A TECNICA DE REVIVER**: revivido dentro do Inferno por quem esta ao lado, ele acorda em casa -- e o tique o deixa la, vivo e sem pena correndo",
				  revivido && Livre() && pl.Zone.Equals(casa.Zona), $"revivido {revivido}; {Estado()}");
		AfirmarPv("A2. **O AVISO**: no mesmo tique ele ouve que a pena ficou parada e que so volta a ela se morrer de novo",
				  Disse(falas, "fica parada") && Disse(falas, "se morrer de novo"), Ultimos(falas));
		AfirmarPv("A3. **O RESTO FICA GUARDADO**: o que faltava da pena (os 30 min inteiros, julgado agora ha pouco) espera a proxima morte",
				  PenaInteiraGuardada(), Estado());
		long? noDisco = mesa.NoDisco(ContaDaPenaDoVivo)?.Ficha.pena_do_inferno_guardada_ms;
		AfirmarPv("A4. **...E VAI PRO DISCO**: o save tem o resto guardado (e nao o relogio andando que o revive gravou)",
				  noDisco == pl.Ficha.pena_do_inferno_guardada_ms && noDisco > 0, $"no disco {noDisco}; na ficha {pl.Ficha.pena_do_inferno_guardada_ms}");
		AfirmarPv("K2. **O KARMA FICA**: a pena nao foi cumprida, entao a volta nao lava o coracao -- continua -50",
				  pl.Karma == KarmaDoCondenadoDeTeste, Estado());

		// ---------------------------------------------------------------- B. o desejo das esferas
		Condenar();
		Ressuscitar(pl, SpawnZone, doPedinte);
		revivido = !pl.Ficha.dead && pl.Zone.Equals(SpawnZone);
		Tique();
		AfirmarPv("B. **O DESEJO DAS ESFERAS**: puxado pra Terra, ao lado de quem pediu, ele fica la -- vivo e sem pena correndo",
				  revivido && Livre() && pl.Zone.Equals(SpawnZone), $"revivido {revivido}; {Estado()}");

		// ---------------------------------------------------------------- C. o Enma pago
		// A unica montagem em que o Enma VENDE a volta a quem tem pena: o karma ja voltou a zero e a pena ainda corre (com o coracao negro
		// ele julga de novo em vez de vender -- o "o zeni nao compra" da `--karmateste`).
		Condenar();
		pl.Karma = Karma.Neutro;
		MoveToZone(pl.Id, alem, naMesa);
		pl.Ficha.Zeni = PrecoDoEnmaPara(pl);
		EnmaReviverPorZeni(pl);
		revivido = !pl.Ficha.dead && pl.Zone.Equals(casa.Zona);
		Tique();
		AfirmarPv("C. **O ENMA PAGO**: a volta vendida na mesa leva pra casa, e o tique o deixa la -- vivo e sem pena correndo",
				  revivido && Livre() && pl.Zone.Equals(casa.Zona), $"revivido {revivido}; {Estado()}");

		// ---------------------------------------------------------------- D. o verbo de admin
		// Ele revive NO LUGAR -- dentro do Inferno --, e e o admin quem o manda pro spawn depois. Com a pena correndo, o tique o devolvia ao
		// portao assim que ele saia.
		Condenar();
		AdminReviver(revivedor, alvo);
		revivido = !pl.Ficha.dead && pl.Zone.Equals(inferno);
		Tique();
		AdminMandarProSpawn(revivedor, alvo);
		Tique();
		AfirmarPv("D. **O VERBO DE ADMIN**: revivido no lugar (no Inferno) e mandado pro spawn pelo admin, o tique o deixa em casa -- vivo e sem pena correndo",
				  revivido && Livre() && pl.Zone.Equals(casa.Zona), $"revivido no Inferno {revivido}; {Estado()}");

		// ---------------------------------------------------------------- E. o save de antes do conserto
		// Quem foi revivido com a versao velha guardou no disco a pena correndo; ao entrar, o tique a encontra num vivo. O tempo fora do
		// jogo correu no relogio velho: o que sobrou e o que se guarda.
		pl.Karma = KarmaDoCondenadoDeTeste;
		pl.Ficha.pena_do_inferno_guardada_ms = 0;
		pl.Ficha.hell_lockout_until = NowMs() + 600_000;
		Tique();
		AfirmarPv("E. **O SAVE DE ANTES DO CONSERTO**: vivo em casa com a pena gravada correndo, o tique guarda o que sobrou (10 min) e o deixa onde esta, com o karma intacto",
				  Livre() && pl.Zone.Equals(casa.Zona) && pl.Karma == KarmaDoCondenadoDeTeste
				  && pl.Ficha.pena_do_inferno_guardada_ms > 600_000 - FolgaDaPenaDeTeste && pl.Ficha.pena_do_inferno_guardada_ms <= 600_000, Estado());
		pl.Ficha.pena_do_inferno_guardada_ms = 0;
		pl.Ficha.hell_lockout_until = NowMs() - 1;
		Tique();
		AfirmarPv("E2. **...E COM A PENA JA VENCIDA**: nada sobrou pra guardar -- o vivo nao e levado ao Outro Mundo nem sai lavado, e nada fica guardado",
				  Livre() && pl.Zone.Equals(casa.Zona) && pl.Karma == KarmaDoCondenadoDeTeste && pl.Ficha.pena_do_inferno_guardada_ms == 0, Estado());

		// ---------------------------------------------------------------- F. a morte seguinte
		Condenar();
		Ressuscitar(pl, SpawnZone, doPedinte);
		Tique();
		long guardada = pl.Ficha.pena_do_inferno_guardada_ms;
		AdiantoDoRelogioDeTeste += 600_000;   // dez minutos vivo
		Tique();
		AfirmarPv("F0. **O RELOGIO PARA ENQUANTO ELE VIVE**: dez minutos vivo depois, ele continua livre e o resto guardado e o mesmo (os 30 min)",
				  Livre() && guardada > penaInteira - FolgaDaPenaDeTeste && pl.Ficha.pena_do_inferno_guardada_ms == guardada,
				  $"guardada logo depois do revive {guardada / 1000} s; {Estado()}");
		MorrerDeNovo();
		bool chegou = pl.Ficha.dead && pl.Zone.Equals(alem);
		falas = Tique();
		long noRelogio = pl.Ficha.hell_lockout_until - NowMs();
		AfirmarPv("F. **A MORTE SEGUINTE LEVA AO INFERNO COM O RESTO**: morto de novo, a alma chega a mesa do Enma e o tique a leva ao portao, com os 30 min no relogio (e nao 20)",
				  chegou && pl.Ficha.dead && pl.Zone.Equals(inferno) && noRelogio > guardada - FolgaDaPenaDeTeste && noRelogio <= guardada,
				  $"chegou na mesa {chegou}; no relogio {noRelogio / 1000} s contra {guardada / 1000} s guardados; {Estado()}");
		AfirmarPv("F2. **O AVISO DA VOLTA**: no mesmo tique ele ouve que a morte o devolveu ao Inferno e quanto falta",
				  Disse(falas, "a morte te devolve ao Inferno") && Disse(falas, "faltam"), Ultimos(falas));

		// ---------------------------------------------------------------- G. o resto cumprido
		AdiantoDoRelogioDeTeste += Math.Max(0, pl.Ficha.hell_lockout_until - NowMs()) + 2_000;
		Tique();
		AfirmarPv("G. **CUMPRIDO O RESTO, A ALMA SAI LIMPA**: vencido o resto, ela volta ao checkpoint do Outro Mundo com o karma zerado, como toda pena cumprida",
				  pl.Ficha.dead && pl.Zone.Equals(alem) && pl.Karma == Karma.Neutro && pl.Ficha.hell_lockout_until == 0
				  && pl.Ficha.pena_do_inferno_guardada_ms == 0, Estado());

		// ---------------------------------------------------------------- os contra-exemplos
		PenaSegueOVivoDeTeste = true;
		try
		{
			Condenar();
			Ressuscitar(pl, SpawnZone, doPedinte);
			Tique();
			AfirmarPv("(defeito injetado: a pena segue o vivo) a regua do vivo REPROVA: o revivido pelas esferas acorda de novo no portao do Inferno, VIVO",
					  !pl.Ficha.dead && pl.Zone.Equals(inferno) && !Livre(), Estado());
		}
		finally { PenaSegueOVivoDeTeste = false; }

		// O segundo parte do vivo JA com o resto guardado (posto na ficha, e nao vindo do tique do vivo): ele mede so a volta na morte.
		PenaGuardadaSomeDeTeste = true;
		try
		{
			pl.Ficha.hell_lockout_until = 0;
			pl.Ficha.pena_do_inferno_guardada_ms = 1_200_000;
			pl.Karma = KarmaDoCondenadoDeTeste;
			MoveToZone(pl.Id, casa.Zona, casa.Pos);
			MorrerDeNovo();
			Tique();
			AfirmarPv("(defeito injetado: a pena guardada some na morte) a regua da morte seguinte REPROVA: a alma fica na mesa do Enma, fora do Inferno e sem pena correndo",
					  pl.Ficha.dead && pl.Zone.Equals(alem) && pl.Ficha.hell_lockout_until == 0, Estado());
		}
		finally { PenaGuardadaSomeDeTeste = false; }
	}
}
