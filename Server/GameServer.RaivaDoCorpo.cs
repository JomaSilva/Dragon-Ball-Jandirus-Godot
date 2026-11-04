using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;

namespace Jandirus.Server;

/// <summary>
/// ============================ A FURIA AO ACHAR O CORPO DO AMIGO -- DONO, 2026-10-18 ============================
/// *"faça com q vc n precise apenas ver seu amigo morrer na hora, caso seu amigo morra pra outra pessoa (ao qual n seja seu amigo) e vc ache
/// o corpo morto dele pela primeira vez, seu personagem vai entrar em furia e vai falar algo relacionando tipo chamar pelo nome algumas vezes
/// e dps disso comeca a cinematica de furia podendo começar a cinematica de transformação caso a furia desperte a transformação"*.
///
/// Ate aqui a furia so vinha de VER a morte (`LutoNaVizinhanca`, no instante do golpe). Agora a morte de um amigo deixa um REGISTRO preso ao
/// corpo (<see cref="MorteComCorpo"/>), e quem chega depois e o encontra entra no mesmo luto -- pelo MESMO gancho (`AmigoAbatido`, grau
/// extremo), depois de chamar o amigo pelo nome. A cena da furia, a da transformacao que a furia desperta, o salto de Ki e o grito com o
/// nome sao os de sempre: este arquivo so decide QUEM, QUANDO, e o que ele diz antes.
///
/// ============================ O CORPO, E QUANTO TEMPO ELE FICA (lido no codigo, 2026-10-18) ============================
///   * NOS PRIMEIROS 2 s o corpo e o proprio morto, deitado onde caiu (`Alem.MsNoChao`, o `KO(-1)` + `sleep(20)` do DM -- foram 15 s ate
///     2026-09-04, e o dono os desfez);
///   * DAI EM DIANTE e o CADAVER, que nasce no instante da viagem pro Outro Mundo (`DeixarOCadaver`), e o registro passa pra ele
///     (<see cref="PassarAMorteProCadaver"/>). O cadaver NAO TEM PRAZO: fica ate ser enterrado, destrocado, empurrado pelo teto da zona
///     (`Cadaver.TetoPorZona`: o mais antigo sai) ou tirado pelo admin, e nao atravessa o reinicio (`Cadaver.VaiProDisco`). O registro some
///     com ele;
///   * QUEM NAO DEIXA CORPO nao deixa o que achar: o absorvido pelo Majin ja e furia na hora (`LutoPelaAbsorcao`), e o bebido pelo bio nao
///     deixa cadaver (`SumiuAbsorvido`). O NPC nao e amigo de ninguem (`EhPessoa`): a morte dele nao deixa registro;
///   * O AMIGO QUE JA VOLTOU (as esferas, o Enma) nao tem corpo pra ser chorado: o registro cai quando ele esta vivo no mundo.
///
/// ============================ O QUE E "ACHAR" ============================
/// Estar na MESMA zona, vivo e acordado, com o corpo NA TELA (<see cref="NaVistaDe"/>). (2026-10-30, o pedido 192) O dono: *"esse corpo nao
/// tenha entrado no seu campo de visao antes, ao entrar seu personagem vai gritar o nome do personagem morto e ficar em furia"*. Ate aqui era
/// o <see cref="RaioDeTestemunha"/> (22 tiles, o `view(22)` da fala) -- tres telas pro lado e seis pra cima no zoom de fabrica: o
/// personagem chamava o nome com o corpo muito longe da tela, e passar a 15 tiles dele ja gastava o "pela primeira vez".
///
/// ============================ O "PELA PRIMEIRA VEZ" ============================
/// UMA VEZ POR MORTE, POR QUEM ACHA: o registro guarda a assinatura de quem ja o viu. Quem estava a vista NA HORA da morte entra nele no
/// ato -- essa pessoa viu o amigo morrer (o caminho de sempre, que a enfureceu ou nao; e desde o pedido 193, 2026-10-31, VER A MORTE tambem
/// e ter o corpo NA TELA -- <see cref="ViuAMorte"/> --, e nao mais os 22 tiles da fala), e voltar ao corpo nao e achar. Quem estava fora da
/// tela nao viu: o corpo fica pra ser achado por ele. Outra morte do mesmo amigo e outro registro.
///
/// ============================ DA MESMA CONEXAO NADA RENDE (o pedido 110) ============================
/// (2026-10-30, o pedido 192) Como no luto de quem ve a morte: o corpo de um personagem jogado do MESMO IP de quem acha, ou o corpo que um
/// personagem desse IP matou, nao enfurece -- o endereco da vitima e o do algoz ficam no registro no instante da morte (o cadaver nao tem
/// conexao). Sem isto um alt morto por um NPC longe dava a furia (e o despertar dela) ao principal que chegasse ao corpo.
///
/// ============================ AS CONDICOES SAO AS DA RAIVA ============================
/// As do `LutoNaVizinhanca`, cada uma no seu tempo: o algoz era inimigo AOS OLHOS DA VITIMA (no instante da morte -- sem isso nem ha
/// registro); quem acha nao e o algoz nem a vitima; quem acha declarou a vitima amiga (`LutoPorMorte`) e o algoz e inimigo aos olhos DELE
/// (no instante em que acha: *"pra outra pessoa (ao qual n seja seu amigo)"*).
///
/// ============================ A ORDEM: O NOME, E SO DEPOIS A FURIA ============================
/// Quem acha chama o amigo pelo nome (`RaivaEmDegraus.ChamadosAoAcharOCorpo`, um a cada `SegundosEntreChamados`), e so depois do ultimo vem
/// a furia -- o gancho de sempre: o grito dele com o nome, a cena da furia ou, se ela despertar uma forma nova, a da transformacao. Quem cai,
/// morre ou sai do mundo no meio dos chamados nao chega a furia, e o corpo volta a poder ser achado por ele.
///
/// O QUE FICA SO COM QUEM VE A MORTE: a partida do Majin corrompido (`SagaNaFuriaDaMorte`). Ela e um evento da saga dele, e o pedido e a
/// furia.
/// ============================================================================================================
///
/// Bancadas: `--raivateste` (familia 13, `GameServer.RaivaDoCorpoTeste.cs`) e `--diagraiva` (as fotos `raiva-1N-corpo-*`).
/// </summary>
public partial class GameServer
{
	/// <summary>Uma morte de amigo que deixou corpo: o corpo (o morto nos 2 s, depois o cadaver), quem morreu, quem matou e quem ja o achou.</summary>
	private sealed class MorteComCorpo
	{
		public required ServerPlayer Corpo;
		public required string Vitima, NomeDaVitima, Algoz;
		/// <summary>(2026-10-30, o pedido 192) De onde a vitima e o algoz jogavam no instante da morte (nulo: NPC, corpo sem conexao).</summary>
		public string? IpDaVitima, IpDoAlgoz;
		public readonly HashSet<string> JaAcharam = new(StringComparer.Ordinal);
	}

	/// <summary>
	/// (2026-10-30, o pedido 192) A TELA DE QUEM OLHA, em px do mundo: a janela base do projeto (1280 x 720) no zoom de fabrica do cliente
	/// (`Settings.Zoom` = 3, cada tile de 32 px vira 96) -- 6,67 tiles pra cada lado e 3,75 pra cima e pra baixo. Quem joga com menos zoom
	/// ve o corpo um pouco antes do grito; com mais, grita um pouco antes de ve-lo.
	/// </summary>
	private const float MeiaLarguraDaVista = 1280f / 2 / 3, MeiaAlturaDaVista = 720f / 2 / 3;

	/// <summary>(2026-10-30, o pedido 192) O corpo esta NA TELA de quem olha -- o retangulo dela, centrado nele (ver <see cref="MeiaLarguraDaVista"/>).</summary>
	private static bool NaVistaDe(ServerPlayer quem, ServerPlayer corpo) =>
		Math.Abs(corpo.Pos.X - quem.Pos.X) <= MeiaLarguraDaVista && Math.Abs(corpo.Pos.Y - quem.Pos.Y) <= MeiaAlturaDaVista;

	/// <summary>
	/// (2026-10-31, o pedido 193) QUEM VIU O AMIGO MORRER: quem estava com o corpo NA TELA na hora (<see cref="NaVistaDe"/>). O dono, na
	/// pergunta do 192 (ver a morte tambem passa a contar so pela tela?): *"Sim so dentro do campo de visao"*. Quem estava fora dela -- mesmo
	/// a 10 tiles, dentro dos 22 da fala -- nao viu: nao se enfurece na hora, e o corpo continua pra ser achado por ele. A mesma pergunta pro
	/// luto de quem ve (`LutoNaVizinhanca`) e pro registro da morte (<see cref="DeixarAMortePraSerAchada"/>).
	/// </summary>
	private static bool ViuAMorte(ServerPlayer quem, ServerPlayer morto) => MorteVistaNosVinteEDoisDeTeste
		? (morto.Pos - quem.Pos).LengthSquared <= RaioDeTestemunha * RaioDeTestemunha
		: NaVistaDe(quem, morto);

	/// <summary>DEFEITO INJETADO (`--raivateste`, familia 13): a morte volta a ser vista nos 22 tiles da fala (o jogo de antes do pedido 193) -- quem estava fora da tela se enfurece na hora e nao acha o corpo depois. Lido pelo <see cref="ViuAMorte"/>. Falso em jogo, sempre.</summary>
	internal static bool MorteVistaNosVinteEDoisDeTeste;

	/// <summary>(2026-10-30, o pedido 192) Quem acha joga da mesma conexao que a vitima ou que o algoz desta morte (o pedido 110, como o `MesmoIp`).</summary>
	private bool DaMesmaConexaoQueAMorte(ServerPlayer quem, MorteComCorpo m) =>
		(!ProcessoDeBancada.Value || MesmoIpNaBancadaDeTeste) && EhPessoa(quem) && IpDe(quem) is { } ip && (ip == m.IpDaVitima || ip == m.IpDoAlgoz);

	/// <summary>Quem achou um corpo e esta chamando o nome: o chamado `Passo` sai em `ProximoEm`; depois do ultimo, a furia.</summary>
	private sealed class Chamando
	{
		public required ServerPlayer Quem;
		public required MorteComCorpo Morte;
		public int Passo;
		public long ProximoEm;
	}

	private readonly List<MorteComCorpo> _mortesComCorpo = [];
	private readonly List<Chamando> _chamando = [];
	private bool _pulsoDoCorpoInscrito;

	/// <summary>DEFEITO INJETADO (`--raivateste`, familia 13): achar o corpo do amigo nao faz nada -- o jogo de antes, em que so contava ver a morte. Lido pelo <see cref="PulsoDoCorpoAchado"/>. Falso em jogo, sempre.</summary>
	internal static bool SoVerAMorteEnfureceDeTeste;

	/// <summary>DEFEITO INJETADO (`--raivateste`, familia 13): a furia irrompe no instante em que acha, sem chamar o nome antes. Lido pelo <see cref="ComecarAChamar"/>. Falso em jogo, sempre.</summary>
	internal static bool FuriaSemChamarONomeDeTeste;

	/// <summary>DEFEITO INJETADO (`--raivateste`, familia 13): o mesmo corpo enfurece toda vez que alguem volta a ve-lo. Lido pelo <see cref="QuemAchaAgora"/>. Falso em jogo, sempre.</summary>
	internal static bool AchaOMesmoCorpoDeNovoDeTeste;

	/// <summary>DEFEITO INJETADO (`--raivateste`, familia 13): o cadaver nao leva o registro do morto -- o corpo so contaria nos 2 s antes da viagem. Lido pelo <see cref="PassarAMorteProCadaver"/>. Falso em jogo, sempre.</summary>
	internal static bool CadaverSemOLutoDeTeste;

	/// <summary>DEFEITO INJETADO (`--raivateste`, familia 13): o registro nao cai quando o amigo volta a vida -- o corpo velho de quem esta vivo enfureceria. Lido pelo <see cref="AmigoVoltou"/>. Falso em jogo, sempre.</summary>
	internal static bool AmigoVivoAindaEChoradoDeTeste;

	/// <summary>
	/// A MORTE DEIXA O REGISTRO. Chamado do `LutoNaVizinhanca` quando o amigo MORREU, depois das condicoes da VITIMA (algoz de verdade,
	/// pessoa, algoz inimigo aos olhos dela). Quem esta a vista agora viu a morte, e entra como quem ja achou o corpo.
	/// </summary>
	private void DeixarAMortePraSerAchada(ServerPlayer vitima, ServerPlayer algoz)
	{
		// UMA MORTE, UM REGISTRO: o funil pode ser chamado de novo pelo mesmo corpo (o nocaute e a morte do mesmo golpe passam os dois por la).
		MorteComCorpo? m = _mortesComCorpo.Find(x => x.Corpo == vitima);
		if (m == null)
		{
			m = new MorteComCorpo { Corpo = vitima, Vitima = vitima.Assinatura, NomeDaVitima = vitima.Name, Algoz = algoz.Assinatura };
			// (2026-10-30, o pedido 192) os enderecos AGORA, com os dois ainda conectados: o cadaver que herda o registro nao tem conexao
			m.IpDaVitima = IpDe(vitima);
			m.IpDoAlgoz = IpDe(algoz);
			_mortesComCorpo.Add(m);
		}

		// QUEM VIU A MORTE ja "achou" o corpo -- (2026-10-31, o pedido 193) quem o tinha NA TELA (`ViuAMorte`); quem estava fora dela nao viu, e o
		// corpo fica pra ser achado por ele.
		foreach (ServerPlayer o in ZoneList(vitima.Zone.Hash))
			if (o != vitima && EhPessoa(o) && ViuAMorte(o, vitima)) m.JaAcharam.Add(o.Assinatura);

		// O PULSO DE 10 Hz (`InscreverNoPulso`) -- o relogio que se desliga sozinho quando nao ha corpo nem chamado no mundo.
		if (!_pulsoDoCorpoInscrito)
		{
			_pulsoDoCorpoInscrito = true;
			InscreverNoPulso(() => _mortesComCorpo.Count > 0 || _chamando.Count > 0, PulsoDoCorpoAchado);
		}
		LigarPulso();
	}

	/// <summary>
	/// O CADAVER LEVA O REGISTRO. Chamado do `DeixarOCadaver`, no instante em que o morto viaja e o corpo fica: dai em diante quem achar o
	/// cadaver achou o amigo.
	/// </summary>
	private void PassarAMorteProCadaver(ServerPlayer morto, ServerPlayer cadaver)
	{
		if (CadaverSemOLutoDeTeste) return;
		foreach (MorteComCorpo m in _mortesComCorpo)
			if (m.Corpo == morto) m.Corpo = cadaver;
	}

	/// <summary>
	/// UM TIQUE DO CORPO ACHADO (10 Hz): o registro que perdeu o corpo ou o morto cai; quem acabou de achar um corpo comeca a chamar o nome;
	/// e quem ja chama da o proximo chamado -- ou, depois do ultimo, entra na furia. Chamado pela bancada tambem, com o relogio adiantado.
	/// </summary>
	private void PulsoDoCorpoAchado()
	{
		long agora = NowMs();
		_mortesComCorpo.RemoveAll(m => !CorpoAindaEsta(m) || AmigoVoltou(m));

		if (!SoVerAMorteEnfureceDeTeste)
			foreach (MorteComCorpo m in _mortesComCorpo.ToList())
				foreach (ServerPlayer o in QuemAchaAgora(m))
					ComecarAChamar(o, m, agora);

		foreach (Chamando c in _chamando.ToList()) Chamar(c, agora);
	}

	/// <summary>O corpo ainda esta la: deitado no mundo dos vivos (`EhCadaver`) e na lista da zona dele. O morto que viajou sem deixar cadaver, o enterrado e o destrocado, nao.</summary>
	private bool CorpoAindaEsta(MorteComCorpo m) => EhCadaver(m.Corpo) && ZoneList(m.Corpo.Zone.Hash).Contains(m.Corpo);

	/// <summary>O amigo esta VIVO no mundo: uma pessoa com a assinatura dele, de pe. Quem morreu e continua morto no Outro Mundo nao conta.</summary>
	private bool AmigoVoltou(MorteComCorpo m) =>
		!AmigoVivoAindaEChoradoDeTeste && _players.Values.Any(p => !p.Ficha.dead && EhPessoa(p) && p.Assinatura == m.Vitima);

	/// <summary>
	/// QUEM ACHA ESTE CORPO AGORA: na zona dele, vivo e acordado, com o corpo na tela, e que ainda nao o tinha visto. Quem o ve entra no
	/// registro -- conte ou nao conte pra furia --, e so segue quem chora o morto, nao e amigo de quem o matou e nao joga da mesma conexao.
	/// </summary>
	private List<ServerPlayer> QuemAchaAgora(MorteComCorpo m)
	{
		var achou = new List<ServerPlayer>();
		foreach (ServerPlayer o in ZoneList(m.Corpo.Zone.Hash))
		{
			if (o == m.Corpo || !EhPessoa(o) || o.Ficha.dead || o.Ficha.KO || EhOlhoDeFora(o)) continue;
			if (o.Assinatura == m.Vitima || o.Assinatura == m.Algoz) continue;
			if (!NaVistaDe(o, m.Corpo)) continue;   // (2026-10-30, o pedido 192) na TELA, e nao nos 22 tiles da fala
			if (m.JaAcharam.Contains(o.Assinatura) && !AchaOMesmoCorpoDeNovoDeTeste) continue;
			if (_chamando.Exists(c => c.Quem == o)) continue;   // ja esta chamando outro: este fica pra quando ele terminar

			m.JaAcharam.Add(o.Assinatura);
			if (DaMesmaConexaoQueAMorte(o, m)) continue;   // (2026-10-30, o pedido 192) o pedido 110: da mesma conexao nada rende
			if (!o.Social.LutoPorMorte(m.Vitima)) continue;
			if (!RaivaSoPelaVitimaDeTeste && !o.Social.AlgozEhInimigo(m.Algoz)) continue;
			achou.Add(o);
		}
		return achou;
	}

	/// <summary>Quem achou o corpo comeca a chamar o nome -- o primeiro chamado sai ja.</summary>
	private void ComecarAChamar(ServerPlayer o, MorteComCorpo m, long agora)
	{
		GD.Print($"[server] {o.Name} achou o corpo de {m.NomeDaVitima}");
		if (FuriaSemChamarONomeDeTeste) { EnfurecerPeloCorpo(o, m); return; }
		var c = new Chamando { Quem = o, Morte = m, ProximoEm = agora };
		_chamando.Add(c);
		Chamar(c, agora);
	}

	/// <summary>O PROXIMO CHAMADO, na hora dele; depois do ultimo, a furia.</summary>
	private void Chamar(Chamando c, long agora)
	{
		ServerPlayer o = c.Quem;
		if (!_players.ContainsKey(o.Id) || o.Ficha.dead || o.Ficha.KO)
		{
			_chamando.Remove(c);
			c.Morte.JaAcharam.Remove(o.Assinatura);
			return;
		}
		if (agora < c.ProximoEm) return;

		string[] chamados = RaivaEmDegraus.ChamadosAoAcharOCorpo(c.Morte.NomeDaVitima);
		if (c.Passo < chamados.Length)
		{
			GritarDaRaiva(o, chamados[c.Passo]);
			c.Passo++;
			c.ProximoEm = agora + (long)(RaivaEmDegraus.SegundosEntreChamados * 1000);
			return;
		}
		_chamando.Remove(c);
		EnfurecerPeloCorpo(o, c.Morte);
	}

	/// <summary>A FURIA, pelo gancho de sempre e com o grau da morte -- e quem esta perto ve o enlutado se enfurecer, como no `LutoNaVizinhanca`.</summary>
	private void EnfurecerPeloCorpo(ServerPlayer o, MorteComCorpo m)
	{
		if (AmigoAbatido(o, m.NomeDaVitima, NivelDeRaiva.Extrema)) AnunciarRaiva(o, extrema: true);
	}
}
