using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// 12) O RAIO CRESCE COM O PODER DE QUEM O SEGURA (`--embatekiteste`).
///
/// O pedido do dono (2026-10-08): *"ao usar um beam e dar um power up (transformacao, kaioken ou oq for) q faca o
/// bp subir o beam vai ficar maior proporcionalmente com esse crescimento, entao se o cara vira um ssj nao
/// masterizado (2x) no meio de um clash, o beam vai aumentar o tamanho em 100%"* -- *"com limite de 3x o tamanho
/// original, e o final flash e o unico q isso n acontece"*.
///
/// ============================ O QUE SE MEDE ============================
/// A cena do pedido: dois raios numa disputa, e um dos donos acende um aumento de poder de producao no meio (o
/// `Kaioken`, o verbo do jogador -- x50 dobra o poder, x100 o quadruplica). O tamanho e lido do estado de producao
/// (`Projetil.EscalaVisual` sobre a de nascenca) e do FIO (`ProjetilState`, escrito e lido de volta):
///   * o DOBRO de poder da o DOBRO de tamanho, em rampa e nao num tique;
///   * o Ki ja gasto nao encolhe o raio nem come o salto (a regua e o MENOR poder desde o disparo);
///   * o quadruplo para no teto de 3x; desligado o aumento, o raio volta ao tamanho de nascenca;
///   * o tamanho e REGRA: a frente da cabeca cresce junto e as duas cabecas da disputa continuam se tocando;
///   * fora de disputa tambem (um raio no ar), o raio SOLTO leva o tamanho que tinha, o trecho DESVIADO por um
///     parry engrossa com o pai -- e o Final Flash, pelo verbo, nao cresce.
///
/// A FICHA ANDA NA MAO depois de cada mudanca (`Ficha.Tick`): em jogo quem escreve o `expressedBP` e o laco de
/// fichas do servidor, e a bancada de boot nao o roda -- a mesma nota da familia 11.
///
/// O CONTRA-EXEMPLO E O JOGO DE ANTES: com `Projetil.RaioNaoCresceDeTeste` a mesma cena deixa o raio do tamanho
/// com que nasceu, e o fio nao carrega escala nenhuma.
/// =====================================================================
/// </summary>
public partial class GameServer
{
	private void ORaioCresceComOPoder()
	{
		GD.Print("[embateki] -- 12) O RAIO CRESCE COM O PODER DE QUEM O SEGURA (o dobro de poder, o dobro de tamanho; teto de 3x; menos o Final Flash)");
		double passoPorTique = Projetil.CrescimentoPorSegundo * Protocol.TickSeconds;

		// ---------------------------------------------------------------- (a) o pedido: 2x no meio da colisao
		var dobro = OSaltoDeTamanhoNoEmbate(pedidoDeKaioken: 50);
		AfirmarEk("PREPARO: a disputa comecou, e o Kaio-ken x50 DOBRA o poder de quem o acende (o \"2x\" do pedido)",
				  dobro.Comecou && Math.Abs(dobro.Salto - 2) < 0.1, $"comecou {dobro.Comecou}, salto {dobro.Salto:0.###}x");
		if (!dobro.Comecou) return;

		GD.Print($"[embateki]      (medido) salto de poder {dobro.Salto:0.###}x -> tamanho {dobro.Antes:0.###}x -> {dobro.NoPrimeiroTique:0.###}x (1o tique) -> "
				 + $"{dobro.Depois:0.###}x (1 s); rival {dobro.DoRival:0.###}x; no fio {dobro.NoFio:0.##} (rival {dobro.NoFioDoRival:0.##}); "
				 + $"frente {dobro.FrenteAntes:0.#} -> {dobro.FrenteDepois:0.#} px; cabecas a {dobro.EntreAsCabecas:0.#} px (contato {dobro.Contato:0.#})");
		AfirmarEk("sem ninguem subir de poder o raio fica do tamanho com que nasceu -- e o Ki ja gasto nao o encolhe",
				  Math.Abs(dobro.Antes - 1) < 1e-9 && Math.Abs(dobro.ComOKiGasto - 1) < 1e-9,
				  $"{dobro.Antes:0.###}x, e {dobro.ComOKiGasto:0.###}x com 70% do Ki");
		AfirmarEk("o DOBRO de poder no meio da colisao da o DOBRO de tamanho (\"vai aumentar o tamanho em 100%\")",
				  Math.Abs(dobro.Depois - dobro.Salto) < 0.02 && Math.Abs(dobro.Depois - 2) < 0.1,
				  $"tamanho {dobro.Depois:0.###}x pra um salto de {dobro.Salto:0.###}x");
		AfirmarEk("...medido contra o poder de ANTES do salto, e nao o do disparo: o Ki gasto ate ali nao come o crescimento",
				  dobro.SaltoContraODisparo < dobro.Salto - 0.2 && dobro.Depois > dobro.SaltoContraODisparo + 0.2,
				  $"contra o disparo o poder so subiu {dobro.SaltoContraODisparo:0.###}x; o raio cresceu {dobro.Depois:0.###}x");
		AfirmarEk("...em RAMPA: no primeiro tique ele anda um passo, nao o salto inteiro",
				  dobro.NoPrimeiroTique > 1 && dobro.NoPrimeiroTique <= 1 + passoPorTique + 1e-9,
				  $"{dobro.NoPrimeiroTique:0.###}x (passo {passoPorTique:0.###})");
		AfirmarEk("...e so o raio de QUEM subiu: o do rival continua do tamanho dele",
				  Math.Abs(dobro.DoRival - 1) < 1e-9, $"{dobro.DoRival:0.###}x");
		AfirmarEk("O TAMANHO E REGRA: a frente da cabeca cresce na mesma proporcao",
				  Math.Abs(dobro.FrenteDepois - dobro.FrenteAntes * dobro.Depois) < 0.01 && dobro.FrenteDepois > dobro.FrenteAntes,
				  $"{dobro.FrenteAntes:0.##} -> {dobro.FrenteDepois:0.##} px");
		AfirmarEk("...e a disputa continua de pe, com as duas cabecas se tocando pelo tamanho NOVO (frente com frente)",
				  dobro.EmEmbate && Math.Abs(dobro.EntreAsCabecas - dobro.Contato) < 0.5 && dobro.Contato > dobro.ContatoAntes + 1,
				  $"em disputa {dobro.EmEmbate}; {dobro.EntreAsCabecas:0.#} px entre as cabecas, contato {dobro.Contato:0.#} (era {dobro.ContatoAntes:0.#})");
		AfirmarEk("O FIO LEVA O TAMANHO DE AGORA (um byte, so de quem cresceu): o raio maior viaja com a escala, o do rival sem ela",
				  Math.Abs(dobro.NoFio - (float)(dobro.Depois * dobro.EscalaDeNascenca)) <= 0.026f && dobro.NoFioDoRival == 0f && dobro.SobrouNoFio == 0,
				  $"no fio {dobro.NoFio:0.###} (de producao {dobro.Depois * dobro.EscalaDeNascenca:0.###}), rival {dobro.NoFioDoRival}, sobraram {dobro.SobrouNoFio} byte(s)");
		AfirmarEk("DESLIGADO o aumento, o raio volta ao tamanho de nascenca -- e o fio para de carregar a escala",
				  Math.Abs(dobro.DeVolta - 1) < 1e-9 && dobro.NoFioDeVolta == 0f, $"{dobro.DeVolta:0.###}x, no fio {dobro.NoFioDeVolta}");

		// ---------------------------------------------------------------- (b) o teto
		var quadruplo = OSaltoDeTamanhoNoEmbate(pedidoDeKaioken: 100);
		AfirmarEk("O TETO: o quadruplo de poder (Kaio-ken x100) para em TRES vezes o tamanho original",
				  quadruplo.Comecou && quadruplo.Salto > 3.5 && Math.Abs(quadruplo.Depois - Projetil.TetoDoCrescimento) < 1e-9,
				  $"salto {quadruplo.Salto:0.###}x, tamanho {quadruplo.Depois:0.###}x");

		// ---------------------------------------------------------------- (c) fora de disputa, o raio solto, e o Final Flash
		var noAr = OVerboCresceNoAr("Kamehameha");
		AfirmarEk("FORA DE DISPUTA TAMBEM: um raio no ar dobra de tamanho quando o dono dobra de poder",
				  noAr.Nasceu && Math.Abs(noAr.Salto - 2) < 0.1 && Math.Abs(noAr.Fator - noAr.Salto) < 0.02,
				  $"nasceu {noAr.Nasceu}, salto {noAr.Salto:0.###}x, tamanho {noAr.Fator:0.###}x");
		AfirmarEk("...e o raio SOLTO leva o tamanho que tinha quando a mao abriu (o dono perde o poder, o raio nao murcha)",
				  noAr.SoltoVivo && Math.Abs(noAr.FatorSolto - noAr.Fator) < 1e-9,
				  $"vivo {noAr.SoltoVivo}, {noAr.Fator:0.###}x -> {noAr.FatorSolto:0.###}x");

		var finalFlash = OVerboCresceNoAr(Projetil.VerboQueNaoCresce);
		AfirmarEk("O FINAL FLASH E O UNICO QUE NAO CRESCE: o mesmo dobro de poder, e ele fica do tamanho com que nasceu",
				  finalFlash.Nasceu && Math.Abs(finalFlash.Salto - 2) < 0.1 && Math.Abs(finalFlash.Fator - 1) < 1e-9,
				  $"nasceu {finalFlash.Nasceu}, salto {finalFlash.Salto:0.###}x, tamanho {finalFlash.Fator:0.###}x");

		// ---------------------------------------------------------------- (d) o trecho desviado por um parry engrossa com o pai
		// DE CIMA (`Voo.AlturaQueAtravessa`), como a cena do fim do alcance da familia 6b: o trecho desviado sai de
		// LADO, pra fora do corredor, e pra um lado SORTEADO -- no chao, uma rodada em duas ele morria no cenario
		// antes da medida (a primeira versao desta cena reprovou assim, com os dois tamanhos certos).
		RecomecarOMapaDaBancada();
		var (pai, raiador, espelho, _, _) = RaioContraOParry("Engrossa", bpDoDefensor: 5_000, altura: Voo.AlturaQueAtravessa);
		Projetil? ramo = pai == null ? null : ProjeteisDaZona(espelho.Zone.Hash).FirstOrDefault(q => q.AlimentadoPor == pai.Id);
		AfirmarEk("PREPARO: o parry desviou o raio e o trecho desviado esta sendo alimentado",
				  pai is { Vivo: true } && pai.DesviadoPor == espelho.Id && ramo is { Vivo: true });
		if (pai != null && ramo != null)
		{
			raiador.Ficha.Tick(agoraMs: NowMs());   // a ficha anda antes do salto -- ver `OVerboCresceNoAr`
			TiqueDoParry(espelho);
			Kaioken(raiador, 50);
			raiador.Ficha.Tick(agoraMs: NowMs());
			for (int i = 0; i < 20; i++) TiqueDoParry(espelho);
			AfirmarEk("O TRECHO DESVIADO E O MESMO RAIO: ele engrossa junto com o pai (uma fita so na tela)",
					  ramo is { Vivo: true } && ramo.AlimentadoPor == pai.Id && pai.EscalaVisual > pai.EscalaDeNascenca * 1.5
					  && Math.Abs(ramo.EscalaVisual - pai.EscalaVisual) < 1e-9,
					  $"pai #{pai.Id} {pai.EscalaVisual:0.###} (vivo {pai.Vivo}), trecho {ramo.EscalaVisual:0.###} (vivo {ramo.Vivo}, "
					  + $"esvaziando {ramo.Esvaziando}), alimentado por #{ramo.AlimentadoPor}");
			Kaioken(raiador, 0);
		}
		LimparEmbatesDaBancada();

		// ---------------------------------------------------------------- (e) o jogo de antes
		Projetil.RaioNaoCresceDeTeste = true;
		try
		{
			var parado = OSaltoDeTamanhoNoEmbate(pedidoDeKaioken: 50);
			AfirmarEk("(defeito injetado: o raio nao cresce) o mesmo dobro de poder deixa o raio do tamanho de nascenca, e o fio sem escala",
					  parado.Comecou && Math.Abs(parado.Salto - 2) < 0.1 && Math.Abs(parado.Depois - 1) < 1e-9 && parado.NoFio == 0f,
					  $"salto {parado.Salto:0.###}x, tamanho {parado.Depois:0.###}x, no fio {parado.NoFio}");
		}
		finally { Projetil.RaioNaoCresceDeTeste = false; }
	}

	/// <summary>
	/// O ROTEIRO DA CENA, medido: um segundo de disputa, a ficha anda com 70% do Ki, o Kaio-ken, um tique, um
	/// segundo, o Kaio-ken desligado, mais um segundo. Os tamanhos sao FATORES sobre a escala de nascenca.
	/// O rival e 1,5x mais forte (como na familia 11): com o dobro de poder a vantagem vira sem a disputa acabar
	/// no meio da medida.
	/// </summary>
	private (bool Comecou, bool EmEmbate, double Salto, double SaltoContraODisparo, double Antes, double ComOKiGasto,
			 double NoPrimeiroTique, double Depois, double DeVolta, double DoRival, double EscalaDeNascenca,
			 float FrenteAntes, float FrenteDepois, float EntreAsCabecas, float Contato, float ContatoAntes,
			 float NoFio, float NoFioDoRival, float NoFioDeVolta, int SobrouNoFio)
		OSaltoDeTamanhoNoEmbate(double pedidoDeKaioken)
	{
		RecomecarOMapaDaBancada();

		(ServerPlayer eu, ServerPlayer outro, DisputaDeKi? d) = DoisRaiosDeFrente(12, bpDoSegundo: 50_000 * 1.5);
		_ = outro;
		if (d?.A.Feixe == null || d.B.Feixe == null) return default;
		LadoDeKi meu = d.A.Quem == eu ? d.A : d.B, dele = meu == d.A ? d.B : d.A;
		Projetil feixe = meu.Feixe!, rival = dele.Feixe!;
		double Fator(Projetil p) => p.EscalaVisual / p.EscalaDeNascenca;

		double bpNoDisparo = eu.Ficha.expressedBP;
		for (int i = 0; i < 30; i++) UmTiqueDoEncontro();
		double antes = Fator(feixe);
		float frenteAntes = Feixe.AlcanceDaCabeca(feixe), contatoAntes = Feixe.Contato(feixe, rival);

		// A FICHA ANDA COM O KI JA GASTO: o `expressedBP` cai com ele (`kiratio`), e a regua do raio desce junto.
		eu.Ficha.Ki = eu.Ficha.MaxKi * 0.7;
		eu.Ficha.Tick(agoraMs: NowMs());
		for (int i = 0; i < 10; i++) UmTiqueDoEncontro();
		double comOKiGasto = Fator(feixe), bpAntes = eu.Ficha.expressedBP;

		Kaioken(eu, pedidoDeKaioken);
		eu.Ficha.Tick(agoraMs: NowMs());
		double salto = eu.Ficha.expressedBP / bpAntes, contraODisparo = eu.Ficha.expressedBP / bpNoDisparo;

		UmTiqueDoEncontro();
		double noPrimeiroTique = Fator(feixe);
		for (int i = 0; i < 29; i++) UmTiqueDoEncontro();
		double depois = Fator(feixe), doRival = Fator(rival);
		bool emEmbate = _emEmbateDeKi.ContainsKey(eu.Id);
		float frenteDepois = Feixe.AlcanceDaCabeca(feixe), entre = (feixe.Pos - rival.Pos).Length, contato = Feixe.Contato(feixe, rival);
		(float noFio, int sobrou) = AEscalaNoFio(feixe);
		(float noFioDoRival, _) = AEscalaNoFio(rival);

		Kaioken(eu, 0);   // o mesmo botao desliga
		eu.Ficha.Tick(agoraMs: NowMs());
		for (int i = 0; i < 30 && feixe.Vivo; i++) UmTiqueDoEncontro();
		double deVolta = Fator(feixe);
		(float noFioDeVolta, _) = feixe.Vivo ? AEscalaNoFio(feixe) : (0f, 0);

		var medido = (true, emEmbate, salto, contraODisparo, antes, comOKiGasto, noPrimeiroTique, depois, deVolta, doRival,
					  feixe.EscalaDeNascenca, frenteAntes, frenteDepois, entre, contato, contatoAntes, noFio, noFioDoRival,
					  noFioDeVolta, sobrou);
		LimparEmbatesDaBancada();
		return medido;
	}

	/// <summary>
	/// A MESA LIMPA E O MAPA DO COMECO. Esta familia e a ultima de uma bancada comprida, e o `CorredorLivre` so anda
	/// pra frente: quando ela comeca ele ja gastou o mapa inteiro (a primeira rodada caiu no ponto de emergencia
	/// dele, o canto (64, 64), e mediu o que havia la). Com tudo que as cenas anteriores puseram no mundo ja
	/// recolhido, as faixas do comeco estao livres de novo.
	/// </summary>
	private void RecomecarOMapaDaBancada()
	{
		LimparEmbatesDaBancada();
		_pjProximoCorredor = 8;   // a primeira faixa: a mesma com que os runners das bancadas de ki comecam
	}

	/// <summary>
	/// A ESCALA DESTE RAIO COMO ELA CHEGA AO CLIENTE: o estado que o snapshot escreveria (`TirosDaZona`), posto no
	/// fio e lido de volta. Zero = o pacote nao a carrega (o raio esta no tamanho de nascenca). `Sobrou` sao os
	/// bytes que o leitor deixou pra tras -- escritor e leitor tem que andar juntos.
	/// </summary>
	private (float Escala, int Sobrou) AEscalaNoFio(Projetil p)
	{
		foreach (ProjetilState estado in TirosDaZona(ZonaDaBancadaDeProjetil.Hash))
		{
			if (estado.Id != p.Id) continue;
			var w = new NetDataWriter();
			estado.Write(w);
			var r = new NetDataReader(w.CopyData());
			ProjetilState lido = ProjetilState.Read(r);
			return (lido.Escala, r.AvailableBytes);
		}
		return (-1f, -1);
	}

	/// <summary>
	/// UM RAIO DESTE VERBO NO AR, e o dono dobrando de poder com ele na mao. Atirado DE CIMA
	/// (`Voo.AlturaQueAtravessa`): o que se mede e o tamanho, nao o que o mapa tem depois do corredor. Depois a
	/// mao abre, o dono PERDE o aumento, e o tamanho do raio solto e medido de novo.
	/// </summary>
	private (bool Nasceu, double Salto, double Fator, bool SoltoVivo, double FatorSolto) OVerboCresceNoAr(string verbo)
	{
		RecomecarOMapaDaBancada();
		ServerPlayer pl = Forjar("NoAr" + verbo, CorredorLivre(12), bp: 50_000);
		pl.Facing = Facing.East;
		pl.Altitude = Voo.AlturaQueAtravessa;
		pl.Ficha.Ki = pl.Ficha.MaxKi;

		Canalizar(pl, verbo, 10 * pl.Ficha.BaseDrain(), SemDeflexao());
		for (int i = 0; i < 30 * 4 && _canais.TryGetValue(pl.Id, out CanalDeKi? c) && c.Raio == null; i++)
			TickDosCanaisDeKi(Protocol.TickSeconds);
		Projetil? raio = _canais.GetValueOrDefault(pl.Id)?.Raio;
		if (raio == null) { LimparEmbatesDaBancada(); return default; }

		// A FICHA ANDA ANTES DO SALTO: a carga e o disparo ja cobraram Ki, e o `expressedBP` so cai com ele quando a
		// ficha anda (`kiratio`). Sem isto o "antes" seria o poder de quando o corpo nasceu, com o tanque cheio -- e
		// o salto medido sairia menor que o dobro (a primeira rodada desta cena leu 1,77x).
		pl.Ficha.Tick(agoraMs: NowMs());
		TickDosCanaisDeKi(Protocol.TickSeconds);
		TickDosProjeteis(Protocol.TickSeconds);
		double bpAntes = pl.Ficha.expressedBP;
		Kaioken(pl, 50);
		pl.Ficha.Tick(agoraMs: NowMs());
		double salto = pl.Ficha.expressedBP / bpAntes;
		for (int i = 0; i < 15; i++) { TickDosCanaisDeKi(Protocol.TickSeconds); TickDosProjeteis(Protocol.TickSeconds); }
		double fator = raio.EscalaVisual / raio.EscalaDeNascenca;

		// A MAO ABRE, e so entao o dono perde o aumento: o raio que ninguem alimenta nao le mais poder nenhum.
		if (_canais.TryGetValue(pl.Id, out CanalDeKi? canal)) FecharCanal(pl.Id, canal, null);
		Kaioken(pl, 0);
		pl.Ficha.Tick(agoraMs: NowMs());
		for (int i = 0; i < 6; i++) TickDosProjeteis(Protocol.TickSeconds);

		var medido = (true, salto, fator, raio.Vivo, raio.EscalaVisual / raio.EscalaDeNascenca);
		LimparEmbatesDaBancada();
		return medido;
	}
}
