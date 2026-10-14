using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.Verbos;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA TELA "MESTRE E ALUNOS" NO SERVIDOR (`--alunosteste`).
///
/// O dono (2026-10-11): *"ter tipo uma aba people so q de alunos (caso vc seja o mestre) e mestre (caso vc seja
/// aluno) ao qual vc pode ver seus alunos e o quao fortes eles estao progredindo no q vc ensinou. e o aluno pode ver
/// o mestre e os outros alunos pra ver o quao bem ele ta progredindo em relacao aos outros alunos"* -- e, sobre o
/// poder: *"o mestre nao pode ver o bp dos alunos ... sem scouter vc n tem como saber seu bp de nenhuma forma"*.
///
/// O QUE SO DAQUI SE VE e a corrente inteira, pelos funis de producao: o vinculo nasce pelo verb -> o diario nasce com
/// ele -> o mestre provoca / ensina pelos verbs -> o diario anota -> o corpo do aluno progride -> o painel que SAI NO
/// FIO (os bytes) diz isso a quem pode ler, e so a ele.
///
///   1. A REGRA PURA: o progresso de uma forma e de uma licao, os pontos, o empate.
///   2. O DIARIO NASCE COM O VINCULO (e o vinculo antigo ganha um, sem data).
///   3. O QUE O MESTRE ENSINA ENTRA NO DIARIO: a tentativa, o despertar assistido, a licao -- e so o que e DELE.
///   4. O MESTRE RECEBE OS ALUNOS com o progresso certo, do mais adiantado pro menos.
///   5. O ALUNO RECEBE O MESTRE E OS COLEGAS: a posicao em cada ensinamento, e do colega so o que e em comum.
///   6. QUEM ESTA DE FORA NAO RECEBE NADA; outro mestre nao ve estes alunos.
///   7. A FOTOGRAFIA DO OFFLINE, o disco (ida e volta) e o arquivo antigo ou estragado.
///   8. NADA DE PODER NOS BYTES, com scouter ou sem.
///   9. COM A TELA ABERTA, so sai quando muda.
///  10. OS GESTOS DO CARTAO: dispensar AQUELE aluno (de qualquer lugar) e ajudar AQUELE aluno (so de frente pra ele).
///  11. O TIQUE: a foto sem ninguem pedir, e a faxina do vinculo que acabou.
///
/// Os defeitos injetados: `ProgressoDeAluno.ColegaVeTudoDeTeste`, `ProgressoDeAluno.SemOrdemDeTeste`,
/// `PainelSemVinculoDeTeste`, `FotoDoAlunoNaoSeGuardaDeTeste`, `PoderNoPainelDeTeste`, `CartaoSemMiraDeTeste`.
///
/// OS CORPOS SAO FORJADOS numa zona pre-feita vazia, e tudo o que a bancada mexe no mundo (os vinculos, os nomes, os
/// diarios, o `mestres.txt` e o arquivo do diario) e fotografado na entrada e devolvido no `finally`.
///
///     Godot --headless --path . --server --port 8300 --alunosteste
/// </summary>
public partial class GameServer
{
	/// <summary>A flag da bancada, lida AQUI e nao no `GameServer.cs` (o molde da `--verbsteste`): quem a dispara e o tique do diario.</summary>
	private static readonly bool AlunosDeTeste = Array.IndexOf(OS.GetCmdlineArgs(), "--alunosteste") >= 0;

	private bool _alunosTestou;

	/// <summary>Faixa de ids de bancada -- bem longe do `_nextId`, e das outras bancadas.</summary>
	private const int IdBaseDosAlunosDeTeste = 98_300;

	/// <summary>A bancada roda UMA vez, no primeiro tique do diario depois do boot (os corpos sao forjados: nao precisa de login).</summary>
	private void DispararABancadaDosAlunos()
	{
		if (_alunosTestou) return;
		_alunosTestou = true;
		GD.Print("[server] BANCADA: a tela Mestre e alunos (--alunosteste)");
		RodarBancadaDosAlunos();
	}

	private void RodarBancadaDosAlunos()
	{
		GD.Print("\n[alunos] ================ MESTRE E ALUNOS: O DIARIO, O PAINEL E QUEM O RECEBE (dono, 2026-10-11) ================");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[alunos]   OK    {nome}"); }
			else { falhou++; GD.PrintErr($"[alunos]   FALHA {nome}   [{detalhe}]"); }
		}
		Checagem Mutar = (nome, cond, detalhe) => Checa(nome, cond, detalhe);

		// ============================ O MUNDO VOLTA COMO ESTAVA ============================
		GarantirDiarios();
		var vinculosDeAntes = new Dictionary<string, string>(_mestreDe, StringComparer.Ordinal);
		var nomesDeAntes = new Dictionary<string, string>(_nomePorAssinatura, StringComparer.Ordinal);
		var diariosDeAntes = new Dictionary<string, DiarioDeAluno>(_diarios, StringComparer.Ordinal);
		var pessoasDeAntes = new Dictionary<string, PessoaDoDiario>(_pessoasDoDiario, StringComparer.Ordinal);
		long relogioDeAntes = AdiantoDoRelogioDeTeste;
		_mestreDe.Clear();
		_nomePorAssinatura.Clear();
		_diarios.Clear();
		_pessoasDoDiario.Clear();
		_paineisMandados.Clear();

		const int T = ZoneCollision.TileSize;
		// UM PLANETA PRE-FEITO SEM NINGUEM -- e cujo nome nao caiba dentro de nenhum texto do elenco (ele e um dos
		// canarios da secao 8: "Namek" seria achado dentro de "Namekian", a raca de um aluno).
		const string textosDoElenco = "Saiyan Namekian Human Aluno Mestre Estranho";
		ZoneKey zona = ZoneKey.Premade(
			Espaco.PreFeitos().Select(p => p.Nome)
				.FirstOrDefault(n => n.Length >= 4 && !textosDoElenco.Contains(n, StringComparison.OrdinalIgnoreCase)
									 && !_players.Values.Any(p => string.Equals(p.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
			?? "Vegeta");

		var corpos = new List<ServerPlayer>();
		ServerPlayer Forjar(string nome, string raca, double bp)
		{
			int i = corpos.Count;
			var novo = new ServerPlayer
			{
				Id = IdBaseDosAlunosDeTeste + i, Peer = null, Name = nome, Race = raca, Genero = "Male", Idade = 25,
				Zone = zona, Pos = new Vec2((100 + 40 * i) * T, 0),
				// A CONTA E O SLOT dao a ASSINATURA, e ela e a chave do discipulado inteiro (ver `EhPessoa`).
				Conta = $"bancada_alunos_{(char)('a' + i)}", Slot = 0,
				Ficha = new Fighter { Race = raca, BP = bp },
				Livro = new SkillBook(),
			};
			novo.Ficha.Class = "Normal";
			PorNoMundo(novo);
			novo.Ficha.Ki = novo.Ficha.MaxKi;
			novo.Ficha.Tick(agoraMs: NowMs());
			corpos.Add(novo);
			return novo;
		}

		// QUEM NAO E O ALVO FICA LONGE (a licao da `--mestreteste`): cada corpo tem a vaga dele, a 40 tiles da vizinha.
		void Estacionar(ServerPlayer p) => p.Pos = new Vec2((100 + 40 * corpos.IndexOf(p)) * T, 0);
		void TodosLonge() { foreach (ServerPlayer p in corpos) Estacionar(p); }
		// `b` colado na frente de `a`, olho no olho, e mais ninguem por perto.
		void FrenteAFrente(ServerPlayer a, ServerPlayer b)
		{
			TodosLonge();
			a.Pos = new Vec2(0, 0);
			b.Pos = new Vec2(0.9f * T, 0);
			a.Facing = Facing.East;
			b.Facing = Facing.West;
		}

		// O PEDIDO, pelo funil de producao (`Verbo`), e os pacotes que sairam por causa dele.
		List<(int Para, byte[] Pacote)> Pedir(ServerPlayer quem, string cmd = "alunos", string arg = "")
		{
			var saidos = new List<(int Para, byte[] Pacote)>();
			EscutaDoDiscipulado = saidos;
			try { Verbo(quem, cmd, arg); }
			finally { EscutaDoDiscipulado = null; }
			return saidos;
		}

		// O PACOTE, lido pelo MESMO leitor do cliente. `Sobrou` = os bytes que ele nao consumiu.
		static (Protocol.S2C Opcode, PainelDoDiscipulado Painel, int Sobrou) Abrir(byte[] pacote)
		{
			var r = new NetDataReader(pacote);
			var opcode = (Protocol.S2C)r.GetByte();
			PainelDoDiscipulado p = FioDosAlunos.Ler(r);
			return (opcode, p, r.AvailableBytes);
		}

		PainelDoDiscipulado? PainelPeloFio(ServerPlayer quem)
		{
			List<(int Para, byte[] Pacote)> s = Pedir(quem);
			return s.Count == 1 && s[0].Para == quem.Id ? Abrir(s[0].Pacote).Painel : null;
		}

		static bool Contem(byte[] onde, byte[] oQue)
		{
			if (oQue.Length == 0) return false;
			for (int i = 0; i + oQue.Length <= onde.Length; i++)
				if (onde.AsSpan(i, oQue.Length).SequenceEqual(oQue)) return true;
			return false;
		}
		static byte[] Texto(string s) => System.Text.Encoding.UTF8.GetBytes(s);

		static EnsinamentoNoPainel? Do(IReadOnlyList<EnsinamentoNoPainel> lista, TipoDeEnsinamento tipo, string id)
		{
			foreach (EnsinamentoNoPainel e in lista) if (e.Tipo == tipo && e.Id == id) return e;
			return null;
		}
		static AlunoNoPainel? Aluno(PainelDoDiscipulado? p, ServerPlayer quem) => p?.Alunos.FirstOrDefault(a => a.Quem.Assinatura == quem.Assinatura);

		try
		{
			// =====================================================================
			// 1) A REGRA PURA
			// =====================================================================
			GD.Print("[alunos] -- 1) a regra pura: o progresso de uma forma e de uma licao, os pontos e o empate --");
			ProgressoNoEnsinamento naoDespertou = ProgressoDeAluno.DaForma(false, 80), recem = ProgressoDeAluno.DaForma(true, 0),
								   meio = ProgressoDeAluno.DaForma(true, 37.26), dominada = ProgressoDeAluno.DaForma(true, 100);
			Checa("FORMA: sem despertar nao ha progresso (0 ponto), mesmo com maestria escrita; recem-despertada vale a METADE; "
				  + "37,26% de maestria le 37% e 372 milesimos; 100% e DOMINADA (1 ponto)",
				  naoDespertou is { Etapa: EtapaDoEnsinamento.AindaNao, Pontos: 0 }
				  && recem.Etapa == EtapaDoEnsinamento.EmCurso && Math.Abs(recem.Pontos - ProgressoDeAluno.MetadeDeReceber) < 1e-9
				  && meio is { Etapa: EtapaDoEnsinamento.EmCurso, Nivel: 37, Teto: 100, Milesimos: 372 }
				  && dominada is { Etapa: EtapaDoEnsinamento.Dominado, Pontos: 1 },
				  $"{naoDespertou} | {recem} | {meio} | {dominada}");

			ProgressoNoEnsinamento esqueceu = ProgressoDeAluno.DaLicao(false, 3, 2, 50, 100), semNiveis = ProgressoDeAluno.DaLicao(true, 0, 0, 0, 0),
								   nivel1 = ProgressoDeAluno.DaLicao(true, 3, 1, 50, 100), noTeto = ProgressoDeAluno.DaLicao(true, 3, 3, 0, 0);
			Checa("LICAO: esquecida nao conta; skill SEM niveis esta completa ao aprender; nivel 1 de 3 com meia barra de exp le 500 "
				  + "milesimos (metade do caminho); no ultimo nivel e DOMINADA",
				  esqueceu is { Etapa: EtapaDoEnsinamento.AindaNao, Pontos: 0 }
				  && semNiveis is { Etapa: EtapaDoEnsinamento.Dominado, Teto: 0, Pontos: 1 }
				  && nivel1 is { Etapa: EtapaDoEnsinamento.EmCurso, Nivel: 1, Teto: 3, Milesimos: 500 }
				  && noTeto is { Etapa: EtapaDoEnsinamento.Dominado, Nivel: 3, Teto: 3 },
				  $"{esqueceu} | {semNiveis} | {nivel1} | {noTeto}");

			int[] posicoes = ProgressoDeAluno.Posicoes([1.0, 0.5, 0.5, 0.0]);
			(double soma, double media) = ProgressoDeAluno.Geral([dominada, recem, naoDespertou]);
			Checa("A ORDEM: o EMPATE divide o lugar (1o, 2o, 2o, 4o), e o progresso geral e a soma (ordena) e a media (o que a tela escreve)",
				  posicoes.SequenceEqual([1, 2, 2, 4]) && Math.Abs(soma - 1.5) < 1e-9 && Math.Abs(media - 0.5) < 1e-9
				  && ProgressoDeAluno.Geral([]) == (0, 0),
				  $"[{string.Join(",", posicoes)}] soma {soma} media {media}");

			var comRequisito = new PosseEscrita { Raca = "Human", Fatos = Fatos.TemMestre };
			var guia = new PosseEscrita { Raca = "Human", Fatos = Fatos.TemAluno };
			var semNada = new PosseEscrita { Raca = "Human" };
			Checa("A ABA SO EXISTE PRA QUEM TEM O VINCULO (a tabela de requisitos): quem tem mestre ou aluno ve o verb do painel; "
				  + "os dois gestos do cartao de um aluno so quem guia; quem nao tem nada, nenhum dos tres",
				  RequisitosDosVerbos.DoVerbo("alunos").Atende(comRequisito) && RequisitosDosVerbos.DoVerbo("alunos").Atende(guia)
				  && !RequisitosDosVerbos.DoVerbo("alunos").Atende(semNada)
				  && RequisitosDosVerbos.DoVerbo("alunos_ensinar").Atende(guia) && !RequisitosDosVerbos.DoVerbo("alunos_ensinar").Atende(comRequisito)
				  && RequisitosDosVerbos.DoVerbo("alunos_dispensar").Atende(guia) && !RequisitosDosVerbos.DoVerbo("alunos_dispensar").Atende(semNada)
				  && RequisitosDosVerbos.Linha("verbo:alunos") is { } linhaDaAba && SemDiscipulado.Contains(linhaDaAba.Recusa)
				  && RequisitosDosVerbos.Linha("verbo:alunos_dispensar") is { } linhaDoGesto && SoPraAlunoSeu.Contains(linhaDoGesto.Recusa));

			if (_skills is not { } cat)
			{
				Checa("o catalogo de skills carregou (sem ele nao ha licao pra ensinar)", false);
				return;
			}

			// AS LICOES, ESCOLHIDAS POR CAMPO (um id escrito a mao envelhece calado): uma ensinavel que SOBE de nivel e
			// duas que nao sobem.
			List<Skill> ensinaveis = [.. cat.Todas.Where(s => s.Ensinavel && !s.Arvore && s.Nome.Length > 0).OrderBy(s => s.Path, StringComparer.Ordinal)];
			Skill? comNiveis = ensinaveis.FirstOrDefault(s => RegrasDeNivel.Get(s.Path) != null && s.MaxNivel >= 2);
			List<Skill> semRegra = [.. ensinaveis.Where(s => RegrasDeNivel.Get(s.Path) == null)];
			Checa("(montagem) o catalogo tem uma skill ensinavel que SOBE de nivel e tres que nao sobem",
				  comNiveis != null && semRegra.Count >= 3, $"com niveis: {comNiveis?.Path}; sem regra: {semRegra.Count}");
			if (comNiveis == null || semRegra.Count < 3) return;
			Skill semNiveisA = semRegra[0], soDoColega = semRegra[1], doEstranho = semRegra[2];

			// =====================================================================
			// O ELENCO
			// =====================================================================
			FormaDef ssj1 = Catalogo.Def("ssj1")!;
			ServerPlayer mestre = Forjar("Mestre Kame", "Saiyan", 1);
			ServerPlayer a = Forjar("Aluno Gohan", "Saiyan", 1);       // desperta com o mestre, e recebe duas licoes
			ServerPlayer b = Forjar("Aluno Kuririn", "Saiyan", 1);     // o mestre tenta e NAO desperta; recebe uma licao
			ServerPlayer c = Forjar("Aluno Dende", "Namekian", 1);     // outra raca; recebe uma licao em comum e uma SO DELE
			ServerPlayer estranho = Forjar("Estranho", "Human", 1);    // sem vinculo nenhum
			ServerPlayer outroMestre = Forjar("Mestre Tsuru", "Human", 1);
			ServerPlayer d = Forjar("Aluno Chaos", "Human", 1);        // aluno do OUTRO mestre

			// O PODER DE CADA UM E UM CANARIO (um numero que nao se parece com mais nada do pacote), e o do mestre paga
			// o portao de 3x do convite com folga.
			double porta = PortaDeTeste(a, "ssj1");
			a.Ficha.BP = Math.Round(porta * 0.6) + 0.321;
			b.Ficha.BP = Math.Round(porta * 0.4) + 0.654;
			c.Ficha.BP = 2_345_678;
			d.Ficha.BP = 3_456_789;
			estranho.Ficha.BP = 4_567_891;
			mestre.Ficha.BP = Math.Max(porta, c.Ficha.BP) * 9 + 7_654_321;
			outroMestre.Ficha.BP = mestre.Ficha.BP * 4 + 1_111_111;     // (na secao 5 ele vira mestre do proprio Mestre Kame)
			foreach (ServerPlayer p in corpos) p.Livro.Conceder(10_000);   // marcos de sobra: a licao exige TER, e nao gasta

			// =====================================================================
			// 2) O DIARIO NASCE COM O VINCULO
			// =====================================================================
			GD.Print("[alunos] -- 2) o diario nasce com o vinculo (pelos verbs de producao) --");
			bool Vincula(ServerPlayer m, ServerPlayer al)
			{
				FrenteAFrente(m, al);
				m.PedidoDoMestre = al.PedidoDoMestre = null;
				UsarHabilidade(m, "mst_convidar");
				UsarHabilidade(al, "mst_aceitar");
				return MestreDe(al.Assinatura) == m.Assinatura;
			}

			Checa("(antes) sem vinculo nao ha diario: ninguem do elenco tem um", _diarios.Count == 0 && DiarioDoVinculo(a.Assinatura) == null);
			long antesDoVinculo = NowMs();
			bool vinculou = Vincula(mestre, a);
			DiarioDeAluno? diarioDeA = _diarios.GetValueOrDefault(a.Assinatura);
			Checa("**O MESTRE CONVIDA E O ALUNO ACEITA: o diario do vinculo NASCE na hora**, com o par certo, a data de hoje e nenhum ensinamento",
				  vinculou && diarioDeA is { Ensinamentos.Count: 0 } && diarioDeA.Mestre == mestre.Assinatura && diarioDeA.Aluno == a.Assinatura
				  && diarioDeA.DesdeMs >= antesDoVinculo && diarioDeA.DesdeMs <= NowMs(),
				  $"vinculou {vinculou}; diario {(diarioDeA == null ? "nulo" : $"desde {diarioDeA.DesdeMs}, {diarioDeA.Ensinamentos.Count} ensinamento(s)")}");
			Checa($"...e ja foi pro disco (o `{NomeDoArquivoDosDiarios}`, ao lado do `mestres.txt`), com o nome e a raca dos dois lembrados",
				  System.IO.File.Exists(ArquivoDosDiarios) && System.IO.File.ReadAllText(ArquivoDosDiarios).Contains(a.Assinatura)
				  && _pessoasDoDiario.GetValueOrDefault(a.Assinatura) is { Nome: "Aluno Gohan", Raca: "Saiyan" }
				  && _pessoasDoDiario.GetValueOrDefault(mestre.Assinatura) is { Nome: "Mestre Kame" });

			// O VINCULO ANTIGO (de antes de o diario existir): entra direto no dicionario, como o `mestres.txt` o traria.
			_mestreDe[b.Assinatura] = mestre.Assinatura;
			_nomePorAssinatura[b.Assinatura] = b.Name;
			DiarioDeAluno? diarioDeB = DiarioDoVinculo(b.Assinatura);
			Checa("O VINCULO DE ANTES DO DIARIO ganha um na primeira consulta -- VAZIO e SEM DATA (nao se inventa \"aluno desde hoje\")",
				  diarioDeB is { DesdeMs: 0, Ensinamentos.Count: 0 } && diarioDeB.Mestre == mestre.Assinatura);
			Checa("(montagem) os outros vinculos: Dende com o mesmo mestre, Chaos com o OUTRO",
				  Vincula(mestre, c) && Vincula(outroMestre, d) && ContarAlunos(mestre.Assinatura) == 3);

			// =====================================================================
			// 3) O QUE O MESTRE ENSINA ENTRA NO DIARIO
			// =====================================================================
			GD.Print("[alunos] -- 3) o que o mestre ensina entra no diario: a tentativa, o despertar assistido, a licao --");
			mestre.Forma.Liberar("ssj1");   // o mestre POSSUI a forma (o `mst_form_known` do DM): pode mostra-la

			// ---- a forma: o Gohan tem 60% da porta (a metade assistida passa), o Kuririn 40% (nao passa) ----
			FrenteAFrente(mestre, a);
			mestre.RecargaDeEnsino = 0;
			UsarHabilidade(mestre, "mst_ensinar:ssj1");
			Ensinamento? tentativa = diarioDeA?.Achar(TipoDeEnsinamento.Forma, "ssj1");
			Checa("**O MESTRE PROVOCA (a oferta sai, a recarga corre): a forma ENTRA no diario como tentativa** -- 1 vez, ainda nao despertou",
				  a.PedidoDoMestre is { Despertar: true } && tentativa is { Tentativas: 1, Assistida: false }
				  && tentativa.Progresso.Etapa == EtapaDoEnsinamento.AindaNao && tentativa.EmMs > 0,
				  tentativa == null ? "sem ensinamento" : $"tentativas {tentativa.Tentativas}, assistida {tentativa.Assistida}, etapa {tentativa.Progresso.Etapa}");
			UsarHabilidade(a, "mst_aceitar");
			Checa("**O ALUNO DESPERTA COM O MESTRE: o diario guarda que foi COM ele**, e a fotografia ja diz \"despertou\"",
				  a.Forma.Atual == "ssj1" && tentativa is { Tentativas: 1, Assistida: true }
				  && tentativa.Progresso.Etapa == EtapaDoEnsinamento.EmCurso && diarioDeA!.Ensinamentos.Count == 1,
				  $"forma {a.Forma.Atual}; {(tentativa == null ? "sem ensinamento" : $"assistida {tentativa.Assistida}, etapa {tentativa.Progresso.Etapa}")}");
			Transformar(a, subir: false);   // volta a base pelo funil (encerra a cena da estreia, como na `--mestreteste`)

			for (int vez = 1; vez <= 2; vez++)
			{
				FrenteAFrente(mestre, b);
				mestre.RecargaDeEnsino = 0;
				UsarHabilidade(mestre, "mst_ensinar:ssj1");
				UsarHabilidade(b, "mst_aceitar");
			}
			Ensinamento? frustrada = diarioDeB?.Achar(TipoDeEnsinamento.Forma, "ssj1");
			Checa("O KURIRIN (40% da porta) NAO DESPERTA nas duas tentativas: o diario dele diz 2 tentativas, sem o \"com o mestre\", ainda nao despertou",
				  b.Forma.Atual == Catalogo.IdBase && !b.Forma.Despertou("ssj1")
				  && frustrada is { Tentativas: 2, Assistida: false } && frustrada.Progresso.Etapa == EtapaDoEnsinamento.AindaNao,
				  frustrada == null ? "sem ensinamento" : $"tentativas {frustrada.Tentativas}, assistida {frustrada.Assistida}");

			// ---- as licoes, pelo verb de ensino de producao ----
			bool Ensina(ServerPlayer quem, ServerPlayer aQuem, Skill s)
			{
				if (!quem.Livro.Sabe(s.Path)) quem.Livro.Dar(s.Path);
				FrenteAFrente(quem, aQuem);
				quem.RecargaDaLicao = 0;
				aQuem.PedidoDeLicao = null;
				UsarHabilidade(quem, $"ens_ensinar:{s.Path}");
				UsarHabilidade(aQuem, "ens_licao_sim");
				return aQuem.Livro.FoiEnsinada(s.Path);
			}

			bool licoes = Ensina(mestre, a, comNiveis) && Ensina(mestre, a, semNiveisA) && Ensina(mestre, b, comNiveis)
						  && Ensina(mestre, c, comNiveis) && Ensina(mestre, c, soDoColega);
			DiarioDeAluno? diarioDeC = _diarios.GetValueOrDefault(c.Assinatura);
			Checa("**O MESTRE PASSA UMA SKILL (o verb de ensino): a licao ENTRA no diario do aluno** -- Gohan com duas, Kuririn com uma, Dende com duas",
				  licoes && diarioDeA!.Ensinamentos.Count == 3 && diarioDeB!.Ensinamentos.Count == 2 && diarioDeC is { Ensinamentos.Count: 2 }
				  && diarioDeA.Achar(TipoDeEnsinamento.Licao, comNiveis.Path) != null && diarioDeA.Achar(TipoDeEnsinamento.Licao, semNiveisA.Path) != null
				  && diarioDeC.Achar(TipoDeEnsinamento.Licao, soDoColega.Path) != null,
				  $"licoes {licoes}; A {diarioDeA?.Ensinamentos.Count}, B {diarioDeB?.Ensinamentos.Count}, C {diarioDeC?.Ensinamentos.Count}");

			bool doEstranhoPegou = Ensina(estranho, a, doEstranho);
			Checa("A LICAO DE UM ESTRANHO NAO E ENSINAMENTO DE MESTRE: o Gohan aprende a skill, e o diario dele continua com tres",
				  doEstranhoPegou && a.Livro.Sabe(doEstranho.Path) && diarioDeA!.Ensinamentos.Count == 3
				  && diarioDeA.Achar(TipoDeEnsinamento.Licao, doEstranho.Path) == null);
			bool praFora = Ensina(mestre, estranho, semNiveisA);
			Checa("...e o que o mestre ensina a quem NAO e aluno dele nao cria diario nenhum (o estranho aprende, e so)",
				  praFora && !_diarios.ContainsKey(estranho.Assinatura) && _diarios.Count == 4, $"{_diarios.Count} diarios");
			Ensina(mestre, a, comNiveis);
			Checa("...e repetir uma licao que o aluno ja tem nao cria uma segunda linha", diarioDeA!.Ensinamentos.Count == 3);

			// =====================================================================
			// 4) O MESTRE RECEBE OS ALUNOS
			// =====================================================================
			GD.Print("[alunos] -- 4) o mestre recebe os alunos, com o progresso lido do corpo de cada um --");
			TodosLonge();
			a.Forma.Maestria.Por("ssj1", 37.26);
			a.Niveis.Por(comNiveis.Path, 1, RegrasDeNivel.Get(comNiveis.Path)!.BarreiraEm(1) * 0.5);
			b.Niveis.Por(comNiveis.Path, 0);
			c.Niveis.Por(comNiveis.Path, Math.Max(1, comNiveis.MaxNivel));

			List<(int Para, byte[] Pacote)> doMestre = Pedir(mestre);
			Checa("**O MESTRE PEDE (o verb `alunos`) E RECEBE: um pacote, so pra ele**", doMestre.Count == 1 && doMestre[0].Para == mestre.Id,
				  $"{doMestre.Count} pacote(s)");
			if (doMestre.Count != 1) return;
			(Protocol.S2C opcode, PainelDoDiscipulado painelDoMestre, int sobrou) = Abrir(doMestre[0].Pacote);
			Checa("...com o opcode do painel, e o leitor do cliente o consome INTEIRO (nao ha campo alem dos da lista)",
				  opcode == Protocol.S2C.MestreEAlunos && sobrou == 0, $"{opcode}, sobraram {sobrou} byte(s) de {doMestre[0].Pacote.Length}");
			Checa("...sem mestre (ele nao e aluno de ninguem) e com os TRES alunos dele -- e nao o do outro mestre",
				  painelDoMestre.Mestre == null && painelDoMestre.Alunos.Count == 3
				  && Aluno(painelDoMestre, a) != null && Aluno(painelDoMestre, b) != null && Aluno(painelDoMestre, c) != null
				  && Aluno(painelDoMestre, d) == null,
				  string.Join(", ", painelDoMestre.Alunos.Select(x => x.Quem.Nome)));

			AlunoNoPainel? cartaoDeA = Aluno(painelDoMestre, a), cartaoDeB = Aluno(painelDoMestre, b), cartaoDeC = Aluno(painelDoMestre, c);
			EnsinamentoNoPainel? formaDeA = cartaoDeA != null ? Do(cartaoDeA.Ensinamentos, TipoDeEnsinamento.Forma, "ssj1") : null;
			EnsinamentoNoPainel? licaoDeA = cartaoDeA != null ? Do(cartaoDeA.Ensinamentos, TipoDeEnsinamento.Licao, comNiveis.Path) : null;
			EnsinamentoNoPainel? semNivelDeA = cartaoDeA != null ? Do(cartaoDeA.Ensinamentos, TipoDeEnsinamento.Licao, semNiveisA.Path) : null;
			Checa("**O CARTAO DO GOHAN: quem ele e** -- o nome, a raca, no mundo, aluno \"desde\" (ha no minimo 1 s) -- e as tres coisas que o mestre lhe ensinou",
				  cartaoDeA is { Quem: { Nome: "Aluno Gohan", Raca: "Saiyan", Online: true, VistoHaSegundos: 0, DesdeHaSegundos: >= 1 }, Ensinamentos.Count: 3 },
				  cartaoDeA == null ? "sem cartao" : $"{cartaoDeA.Quem}; {cartaoDeA.Ensinamentos.Count} ensinamento(s)");
			Checa("**...a FORMA: despertou COM o mestre, 1 tentativa, maestria 37% (372 milesimos)**",
				  formaDeA is { Assistida: true, Tentativas: 1, HaSegundos: >= 1, Progresso: { Etapa: EtapaDoEnsinamento.EmCurso, Nivel: 37, Teto: 100, Milesimos: 372 } },
				  formaDeA?.ToString() ?? "sem a forma");
			int tetoDaLicao = Math.Max(1, comNiveis.MaxNivel);
			Checa($"**...a LICAO que sobe de nivel ({comNiveis.Nome}): nivel 1 de {tetoDaLicao}, com meia barra de exp andada**",
				  licaoDeA is { Progresso: { Etapa: EtapaDoEnsinamento.EmCurso, Nivel: 1 } } && licaoDeA.Value.Progresso.Teto == tetoDaLicao
				  && licaoDeA.Value.Progresso.Milesimos == (ushort)Math.Floor(1.5 / tetoDaLicao * 1000),
				  licaoDeA?.ToString() ?? "sem a licao");
			Checa($"...e a LICAO que nao tem niveis ({semNiveisA.Nome}): aprendida, completa, teto 0",
				  semNivelDeA is { Progresso: { Etapa: EtapaDoEnsinamento.Dominado, Teto: 0, Milesimos: 1000 } }, semNivelDeA?.ToString() ?? "sem a licao");
			Checa("O CARTAO DO KURIRIN: aluno SEM data (o vinculo e de antes do diario), a forma com 2 tentativas e AINDA NAO desperta, a licao no nivel 0",
				  cartaoDeB is { Quem.DesdeHaSegundos: 0 } && Do(cartaoDeB.Ensinamentos, TipoDeEnsinamento.Forma, "ssj1") is { Tentativas: 2, Assistida: false, Progresso.Etapa: EtapaDoEnsinamento.AindaNao }
				  && Do(cartaoDeB.Ensinamentos, TipoDeEnsinamento.Licao, comNiveis.Path) is { Progresso: { Etapa: EtapaDoEnsinamento.EmCurso, Nivel: 0, Milesimos: 0 } },
				  cartaoDeB == null ? "sem cartao" : string.Join(" | ", cartaoDeB.Ensinamentos));
			Checa("O CARTAO DO DENDE: a licao em comum no TETO (dominou) e a licao que so ele recebeu",
				  cartaoDeC is { Quem.Raca: "Namekian", Ensinamentos.Count: 2 }
				  && Do(cartaoDeC.Ensinamentos, TipoDeEnsinamento.Licao, comNiveis.Path) is { Progresso.Etapa: EtapaDoEnsinamento.Dominado }
				  && Do(cartaoDeC.Ensinamentos, TipoDeEnsinamento.Licao, soDoColega.Path) != null);

			// A ORDEM. Gohan: forma em curso (0,686) + licao em curso + licao completa (1); Dende: duas completas (2,0);
			// Kuririn: forma nao despertada (0) + licao no nivel 0 (0,5).
			double PontosDe(AlunoNoPainel? x) => x == null ? -1 : ProgressoDeAluno.Geral(x.Ensinamentos.Select(e => e.Progresso)).Soma;
			bool EmOrdemDeAdiantamento(PainelDoDiscipulado? p) =>
				p != null && p.Alunos.Count == 3
				&& p.Alunos.Zip(p.Alunos.Skip(1), (x, y) => PontosDe(x) >= PontosDe(y)).All(v => v)
				&& p.Alunos[^1].Quem.Assinatura == b.Assinatura;
			List<RankingDoEnsinamento> conjunto = ProgressoDeAluno.Conjunto(painelDoMestre.Alunos);
			RankingDoEnsinamento? rankingDaLicao = conjunto.FirstOrDefault(r => r.Tipo == TipoDeEnsinamento.Licao && r.Id == comNiveis.Path);
			bool RankingCerto(PainelDoDiscipulado? p)
			{
				if (p == null) return false;
				RankingDoEnsinamento? r = ProgressoDeAluno.Conjunto(p.Alunos).FirstOrDefault(x => x.Tipo == TipoDeEnsinamento.Licao && x.Id == comNiveis.Path);
				return r is { Lugares.Count: 3 }
					   && p.Alunos[r.Lugares[0].Aluno].Quem.Assinatura == c.Assinatura && r.Lugares[0].Posicao == 1
					   && p.Alunos[r.Lugares[1].Aluno].Quem.Assinatura == a.Assinatura && r.Lugares[1].Posicao == 2
					   && p.Alunos[r.Lugares[2].Aluno].Quem.Assinatura == b.Assinatura && r.Lugares[2].Posicao == 3;
			}
			Mutacao(Mutar,
				"**OS CARTOES VEM DO MAIS ADIANTADO PRO MENOS** (a soma do progresso no que foi ensinado), com o Kuririn por ultimo -- "
				+ $"e no ensinamento em comum ({comNiveis.Nome}) o ranking e Dende (teto), Gohan (nivel 1), Kuririn (nivel 0)",
				"as listas saem na ordem em que foram anotadas (`ProgressoDeAluno.SemOrdemDeTeste`)",
				() => PainelPeloFio(mestre) is { } p && EmOrdemDeAdiantamento(p) && RankingCerto(p),
				() => ProgressoDeAluno.SemOrdemDeTeste = true,
				() => ProgressoDeAluno.SemOrdemDeTeste = false);
			Checa("...e a visao de conjunto tem os quatro ensinamentos do mestre (a forma, a licao em comum e as duas licoes de um so)",
				  conjunto.Count == 4 && rankingDaLicao is { Lugares.Count: 3 }
				  && conjunto.First(r => r.Tipo == TipoDeEnsinamento.Forma).Lugares is { Count: 2 } daForma && daForma[0].Posicao == 1 && daForma[1].Posicao == 2,
				  string.Join(" | ", conjunto.Select(r => $"{r.Tipo}:{r.Id.Split('/')[^1]} x{r.Lugares.Count}")));

			// O ALUNO ESQUECE A LICAO (o verb de producao): a linha continua no diario, e diz que ele a esqueceu.
			UsarHabilidade(a, $"ens_esquecer:{semNiveisA.Path}");
			Checa("O GOHAN ESQUECE uma licao (o verb `ens_esquecer`): ela continua no cartao dele, como ESQUECIDA (0 ponto)",
				  !a.Livro.Sabe(semNiveisA.Path) && Aluno(PainelPeloFio(mestre), a) is { } depoisDeEsquecer
				  && Do(depoisDeEsquecer.Ensinamentos, TipoDeEnsinamento.Licao, semNiveisA.Path) is { Progresso: { Etapa: EtapaDoEnsinamento.AindaNao, Pontos: 0 } });
			a.Livro.DarComoEnsinada(semNiveisA.Path);

			// =====================================================================
			// 5) O ALUNO RECEBE O MESTRE E OS COLEGAS
			// =====================================================================
			GD.Print("[alunos] -- 5) o aluno recebe o mestre e os colegas: a posicao em cada ensinamento, e do colega so o que e em comum --");
			List<(int Para, byte[] Pacote)> doAluno = Pedir(a);
			Checa("**O ALUNO PEDE E RECEBE: um pacote, so pra ele, lido inteiro**",
				  doAluno.Count == 1 && doAluno[0].Para == a.Id && Abrir(doAluno[0].Pacote) is (Protocol.S2C.MestreEAlunos, _, 0),
				  $"{doAluno.Count} pacote(s)");
			if (doAluno.Count != 1) return;
			byte[] bytesDoAluno = doAluno[0].Pacote;
			PainelDoDiscipulado painelDoAluno = Abrir(bytesDoAluno).Painel;
			MestreNoPainel? mestreVisto = painelDoAluno.Mestre;
			Checa("...o MESTRE dele (nome, raca, no mundo, mestre \"desde\"), nenhum aluno (ele nao guia ninguem), e os dois COLEGAS pelo nome, em ordem",
				  mestreVisto is { Quem: { Nome: "Mestre Kame", Raca: "Saiyan", Online: true, DesdeHaSegundos: >= 1 } }
				  && mestreVisto.Quem.Assinatura == mestre.Assinatura && painelDoAluno.Alunos.Count == 0
				  && mestreVisto.Colegas.SequenceEqual(["Aluno Dende", "Aluno Kuririn"]),
				  mestreVisto == null ? "sem mestre" : $"{mestreVisto.Quem}; colegas [{string.Join(",", mestreVisto.Colegas)}]");
			if (mestreVisto == null) return;

			EnsinamentoDoMestre? aForma = mestreVisto.Ensinamentos.FirstOrDefault(e => e.Meu.Tipo == TipoDeEnsinamento.Forma && e.Meu.Id == "ssj1");
			EnsinamentoDoMestre? aLicao = mestreVisto.Ensinamentos.FirstOrDefault(e => e.Meu.Tipo == TipoDeEnsinamento.Licao && e.Meu.Id == comNiveis.Path);
			EnsinamentoDoMestre? aSemNivel = mestreVisto.Ensinamentos.FirstOrDefault(e => e.Meu.Tipo == TipoDeEnsinamento.Licao && e.Meu.Id == semNiveisA.Path);
			Checa("**O QUE O MESTRE LHE ENSINOU, com o progresso DELE em cada coisa**: as tres, na ordem em que vieram, e o mesmo progresso que o mestre le",
				  mestreVisto.Ensinamentos.Count == 3 && mestreVisto.Ensinamentos[0].Meu.Id == "ssj1"
				  && aForma?.Meu.Progresso == formaDeA?.Progresso && aLicao?.Meu.Progresso == licaoDeA?.Progresso && aSemNivel != null,
				  string.Join(" | ", mestreVisto.Ensinamentos.Select(e => e.Meu)));

			// Na forma: o Gohan (despertou) e o Kuririn (nao). Na licao em comum: Dende (teto), Gohan, Kuririn.
			(int Posicao, int De, bool Empate) naForma = aForma != null ? ProgressoDeAluno.Posicao(aForma.Meu.Progresso, aForma.Colegas) : default;
			(int Posicao, int De, bool Empate) naLicao = aLicao != null ? ProgressoDeAluno.Posicao(aLicao.Meu.Progresso, aLicao.Colegas) : default;
			Checa("**A POSICAO DELE EM CADA ENSINAMENTO**: 1o de 2 na forma (o Kuririn nao despertou) e 2o de 3 na licao em comum (o Dende esta no teto)",
				  naForma == (1, 2, false) && naLicao == (2, 3, false), $"forma {naForma}, licao {naLicao}");
			Checa("...com os colegas do ensinamento do mais adiantado pro menos (Dende, depois Kuririn), cada um com o progresso DELE",
				  aLicao is { Colegas.Count: 2 } && mestreVisto.Colegas[aLicao.Colegas[0].Colega] == "Aluno Dende"
				  && aLicao.Colegas[0].Progresso.Etapa == EtapaDoEnsinamento.Dominado
				  && mestreVisto.Colegas[aLicao.Colegas[1].Colega] == "Aluno Kuririn" && aLicao.Colegas[1].Progresso is { Etapa: EtapaDoEnsinamento.EmCurso, Nivel: 0 }
				  && aForma is { Colegas.Count: 1 } && mestreVisto.Colegas[aForma.Colegas[0].Colega] == "Aluno Kuririn",
				  aLicao == null ? "sem a licao" : string.Join(" | ", aLicao.Colegas));
			Checa("...e na licao que SO ele recebeu nao ha colega nenhum (nao ha com quem comparar)", aSemNivel is { Colegas.Count: 0 });

			// O CORTE: o que o mestre ensinou SO ao Dende nao chega ao Gohan -- nem como linha, nem em byte nenhum.
			bool SoOQueEEmComum()
			{
				List<(int Para, byte[] Pacote)> s = Pedir(a);
				if (s.Count != 1) return false;
				PainelDoDiscipulado p = Abrir(s[0].Pacote).Painel;
				return p.Mestre is { } m && m.Ensinamentos.All(e => diarioDeA.Achar(e.Meu.Tipo, e.Meu.Id) != null)
					   && !Contem(s[0].Pacote, Texto(soDoColega.Path));
			}
			Mutacao(Mutar,
				$"**DO COLEGA SO CHEGA O QUE E EM COMUM**: a licao que o mestre deu so ao Dende ({soDoColega.Nome}) nao esta no painel do Gohan, "
				+ "nem como linha nem em byte nenhum do pacote",
				"o aluno recebe todos os ensinamentos dos colegas (`ProgressoDeAluno.ColegaVeTudoDeTeste`)",
				SoOQueEEmComum,
				() => ProgressoDeAluno.ColegaVeTudoDeTeste = true,
				() => ProgressoDeAluno.ColegaVeTudoDeTeste = false);
			Checa("...e do colega so viaja o NOME: nem a assinatura dele, nem a raca (o Dende e o unico Namekuseijin do elenco) estao no pacote",
				  Contem(bytesDoAluno, Texto("Aluno Dende")) && !Contem(bytesDoAluno, Texto(c.Assinatura))
				  && !Contem(bytesDoAluno, Texto(b.Assinatura)) && !Contem(bytesDoAluno, Texto("Namekian")));

			// QUEM E MESTRE DE UNS E ALUNO DE OUTRO ve as duas partes.
			bool duplo = Vincula(outroMestre, mestre);
			PainelDoDiscipulado? dasDuasPartes = PainelPeloFio(mestre);
			Checa("QUEM E MESTRE DE UNS E ALUNO DE OUTRO recebe as DUAS partes: o mestre dele (com o colega Chaos) e os tres alunos dele",
				  duplo && dasDuasPartes is { Mestre: { Quem.Nome: "Mestre Tsuru" } m2, Alunos.Count: 3 } && m2.Colegas.SequenceEqual(["Aluno Chaos"]),
				  dasDuasPartes == null ? "sem pacote" : $"mestre {dasDuasPartes.Mestre?.Quem.Nome}, {dasDuasPartes.Alunos.Count} aluno(s)");
			Checa("...e os alunos DELE nao viram colegas de ninguem la em cima: o Chaos ve o mestre e UM colega (o Mestre Kame), e nada do Gohan",
				  PainelPeloFio(d) is { Mestre: { } m3, Alunos.Count: 0 } && m3.Colegas.SequenceEqual(["Mestre Kame"])
				  && Pedir(d) is { Count: 1 } deD && !Contem(deD[0].Pacote, Texto("Aluno Gohan")) && !Contem(deD[0].Pacote, Texto(a.Assinatura)));
			Desvincular(mestre.Assinatura, "preparo de bancada");

			// =====================================================================
			// 6) QUEM ESTA DE FORA NAO RECEBE NADA
			// =====================================================================
			GD.Print("[alunos] -- 6) quem esta de fora nao recebe nada --");
			bool OEstranhoFicaSemNada() => Pedir(estranho).Count == 0 && Pedir(estranho, "alunos", "seguir").Count == 0;
			Mutacao(Mutar,
				"**QUEM NAO TEM MESTRE NEM ALUNO PEDE E NAO RECEBE PACOTE NENHUM** (um cliente adulterado mandando o verb na mao)",
				"o painel sai pra qualquer um que peca (`PainelSemVinculoDeTeste`)",
				OEstranhoFicaSemNada,
				() => PainelSemVinculoDeTeste = true,
				() => PainelSemVinculoDeTeste = false);
			List<string> recusa = Ouvir(() => Verbo(estranho, "alunos", ""));
			List<string> calado = Ouvir(() => Verbo(estranho, "alunos", "seguir"));
			Checa("...o pedido de abrir ouve a recusa em palavras; o de \"seguir\" (a tela que ainda nao soube que o vinculo acabou) e recusado calado",
				  Disse(recusa, "não tem mestre nem alunos") && calado.Count == 0, $"{Ultimos(recusa)} || {Ultimos(calado)}");
			Checa("...e `alunos` e um verb DESTE canal (o funil nao segue pra \"comando desconhecido\"), inclusive recusado",
				  ComandoDeAlunos(estranho, "alunos", "") && ComandoDeAlunos(estranho, "alunos_dispensar", "") && !ComandoDeAlunos(estranho, "alunoz", ""));

			// (direto no canal da tela: um corpo sem conta nao passa inteiro pelos outros canais do `Verbo`)
			var semDono = new ServerPlayer { Id = IdBaseDosAlunosDeTeste + 90, Name = "Sem dono", Race = "Human", Zone = zona, Ficha = new Fighter { Race = "Human", BP = 10 }, Livro = new SkillBook() };
			var praOSemDono = new List<(int Para, byte[] Pacote)>();
			EscutaDoDiscipulado = praOSemDono;
			try { ComandoDeAlunos(semDono, "alunos", ""); }
			finally { EscutaDoDiscipulado = null; }
			Checa("...um corpo SEM identidade (NPC, clone) tambem nao recebe nada", praOSemDono.Count == 0 && PainelDe(semDono) is { Mestre: null, Alunos.Count: 0 });

			PainelDoDiscipulado? doOutro = PainelPeloFio(outroMestre);
			Checa("**O OUTRO MESTRE so recebe o aluno DELE**: o Chaos, e nenhum dos tres do Mestre Kame",
				  doOutro is { Mestre: null, Alunos.Count: 1 } && doOutro.Alunos[0].Quem.Nome == "Aluno Chaos",
				  doOutro == null ? "sem pacote" : string.Join(",", doOutro.Alunos.Select(x => x.Quem.Nome)));

			// =====================================================================
			// 7) A FOTOGRAFIA DO OFFLINE, E O DISCO
			// =====================================================================
			GD.Print("[alunos] -- 7) a fotografia de quem saiu do mundo, o disco e o arquivo antigo --");
			bool OOfflineGuardaAFoto()
			{
				// (cada volta comeca com o Gohan no mundo, na maestria de 37%)
				if (!_players.ContainsKey(a.Id)) { _players[a.Id] = a; ZoneList(zona.Hash).Add(a); }
				a.Forma.Maestria.Por("ssj1", 37.26);
				// A foto do diario volta ao que era NO DIA DA ANOTACAO (a forma recem-despertada, 0%): so o tique a refaz.
				diarioDeA.Achar(TipoDeEnsinamento.Forma, "ssj1")!.Milesimos = 0;
				diarioDeA.Achar(TipoDeEnsinamento.Forma, "ssj1")!.Nivel = 0;
				TickDosAlunos();                       // o tique de 1 Hz fotografa quem esta no mundo
				_players.Remove(a.Id);                 // ...e ele SAI do mundo
				ZoneList(zona.Hash).Remove(a);
				a.Forma.Maestria.Por("ssj1", 80);      // (o que o corpo dele vira fora do mundo nao e de ninguem)
				AdiantoDoRelogioDeTeste += 90_000;     // um minuto e meio depois
				TickDosAlunos();
				AlunoNoPainel? fora = Aluno(PainelPeloFio(mestre), a);
				return fora is { Quem: { Online: false, Nome: "Aluno Gohan", Raca: "Saiyan", VistoHaSegundos: >= 89 and <= 95 } }
					   && Do(fora.Ensinamentos, TipoDeEnsinamento.Forma, "ssj1") is { Progresso: { Etapa: EtapaDoEnsinamento.EmCurso, Nivel: 37, Milesimos: 372 } };
			}
			Mutacao(Mutar,
				"**O ALUNO QUE SAIU DO MUNDO CONTINUA NO PAINEL, com a ULTIMA FOTOGRAFIA**: fora do mundo, o nome e a raca lembrados, "
				+ "\"visto por ultimo\" ha um minuto e meio, e a maestria de quando ele saiu (37%) -- e nao a que o corpo dele tem agora",
				"a fotografia do progresso nao e guardada (`FotoDoAlunoNaoSeGuardaDeTeste`)",
				OOfflineGuardaAFoto,
				() => FotoDoAlunoNaoSeGuardaDeTeste = true,
				() => FotoDoAlunoNaoSeGuardaDeTeste = false);
			Checa("...e o COLEGA tambem le a fotografia dele: pro Kuririn, o Gohan (fora do mundo) continua 1o de 2 na forma",
				  PainelPeloFio(b) is { Mestre: { } m4 }
				  && m4.Ensinamentos.FirstOrDefault(e => e.Meu.Tipo == TipoDeEnsinamento.Forma) is { Colegas.Count: 1 } formaDoB
				  && formaDoB.Colegas[0].Progresso is { Etapa: EtapaDoEnsinamento.EmCurso, Nivel: 37 }
				  && ProgressoDeAluno.Posicao(formaDoB.Meu.Progresso, formaDoB.Colegas) == (2, 2, false));

			// O DISCO: o que esta na memoria vai e volta igual.
			SalvarDiarios();
			string conteudoAntes = string.Join("\n", _diarios.Values.OrderBy(x => x.Aluno, StringComparer.Ordinal)
				.Select(x => $"{x.Aluno}>{x.Mestre}@{x.DesdeMs}:" + string.Join(";", x.Ensinamentos.Select(e => $"{e.Tipo},{e.Id},{e.EmMs},{e.Tentativas},{e.Assistida},{e.Etapa},{e.Nivel},{e.Teto},{e.Milesimos}"))));
			_diarios.Clear();
			_pessoasDoDiario.Clear();
			CarregarDiarios();
			string conteudoDepois = string.Join("\n", _diarios.Values.OrderBy(x => x.Aluno, StringComparer.Ordinal)
				.Select(x => $"{x.Aluno}>{x.Mestre}@{x.DesdeMs}:" + string.Join(";", x.Ensinamentos.Select(e => $"{e.Tipo},{e.Id},{e.EmMs},{e.Tentativas},{e.Assistida},{e.Etapa},{e.Nivel},{e.Teto},{e.Milesimos}"))));
			Checa("**O DIARIO ATRAVESSA O REINICIO**: gravado, esquecido e lido de novo, ele volta IGUAL (os quatro vinculos, as datas, os ensinamentos e as fotografias)",
				  conteudoAntes == conteudoDepois && _diarios.Count == 4 && conteudoAntes.Contains("ssj1")
				  && _pessoasDoDiario.GetValueOrDefault(a.Assinatura) is { Nome: "Aluno Gohan", VistoEm: > 0 },
				  $"{_diarios.Count} diarios; iguais {conteudoAntes == conteudoDepois}");
			diarioDeA = _diarios[a.Assinatura];
			diarioDeB = _diarios[b.Assinatura];
			diarioDeC = _diarios[c.Assinatura];
			Checa("...e, relido do disco, o painel do mestre continua mostrando o Gohan (fora do mundo) com a fotografia dele",
				  Aluno(PainelPeloFio(mestre), a) is { Quem.Online: false } relido
				  && Do(relido.Ensinamentos, TipoDeEnsinamento.Forma, "ssj1") is { Assistida: true, Progresso.Nivel: 37 });

			// O ARQUIVO DE UMA VERSAO MAIS VELHA (sem os campos da fotografia, sem a lista de pessoas) e o arquivo ESTRAGADO.
			string doDisco = System.IO.File.ReadAllText(ArquivoDosDiarios);
			try
			{
				System.IO.File.WriteAllText(ArquivoDosDiarios,
					$"{{\"Diarios\":[{{\"Aluno\":\"{a.Assinatura}\",\"Mestre\":\"{mestre.Assinatura}\",\"Ensinamentos\":[{{\"Tipo\":1,\"Id\":\"ssj1\"}},null,{{\"Tipo\":9,\"Id\":\"x\"}}]}},"
					+ $"{{\"Aluno\":\"{b.Assinatura}\",\"Mestre\":\"{mestre.Assinatura}\",\"Ensinamentos\":null}},{{\"Aluno\":\"\"}},null],\"CampoDoFuturo\":7}}");
				CarregarDiarios();
				Checa("O ARQUIVO DE OUTRA VERSAO CARREGA: sem data, sem fotografia e sem a lista de pessoas ele da um diario valido; a entrada nula, "
					  + "a sem aluno e o ensinamento de tipo desconhecido sao descartados, e o campo que nao existe aqui e ignorado",
					  _diarios.Count == 2 && _diarios[a.Assinatura] is { DesdeMs: 0, Ensinamentos.Count: 1 } velho
					  && velho.Ensinamentos[0] is { Tentativas: 0, Assistida: false, Etapa: 0 }
					  && _diarios[b.Assinatura].Ensinamentos.Count == 0 && _pessoasDoDiario.Count == 0,
					  $"{_diarios.Count} diarios");
				Checa("...e o painel sai mesmo assim: o Gohan (fora do mundo, sem lembranca) aparece pelo nome do `mestres.txt`, sem raca e sem \"visto\"",
					  Aluno(PainelPeloFio(mestre), a) is { Quem: { Nome: "Aluno Gohan", Raca: "", Online: false, VistoHaSegundos: 0, DesdeHaSegundos: 0 } });

				System.IO.File.WriteAllText(ArquivoDosDiarios, "{ isto nao e json ]");
				CarregarDiarios();
				Checa("O ARQUIVO ESTRAGADO NAO DERRUBA NADA: o diario recomeca vazio, e o painel do mestre sai com os tres alunos sem ensinamento",
					  _diarios.Count == 0 && PainelPeloFio(mestre) is { Alunos.Count: 3 } vazio && vazio.Alunos.All(x => x.Ensinamentos.Count == 0));
			}
			finally
			{
				System.IO.File.WriteAllText(ArquivoDosDiarios, doDisco);
				CarregarDiarios();
				diarioDeA = _diarios[a.Assinatura];
				diarioDeB = _diarios[b.Assinatura];
				diarioDeC = _diarios[c.Assinatura];
			}

			// O GOHAN VOLTA AO MUNDO: a foto e refeita com o corpo de agora.
			_players[a.Id] = a;
			ZoneList(zona.Hash).Add(a);
			Checa("...e quando o Gohan VOLTA ao mundo o cartao dele e o corpo de agora: no mundo, maestria 80%",
				  Aluno(PainelPeloFio(mestre), a) is { Quem: { Online: true, VistoHaSegundos: 0 } } voltou
				  && Do(voltou.Ensinamentos, TipoDeEnsinamento.Forma, "ssj1") is { Progresso: { Nivel: 80, Milesimos: 800 } });
			a.Forma.Maestria.Por("ssj1", 37.26);

			// =====================================================================
			// 8) NADA DE PODER NOS BYTES
			// =====================================================================
			GD.Print("[alunos] -- 8) nada de poder (nem de classe, conta ou lugar) nos bytes do pacote, com scouter ou sem --");
			a.Ficha.Class = a.Class = "Legendary";   // (o canario da classe: so nesta secao, que nao transforma ninguem)
			foreach (ServerPlayer p in corpos) p.Ficha.PowerLevel(agoraMs: NowMs());

			string campos = string.Join(" / ", new[] { typeof(PessoaNoPainel), typeof(EnsinamentoNoPainel), typeof(ProgressoNoEnsinamento), typeof(ColegaNoEnsinamento) }
				.Select(t => string.Join(",", t.GetProperties().Select(pr => pr.Name).Order(StringComparer.Ordinal))));
			Checa("**O QUE VIAJA E UMA LISTA FECHADA**: da pessoa, a assinatura, o nome, a raca, a presenca e duas datas; do ensinamento, o que e, quando, "
				  + "as tentativas e o progresso (etapa, nivel, teto, milesimos -- `Pontos` e conta, nao campo); do colega, o indice e o progresso",
				  campos == "Assinatura,DesdeHaSegundos,Nome,Online,Raca,VistoHaSegundos / Assistida,HaSegundos,Id,Progresso,Tentativas,Tipo / "
							+ "Etapa,Milesimos,Nivel,Pontos,Teto / Colega,Progresso", campos);

			List<(string OQue, byte[] Bytes)> Canarios()
			{
				var l = new List<(string, byte[])>();
				foreach (ServerPlayer p in new[] { mestre, a, b, c })
				{
					l.Add(($"o poder base de {p.Name} (escrito)", Texto(((long)p.Ficha.BP).ToString())));
					l.Add(($"o poder base de {p.Name} (o numero)", BitConverter.GetBytes(p.Ficha.BP)));
					// (poder expresso ZERO seriam oito bytes nulos, e isso qualquer pacote tem: o controle cobra que nao seja)
					if (p.Ficha.expressedBP > 0) l.Add(($"o poder expresso de {p.Name} (o numero)", BitConverter.GetBytes(p.Ficha.expressedBP)));
					l.Add(($"o poder base de {p.Name} (float)", BitConverter.GetBytes((float)p.Ficha.BP)));
					l.Add(($"a conta de {p.Name}", Texto(p.Conta)));
				}
				l.Add(("a classe do Gohan", Texto(a.Ficha.Class)));
				l.Add(("o planeta onde eles estao", Texto(zona.Name)));
				return l;
			}
			string Vazou(ServerPlayer quem)
			{
				List<(int Para, byte[] Pacote)> s = Pedir(quem);
				if (s.Count != 1) return "sem pacote";
				var achados = Canarios().Where(k => Contem(s[0].Pacote, k.Bytes)).Select(k => k.OQue).ToList();
				if (Abrir(s[0].Pacote).Sobrou != 0) achados.Add("bytes alem do ultimo campo");
				return string.Join(", ", achados);
			}

			byte[] bytesDoMestre = Pedir(mestre)[0].Pacote;
			Checa("CONTROLE: os canarios existem (ha o que procurar, e os poderes sao todos diferentes) e a busca acha o que ESTA no pacote (o nome do aluno)",
				  Canarios().All(k => k.Bytes.Length > 0) && corpos.Select(p => p.Ficha.BP).Distinct().Count() == corpos.Count
				  && new[] { mestre, a, b, c }.All(p => p.Ficha.expressedBP > 0 && p.Ficha.BP > 100_000)
				  && Contem(bytesDoMestre, Texto("Aluno Gohan")) && Contem(bytesDoMestre, Texto(a.Assinatura)),
				  string.Join(", ", new[] { mestre, a, b, c }.Select(p => $"{p.Name}: {p.Ficha.BP:0} / {p.Ficha.expressedBP:0}")));
			Mutacao(Mutar,
				"**NENHUM PODER NO PAINEL DO MESTRE**: nem o poder base nem o expresso de nenhum aluno (nem o dele), em texto ou em numero; "
				+ "nem classe, conta ou lugar -- e o pacote acaba no ultimo campo da lista",
				"o poder de luta viaja no campo da raca e no fim do pacote (`PoderNoPainelDeTeste`)",
				() => Vazou(mestre).Length == 0,
				() => PoderNoPainelDeTeste = true,
				() => PoderNoPainelDeTeste = false);
			Checa("**...NEM NO DO ALUNO**: o poder do mestre e o dos colegas nao estao em byte nenhum do que o Gohan e o Kuririn recebem",
				  Vazou(a).Length == 0 && Vazou(b).Length == 0, $"{Vazou(a)} || {Vazou(b)}");

			// O SCOUTER NAO MUDA NADA: a tela nao e um medidor.
			Protocol.Poder poderesDoMestre = mestre.Poderes, poderesDoAluno = a.Poderes;
			string semScouterDoMestre = ProgressoDeAluno.Conteudo(PainelDe(mestre)), semScouterDoAluno = ProgressoDeAluno.Conteudo(PainelDe(a));
			try
			{
				mestre.Poderes |= Protocol.Poder.Scouter;
				a.Poderes |= Protocol.Poder.Scouter | Protocol.Poder.Sense;
				PainelDoDiscipulado? comScouterDoMestre = PainelPeloFio(mestre), comScouterDoAluno = PainelPeloFio(a);
				Checa("**COM O SCOUTER LIGADO (no mestre e no aluno) O PAINEL E O MESMO DE ANTES**: nenhum numero aparece, nenhum campo novo -- esta tela nao e um medidor",
					  TemScouter(mestre) && TemScouter(a) && !TemScouter(b) && comScouterDoMestre != null && comScouterDoAluno != null
					  && ProgressoDeAluno.Conteudo(comScouterDoMestre) == semScouterDoMestre && ProgressoDeAluno.Conteudo(comScouterDoAluno) == semScouterDoAluno
					  && Vazou(mestre).Length == 0 && Vazou(a).Length == 0, $"{Vazou(mestre)} || {Vazou(a)}");
			}
			finally { mestre.Poderes = poderesDoMestre; a.Poderes = poderesDoAluno; }
			a.Ficha.Class = a.Class = "Normal";

			// =====================================================================
			// 9) COM A TELA ABERTA, SO SAI QUANDO MUDA
			// =====================================================================
			GD.Print("[alunos] -- 9) com a tela aberta: o pedido de \"seguir\" so e respondido quando o conteudo mudou --");
			Pedir(mestre);
			Checa("ABERTA E PARADA: o `alunos seguir` nao rende pacote nenhum (o mestre ja tem este conteudo)", Pedir(mestre, "alunos", "seguir").Count == 0);
			AdiantoDoRelogioDeTeste += 600_000;
			Checa("...nem dez minutos depois: os \"ha quanto tempo\" andam no relogio do CLIENTE, e nao por pacote",
				  Pedir(mestre, "alunos", "seguir").Count == 0);
			a.Forma.Maestria.Por("ssj1", 41);
			List<(int Para, byte[] Pacote)> mudou = Pedir(mestre, "alunos", "seguir");
			Checa("**O ALUNO PROGRIDE: o `seguir` seguinte traz UM pacote**, com a maestria nova (41%)",
				  mudou.Count == 1 && Aluno(Abrir(mudou[0].Pacote).Painel, a) is { } andou
				  && Do(andou.Ensinamentos, TipoDeEnsinamento.Forma, "ssj1") is { Progresso.Nivel: 41 }, $"{mudou.Count} pacote(s)");
			Checa("...e de novo nada ate a proxima mudanca; ja o pedido de ABRIR (`alunos`) responde sempre",
				  Pedir(mestre, "alunos", "seguir").Count == 0 && Pedir(mestre).Count == 1);
			a.Forma.Maestria.Por("ssj1", 37.26);

			// =====================================================================
			// 10) OS GESTOS DO CARTAO
			// =====================================================================
			GD.Print("[alunos] -- 10) os gestos do cartao de um aluno: ajudar AQUELE aluno, dispensar AQUELE aluno --");
			// OS DOIS SAIYAJINS PODEM SER PROVOCADOS: o Gohan volta a nao ter a forma, e o Kuririn sobe pros 60% da porta.
			a.Forma.Entrar(Catalogo.IdBase);
			a.Forma.Liberadas.Clear();
			a.Forma.PortasCortadas.Clear();
			b.Ficha.BP = a.Ficha.BP + 1;
			a.PedidoDoMestre = b.PedidoDoMestre = null;

			// O Kuririn COLADO na frente do mestre; o Gohan atras dele, fora do alcance.
			FrenteAFrente(mestre, b);
			a.Pos = new Vec2(-6 * T, 0);
			mestre.AlvoId = 0;
			bool OCartaoMiraOAlunoDele()
			{
				a.PedidoDoMestre = b.PedidoDoMestre = null;
				mestre.RecargaDeEnsino = 0;
				List<string> falas = Ouvir(() => Verbo(mestre, "alunos_ensinar", a.Assinatura));
				return a.PedidoDoMestre == null && b.PedidoDoMestre == null && mestre.RecargaDeEnsino == 0
					   && Disse(falas, "fique de frente para Aluno Gohan");
			}
			Mutacao(Mutar,
				"**\"AJUDAR A TRANSFORMAR\" NO CARTAO DO GOHAN, COM O KURIRIN NA FRENTE**: ninguem e provocado, o folego do mestre nao e gasto, "
				+ "e o servidor diz de quem ele tem que ficar de frente",
				"o gesto do cartao nao confere quem esta na frente (`CartaoSemMiraDeTeste`)",
				OCartaoMiraOAlunoDele,
				() => CartaoSemMiraDeTeste = true,
				() => CartaoSemMiraDeTeste = false);

			// Os DOIS na frente, o Kuririn mais perto: sem marca o verb pegaria o Kuririn; o cartao do Gohan pega o Gohan.
			a.PedidoDoMestre = b.PedidoDoMestre = null;
			a.Pos = new Vec2(0.95f * T, 0);
			b.Pos = new Vec2(0.6f * T, 0);
			mestre.RecargaDeEnsino = 0;
			mestre.AlvoId = 777;
			Verbo(mestre, "alunos_ensinar", a.Assinatura);
			Checa("**COM OS DOIS NA FRENTE (o Kuririn mais perto), o cartao do GOHAN provoca o GOHAN** -- pelo verb de sempre (a oferta, a recarga)",
				  a.PedidoDoMestre is { Despertar: true, FormaId: "ssj1" } && b.PedidoDoMestre == null && mestre.RecargaDeEnsino > NowMs(),
				  $"gohan {(a.PedidoDoMestre != null)}, kuririn {(b.PedidoDoMestre != null)}");
			Checa("...e a mira do proprio jogador (quem ele tinha marcado) volta a ser a de antes do gesto", mestre.AlvoId == 777);
			Checa("...e a tentativa entrou no diario do Gohan (2 agora: e o MESMO gesto da aba Learning)",
				  diarioDeA.Achar(TipoDeEnsinamento.Forma, "ssj1") is { Tentativas: 2 });
			mestre.AlvoId = 0;
			a.PedidoDoMestre = null;

			// A RECARGA E AS OUTRAS RECUSAS SAO AS DO VERB: o cartao nao e porta dos fundos.
			List<string> semFolego = Ouvir(() => Verbo(mestre, "alunos_ensinar", a.Assinatura));
			Checa("...o cartao NAO fura a recarga do mestre: de novo, logo em seguida, ele ouve que esta sem folego e ninguem e provocado",
				  Disse(semFolego, "sem folego") && a.PedidoDoMestre == null, Ultimos(semFolego));

			_players.Remove(a.Id);
			ZoneList(zona.Hash).Remove(a);
			mestre.RecargaDeEnsino = 0;
			List<string> ausente = Ouvir(() => Verbo(mestre, "alunos_ensinar", a.Assinatura));
			Checa("...com o aluno FORA do mundo, o gesto diz que ele nao esta (e nao provoca o Kuririn, que continua na frente)",
				  Disse(ausente, "não está no mundo") && b.PedidoDoMestre == null && mestre.RecargaDeEnsino == 0, Ultimos(ausente));

			List<string> deOutro = Ouvir(() => Verbo(mestre, "alunos_ensinar", d.Assinatura));
			List<string> doAlunoSobreOColega = Ouvir(() => Verbo(b, "alunos_dispensar", c.Assinatura));
			List<string> sobreOAlunoDeOutro = Ouvir(() => Verbo(mestre, "alunos_dispensar", d.Assinatura));
			List<string> doEstranhoSobreOAluno = Ouvir(() => Verbo(estranho, "alunos_dispensar", a.Assinatura));
			Checa("**OS GESTOS SO VALEM PRA UM ALUNO SEU**: o mestre nao guia nem dispensa o aluno de OUTRO mestre, o aluno nao dispensa o colega, "
				  + "e um estranho nao dispensa ninguem -- os quatro ouvem a recusa, e os vinculos continuam de pe",
				  new[] { deOutro, doAlunoSobreOColega, sobreOAlunoDeOutro, doEstranhoSobreOAluno }.All(f => Disse(f, "só vale pra um aluno seu"))
				  && MestreDe(d.Assinatura) == outroMestre.Assinatura && MestreDe(c.Assinatura) == mestre.Assinatura && MestreDe(a.Assinatura) == mestre.Assinatura,
				  $"{Ultimos(deOutro)} || {Ultimos(doAlunoSobreOColega)} || {Ultimos(sobreOAlunoDeOutro)} || {Ultimos(doEstranhoSobreOAluno)}");

			// DISPENSAR PELO CARTAO: de qualquer lugar, com o aluno fora do mundo (o `mstdrop` do painel do original).
			TodosLonge();
			List<(int Para, byte[] Pacote)> aoDispensar = Pedir(mestre, "alunos_dispensar", a.Assinatura);
			Checa("**\"DISPENSAR ALUNO\" NO CARTAO DO GOHAN, com ele FORA do mundo e o mestre longe de todos: o vinculo se desfaz** "
				  + "(o `mstdrop` do painel do original), e so o dele",
				  MestreDe(a.Assinatura).Length == 0 && MestreDe(b.Assinatura) == mestre.Assinatura && MestreDe(c.Assinatura) == mestre.Assinatura
				  && ContarAlunos(mestre.Assinatura) == 2);
			Checa("...e a tela de quem dispensou se acerta NA HORA: um painel novo, sem o Gohan",
				  aoDispensar.Count == 1 && aoDispensar[0].Para == mestre.Id && Abrir(aoDispensar[0].Pacote).Painel is { Alunos.Count: 2 } semOGohan
				  && Aluno(semOGohan, a) == null, $"{aoDispensar.Count} pacote(s)");
			_players[a.Id] = a;
			ZoneList(zona.Hash).Add(a);
			Checa("...o Gohan, sem vinculo, deixa de receber o painel; e o Kuririn deixa de te-lo como colega",
				  Pedir(a).Count == 0 && PainelPeloFio(b) is { Mestre: { } m5 } && m5.Colegas.SequenceEqual(["Aluno Dende"]));

			// =====================================================================
			// 11) O TIQUE
			// =====================================================================
			GD.Print("[alunos] -- 11) o tique: a fotografia sem ninguem pedir, e a faxina do vinculo que acabou --");
			Checa("(antes do tique) o diario do Gohan ainda esta na memoria -- o vinculo acabou agora ha pouco", _diarios.ContainsKey(a.Assinatura));
			TickDosAlunos();
			Checa("**O TIQUE JOGA FORA O DIARIO DO VINCULO QUE ACABOU** (e a lembranca de quem nao esta mais em vinculo nenhum), e so ele",
				  !_diarios.ContainsKey(a.Assinatura) && !_pessoasDoDiario.ContainsKey(a.Assinatura)
				  && _diarios.ContainsKey(b.Assinatura) && _diarios.ContainsKey(c.Assinatura) && _diarios.ContainsKey(d.Assinatura)
				  && _pessoasDoDiario.ContainsKey(mestre.Assinatura), $"{_diarios.Count} diarios, {_pessoasDoDiario.Count} pessoas");

			bool refez = Vincula(mestre, a);
			Checa("...e o vinculo REFEITO comeca do zero: diario novo, com a data de agora e nenhum ensinamento (o que foi ensinado antes nao volta)",
				  refez && _diarios.GetValueOrDefault(a.Assinatura) is { Ensinamentos.Count: 0, DesdeMs: > 0 });

			TodosLonge();
			c.Niveis.Por(comNiveis.Path, 0);
			ushort antesDoTique = diarioDeC.Achar(TipoDeEnsinamento.Licao, comNiveis.Path)!.Nivel;
			TickDosAlunos();
			Checa("**O TIQUE FOTOGRAFA SEM NINGUEM PEDIR**: o nivel do Dende mudou no corpo, e a fotografia do diario acompanha no tique seguinte",
				  antesDoTique == tetoDaLicao && _diarios[c.Assinatura].Achar(TipoDeEnsinamento.Licao, comNiveis.Path) is { Nivel: 0, Etapa: (byte)EtapaDoEnsinamento.EmCurso },
				  $"antes {antesDoTique}, depois {_diarios[c.Assinatura].Achar(TipoDeEnsinamento.Licao, comNiveis.Path)?.Nivel}");

			_mestreDe.Remove(d.Assinatura);
			_mestreDe.Remove(b.Assinatura);
			TickDosAlunos();
			Checa("...e um vinculo desfeito POR QUALQUER PORTA (aqui, direto no dicionario) leva o diario junto: o laco pergunta, nao espera aviso",
				  !_diarios.ContainsKey(d.Assinatura) && !_diarios.ContainsKey(b.Assinatura) && !_pessoasDoDiario.ContainsKey(outroMestre.Assinatura)
				  && _diarios.Count == 2, $"{_diarios.Count} diarios");
		}
		catch (Exception e)
		{
			Checa("a bancada correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			GD.PrintErr(e.ToString());
		}
		finally
		{
			// OS DEFEITOS DESLIGADOS, OS CORPOS FORA, E O MUNDO COMO ESTAVA -- na memoria e no disco.
			ProgressoDeAluno.ColegaVeTudoDeTeste = ProgressoDeAluno.SemOrdemDeTeste = false;
			PainelSemVinculoDeTeste = FotoDoAlunoNaoSeGuardaDeTeste = PoderNoPainelDeTeste = CartaoSemMiraDeTeste = false;
			EscutaDoDiscipulado = null;
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			foreach (ServerPlayer p in corpos)
			{
				_players.Remove(p.Id);
				ZoneList(zona.Hash).Remove(p);
			}
			_mestreDe.Clear();
			_nomePorAssinatura.Clear();
			_diarios.Clear();
			_pessoasDoDiario.Clear();
			_paineisMandados.Clear();
			_doDiscipuladoNoMundo.Clear();
			foreach (KeyValuePair<string, string> kv in vinculosDeAntes) _mestreDe[kv.Key] = kv.Value;
			foreach (KeyValuePair<string, string> kv in nomesDeAntes) _nomePorAssinatura[kv.Key] = kv.Value;
			foreach (KeyValuePair<string, DiarioDeAluno> kv in diariosDeAntes) _diarios[kv.Key] = kv.Value;
			foreach (KeyValuePair<string, PessoaDoDiario> kv in pessoasDeAntes) _pessoasDoDiario[kv.Key] = kv.Value;
			SalvarMestres();
			SalvarDiarios();

			GD.Print($"[alunos] ================ {ok} OK, {falhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// O PALCO DA BANCADA DE JANELA (`--diagalunos`) -- so bancada, e so alcancavel no mesmo processo (`--host`)
	// =====================================================================
	/// <summary>Os corpos forjados do palco e as assinaturas (de ALUNO) dos vinculos que ele plantou.</summary>
	private readonly List<ServerPlayer> _corposDoPalcoDeAlunos = [];
	private readonly List<string> _vinculosDoPalcoDeAlunos = [];

	/// <summary>Quantos corpos o palco ja forjou neste processo (o id de cada um sai daqui, e nunca se repete).</summary>
	private int _forjadosNoPalcoDeAlunos;

	/// <summary>As tres licoes do palco: uma que SOBE de nivel, uma que nao sobe, e uma terceira que so um colega recebe.</summary>
	internal (string ComNiveis, string SemNiveis, string SoDeUm)? LicoesDoPalcoDeAlunos()
	{
		if (_skills is not { } cat) return null;
		Skill? Escolher(string preferida, Func<Skill, bool> crivo, string menos = "")
		{
			bool Serve(Skill s) => s.Ensinavel && !s.Arvore && s.Nome.Length > 0 && crivo(s) && s.Path != menos;
			return cat.Get(preferida) is { } p && Serve(p) ? p : cat.Todas.Where(Serve).OrderBy(s => s.Path, StringComparer.Ordinal).FirstOrDefault();
		}
		Skill? comNiveis = Escolher("/datum/skill/flying", s => RegrasDeNivel.Get(s.Path) != null && s.MaxNivel >= 3);
		Skill? semNiveis = Escolher("/datum/skill/rank/Kamehameha", s => RegrasDeNivel.Get(s.Path) == null);
		Skill? soDeUm = Escolher("/datum/skill/rank/Makkankosappo", s => RegrasDeNivel.Get(s.Path) == null, semNiveis?.Path ?? "");
		return comNiveis != null && semNiveis != null && soDeUm != null ? (comNiveis.Path, semNiveis.Path, soDeUm.Path) : null;
	}

	/// <summary>
	/// Um corpo de palco: uma PESSOA (conta e slot dao a assinatura) sem tela, a `tiles` do host, com uma fracao do
	/// poder dele e apresentada a zona (`TrocarAparencias`: e por ela que o cliente fica sabendo o nome e o rosto).
	/// </summary>
	private ServerPlayer ForjarNoPalcoDeAlunos(ServerPlayer host, string nome, string raca, string cabelo, Vec2 tiles, double fracaoDoPoder)
	{
		// O ID NUNCA SE REPETE entre um ato do palco e o seguinte (o cliente guarda coisas por id), e A CONTA SAI DO
		// NOME: o mesmo personagem de palco tem a MESMA assinatura em todos os atos, e dois personagens diferentes
		// nunca dividem uma. Na primeira versao a conta era a ordem de chegada, e o "Bardock" do segundo ato herdava a
		// assinatura do "Gohan" do primeiro -- que o host ja conhecia por esse nome (a lista de conhecidos dele ficava
		// com o nome velho, e a leitura do sentido de Ki nao casava com ninguem).
		var novo = new ServerPlayer
		{
			Id = IdBaseDosAlunosDeTeste + 100 + _forjadosNoPalcoDeAlunos++, Peer = null, Name = nome, Race = raca, Genero = "Male", Idade = 25,
			Zone = host.Zone, Pos = host.Pos + new Vec2(tiles.X * ZoneCollision.TileSize, tiles.Y * ZoneCollision.TileSize),
			Conta = "palco_alunos_" + nome.ToLowerInvariant(), Slot = 0,
			Ficha = new Fighter { Race = raca, BP = Math.Max(1000, host.Ficha.BP * fracaoDoPoder) },
			Livro = new SkillBook(),
		};
		novo.Ficha.Class = "Normal";
		novo.Visual = host.Visual.Copiar();
		novo.Visual.Cabelo = cabelo;
		novo.Visual.CorCabelo = null;
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.Ficha.Tick(agoraMs: NowMs());
		TrocarAparencias(novo);
		_corposDoPalcoDeAlunos.Add(novo);
		return novo;
	}

	/// <summary>
	/// O vinculo de palco entre dois corpos no mundo, pelo `Vincular` de producao (o diario nasce com ele) -- e a
	/// data puxada pra tras, pra tela ter o que escrever em "aluno desde".
	/// </summary>
	private DiarioDeAluno VincularNoPalco(ServerPlayer mestre, ServerPlayer aluno, int haDias)
	{
		Vincular(mestre, aluno);
		_vinculosDoPalcoDeAlunos.Add(aluno.Assinatura);
		DiarioDeAluno d = _diarios[aluno.Assinatura];
		d.DesdeMs = NowMs() - haDias * 86_400_000L;
		return d;
	}

	/// <summary>O vinculo de palco de um aluno que NAO esta no mundo: so o que os dois arquivos guardariam dele.</summary>
	private DiarioDeAluno VincularAusenteNoPalco(string sigMestre, string conta, string nome, string raca, int haDias, double vistoHaHoras)
	{
		string sig = ServerPlayer.AssinaturaDe(conta, 0);
		long agora = NowMs();
		_mestreDe[sig] = sigMestre;
		_nomePorAssinatura[sig] = nome;
		_vinculosDoPalcoDeAlunos.Add(sig);
		var d = new DiarioDeAluno { Aluno = sig, Mestre = sigMestre, DesdeMs = agora - haDias * 86_400_000L };
		_diarios[sig] = d;
		_pessoasDoDiario[sig] = new PessoaDoDiario { Nome = nome, Raca = raca, VistoEm = agora - (long)(vistoHaHoras * 3_600_000) };
		return d;
	}

	/// <summary>Uma forma no diario de palco, com o estado do corpo (se ele esta no mundo) ou so a fotografia (se nao esta).</summary>
	private static void FormaNoPalco(DiarioDeAluno d, ServerPlayer? corpo, int tentativas, bool despertou, double maestria, int haDias)
	{
		Ensinamento e = ProgressoDeAluno.AnotarTentativa(d, "ssj1", NowMs() - haDias * 86_400_000L)!;
		e.Tentativas = tentativas;
		e.Assistida = despertou;
		if (corpo != null)
		{
			corpo.FormasVistas.Add(Catalogo.Rede("ssj1"));   // ele VIU a forma: o mestre pode guia-lo nela sem possui-la
			if (despertou)
			{
				corpo.Forma.Liberar("ssj1");
				corpo.Forma.Maestria.Por("ssj1", maestria);
			}
		}
		ProgressoDeAluno.Fotografar(e, ProgressoDeAluno.DaForma(despertou, maestria));
	}

	/// <summary>Uma licao no diario de palco, idem.</summary>
	private void LicaoNoPalco(DiarioDeAluno d, ServerPlayer? corpo, string path, int nivel, double fracaoDoNivel, int haDias)
	{
		Ensinamento e = ProgressoDeAluno.AnotarLicao(d, path, NowMs() - haDias * 86_400_000L)!;
		RegraDeNivel? regra = RegrasDeNivel.Get(path);
		int teto = regra != null ? Math.Max(1, _skills?.Get(path)?.MaxNivel ?? 1) : 0;
		nivel = Math.Clamp(nivel, 0, teto);   // (um nivel pedido acima do teto e "o ultimo": o palco nao precisa saber qual e)
		double barreira = regra?.BarreiraEm(nivel) ?? 0;
		if (corpo != null)
		{
			corpo.Livro.DarComoEnsinada(path);
			corpo.Niveis.Por(path, nivel, barreira * fracaoDoNivel);
		}
		ProgressoDeAluno.Fotografar(e, ProgressoDeAluno.DaLicao(true, teto, nivel, barreira * fracaoDoNivel, barreira));
	}

	/// <summary>
	/// SO PRA BANCADA DE JANELA: o host vira MESTRE de quatro alunos em pontos diferentes do caminho -- tres no mundo
	/// (dois ao lado dele, um longe) e um fora dele. Tudo o que vem depois (o fato que acende a aba, o pedido, o
	/// painel que chega, os gestos do cartao) e o caminho de producao.
	/// </summary>
	internal bool PlantarAlunosDeTeste(int idDoHost)
	{
		if (!_players.TryGetValue(idDoHost, out ServerPlayer? host) || !EhPessoa(host) || LicoesDoPalcoDeAlunos() is not { } l) return false;
		GarantirDiarios();

		ServerPlayer gohan = ForjarNoPalcoDeAlunos(host, "Gohan", "Saiyan", "Kid Gohan", new Vec2(4, 0), 0.80);
		ServerPlayer trunks = ForjarNoPalcoDeAlunos(host, "Trunks", "Saiyan", "Vegeta", new Vec2(0, 5), 0.45);
		ServerPlayer goten = ForjarNoPalcoDeAlunos(host, "Goten", "Saiyan", "Yamcha", new Vec2(-40, 0), 0.30);

		DiarioDeAluno dGohan = VincularNoPalco(host, gohan, haDias: 12);
		FormaNoPalco(dGohan, gohan, tentativas: 1, despertou: true, maestria: 62, haDias: 10);
		LicaoNoPalco(dGohan, gohan, l.ComNiveis, nivel: 2, fracaoDoNivel: 0.5, haDias: 8);
		LicaoNoPalco(dGohan, gohan, l.SemNiveis, nivel: 0, fracaoDoNivel: 0, haDias: 3);

		DiarioDeAluno dTrunks = VincularNoPalco(host, trunks, haDias: 5);
		FormaNoPalco(dTrunks, trunks, tentativas: 3, despertou: true, maestria: 12, haDias: 4);
		LicaoNoPalco(dTrunks, trunks, l.ComNiveis, nivel: 1, fracaoDoNivel: 0.2, haDias: 2);

		DiarioDeAluno dGoten = VincularNoPalco(host, goten, haDias: 2);
		FormaNoPalco(dGoten, goten, tentativas: 2, despertou: false, maestria: 0, haDias: 1);
		LicaoNoPalco(dGoten, goten, l.ComNiveis, nivel: 0, fracaoDoNivel: 0, haDias: 1);

		DiarioDeAluno dKuririn = VincularAusenteNoPalco(host.Assinatura, "palco_alunos_ausente_k", "Kuririn", "Human", haDias: 30, vistoHaHoras: 2);
		LicaoNoPalco(dKuririn, null, l.ComNiveis, nivel: 99, fracaoDoNivel: 0, haDias: 25);
		LicaoNoPalco(dKuririn, null, l.SemNiveis, nivel: 0, fracaoDoNivel: 0, haDias: 20);

		SalvarMestres();
		SalvarDiarios();
		return true;
	}

	/// <summary>
	/// SO PRA BANCADA DE JANELA: o host vira ALUNO de um mestre forjado (ao lado dele, quatro vezes mais forte) que
	/// tem outros tres alunos -- um no mundo, dois fora; um deles sem nenhum ensinamento em comum com o host.
	/// </summary>
	internal bool PlantarMestreDeTeste(int idDoHost)
	{
		if (!_players.TryGetValue(idDoHost, out ServerPlayer? host) || !EhPessoa(host) || LicoesDoPalcoDeAlunos() is not { } l) return false;
		GarantirDiarios();

		ServerPlayer bardock = ForjarNoPalcoDeAlunos(host, "Bardock", "Saiyan", "Raditz", new Vec2(0, -4), 4.0);
		ServerPlayer vegeta = ForjarNoPalcoDeAlunos(host, "Vegeta", "Saiyan", "Vegeta", new Vec2(40, 6), 1.5);

		DiarioDeAluno meu = VincularNoPalco(bardock, host, haDias: 9);
		FormaNoPalco(meu, host, tentativas: 2, despertou: true, maestria: 30, haDias: 7);
		LicaoNoPalco(meu, host, l.ComNiveis, nivel: 1, fracaoDoNivel: 0.5, haDias: 6);
		LicaoNoPalco(meu, host, l.SemNiveis, nivel: 0, fracaoDoNivel: 0, haDias: 2);
		// (o livro do host mudou por fora dos verbs: a tela dele precisa saber)
		MandarSkills(host, forcar: true);
		AplicarPoderes(host);

		DiarioDeAluno dVegeta = VincularNoPalco(bardock, vegeta, haDias: 20);
		FormaNoPalco(dVegeta, vegeta, tentativas: 1, despertou: true, maestria: 62, haDias: 18);
		LicaoNoPalco(dVegeta, vegeta, l.ComNiveis, nivel: 2, fracaoDoNivel: 0.5, haDias: 15);

		DiarioDeAluno dRaditz = VincularAusenteNoPalco(bardock.Assinatura, "palco_alunos_ausente_r", "Raditz", "Saiyan", haDias: 40, vistoHaHoras: 30);
		FormaNoPalco(dRaditz, null, tentativas: 2, despertou: false, maestria: 0, haDias: 35);
		LicaoNoPalco(dRaditz, null, l.ComNiveis, nivel: 0, fracaoDoNivel: 0, haDias: 30);

		DiarioDeAluno dNappa = VincularAusenteNoPalco(bardock.Assinatura, "palco_alunos_ausente_n", "Nappa", "Saiyan", haDias: 60, vistoHaHoras: 100);
		LicaoNoPalco(dNappa, null, l.SoDeUm, nivel: 0, fracaoDoNivel: 0, haDias: 50);

		SalvarMestres();
		SalvarDiarios();
		return true;
	}

	/// <summary>SO PRA BANCADA DE JANELA: tira o palco inteiro -- os corpos forjados, os vinculos plantados e os diarios deles.</summary>
	internal void RecolherDiscipuladoDeTeste()
	{
		foreach (string sig in _vinculosDoPalcoDeAlunos)
		{
			_mestreDe.Remove(sig);
			_diarios.Remove(sig);
			_nomePorAssinatura.Remove(sig);
			_pessoasDoDiario.Remove(sig);
		}
		_vinculosDoPalcoDeAlunos.Clear();
		foreach (ServerPlayer p in _corposDoPalcoDeAlunos)
		{
			_nomePorAssinatura.Remove(p.Assinatura);
			_pessoasDoDiario.Remove(p.Assinatura);
			if (_players.ContainsKey(p.Id)) RemoverNpc(p);
		}
		_corposDoPalcoDeAlunos.Clear();
		SalvarMestres();
		SalvarDiarios();
	}

	/// <summary>
	/// SO PRA BANCADA DA LIMPEZA TOTAL (`--wipeteste`, no `SemearMundoJogado`): o diario de um vinculo semeado, com um
	/// ensinamento e a lembranca do aluno -- direto na memoria, como o vinculo ao lado dele. E o que faz o retrato do
	/// mundo JOGADO ter um diario que a limpeza precisa levar.
	/// </summary>
	private void SemearDiarioDeTeste(string sigAluno, string sigMestre)
	{
		GarantirDiarios();
		long agora = NowMs();
		var d = new DiarioDeAluno { Aluno = sigAluno, Mestre = sigMestre, DesdeMs = agora };
		ProgressoDeAluno.AnotarTentativa(d, "ssj1", agora);
		_diarios[sigAluno] = d;
		_pessoasDoDiario[sigAluno] = new PessoaDoDiario { Nome = "AlunoDoWipe", Raca = "Human", VistoEm = agora };
		_diariosSujos = true;
	}

	/// <summary>SO PRA BANCADA DE JANELA: o painel que o SERVIDOR montaria agora pra este jogador (a verdade com que a tela e comparada).</summary>
	internal PainelDoDiscipulado? PainelDoDiscipuladoDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? PainelDe(pl) : null;

	/// <summary>SO PRA BANCADA DE JANELA: o mestre que o servidor guarda pra esta assinatura ("" = nenhum).</summary>
	internal string MestreDeTeste(string sigAluno) => MestreDe(sigAluno);

	/// <summary>SO PRA BANCADA DE JANELA: a assinatura deste jogador (a chave do discipulado).</summary>
	internal string AssinaturaDeTeste(int id) => _players.TryGetValue(id, out ServerPlayer? pl) ? pl.Assinatura : "";

	/// <summary>SO PRA BANCADA DE JANELA: a assinatura do corpo do palco com este nome ("" = nao ha nenhum no mundo).</summary>
	internal string AssinaturaDoPalcoDeTeste(string nome) =>
		_corposDoPalcoDeAlunos.FirstOrDefault(p => p.Name == nome)?.Assinatura ?? "";

	/// <summary>
	/// SO PRA BANCADA DE JANELA: poe um corpo do palco COLADO na frente do host -- na frente PRA ONDE ELE OLHA AGORA
	/// (quem manda no olhar e o cliente dele), pra o gesto de "ajudar a transformar" ter em quem cair. Devolve se o
	/// corpo ficou mesmo ao alcance do gesto (a mesma conta do <see cref="QuemEstaNaFrente"/>).
	/// </summary>
	internal bool PorNaFrenteDeTeste(int idDoHost, string sig)
	{
		if (!_players.TryGetValue(idDoHost, out ServerPlayer? host) || CorpoDaAssinatura(sig) is not { Peer: null } corpo) return false;
		corpo.Pos = host.Pos + Jandirus.Core.Combat.MeleeArea.Frente(host.Facing) * (0.9f * ZoneCollision.TileSize);
		return Jandirus.Core.Combat.MeleeArea.NoAlcance(host.Pos, host.Facing, corpo.Pos);
	}

	/// <summary>SO PRA BANCADA DE JANELA: tira um corpo do palco de perto do host (`tiles` pra tras de onde ele olha).</summary>
	internal bool AfastarDeTeste(int idDoHost, string sig, float tiles)
	{
		if (!_players.TryGetValue(idDoHost, out ServerPlayer? host) || CorpoDaAssinatura(sig) is not { Peer: null } corpo) return false;
		corpo.Pos = host.Pos - Jandirus.Core.Combat.MeleeArea.Frente(host.Facing) * (tiles * ZoneCollision.TileSize);
		return true;
	}

	/// <summary>SO PRA BANCADA DE JANELA: de quem e a oferta de DESPERTAR que esta na mesa deste corpo ("" = nenhuma).</summary>
	internal string OfertaDeDespertarDeTeste(string sig) =>
		CorpoDaAssinatura(sig)?.PedidoDoMestre is { Despertar: true } p && NowMs() <= p.Ate ? p.SigMestre : "";

	/// <summary>SO PRA BANCADA DE JANELA: tira a oferta que estiver na mesa deste corpo do palco (ele nao tem tela pra responder).</summary>
	internal void EsquecerOfertaDeTeste(string sig)
	{
		if (CorpoDaAssinatura(sig) is { Peer: null } corpo) corpo.PedidoDoMestre = null;
	}

	/// <summary>SO PRA BANCADA DE JANELA: quantos segundos faltam pra este jogador poder guiar alguem de novo (0 = ja pode).</summary>
	internal double RecargaDeEnsinoDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? Math.Max(0, (pl.RecargaDeEnsino - NowMs()) / 1000.0) : 0;

	/// <summary>SO PRA BANCADA DE JANELA: muda a maestria de uma forma num corpo do palco (o aluno "treinou").</summary>
	internal bool MaestriaDeTeste(string sig, string forma, double pct)
	{
		if (CorpoDaAssinatura(sig) is not { } corpo) return false;
		corpo.Forma.Maestria.Por(forma, pct);
		return true;
	}

	/// <summary>SO PRA BANCADA DE JANELA: zera a recarga de ensino do jogador (o gesto do cartao e medido mais de uma vez).</summary>
	internal void ZerarRecargaDeEnsinoDeTeste(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.RecargaDeEnsino = 0;
	}

	/// <summary>
	/// SO PRA BANCADA DE JANELA: o palco da LICAO DE MESTRE. O jogador passa a saber uma skill que NAO se ensina a
	/// estranho e que e do corpo deste aluno (escolhida por predicado, a primeira pelo caminho), o aluno ganha os
	/// marcos dela, e a recarga de licao do jogador e zerada. Devolve o caminho da skill ("" = nao deu).
	/// </summary>
	internal string PrepararLicaoDeMestreDeTeste(int idDoHost, string sigDoAluno)
	{
		if (_skills == null || !_players.TryGetValue(idDoHost, out ServerPlayer? mestre) || CorpoDaAssinatura(sigDoAluno) is not { Peer: null } aluno) return "";
		aluno.Livro.Conceder(60);
		Jandirus.Core.Skills.Skill? s = _skills.Todas
			.Where(x => !x.Ensinavel && !x.Arvore && x.Nome.Length > 0 && Jandirus.Core.Skills.EnsinoDeSkill.Custo(x) > 0
						&& !aluno.Livro.Sabe(x.Path) && !mestre.Livro.Sabe(x.Path)
						&& !Jandirus.Core.Skills.EnsinoDeSkill.ForaDoCorpo(aluno.Livro.PodeAprender(_skills, x.Path, aluno.Race, aluno.Class, vilao: false)))
			.OrderBy(x => x.Path, StringComparer.Ordinal).FirstOrDefault();
		if (s == null) return "";
		mestre.Livro.Dar(s.Path);
		AplicarPoderes(mestre);
		AplicarEfeitos(mestre);   // (e daqui que o pacote de skills sai pra tela do jogador)
		mestre.RecargaDaLicao = 0;
		return s.Path;
	}

	/// <summary>SO PRA BANCADA DE JANELA: a licao que esta na mesa deste corpo do palco -- o caminho da skill, ou `tecnica:id`; "" = nenhuma.</summary>
	internal string LicaoOferecidaDeTeste(string sig) =>
		CorpoDaAssinatura(sig)?.PedidoDeLicao is { } p && NowMs() <= p.Ate ? (p.Tecnica > 0 ? $"tecnica:{p.Tecnica}" : p.Path) : "";

	/// <summary>SO PRA BANCADA DE JANELA: tira a licao da mesa deste corpo do palco (ele nao tem tela pra responder) e zera a recarga de licao do jogador.</summary>
	internal void EsquecerLicaoDeTeste(int idDoHost, string sig)
	{
		if (CorpoDaAssinatura(sig) is { Peer: null } corpo) corpo.PedidoDeLicao = null;
		if (_players.TryGetValue(idDoHost, out ServerPlayer? pl)) pl.RecargaDaLicao = 0;
	}
}
