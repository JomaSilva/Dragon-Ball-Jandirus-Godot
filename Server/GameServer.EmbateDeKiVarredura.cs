using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// 1d) AS CABECAS NUNCA SE SOBREPOEM -- uma VARREDURA de cenas de frente (`--embatekiteste`).
///
/// ============================ POR QUE UMA VARREDURA, E NAO MAIS UMA CENA ============================
/// A familia 1b mede UMA situacao: dois jogadores a 12 tiles, na mesma linha, disparando o Ki Wave no
/// mesmo instante. Ela passa -- e o dono voltou em 2026-09-23 dizendo *"elas ainda estao se sobrepondo
/// AS VEZES"*. "As vezes" e exatamente o que uma cena so nao ve: o defeito mora nas cenas que ninguem
/// montou. Aqui cada cena muda UMA coisa (quem atira quando, a distancia entre as linhas, quem solta o
/// raio, a revanche depois de perder, a tecnica e a escala dela) e todas sao medidas pela MESMA regua.
///
/// A REGUA e a frente DESENHADA da cabeca (<see cref="Feixe.AlcanceDaCabeca(Projetil)"/>: a arte da
/// folha vezes a escala do tiro), e nao o raio de impacto: o que o dono ve e o desenho. Duas cabecas de
/// frente, a menos de um contato de distancia na lateral, nunca podem ter os centros mais perto que a
/// soma das duas frentes; uma cabeca nunca pode ter o centro mais perto de um corpo que a frente dela
/// mais a meia largura dele. A medida e tirada a cada tique, pelo tique de producao.
/// =================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Uma cena de frente: o que muda em relacao ao controle (dois Ki Wave juntos a 12 tiles).</summary>
	private sealed class CenaDeFrente
	{
		public string Nome = "";
		public int Tiles = 12;
		/// <summary>Quantos pixels a linha de B fica abaixo da de A.</summary>
		public float Lateral;
		/// <summary>B comeca a carregar este tanto de segundos depois de A.</summary>
		public double AtrasoB;
		public bool BAtira = true;
		/// <summary>A SOLTA o raio este tanto de segundos depois de ele nascer (-1 = nunca solta).</summary>
		public double SoltarAEm = -1;
		public double EscalaA = 1, EscalaB = 1;
		public string VerboA = "Ki_Wave", VerboB = "Ki_Wave";
		/// <summary>A primeira disputa acaba com A vencendo na hora, e B atira DE NOVO no feixe que vem.</summary>
		public bool Revanche;
		/// <summary>B tambem SOLTA o raio este tanto de segundos depois de ele nascer (-1 = nunca solta).</summary>
		public double SoltarBEm = -1;
		/// <summary>A aperta TODAS as letras da disputa: ela vai ate o fim, com o medidor cheio.</summary>
		public bool AEmpurra;
		/// <summary>B atira desta altura (voando). Zero = no chao.</summary>
		public float AlturaDeB;
		/// <summary>B fica este tanto de pixels ABAIXO da linha do feixe de A, mas NAO atira (o corpo de lado).</summary>
		public float CorpoDeLado;
		/// <summary>A queima-roupa: o corpo esta dentro do desenho da cabeca, e a regua do corpo nao se aplica.</summary>
		public bool QueimaRoupa;
		/// <summary>Quantas disputas a cena tem que ter (0 = nenhuma exigida).</summary>
		public int DisputasEsperadas;
		public double Segundos = 7;
	}

	private sealed class MedidaDeFrente
	{
		public float PiorEntreCabecas;
		public string OndeEntreCabecas = "nunca";
		public float PiorNoCorpo;
		/// <summary>
		/// DURANTE UMA DISPUTA, o quanto as duas cabecas se afastaram do contato exato (frente com frente) --
		/// pra cima ou pra baixo. A invasao mede so o "pra dentro"; isto pega tambem o VAO, que e o outro
		/// jeito de a disputa desenhar errado (as cabecas separadas por um buraco, sem se tocar).
		/// </summary>
		public float PiorFolgaNaDisputa;
		public string OndeFolga = "nunca";
		public string OndeNoCorpo = "nunca";
		public int Disputas;
		/// <summary>Como o primeiro raio de A acabou (Nenhum = ainda vivo no fim da cena).</summary>
		public FimDeProjetil FimDoRaioDeA = FimDeProjetil.Nenhum;
		/// <summary>
		/// O PIOR FEIXE AO CONTRARIO: o quanto uma cabeca em disputa ficou ATRAS da propria cauda (a mao do dono),
		/// medido no rumo dele. Zero = nunca.
		/// </summary>
		public float PiorAoContrario;
		public string OndeAoContrario = "nunca";
		/// <summary>O PIOR FEIXE TORTO: o quanto uma cabeca alimentada saiu da linha que passa pela mao no rumo dele.</summary>
		public float PiorTorto;
		public string OndeTorto = "nunca";
		/// <summary>B levou o feixe de A (a vida dele caiu) depois de a disputa acabar.</summary>
		public bool BLevou;
	}

	private void NuncaSeSobrepoem()
	{
		GD.Print("[embateki] -- 1d) AS CABECAS NUNCA SE SOBREPOEM: varredura de cenas de frente (a regua e a FRENTE desenhada)");

		CenaDeFrente[] cenas =
		[
			new() { Nome = "juntos a 12 tiles (o controle da 1b)", DisputasEsperadas = 1 },
			new() { Nome = "B carrega 0,4 s depois", AtrasoB = 0.4, DisputasEsperadas = 1 },
			new() { Nome = "B carrega 1,0 s depois", AtrasoB = 1.0, DisputasEsperadas = 1 },
			new() { Nome = "linhas a 12 px uma da outra", Lateral = 12, DisputasEsperadas = 1 },
			new() { Nome = "linhas a 24 px uma da outra", Lateral = 24, DisputasEsperadas = 1 },
			new() { Nome = "linhas VIZINHAS (40 px): o `range(1)` do DM disputa", Lateral = 40, DisputasEsperadas = 1 },
			new() { Nome = "A VENCE empurrando ate o fim (o medidor cheio)", AEmpurra = true, DisputasEsperadas = 1, Segundos = 16 },
			new() { Nome = "B atira VOANDO (outro andar): ninguem disputa nem se empurra", AlturaDeB = 400, Segundos = 4 },
			new() { Nome = "B PAIRA (andar 1) contra A no chao: podem se tocar, e disputam", AlturaDeB = 100, DisputasEsperadas = 1 },
			new() { Nome = "Final Flash num corpo 60 px DE LADO: acerta, e o feixe continua RETO", VerboA = "Final_Flash",
					EscalaA = 4, BAtira = false, Tiles = 16, CorpoDeLado = 60 },
			new() { Nome = "Final Flash A QUEIMA-ROUPA (corpo a 3 tiles): o feixe nunca fica ao contrario", VerboA = "Final_Flash",
					EscalaA = 4, BAtira = false, Tiles = 3, QueimaRoupa = true, Segundos = 4 },
			new() { Nome = "A SOLTA o raio antes do encontro (solto x alimentado)", SoltarAEm = 0.2 },
			new() { Nome = "os DOIS soltam o raio antes do encontro (solto x solto)", SoltarAEm = 0.2, SoltarBEm = 0.2 },
			new() { Nome = "REVANCHE: B perde a disputa e atira de novo no feixe que vem", Tiles = 20, Revanche = true, DisputasEsperadas = 2 },
			new() { Nome = "Final Flash x Final Flash (folha de 64 px, escala 4)", VerboA = "Final_Flash", VerboB = "Final_Flash",
					EscalaA = 4, EscalaB = 4, Tiles = 22, DisputasEsperadas = 1 },
			new() { Nome = "escala 2 x escala 1", EscalaA = 2, Tiles = 14, DisputasEsperadas = 1 },
			new() { Nome = "Final Flash num corpo PARADO", VerboA = "Final_Flash", EscalaA = 4, BAtira = false, Tiles = 16 },
			new() { Nome = "Ki Wave num corpo PARADO (o controle do corpo)", BAtira = false },
		];

		// UM CORREDOR SO PRA VARREDURA INTEIRA. Cada cena limpa corpos e tiros no fim, entao o mesmo chao serve
		// a todas -- e a primeira versao, que pedia um corredor novo por cena, esgotou o mapa da bancada: as
		// familias que vem DEPOIS desta ficaram sem chao livre ("achei um corredor livre de 18 tiles" reprovava
		// calado, no placar da `--projetilteste`, e nao no desta).
		_corredorDaVarredura = CorredorDuplo(28);

		foreach (CenaDeFrente c in cenas)
		{
			MedidaDeFrente m = RodarCenaDeFrente(c);
			AfirmarEk($"{c.Nome}: duas cabecas de frente nunca se invadem (pior {m.PiorEntreCabecas:0.0} px)",
					  m.PiorEntreCabecas <= 1f, m.OndeEntreCabecas);
			if (!c.QueimaRoupa)
				AfirmarEk($"{c.Nome}: nenhuma cabeca invade o corpo que ela encosta (pior {m.PiorNoCorpo:0.0} px)",
						  m.PiorNoCorpo <= 1f, m.OndeNoCorpo);
			AfirmarEk($"{c.Nome}: nenhum feixe alimentado fica TORTO, com a cabeca fora da linha da mao (pior {m.PiorTorto:0.0} px)",
					  m.PiorTorto <= 0.5f, m.OndeTorto);
			if (c.CorpoDeLado > 0 || c.QueimaRoupa)
				AfirmarEk($"{c.Nome}: o corpo foi acertado", m.BLevou);
			AfirmarEk($"{c.Nome}: nenhum feixe fica AO CONTRARIO, com a cabeca atras da propria mao (pior {m.PiorAoContrario:0.0} px)",
					  m.PiorAoContrario <= 0.5f, m.OndeAoContrario);
			if (c.AEmpurra)
				AfirmarEk($"{c.Nome}: depois de vencer, o feixe de A segue e ACERTA o corpo de B", m.BLevou);
			if (c.AlturaDeB > 0 && !Feixe.PodemSeTocar(0, c.AlturaDeB))
				AfirmarEk($"{c.Nome}: nenhuma disputa entre andares que nao se tocam ({m.Disputas})", m.Disputas == 0);
			if (c.DisputasEsperadas > 0)
			{
				AfirmarEk($"{c.Nome}: houve {c.DisputasEsperadas} disputa(s) ({m.Disputas})",
						  m.Disputas >= c.DisputasEsperadas);
				AfirmarEk($"{c.Nome}: na disputa as cabecas ficam FRENTE COM FRENTE, sem vao (pior {m.PiorFolgaNaDisputa:0.0} px)",
						  m.PiorFolgaNaDisputa <= 1f, m.OndeFolga);
			}

			// O SOLTO CONTRA O ALIMENTADO NAO SO PARA: ELE E EMPURRADO ATE SUMIR. Sem esta linha a cena ficaria
			// verde com as duas cabecas paradas encostadas pra sempre, que nao e o que o dono pediu
			// ("se empurrando") -- o `Defletido` e o fim que so o `EmpurrarCabecaSolta` da.
			if (c.SoltarAEm >= 0 && c.SoltarBEm < 0)
				AfirmarEk($"{c.Nome}: o feixe SOLTO de A foi empurrado pelo alimentado ate ser engolido ({m.FimDoRaioDeA})",
						  m.FimDoRaioDeA == FimDeProjetil.Defletido);
		}

		// ============================ OS DEFEITOS INJETADOS: a regua sabe ficar vermelha ============================
		// Cada um devolve um pedaco do codigo de antes de 2026-09-23 -- e a cena que o pegava tem que reprovar.
		// Uma varredura toda verde que nunca viu vermelho nao prova nada (a casa ja pagou por acreditar nisso).
		// ============================================================================================================
		Feixe.AlcanceFixoDeTeste = true;
		try
		{
			MedidaDeFrente m = RodarCenaDeFrente(cenas.First(x => x.VerboA == "Final_Flash" && x.BAtira));
			// A REGUA CONTINUA MEDINDO A FRENTE DE VERDADE: a injecao so muda o que o servidor USA. Por isso a
			// regua aqui recalcula o alcance pela folha e pela escala, sem a injecao.
			AfirmarEk("(injetado) com o alcance fixo de 16 px, dois Final Flash disputam com uma cabeca dentro da outra",
					  m.Disputas >= 1 && m.PiorEntreCabecas > 100f, $"{m.PiorEntreCabecas:0.0} px");
		}
		finally { Feixe.AlcanceFixoDeTeste = false; }

		Feixe.AtravessaDeFrenteDeTeste = true;
		try
		{
			MedidaDeFrente m = RodarCenaDeFrente(cenas.First(x => x.SoltarAEm >= 0 && x.SoltarBEm < 0));
			AfirmarEk("(injetado) sem a regra de frente-sem-disputa, o feixe solto atravessa o alimentado",
					  m.PiorEntreCabecas > 16f, $"{m.PiorEntreCabecas:0.0} px");
		}
		finally { Feixe.AtravessaDeFrenteDeTeste = false; }

		EmbateDeKi.FeixesForaDoEixoDeTeste = true;
		try
		{
			MedidaDeFrente m = RodarCenaDeFrente(cenas.First(x => x.Lateral == 40));
			AfirmarEk("(injetado) sem apontar os feixes pelo eixo, a disputa em linhas vizinhas ENTORTA os dois",
					  m.Disputas >= 1 && m.PiorTorto > 8f, $"{m.PiorTorto:0.0} px");
		}
		finally { EmbateDeKi.FeixesForaDoEixoDeTeste = false; }

		EmbateDeKi.EncontroAteOCorpoDeTeste = true;
		try
		{
			MedidaDeFrente m = RodarCenaDeFrente(cenas.First(x => x.AEmpurra));
			AfirmarEk("(injetado) com o encontro indo ate o CORPO do perdedor, o feixe dele fica ao contrario no fim",
					  m.PiorAoContrario > 8f, $"{m.PiorAoContrario:0.0} px");
		}
		finally { EmbateDeKi.EncontroAteOCorpoDeTeste = false; }

		EmbateDeKi.RevancheProibidaDeTeste = true;
		try
		{
			MedidaDeFrente m = RodarCenaDeFrente(cenas.First(x => x.Revanche));
			AfirmarEk("(injetado) com o `JaDisputou` de volta no gatilho, a revanche nao disputa -- so a primeira disputa acontece",
					  m.Disputas == 1, $"{m.Disputas} disputa(s)");
		}
		finally { EmbateDeKi.RevancheProibidaDeTeste = false; }
	}

	/// <summary>
	/// UMA CENA INTEIRA, pelo tique de producao: `Canalizar` abre os canais, o tique fecha a carga e poe o
	/// raio no mundo, o tique do projetil anda com ele (e dispara a disputa quando ela vier), e a bancada so
	/// MEDE -- a unica mao dela e a do jogador: carregar, soltar, e (na revanche) o funil `Resolver`, que e
	/// quem o `Decidir` chama quando o medidor enche.
	/// </summary>
	private MedidaDeFrente RodarCenaDeFrente(CenaDeFrente c)
	{
		LimparEmbatesDaBancada();
		const int T = ZoneCollision.TileSize;
		Vec2 chao = _corredorDaVarredura;
		ServerPlayer a = Forjar("Frente A", chao, bp: 50_000);
		ServerPlayer b = Forjar("Frente B", new Vec2(chao.X + c.Tiles * T, chao.Y + c.Lateral + c.CorpoDeLado), bp: 50_000);
		a.Facing = Facing.East;
		b.Facing = Facing.West;
		a.Ficha.Ki = a.Ficha.MaxKi;
		b.Ficha.Ki = b.Ficha.MaxKi;
		// OS DOIS TEM TECLADO E NINGUEM APERTA: o medidor fica no meio e a disputa dura o que a cena durar.
		// A varredura nao mede quem vence -- mede onde as cabecas estao. (Na cena `AEmpurra`, A aperta.)
		_comTecladoDeTeste.Add(a.Id);
		_comTecladoDeTeste.Add(b.Id);
		b.Altitude = c.AlturaDeB;
		double vidaDeB = b.Combate!.Corpo.Vida();

		ReceitaDeProjetil ra = SemDeflexao();
		ra.EscalaVisual = c.EscalaA;
		ReceitaDeProjetil rb = SemDeflexao();
		rb.EscalaVisual = c.EscalaB;

		var m = new MedidaDeFrente();
		Canalizar(a, c.VerboA, 10 * a.Ficha.BaseDrain(), ra);

		bool bAtirou = false, aSoltou = false, bSoltou = false, revanchou = false;
		double aNasceuEm = -1, bNasceuEm = -1, t = 0;
		Projetil? raioDeA = null;
		DisputaDeKi? vista = null;
		int tiques = (int)(c.Segundos / Protocol.TickSeconds);
		for (int i = 0; i < tiques; i++, t += Protocol.TickSeconds)
		{
			if (c.BAtira && !bAtirou && t >= c.AtrasoB)
			{
				Canalizar(b, c.VerboB, 10 * b.Ficha.BaseDrain(), rb);
				bAtirou = true;
			}

			if (c.AEmpurra && _emEmbateDeKi.TryGetValue(a.Id, out DisputaDeKi? dA))
			{
				LadoDeKi meu = dA.A.Quem == a ? dA.A : dA.B;
				if (meu.Letra != '\0') TeclaDeQualquerEmbate(a, meu.Letra);
			}

			UmTiqueDoEncontro();

			if (aNasceuEm < 0 && _canais.GetValueOrDefault(a.Id)?.Raio is { } nasceuA) { aNasceuEm = t; raioDeA = nasceuA; }
			if (bNasceuEm < 0 && _canais.GetValueOrDefault(b.Id)?.Raio != null) bNasceuEm = t;
			if (c.SoltarAEm >= 0 && !aSoltou && aNasceuEm >= 0 && t - aNasceuEm >= c.SoltarAEm)
			{
				SoltarCanal(a, c.VerboA);
				aSoltou = true;
			}
			if (c.SoltarBEm >= 0 && !bSoltou && bNasceuEm >= 0 && t - bNasceuEm >= c.SoltarBEm)
			{
				SoltarCanal(b, c.VerboB);
				bSoltou = true;
			}

			if (_emEmbateDeKi.TryGetValue(a.Id, out DisputaDeKi? d) && !ReferenceEquals(d, vista))
			{
				vista = d;
				m.Disputas++;
				if (c.Revanche && !revanchou)
				{
					// A VENCE NA HORA, pelo funil de producao (`Resolver`: o feixe de B morre, o canal de B
					// cai, o de A ganha `JaDisputou` e segue) -- e B carrega DE NOVO contra o feixe que vem.
					LadoDeKi la = d.A.Quem == a ? d.A : d.B;
					LadoDeKi lb = ReferenceEquals(la, d.A) ? d.B : d.A;
					Resolver(d, la, lb, "bancada");
					revanchou = true;
					b.Ficha.Ki = b.Ficha.MaxKi;
					Canalizar(b, c.VerboB, 10 * b.Ficha.BaseDrain(), rb);
				}
			}

			MedirSobreposicao(m, a, b, t);
		}

		m.FimDoRaioDeA = raioDeA is { Vivo: false } morto ? morto.Fim : FimDeProjetil.Nenhum;
		m.BLevou = b.Combate!.Corpo.Vida() < vidaDeB - 1e-6;
		LimparEmbatesDaBancada();
		return m;
	}

	/// <summary>
	/// A REGUA. Pra cada par de feixes de donos diferentes que VEM UM CONTRA O OUTRO, e pra cada corpo na
	/// frente de uma cabeca: quanto os desenhos se invadem, medido no eixo do feixe.
	/// </summary>
	private void MedirSobreposicao(MedidaDeFrente m, ServerPlayer a, ServerPlayer b, double t)
	{
		List<Projetil> lista = ProjeteisDaZona(a.Zone.Hash);
		for (int i = 0; i < lista.Count; i++)
		{
			Projetil p = lista[i];
			if (!p.Vivo || p.Tipo != TipoDeProjetil.Beam) continue;
			float ap = AlcanceDesenhado(p);

			// TORTO: um feixe ALIMENTADO e uma reta que sai da mao do dono no rumo do tiro. A cabeca fora dessa
			// reta e o desenho entortando (a cabeca plantada de lado, a empurrada pra outra linha).
			//
			// E A DISPUTA NAO E MAIS EXCECAO (2026-10-07). Esta regua pulava os feixes em embate -- porque la a
			// cabeca era levada pro eixo corpo a corpo e a cauda ficava na boca do rumo cardeal: todo embate fora
			// da mesma linha era torto POR CONSTRUCAO, e medir so teria reprovado a varredura inteira. Era o
			// "beam todo torto" que o dono viu. O `Comecar` passou a apontar os dois feixes pelo eixo, e a regua
			// vale pra todo mundo.
			if (p.Canalizando)
			{
				Vec2 dm = p.Pos - p.Cauda;
				float torto = MathF.Abs(dm.X * p.Rumo.Y - dm.Y * p.Rumo.X);
				if (torto > m.PiorTorto)
				{
					m.PiorTorto = torto;
					m.OndeTorto = $"t={t:0.00}s #{p.Id}[{EstadoDoFeixe(p)}]: cabeca {torto:0.0} px fora da linha da mao";
				}
			}

			// AO CONTRARIO: a cabeca de um feixe ALIMENTADO atras da propria cauda (a mao do dono), no rumo dele --
			// na disputa (o fim dela) e fora (a queima-roupa).
			if (p.Canalizando)
			{
				Vec2 corpo = p.Pos - p.Cauda;
				float atras = -(corpo.X * p.Rumo.X + corpo.Y * p.Rumo.Y);
				if (atras > m.PiorAoContrario)
				{
					m.PiorAoContrario = atras;
					m.OndeAoContrario = $"t={t:0.00}s #{p.Id}: cabeca {atras:0.0} px atras da mao do dono";
				}
			}

			for (int j = i + 1; j < lista.Count; j++)
			{
				Projetil q = lista[j];
				if (!q.Vivo || q.Tipo != TipoDeProjetil.Beam || q.Dono == p.Dono) continue;
				if (!Feixe.VemContra(p.Rumo, q.Rumo)) continue;
				// ANDARES QUE NAO SE TOCAM NAO SE TOCAM NO DESENHO: cada um e subido pela propria altura.
				if (!Feixe.PodemSeTocar(p.Altitude, q.Altitude)) continue;

				// O MODELO E O DO GATILHO, NO PLANO DESENHADO: cada cabeca e um circulo do tamanho da frente dela,
				// na posicao em que ela APARECE, e dois circulos se invadem quando os centros estao mais perto que
				// a soma. MAS UMA CABECA QUE JA PASSOU A OUTRA (frente negativa no eixo) na mesma faixa atravessou o
				// circulo dela em algum sub-passo entre duas medidas -- e isso conta inteiro, mesmo que agora os
				// centros estejam longe.
				float contato = ap + AlcanceDesenhado(q);
				Vec2 entre = Feixe.NaTela(q) - Feixe.NaTela(p);
				(float frente, float lado) = NoEixoDoFeixe(entre, p.Rumo);
				if (lado >= contato) continue;   // linhas afastadas o bastante: os desenhos nao se tocam

				float invade = frente >= 0 ? contato - entre.Length : contato - frente;
				if (p.EmEmbate && q.EmEmbate)
				{
					float folga = MathF.Abs(entre.Length - contato);
					if (folga > m.PiorFolgaNaDisputa)
					{
						m.PiorFolgaNaDisputa = folga;
						m.OndeFolga = $"t={t:0.00}s: {entre.Length:0.0} px entre as cabecas desenhadas, contato {contato:0.0}";
					}
				}
				if (invade <= m.PiorEntreCabecas) continue;
				m.PiorEntreCabecas = invade;
				m.OndeEntreCabecas = $"t={t:0.00}s #{p.Id}[{EstadoDoFeixe(p)}] x #{q.Id}[{EstadoDoFeixe(q)}]: "
								   + $"{frente:0.0} px no eixo, {lado:0.0} px de lado, contato {contato:0.0} px";
			}

			foreach (ServerPlayer o in new[] { a, b })
			{
				if (o.Id == p.Dono) continue;
				float contato = ap + Feixe.MeioCorpo;
				(float frente, float lado) = NoEixoDoFeixe(o.Pos + Feixe.Subida(o.Altitude) - Feixe.NaTela(p), p.Rumo);
				// SO O QUE ESTA NA FRENTE (ou dentro) da cabeca: um corpo que ficou pra tras dela inteira foi
				// ultrapassado (esquiva, corte), e isso e outra conversa.
				if (lado >= contato || frente < -ap) continue;

				float invade = contato - frente;
				if (invade <= m.PiorNoCorpo) continue;
				m.PiorNoCorpo = invade;
				m.OndeNoCorpo = $"t={t:0.00}s #{p.Id}[{EstadoDoFeixe(p)}] x {o.Name}: {frente:0.0} px no eixo, "
							  + $"{lado:0.0} px de lado, contato {contato:0.0} px";
			}
		}
	}

	/// <summary>
	/// A FRENTE DESENHADA DE VERDADE -- a folha vezes a escala, lida direto da tabela e NAO pelo
	/// `Feixe.AlcanceDaCabeca`: a regua tem que continuar medindo o desenho quando a bancada injeta o
	/// alcance fixo no servidor (`Feixe.AlcanceFixoDeTeste`). Uma regua que usasse a mesma funcao que o
	/// defeito estraga ficaria verde junto com ele.
	/// </summary>
	private static float AlcanceDesenhado(Projetil p) =>
		p.Tipo == TipoDeProjetil.Beam
			? ArteDeProjetil.FrenteDaCabeca(p.Arte) * (float)(p.EscalaVisual > 0 ? p.EscalaVisual : 1)
			: Projetil.RaioDeImpacto;

	private static (float Frente, float Lado) NoEixoDoFeixe(Vec2 d, Vec2 rumo)
		=> (d.X * rumo.X + d.Y * rumo.Y, MathF.Abs(d.X * rumo.Y - d.Y * rumo.X));

	private static string EstadoDoFeixe(Projetil p) =>
		(p.Canalizando ? "alimentado" : "solto")
		+ (p.EmEmbate ? ",disputa" : "") + (p.JaDisputou ? ",ja-disputou" : "")
		+ (p.Esperando ? ",esperando" : "") + (p.Encostado ? ",encostado" : "")
		+ (p.Esvaziando ? ",esvaziando" : "") + $",alcance {AlcanceDesenhado(p):0}";

	/// <summary>
	/// UM CORREDOR DE DUAS FILEIRAS em que um corpo pode ficar de pe: a cena de linhas desencontradas poe o
	/// segundo duelista (e o raio dele) ate 31 px abaixo -- na fileira de baixo. O `CorredorDeChao` so
	/// promete uma.
	/// </summary>
	// =====================================================================
	// 1e) O TRONCO LARGO: cruzar e cortar na BEIRADA desenhada (2026-09-23)
	// =====================================================================
	/// <summary>
	/// O tronco do Final Flash e desenhado 108 px pra cada lado do eixo (a `Beam - Big Fire.dmi`, 27 px de meia
	/// espessura, na escala 4). Duas regras encostam nele e as duas encostavam no EIXO: a cabeca que cruza (e
	/// espera) entrava 92 px no desenho antes de parar, e um corpo a 80 px do eixo -- dentro do desenho -- nao
	/// cortava nada. Mede-se as duas, e o defeito injetado (o alcance fixo, que tambem afina o tronco) volta a
	/// deixar a cabeca entrar.
	/// </summary>
	private void OTroncoLargoEncostaNaBeirada()
	{
		GD.Print("[embateki] -- 1e) O TRONCO LARGO: quem cruza espera, e quem encosta corta, na BEIRADA desenhada");
		float Espera(out bool esperou)
		{
			LimparEmbatesDaBancada();
			const int T = ZoneCollision.TileSize;
			Vec2 canto = _cantoDoTroncoLargo;
			ServerPlayer a = Forjar("Tronco Largo", new Vec2(canto.X, canto.Y + 3 * T), bp: 50_000);
			a.Facing = Facing.East;
			ServerPlayer b = Forjar("Quem Cruza", new Vec2(canto.X + 7 * T, canto.Y + 13 * T), bp: 50_000);
			b.Facing = Facing.North;

			ReceitaDeProjetil ff = SemDeflexao();
			ff.EscalaVisual = 4;
			Projetil pa = Disparar(a, ff, verbo: "Final_Flash");
			pa.Canalizando = true;
			// UM TRONCO JA ESTENDIDO E QUIETO: a cabeca 12 tiles adiante e andando devagar, pra o cruzamento
			// acontecer no tronco (e nao na cabeca) enquanto a cena dura.
			pa.Pos = a.Pos + new Vec2(12 * T, 0);
			pa.SegundosPorTile = 1000;

			Projetil pb = Disparar(b, SemDeflexao(), verbo: "Ki_Wave");
			pb.Canalizando = true;
			for (int i = 0; i < 90 && pb.Vivo && !pb.Esperando; i++) TickDosProjeteis(Protocol.TickSeconds);
			esperou = pb.Esperando;
			float doEixo = MathF.Abs(pb.Pos.Y - pa.Pos.Y);
			LimparEmbatesDaBancada();
			return doEixo;
		}

		_cantoDoTroncoLargo = QuadradoLivre(16);
		float esperada = Projetil.RaioDeImpacto + 27f * 4f;
		float doEixo = Espera(out bool esperou);
		AfirmarEk($"a cabeca que CRUZA o tronco de um Final Flash espera com a frente na BEIRADA desenhada dele "
				  + $"({doEixo:0.0} px do eixo; a beirada + a frente = {esperada:0})",
				  esperou && doEixo >= esperada - 1f && doEixo <= esperada + Projetil.RaioDeImpacto + 1f);

		Feixe.AlcanceFixoDeTeste = true;
		try
		{
			float fina = Espera(out bool esperouFina);
			AfirmarEk($"(injetado) com o tronco tratado como um fio, a cabeca entra no desenho dele ({fina:0.0} px do eixo)",
					  esperouFina && fina < esperada - 40f);
		}
		finally { Feixe.AlcanceFixoDeTeste = false; }

		// O CORTE NA BEIRADA: um corpo a 80 px do eixo esta dentro do desenho do tronco, e corta.
		LimparEmbatesDaBancada();
		{
			const int T = ZoneCollision.TileSize;
			Vec2 canto = _cantoDoTroncoLargo;
			ServerPlayer a = Forjar("Tronco Largo", new Vec2(canto.X, canto.Y + 3 * T), bp: 50_000);
			a.Facing = Facing.East;
			ReceitaDeProjetil ff = SemDeflexao();
			ff.EscalaVisual = 4;
			Projetil pa = Disparar(a, ff, verbo: "Final_Flash");
			pa.Canalizando = true;
			pa.Pos = a.Pos + new Vec2(12 * T, 0);
			pa.SegundosPorTile = 1000;
			ServerPlayer c = Forjar("Ao Lado", new Vec2(canto.X + 5 * T, canto.Y + 3 * T + 80), bp: 50_000);
			for (int i = 0; i < 3; i++) TickDosProjeteis(Protocol.TickSeconds);
			bool cortou = ProjeteisDaZona(a.Zone.Hash).Exists(x => x.NascidoDoCorte == pa.Id);
			AfirmarEk("um corpo a 80 px do eixo do Final Flash (dentro dos 108 px do desenho) CORTA o tronco",
					  cortou && pa.Pos.X < c.Pos.X, $"cabeca de ca em x {pa.Pos.X:0}, corpo em x {c.Pos.X:0}");
		}
		LimparEmbatesDaBancada();
	}

	private Vec2 _cantoDoTroncoLargo;

	/// <summary>O corredor de duas fileiras que TODA cena da varredura usa -- ver `NuncaSeSobrepoem`.</summary>
	private Vec2 _corredorDaVarredura;

	private Vec2 CorredorDuplo(int tiles)
	{
		ZoneCollision? mapa = _pjMapa;
		if (mapa == null) return CorredorLivre(tiles);

		for (int y = _pjProximoCorredor; y < 249; y++)
			for (int x = 4; x + tiles < 250; x++)
			{
				bool serve = true;
				for (int d = 0; d <= tiles && serve; d++)
					serve &= mapa.ServeDeChao(x + d, y) && mapa.ServeDeChao(x + d, y + 1);
				if (!serve) continue;

				_pjProximoCorredor = y + 4;
				return new Vec2(x * ZoneCollision.TileSize + 16, y * ZoneCollision.TileSize + 16);
			}

		AfirmarEk($"achei um corredor de duas fileiras e {tiles} tiles", false, "varredura do mapa falhou");
		return CorredorLivre(tiles);
	}
}
