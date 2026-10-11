using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.Social;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DAS VALIDACOES, TERCEIRA RODADA (`--validacoesteste`, secoes 18 a 20).
///
/// O dono (2026-10-11), sobre as tres sobras que a segunda rodada deixou escritas: *"sim pode fechar os dois, e a
/// investida do soco etc tb pode resolver"*. No molde das outras (`GameServer.ValidacoesTeste.cs`): a recusa com a
/// frase, nenhum efeito colateral, o caminho legitimo de pe, e o defeito injetado de cada conserto reprovando a
/// prova.
///
///   18. So o que foi COMPRADO com marcos devolve marco: a herdada pela absorcao Namekuseijin, a dada por admin e a
///       concedida de graca se esquecem sem pagar (`SkillBook.FoiComprada`). O save novo, o antigo, e o pacote.
///   19. O preso da Sala do Tempo nao sai dela dentro de outro corpo -- passageiro de fusao ou absorvido de Majin.
///   20. Num PLANETA GERADO de verdade, montanha, arvore, rocha grande e bloco de base barram a investida do soco, a
///       piscada, o avanco das tecnicas, o puxao da fusao e o fim do embate de Zanzoken (`MapaDoSalto`).
///
/// A SECAO 18 USA O HOST numa parte: o `admin_skill_dar` so atende JOGADOR (`EhJogador` pede `Peer`), e corpo
/// forjado nao tem. O livro e os marcos dele voltam como estavam.
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// 18) o marco so do que foi comprado
	// =====================================================================
	private void ValOMarcoSoDoComprado(ServerPlayer host)
	{
		GD.Print("[validacoes] -- 18) `skill_esquecer`: so o que foi COMPRADO com marcos devolve marco (dono, 2026-10-11) --");
		if (_skills == null || host.Livro == null) { ValCheca("(montagem) ha catalogo de skills e o host tem livro", false); return; }
		const string treino = "/datum/skill/training", evasao = "/datum/skill/evasive";
		const StringComparison SemCaixa = StringComparison.OrdinalIgnoreCase;
		bool NaLista(List<string> lista, string path) => lista.Exists(p => string.Equals(p, path, SemCaixa));

		// ---- (a) A MESMA SKILL, NO MESMO JOGADOR: comprada devolve; dada por admin, nao
		SkillBook livro = host.Livro;
		(int Totais, int Livres) marcosDoHost = (livro.MarcosTotais, livro.MarcosLivres);
		bool sabiaAntes = livro.Sabe(treino);
		try
		{
			if (livro.MarcosLivres < 1) livro.Conceder(1);
			int marcos = livro.MarcosLivres;
			ValCheca("(montagem) o host ainda nao sabe Basic Training e tem marco pra compra-la", !sabiaAntes && marcos >= 1, $"sabe {sabiaAntes}, marcos {marcos}");
			if (sabiaAntes) return;

			Aprender(host, treino);
			bool comprou = livro.Sabe(treino) && livro.FoiComprada(treino) && livro.MarcosLivres == marcos - 1;
			bool telaDaComprada = !NaLista(SkillsSemReembolso(host), treino);
			List<string> vende = Ouvir(() => Verbo(host, "skill_esquecer", treino));
			ValCheca("O CAMINHO LEGITIMO: COMPRADA pelo funil (`Aprender`), a skill entra entre as compradas, o pacote nao a poe entre as "
					+ "que nao devolvem marco, e esquece-la devolve o que custou",
					comprou && telaDaComprada && !livro.Sabe(treino) && livro.MarcosLivres == marcos && Disse(vende, "1 marco(s) de volta"),
					$"comprou {comprou}, tela {telaDaComprada}, marcos {marcos}->{livro.MarcosLivres} | {Ultimos(vende)}");

			List<string> presente = Ouvir(() => AdminSkill(host, $"{host.Id}|{treino}", dar: true));
			bool dada = livro.Sabe(treino) && !livro.FoiComprada(treino) && livro.MarcosLivres == marcos;
			bool telaDaDada = NaLista(SkillsSemReembolso(host), treino);
			List<string> esquece = Ouvir(() => Verbo(host, "skill_esquecer", treino));
			ValCheca("**O QUE NINGUEM COMPROU NAO DEVOLVE MARCO**: a mesma skill, DADA por um admin (`admin_skill_dar`), se esquece como "
					+ "qualquer outra -- e o livro nao paga por ela. O dono le por que, e o pacote ja a punha entre as que nao devolvem",
					dada && telaDaDada && !livro.Sabe(treino) && livro.MarcosLivres == marcos && Disse(esquece, "nao devolve marco"),
					$"dada {dada}, tela {telaDaDada}, marcos {marcos}->{livro.MarcosLivres} | {Ultimos(presente)} | {Ultimos(esquece)}");

			Ouvir(() => AdminSkill(host, $"{host.Id}|{treino}", dar: true));
			SkillBook.TudoContaComoCompradoDeTeste = true;
			try
			{
				bool telaPromete = !NaLista(SkillsSemReembolso(host), treino);
				Ouvir(() => Verbo(host, "skill_esquecer", treino));
				ValCheca("(defeito injetado: o livro nao sabe o que foi comprado) a prova de cima REPROVA -- a skill dada por admin devolve "
						+ "1 marco, e o pacote a prometia a tela",
						livro.MarcosLivres == marcos + 1 && telaPromete, $"marcos {marcos}->{livro.MarcosLivres}, tela promete {telaPromete}");
			}
			finally { SkillBook.TudoContaComoCompradoDeTeste = false; }
		}
		finally
		{
			if (!sabiaAntes && livro.Sabe(treino)) livro.Esquecer(treino);
			(livro.MarcosTotais, livro.MarcosLivres) = marcosDoHost;
			AplicarPoderes(host);
			AplicarEfeitos(host);
		}

		// ---- (b) A HERDADA PELA ABSORCAO NAMEKUSEIJIN, pela corrente de producao (convite, duas confirmacoes, cena)
		Skill? ensinavel = _skills.Todas.FirstOrDefault(s => s is { Ensinavel: true, Esquecivel: true, Arvore: false });
		using (PalcoDeApagamentos palco = PalcoDeApagamentosDeBancada())
		{
			Vec2 praca = CorredorLivre(24);
			ServerPlayer dono = ValGente("NamekQueAbsorveDaBancada", "Namekian", praca, bp: 2_000_000);
			ServerPlayer comido = ValGente("NamekAbsorvidoDaBancada", "Namekian", praca + new Vec2(ZoneCollision.TileSize, 0), bp: 400_000);
			comido.Livro!.Conceder(5);
			AplicarEfeitos(comido);
			Aprender(comido, treino);
			if (ensinavel != null) comido.Livro.Dar(ensinavel.Path);
			dono.Livro!.Conceder(3);
			AplicarEfeitos(dono);
			palco.Emprestar(comido.Conta, comido.Slot, AccountStore.DeJogador(comido, NowMs()));
			ValCheca("(montagem) dois Namekuseijins colados: o que vai ser absorvido COMPROU Basic Training e sabe uma skill que se ensina",
					comido.Livro.FoiComprada(treino) && ensinavel != null && comido.Livro.Sabe(ensinavel.Path) && !dono.Livro.Sabe(treino),
					$"comprou {comido.Livro.FoiComprada(treino)}, ensinavel {ensinavel?.Path ?? "nenhuma"}");

			List<string> absorcao = Ouvir(() => AbsorverDeVerdade(dono, comido));
			// (o corpo forjado nao tem conexao pra cair: quem diz que o personagem acabou e o `PersonagemConsumido`)
			bool herdou = dono.Livro.Sabe(treino) && comido.PersonagemConsumido;
			ValCheca("(montagem) a absorcao correu inteira: o personagem do absorvido foi consumido e a skill dele esta no livro de quem absorveu",
					herdou, $"sabe {dono.Livro.Sabe(treino)}, consumido {comido.PersonagemConsumido} | {Ultimos(absorcao)}");
			if (herdou)
			{
				int marcos = dono.Livro.MarcosLivres;
				bool repassa = ensinavel != null && dono.Livro.Sabe(ensinavel.Path) && !dono.Livro.FoiEnsinada(ensinavel.Path)
							   && EnsinoDeSkill.PodeRepassar(dono.Livro, ensinavel);
				CharacterSave doHerdeiro = AccountStore.DeJogador(dono, NowMs());
				bool noSave = NaLista(doHerdeiro.Skills, treino) && doHerdeiro.SkillsCompradas is { } pagas && !NaLista(pagas, treino)
							  && !NaLista(doHerdeiro.SkillsEnsinadas, treino);
				ValCheca("**A HERDADA NAO FOI COMPRADA POR QUEM A HERDOU**: a compra era do absorvido e morreu com ele -- no livro de quem "
						+ "absorveu ela nao esta entre as compradas (nem no save dele), e o pacote a poe entre as que nao devolvem marco. E NAO "
						+ "virou copia ensinada: o Namekuseijin continua podendo repassar o que herdou",
						!dono.Livro.FoiComprada(treino) && !dono.Livro.FoiEnsinada(treino) && NaLista(SkillsSemReembolso(dono), treino) && repassa && noSave,
						$"comprada {dono.Livro.FoiComprada(treino)}, ensinada {dono.Livro.FoiEnsinada(treino)}, repassa {repassa}, no save {noSave}");

				List<string> larga = Ouvir(() => Verbo(dono, "skill_esquecer", treino));
				ValCheca("...e esquecida, ela sai do livro SEM devolver marco (era 1 marco por skill herdada, a cada absorcao)",
						!dono.Livro.Sabe(treino) && dono.Livro.MarcosLivres == marcos && Disse(larga, "nao devolve marco"),
						$"marcos {marcos}->{dono.Livro.MarcosLivres} | {Ultimos(larga)}");

				// CONTRA-EXEMPLO DO REPASSE: a mesma skill, entrando como LICAO, nao se repassa -- e a herdada nao e isso.
				if (ensinavel != null)
				{
					var aluno = new SkillBook();
					aluno.DarComoEnsinada(ensinavel.Path);
					ValCheca("CONTRA-EXEMPLO: a mesma skill recebida como LICAO (`DarComoEnsinada`) nao se repassa -- a regua do repasse sabe dizer nao",
							!EnsinoDeSkill.PodeRepassar(aluno, ensinavel));
				}
			}
		}

		// ---- (c) O SAVE: o campo novo vai e volta pelo `AccountStore` de producao; o arquivo de ANTES dele carrega com tudo comprado
		ServerPlayer leitor = ValGente("LeitorDeSaveDaBancada", "Human", CorredorLivre(24));
		leitor.Livro!.Conceder(5);
		AplicarEfeitos(leitor);
		Aprender(leitor, treino);
		leitor.Livro.Dar(evasao);
		string pasta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jandirus-validacoes-" + Guid.NewGuid().ToString("N"));
		System.IO.Directory.CreateDirectory(pasta);
		try
		{
			var store = new AccountStore(pasta);
			var conta = new AccountSave { Conta = "bancada_valid_save" };
			conta.Slots[0] = AccountStore.DeJogador(leitor, NowMs());
			store.Gravar(conta);
			string arquivo = System.IO.Path.Combine(pasta, AccountStore.NomeDeArquivo(conta.Conta) + ".json");
			string json = System.IO.File.ReadAllText(arquivo);
			CharacterSave? novo = store.Carregar(conta.Conta)?.Slots[0];
			ValCheca("**O SAVE NOVO GUARDA O QUE FOI COMPRADO** (`CharacterSave.SkillsCompradas`): gravado e lido de volta pelo `AccountStore` "
					+ "de producao, traz a comprada e nao a dada -- as duas continuam em `Skills`",
					novo is { SkillsCompradas.Count: 1, Skills.Count: 2 } && NaLista(novo.SkillsCompradas, treino) && NaLista(novo.Skills, evasao)
					&& json.Contains("\"SkillsCompradas\"", StringComparison.Ordinal),
					$"compradas [{string.Join(",", novo?.SkillsCompradas ?? [])}], skills [{string.Join(",", novo?.Skills ?? [])}]");
			if (novo == null) return;

			PrepararSkills(leitor, novo);
			ValCheca("...e o livro montado dele sabe a diferenca: a comprada devolve marco, a dada nao (e as duas estao no livro)",
					leitor.Livro.FoiComprada(treino) && !leitor.Livro.FoiComprada(evasao) && leitor.Livro.Sabe(evasao));

			// O ARQUIVO DE ANTES DE 2026-10-11: o MESMO, sem o campo.
			string antigo = System.Text.RegularExpressions.Regex.Replace(json, "\"SkillsCompradas\"\\s*:\\s*\\[[^\\]]*\\]\\s*,", "");
			System.IO.File.WriteAllText(arquivo, antigo);
			CharacterSave? velho = store.Carregar(conta.Conta)?.Slots[0];
			ValCheca("(montagem) o mesmo arquivo SEM o campo -- o save de antes da lista -- e lido pelo `AccountStore` de producao, e o campo chega NULO",
					antigo != json && !antigo.Contains("SkillsCompradas", StringComparison.Ordinal) && velho is { SkillsCompradas: null, Skills.Count: 2 },
					$"lido {velho != null}, campo {(velho?.SkillsCompradas == null ? "nulo" : "preenchido")}");
			if (velho == null) return;

			PrepararSkills(leitor, velho);
			CharacterSave regravado = AccountStore.DeJogador(leitor, NowMs());
			ValCheca("**O SAVE ANTIGO CARREGA COM TUDO COMPRADO**: sem o campo, o que esta no livro e ninguem ensinou conta como comprado -- "
					+ "ninguem perde, na virada, o marco de uma compra que o save nao sabia descrever. E o primeiro salvamento ja grava a lista",
					leitor.Livro.FoiComprada(treino) && leitor.Livro.FoiComprada(evasao) && regravado.SkillsCompradas is { Count: 2 },
					$"regravado [{string.Join(",", regravado.SkillsCompradas ?? [])}]");

			velho.SkillsCompradas = [];
			PrepararSkills(leitor, velho);
			ValCheca("LISTA VAZIA NAO E SAVE ANTIGO: e quem nao comprou nada -- as skills continuam no livro, e nenhuma devolve marco",
					leitor.Livro.Sabe(treino) && leitor.Livro.Sabe(evasao) && !leitor.Livro.FoiComprada(treino) && !leitor.Livro.FoiComprada(evasao));

			velho.SkillsCompradas = [treino, evasao, "/datum/skill/que_o_livro_nao_tem"];
			velho.SkillsEnsinadas = [evasao];
			PrepararSkills(leitor, velho);
			ValCheca("...copia ENSINADA nao e compra mesmo que o arquivo diga, e marca de skill que o livro nao tem e descartada",
					leitor.Livro.FoiComprada(treino) && !leitor.Livro.FoiComprada(evasao) && leitor.Livro.Compradas.Count == 1,
					$"compradas [{string.Join(",", leitor.Livro.Compradas)}]");

			// A SKILL APOSENTADA (`SkillsAposentadas.Limpar`, no login): o marco volta so pra quem a comprou.
			SkillsAposentadas.Aposentada fora = SkillsAposentadas.Todas[0];
			var comAposentada = new CharacterSave { Skills = [treino, fora.Path], SkillsCompradas = [treino], MarcosTotais = 3, MarcosLivres = 0 };
			PrepararSkills(leitor, comAposentada);
			int seDada = leitor.Livro.MarcosLivres;
			comAposentada.SkillsCompradas = [treino, fora.Path];
			PrepararSkills(leitor, comAposentada);
			int seComprada = leitor.Livro.MarcosLivres;
			comAposentada.SkillsCompradas = null;
			PrepararSkills(leitor, comAposentada);
			int noSaveAntigo = leitor.Livro.MarcosLivres;
			ValCheca($"**A SKILL QUE SAIU DO JOGO DEVOLVE O MARCO SO A QUEM A COMPROU** ({fora.Nome}, no login): dada, nada; comprada, o custo; e "
					+ "no save antigo, o custo -- la tudo o que ninguem ensinou conta como comprado",
					seDada == 0 && seComprada == fora.Custo && noSaveAntigo == fora.Custo && !leitor.Livro.Sabe(fora.Path),
					$"dada {seDada}, comprada {seComprada}, save antigo {noSaveAntigo}");
		}
		finally
		{
			try { System.IO.Directory.Delete(pasta, recursive: true); }
			catch (Exception e) { GD.PushWarning($"[validacoes] a pasta temporaria do save nao saiu ('{pasta}'): {e.Message}"); }
		}

		// ---- (d) O FIO: a cauda do `S2C.Skills`, lida pelo leitor do cliente
		ServerPlayer doFio = ValGente("DoFioDaBancada", "Human", CorredorLivre(24));
		doFio.Livro!.Conceder(5);
		AplicarEfeitos(doFio);
		Aprender(doFio, treino);
		doFio.Livro.Dar(evasao);
		AplicarEfeitos(doFio);

		var r = new NetDataReader(MontarPacoteDeSkills(doFio).CopyData());
		r.GetByte();
		r.GetInt();
		r.GetInt();
		r.GetBool();
		int quantas = r.GetUShort();
		for (int i = 0; i < quantas; i++) r.GetString(96);
		(_, _, _, List<string> semReembolso) = Protocol.LerEstadoDeSkills(r);
		ValCheca("**O PACOTE DE SKILLS DIZ O QUE NAO DEVOLVE MARCO** (a cauda do `S2C.Skills`): lido pelo leitor que o CLIENTE usa, traz a dada "
				+ "e nao a comprada, e acaba exatamente onde o leitor para",
				quantas == 2 && r.EndOfData && semReembolso.Count == 1 && NaLista(semReembolso, evasao),
				$"{quantas} aprendidas, {r.AvailableBytes} byte(s) sobrando, sem reembolso [{string.Join(",", semReembolso)}]");

		// OS DOIS LADOS DO FIO ANDAM JUNTOS: o pacote de ANTES do campo (o mesmo, sem os 2 bytes da contagem nova), no
		// leitor de hoje, e lido ALEM do fim -- o que o `GameClient.PacotesMalLidos` conta. E a impressao digital do
		// protocolo muda com a revisao da cauda, entao a porta recusa o build desencontrado antes de ele ler isto.
		NetDataWriter deOntem = Protocol.Begin(Protocol.S2C.Skills);
		Protocol.PorEstadoDeSkills(deOntem, doFio.Livro, TecnicasDe(doFio), []);
		byte[] cheio = deOntem.CopyData();
		var leitorDeHoje = new NetDataReader(cheio[..^2]);
		leitorDeHoje.GetByte();
		bool alemDoFim;
		try
		{
			Protocol.LerEstadoDeSkills(leitorDeHoje);
			alemDoFim = leitorDeHoje.AvailableBytes < 0;
		}
		catch (Exception) { alemDoFim = true; }
		ValCheca("OS DOIS LADOS DO FIO ANDAM JUNTOS: o pacote sem o campo novo, no leitor de hoje, e lido ALEM do fim (estoura ou le lixo) -- e a "
				+ "impressao digital do protocolo carrega a revisao da cauda, pra porta recusar o build desencontrado",
				alemDoFim && Jandirus.Net.FioDaVersao.DescricaoDoProtocolo()
					.Contains($"const Jandirus.Net.Protocol.RevisaoDaCaudaDeSkills={Protocol.RevisaoDaCaudaDeSkills}\n", StringComparison.Ordinal),
				$"alem do fim {alemDoFim}");

		MandarSkills(doFio, forcar: true);
		string sig = doFio.SigSkills;
		MandarSkills(doFio);
		bool estavel = doFio.SigSkills == sig;
		doFio.Livro.CarregarCompradas(null);          // (palco) a dada passa a contar como comprada: e SO isso que muda no livro
		MandarSkills(doFio);
		ValCheca("**E A ASSINATURA DO PACOTE COBRE O CAMPO NOVO**: com nada mudando ela fica; muda SO o que devolve marco (marcos, skills, "
				+ "arvores e verbs iguais) e o pacote sai de novo -- senao a tela continuaria prometendo o que deixou de valer",
				estavel && doFio.SigSkills != sig, $"estavel {estavel}; '{sig}' -> '{doFio.SigSkills}'");
	}

	// =====================================================================
	// 19) o preso carregado pra fora
	// =====================================================================
	private void ValOPresoCarregado()
	{
		GD.Print("[validacoes] -- 19) o preso da Sala do Tempo nao sai dela dentro de outro corpo: nem funde, nem e absorvido por um Majin --");
		const int T = ZoneCollision.TileSize;
		ZoneKey sala = ZoneKey.Premade(ZonaDaSala);
		Vec2 noMeio = new((EntradaByondX - 1) * T + T / 2f, 345 * T + T / 2f);
		Vec2 palco = CorredorLivre(24);

		void PorNaSala(ServerPlayer p, Vec2 onde)
		{
			ValMover(p, sala, onde);
			p.Moving = false;
		}
		// UM TILE AO SUL DA SAIDA, OLHANDO PRA ELA E ANDANDO: o gesto que a passagem atende (o palco da `--salateste`).
		void EmpurrarASaida(ServerPlayer p)
		{
			p.Pos = new Vec2(145 * T + 16, 339 * T + 8);
			p.Facing = Facing.North;
			p.Moving = true;
			_acabouDeAtravessar.Remove(p.Id);
			_naBocaDaPassagem.Remove(p.Id);
			_avisoDePrisao.Remove(p.Id);
		}
		// A FUSAO ACABA e os dois voltam pro meio da Sala, sem recarga, sem cena e sem convite pendente.
		void Recomecar(ServerPlayer a, ServerPlayer b)
		{
			if (FusaoDe(a.Id) is { } f) Separar(f, "fim da prova");
			foreach (ServerPlayer p in new[] { a, b })
			{
				if (_emCenaDeFusao.GetValueOrDefault(p.Id) is { } c) SoltarDaCenaDeFusao(c);
				_pedidosDeFusao.Remove(p.Id);
				p.Ficha.fusion_cooldown_until = 0;
				p.Combate.Stun = 0;
				p.CenaSegundos = 0;
			}
			PorNaSala(a, noMeio);
			PorNaSala(b, noMeio + new Vec2(T, 0));
		}

		ServerPlayer livre = ValGente("LivreDaSalaDaBancada", "Human", palco, bp: 500_000);
		ServerPlayer preso = ValGente("PresoDaSalaDaBancada", "Human", palco + new Vec2(T, 0), bp: 500_000);
		ServerPlayer majin = ValGente("MajinDaSalaDaBancada", "Majin", palco + new Vec2(2 * T, 0), bp: 1_000_000, skills: [AbsorcaoMajin.Skill]);
		ServerPlayer caido = ValGente("CaidoDaSalaDaBancada", "Human", palco + new Vec2(3 * T, 0), bp: 400_000);
		try
		{
			// =====================================================================
			// A FUSAO
			// =====================================================================
			Recomecar(livre, preso);
			Convidar(livre, preso, TipoDeFusao.Potara);
			ValCheca("(montagem) dois SOLTOS, colados na Sala do Tempo: o convite da Potara sai -- o gesto e legitimo la dentro",
					_pedidosDeFusao.Remove(preso.Id));

			preso.SalaPreso = true;
			List<string> aoLivre = Ouvir(() => Convidar(livre, preso, TipoDeFusao.Potara));
			bool naoConvidaOPreso = !_pedidosDeFusao.ContainsKey(preso.Id);
			List<string> aoPreso = Ouvir(() => Convidar(preso, livre, TipoDeFusao.Potara));
			bool oPresoNaoConvida = !_pedidosDeFusao.ContainsKey(livre.Id);
			ValCheca("**O PRESO NAO FUNDE** -- nem convidado, nem convidando (o `PassarOControle` faria dele o passageiro a qualquer hora): o "
					+ "convite nao nasce, e cada um le a frase da prisao",
					naoConvidaOPreso && oPresoNaoConvida && Disse(aoLivre, "está preso na Sala do Tempo") && Disse(aoPreso, "você está preso na Sala do Tempo"),
					$"{Ultimos(aoLivre)} || {Ultimos(aoPreso)}");
			Recomecar(livre, preso);

			// O ACEITE: o convite saiu com os dois soltos, e a tranca fechou antes do "sim".
			preso.SalaPreso = false;
			Convidar(livre, preso, TipoDeFusao.Potara);
			preso.SalaPreso = true;
			List<string> noSim = Ouvir(() => ResponderAoConvite(preso, aceitou: true));
			ValCheca("...e o ACEITE pergunta de novo: o convite feito com os dois soltos nao vira fusao se a tranca fechou antes do \"sim\" -- "
					+ "nem puxao, nem cena",
					!EstaFundido(livre.Id) && !_emCenaDeFusao.ContainsKey(livre.Id) && !_sendoPuxadoPraFusao.ContainsKey(livre.Id)
					&& Disse(noSim, "preso na Sala do Tempo"), Ultimos(noSim));
			Recomecar(livre, preso);

			PresoEntraEmQuemSaiDeTeste = true;
			try
			{
				FusaoAtiva? burla = FundirDeVerdade(livre, preso, TipoDeFusao.Potara);
				bool fundiu = burla != null && burla.Dono == livre && burla.Passageiro == preso && EhOSelo(preso.Zone);
				EmpurrarASaida(livre);
				List<string> naPorta = Ouvir(TickDasPassagens);
				ValCheca("(defeito injetado: o preso funde) as provas de cima REPROVAM -- a fusao nasce com o preso de passageiro. E a rede de "
						+ "baixo segura: quem o carrega nao passa pela porta",
						fundiu && NaSala(livre) && Disse(naPorta, "está dentro de você"), $"fundiu {fundiu}, dono em {livre.Zone.Name} | {Ultimos(naPorta)}");

				CargaPresaSaiDeTeste = true;
				try
				{
					EmpurrarASaida(livre);
					Ouvir(TickDasPassagens);
					bool donoSaiu = !NaSala(livre);
					if (burla != null) Separar(burla, "fim da prova");
					ValCheca("(os dois defeitos: o preso funde, e quem carrega sai) A FUGA DE ANTES -- o outro atravessa a saida, a fusao acaba "
							+ "la fora, e o preso reaparece FORA da Sala do Tempo, ao lado dele",
							donoSaiu && !NaSala(preso) && !EhOSelo(preso.Zone) && preso.Zone.Hash == livre.Zone.Hash,
							$"dono em {livre.Zone.Name}, preso em {preso.Zone.Name}");
				}
				finally { CargaPresaSaiDeTeste = false; }
			}
			finally { PresoEntraEmQuemSaiDeTeste = false; }
			Recomecar(livre, preso);

			// A TRANCA FECHA POR RELOGIO, COM A CINEMATICA RODANDO -- pelo tique de producao (`JanelaOuTranca`).
			preso.SalaPreso = false;
			preso.SalaJanelaAte = 0;
			Convidar(livre, preso, TipoDeFusao.Potara);
			ResponderAoConvite(preso, aceitou: true);
			CenaDeFusao? cena = _emCenaDeFusao.GetValueOrDefault(livre.Id);
			_naSala[preso.Assinatura] = NowMs();                                   // (palco) ele atravessou a porta: tem sessao
			preso.SalaDiasDaSessao = Jandirus.Core.World.SalaDoTempo.SessaoEmDias;    // ...ela acabou
			preso.SalaJanelaAte = NowMs() - 1;                                        // ...e os dois minutos da janela tambem
			TickDaSessaoDaSala(1.0);
			bool trancou = preso.SalaPreso;
			if (cena != null)
			{
				cena.Funde = NowMs() - 1;
				TickDaCenaDeFusao();
				cena.Acaba = NowMs() - 1;
				TickDaCenaDeFusao();
			}
			FusaoAtiva? fusao = FusaoDe(livre.Id);
			ValCheca("(montagem) a tranca fecha por RELOGIO (`JanelaOuTranca`, o tique de producao) com o convite ja aceito e a cinematica "
					+ "rodando: a fusao nasce com um preso de passageiro -- o caso que a porta do convite nao ve",
					cena != null && trancou && fusao != null && fusao.Dono == livre && fusao.Passageiro == preso && EhOSelo(preso.Zone),
					$"cena {cena != null}, trancou {trancou}, fundiu {fusao != null}");

			string pergunta = PorQueNaoSaiDaqui(livre);
			EmpurrarASaida(livre);
			List<string> porta = Ouvir(TickDasPassagens);
			ValCheca("**QUEM CARREGA UM PRESO NAO SAI COM ELE**: a porta recusa o corpo de fora dizendo quem esta dentro dele, e a pergunta de "
					+ "todo teleporte (`PorQueNaoSaiDaqui`) responde o mesmo",
					NaSala(livre) && Disse(porta, "a porta não se abre") && Disse(porta, preso.Name)
					&& pergunta.Contains("preso na Sala do Tempo", StringComparison.Ordinal) && pergunta.Contains(preso.Name, StringComparison.Ordinal),
					$"dono em {livre.Zone.Name} | porta: {Ultimos(porta)} | pergunta: {pergunta}");

			CargaPresaSaiDeTeste = true;
			try
			{
				bool perguntaCala = PorQueNaoSaiDaqui(livre).Length == 0;
				EmpurrarASaida(livre);
				Ouvir(TickDasPassagens);
				bool donoSaiu = !NaSala(livre);
				if (fusao != null) Separar(fusao, "fim da prova");
				ValCheca("(defeito injetado: quem carrega um preso sai) a prova de cima REPROVA -- o dono atravessa a saida, o teleporte nao "
						+ "pergunta nada, e na separacao o preso reaparece ao lado dele, FORA da Sala",
						perguntaCala && donoSaiu && !NaSala(preso) && preso.Zone.Hash == livre.Zone.Hash,
						$"dono em {livre.Zone.Name}, preso em {preso.Zone.Name}");
			}
			finally { CargaPresaSaiDeTeste = false; }
			_naSala.Remove(preso.Assinatura);
			preso.SalaPreso = false;
			preso.SalaJanelaAte = 0;
			preso.SalaDiasDaSessao = 0;
			Recomecar(livre, preso);

			FusaoAtiva? soltos = FundirDeVerdade(livre, preso, TipoDeFusao.Potara);
			EmpurrarASaida(livre);
			Ouvir(TickDasPassagens);
			ValCheca("O CAMINHO LEGITIMO: com um passageiro que NAO esta preso, a mesma dupla fundida sai pela porta -- a fusao de quem esta "
					+ "solto nao mudou",
					soltos != null && !NaSala(livre), $"fundiu {soltos != null}, dono em {livre.Zone.Name}");
			Recomecar(livre, preso);

			// A NAMEKUSEIJIN FICA COMO ESTAVA: por ela ninguem sai (o absorvido deixa de existir, quem absorve fica no corpo dele).
			ServerPlayer namek = ValGente("NamekDaSalaDaBancada", "Namekian", palco + new Vec2(4 * T, 0));
			ServerPlayer namekPreso = ValGente("NamekPresoDaSalaDaBancada", "Namekian", palco + new Vec2(5 * T, 0));
			PorNaSala(namek, noMeio + new Vec2(0, 6 * T));
			PorNaSala(namekPreso, noMeio + new Vec2(T, 6 * T));
			namekPreso.SalaPreso = true;
			Convidar(namek, namekPreso, TipoDeFusao.Namek);
			ValCheca("O QUE NAO MUDA: a fusao NAMEKUSEIJIN (permanente: o absorvido deixa de existir, e ninguem sai por ela) continua podendo "
					+ "ser oferecida a um preso", _pedidosDeFusao.Remove(namekPreso.Id));
			namekPreso.SalaPreso = false;

			// =====================================================================
			// O MAJIN
			// =====================================================================
			void Deitar()
			{
				CuspirTodosDoMajin(majin.Id, "");
				PorNaSala(majin, noMeio + new Vec2(0, 3 * T));
				PorNaSala(caido, noMeio + new Vec2(T, 3 * T));
				if (!caido.Ficha.KO) caido.Combate.Nocautear(600);
				_prontoAbsorver.Remove(majin.Id);
			}
			bool Selado() => DentroDeUmMajin(caido) && AbsorvidosPor(majin.Id).Any(a => a.Quem == caido.Id);

			Deitar();
			caido.SalaPreso = true;
			List<string> boca = Ouvir(() => UsarHabilidade(majin, "absorver"));
			ValCheca("**NENHUM MAJIN ABSORVE O PRESO**: nocauteado e ao lado, o preso continua na Sala -- ninguem selado, nenhuma imagem erguida, "
					+ "e o Majin le por que",
					caido.Ficha.KO && NaSala(caido) && AbsorvidosPor(majin.Id).Count == 0 && Disse(boca, "está preso na Sala do Tempo"), Ultimos(boca));

			PresoEntraEmQuemSaiDeTeste = true;
			try
			{
				Deitar();
				Ouvir(() => UsarHabilidade(majin, "absorver"));
				bool selou = Selado();
				string rede = PorQueNaoSaiDaqui(majin);
				EmpurrarASaida(majin);
				List<string> naPorta = Ouvir(TickDasPassagens);
				ValCheca("(defeito injetado: o preso e absorvido) a prova de cima REPROVA -- ele vai pro interior do Majin. E a rede de baixo "
						+ "segura: o Majin que o carrega nao passa pela porta nem por teleporte",
						selou && NaSala(majin) && rede.Contains(caido.Name, StringComparison.Ordinal) && Disse(naPorta, "a porta não se abre"),
						$"selou {selou}, Majin em {majin.Zone.Name} | {rede} | {Ultimos(naPorta)}");

				CargaPresaSaiDeTeste = true;
				try
				{
					EmpurrarASaida(majin);
					Ouvir(TickDasPassagens);
					bool majinSaiu = !NaSala(majin);
					CuspirTodosDoMajin(majin.Id, "");
					ValCheca("(os dois defeitos) A FUGA DE ANTES: o Majin sai pela porta com o preso dentro e o cospe do lado de fora",
							majinSaiu && !NaSala(caido) && !DentroDeUmMajin(caido) && caido.Zone.Hash == majin.Zone.Hash,
							$"Majin em {majin.Zone.Name}, preso em {caido.Zone.Name}");
				}
				finally { CargaPresaSaiDeTeste = false; }
			}
			finally { PresoEntraEmQuemSaiDeTeste = false; }

			// O QUE NAO MUDA: o caido SOLTO e absorvido e sai dentro do Majin; e o Majin PRESO continua absorvendo.
			Deitar();
			caido.SalaPreso = false;
			Ouvir(() => UsarHabilidade(majin, "absorver"));
			bool absorveuOSolto = Selado();
			EmpurrarASaida(majin);
			Ouvir(TickDasPassagens);
			ValCheca("O CAMINHO LEGITIMO: o caido que NAO esta preso e absorvido e sai da Sala dentro do Majin, como sempre",
					absorveuOSolto && !NaSala(majin), $"selou {absorveuOSolto}, Majin em {majin.Zone.Name}");

			Deitar();
			majin.SalaPreso = true;
			Ouvir(() => UsarHabilidade(majin, "absorver"));
			bool oPresoAbsorve = Selado();
			EmpurrarASaida(majin);
			Ouvir(TickDasPassagens);
			ValCheca("...e o Majin PRESO continua absorvendo (quem fica e ele, e quem ele cospe nao estava preso): absorve, e a porta o recusa "
					+ "por ele mesmo", oPresoAbsorve && NaSala(majin), $"selou {oPresoAbsorve}, Majin em {majin.Zone.Name}");
		}
		finally
		{
			PresoEntraEmQuemSaiDeTeste = CargaPresaSaiDeTeste = false;
			CuspirTodosDoMajin(majin.Id, "");
			foreach (ServerPlayer p in _players.Values.Where(p => p.Id >= IdBaseDeProjetil).ToList())
			{
				if (FusaoDe(p.Id) is { } f) Separar(f, "fim da bancada");
				if (_emCenaDeFusao.GetValueOrDefault(p.Id) is { } c) SoltarDaCenaDeFusao(c);
				_pedidosDeFusao.Remove(p.Id);
				_prontoAbsorver.Remove(p.Id);
				_avisoDePrisao.Remove(p.Id);
				_acabouDeAtravessar.Remove(p.Id);
				_naBocaDaPassagem.Remove(p.Id);
				if (p.Assinatura.Length > 0) _naSala.Remove(p.Assinatura);
				p.SalaPreso = false;
				p.Moving = false;
			}
		}
	}

	// =====================================================================
	// 20) a parede no planeta gerado
	// =====================================================================
	private void ValAParedeNoPlanetaGerado(ZoneKey terra)
	{
		GD.Print("[validacoes] -- 20) num PLANETA GERADO de verdade: montanha, arvore, rocha grande e bloco de base barram o soco, a piscada, "
				+ "o avanco das tecnicas, o puxao da fusao e o fim do embate de Zanzoken --");
		const int T = ZoneCollision.TileSize;
		Vec2 palco = CorredorLivre(24);
		Vec2 Pe(int cx, int cy) => new(cx * T + T / 2f, cy * T + T / 2f - MoveRules.FeetOffsetY);

		// ---- o mundo: sorteado, vivo, com rocha grande, arvore e montanha (a busca e a da `--rochasteste`)
		PlanetaNoEspaco? achado = null;
		TerrenoGerado? terreno = null;
		for (int i = 1; i <= 600 && achado == null; i++)
			foreach (PlanetaNoEspaco p in Espaco.PorPerto(SeedDoUniverso, new ChunkId(i * 37, i * 53)))
			{
				if (p.Premade || PlanetaMorto(p)) continue;
				MundoProcedural ficha = MundoProcedural.DaSeed(p.Seed, p.Nome);
				if (ficha.Lado > 400) continue;   // a bancada nao precisa de um mundo de um milhao de celulas
				TerrenoGerado t = GeradorDeTerreno.Gerar(ficha.Parametros());
				if (!t.Cobertura.Contains((byte)CoberturaDeTerreno.RochaGrande) || !t.Cobertura.Contains((byte)CoberturaDeTerreno.Arvore)
					|| !t.Chao.Contains((byte)ClasseDeTerreno.Montanha)) continue;
				achado = p;
				terreno = t;
				break;
			}
		if (achado is not { } mundo || terreno == null)
		{
			ValCheca("(montagem) ha um mundo sorteado com rocha grande, arvore e montanha no universo de producao", false);
			return;
		}

		ServerPlayer obreiro = ValGente("ObreiroDoPlanetaDaBancada", "Human", palco);
		obreiro.Ficha.Zeni = 1_000_000;
		bool pousou = PousarDeVerdade(obreiro, mundo);
		ZoneKey zona = Espaco.ZonaDe(mundo);
		ZonaGerada? viva = MundoDaZona(zona);
		ValCheca($"(montagem) o corpo POUSOU em {mundo.Nome} pelo funil de producao. O catalogo de mapas de arquivo NAO conhece esta zona; o "
				+ "funil de \"onde ha parede\" devolve o mapa que o servidor gerou",
				pousou && viva != null && obreiro.Zone.Equals(zona) && _catalogo?.Get(zona)?.Mapa == null && ReferenceEquals(MapaDaZonaOuCatalogo(zona), viva.Colisao),
				$"pousou {pousou}, mundo vivo {viva != null}");
		if (viva == null) return;
		ZoneCollision mapa = viva.Colisao;

		bool Rocha(int x, int y) => RochaGrande.Cobre(terreno.Largura, terreno.Altura, terreno.Cobertura, x, y);
		bool Arvore(int x, int y) => terreno.CoberturaEm(x, y) is CoberturaDeTerreno.Arvore or CoberturaDeTerreno.ArvoreAcento && !Rocha(x, y);
		bool Montanha(int x, int y) => terreno.ClasseEm(x, y) == ClasseDeTerreno.Montanha && !Rocha(x, y)
									   && terreno.CoberturaEm(x, y) is not (CoberturaDeTerreno.Arvore or CoberturaDeTerreno.ArvoreAcento);

		// UM CORREDOR NA HORIZONTAL: chao em (x0, y); de x0+1 a x0+k, so parede DO TIPO pedido; e mais dois de chao. O
		// atacante fica em x0, o alvo em x0+k+2, e a investida pararia em x0+k+1 -- do outro lado.
		(int X, int Y, int K)? Corredor(Func<int, int, bool> doTipo, int kMax)
		{
			for (int y = 8; y < mapa.Height - 8; y++)
				for (int x0 = 8; x0 < mapa.Width - kMax - 12; x0++)
				{
					if (!mapa.ServeDeChao(x0, y)) continue;
					int k = 0;
					while (k < kMax && mapa.BlockedCell(x0 + 1 + k, y) && doTipo(x0 + 1 + k, y)) k++;
					if (k == 0 || !mapa.ServeDeChao(x0 + k + 1, y) || !mapa.ServeDeChao(x0 + k + 2, y)) continue;
					return (x0, y, k);
				}
			return null;
		}
		// CAMPO ABERTO: quatro de chao em fila, sem nada -- o bloco de base sobe no segundo.
		(int X, int Y, int K)? Aberto()
		{
			for (int y = 8; y < mapa.Height - 8; y++)
				for (int x0 = 8; x0 < mapa.Width - 12; x0++)
					if (mapa.ServeDeChao(x0, y) && mapa.ServeDeChao(x0 + 1, y) && mapa.ServeDeChao(x0 + 2, y) && mapa.ServeDeChao(x0 + 3, y)
						&& !mapa.Indestrutivel(x0 + 1, y) && !mapa.NasceuDentro(x0 + 1, y))
						return (x0, y, 1);
			return null;
		}

		// ---- OS CINCO, contra UM corredor, com um par de corpos novo: devolve quem ficou do lado de ca.
		// (palco) BP DE 5: o golpe que fecha o embate racha o chao em volta do perdedor quando o poder do encontro
		// alcanca a resistencia padrao (`Empurrao.ResistenciaPadrao`, 20) -- e derrubaria a parede que a rodada
		// seguinte vai medir. Nenhum dos cinco gestos pergunta pelo BP.
		const double BpQueNaoRachaOChao = 5;
		int pares = 0;
		(bool Piscada, bool Soco, bool Avanco, bool Puxao, bool Embate, string Detalhe) Rodada((int X, int Y, int K) c)
		{
			Vec2 deCa = Pe(c.X, c.Y), parede = Pe(c.X + 1, c.Y), alem = Pe(c.X + c.K + 2, c.Y);
			float divisa = (c.X + 1) * T;                 // a face de ca da parede: X maior que isto e ter entrado nela
			int n = pares++;
			ServerPlayer a = ValGente($"SocadorDoPlaneta{n}DaBancada", "Human", palco, bp: BpQueNaoRachaOChao, skills: [PathDoZanzoken], kiMin: 5_000_000);
			ServerPlayer t = ValGente($"AlvoDoPlaneta{n}DaBancada", "Human", palco + new Vec2(T, 0), bp: BpQueNaoRachaOChao, kiMin: 5_000_000);
			ValMover(a, zona, deCa);
			ValMover(t, zona, alem);
			void DeVolta()
			{
				a.Pos = deCa;
				a.Facing = Facing.East;
				a.Ficha.Ki = a.Ficha.MaxKi;
				a.DashLivreEm = 0;
				a.AlvoId = t.Id;
				t.Pos = alem;
			}
			try
			{
				DeVolta();
				Zanzoken(a, parede);
				float piscada = a.Pos.X;

				DeVolta();
				Aproximar(a, longo: true);
				float soco = a.Pos.X;

				DeVolta();
				AvancarG3(a, t, (c.K + 3) * T);
				float avanco = a.Pos.X;

				DeVolta();
				AndarNoPuxao(a, t, 20 * T);
				float puxao = a.Pos.X;

				// O FIM DO EMBATE: o perdedor de costas pra parede, e o vencedor nasce ATRAS dele (`Terminar`).
				DeVolta();
				a.Facing = Facing.West;
				Comecar(t, a, NowMs());
				float embate = float.NaN;
				if (_emEmbate.GetValueOrDefault(t.Id) is { } e)
				{
					a.Pos = deCa;
					a.Facing = Facing.West;
					t.Pos = alem;
					e.PtsA = 3;
					e.Acaba = NowMs() - 1;
					TickDosEmbates();
					embate = t.Pos.X;
				}

				return (piscada < divisa, soco < divisa, avanco < divisa, puxao < divisa, embate < divisa,
						$"k={c.K}; piscada x={piscada:0}, soco x={soco:0}, avanco x={avanco:0}, puxao x={puxao:0}, vencedor x={embate:0} (a parede comeca em x={divisa:0})");
			}
			finally
			{
				foreach (ServerPlayer p in new[] { a, t })
				{
					_emEmbate.Remove(p.Id);
					_invisiveis.Remove(p.Id);
					p.AlvoId = 0;
					ValMover(p, terra, palco);
				}
				_embates.RemoveAll(x => x.A == a || x.B == a);
			}
		}
		static bool Todos((bool Piscada, bool Soco, bool Avanco, bool Puxao, bool Embate, string Detalhe) r) => r.Piscada && r.Soco && r.Avanco && r.Puxao && r.Embate;
		static bool Nenhum((bool Piscada, bool Soco, bool Avanco, bool Puxao, bool Embate, string Detalhe) r) => !r.Piscada && !r.Soco && !r.Avanco && !r.Puxao && !r.Embate;

		void Provar(string nome, (int X, int Y, int K)? corredor)
		{
			if (corredor is not { } c)
			{
				ValCheca($"(montagem) ha um corredor com {nome} entre dois chaos livres neste mundo", false);
				return;
			}
			(bool, bool, bool, bool, bool, string Detalhe) certo = Rodada(c);
			ValCheca($"**{nome} BARRA** ({c.K} celula(s), em ({c.X + 1},{c.Y})): a piscada do Zanzoken, a investida do soco, o avanco das "
					+ "tecnicas, o puxao da fusao e o vencedor do embate ficam todos do lado de ca", Todos(certo), certo.Detalhe);

			// A PAREDE CONTINUA DE PE entre uma rodada e outra: sem isto o defeito "atravessaria" um chao que a primeira abriu.
			bool dePe = Enumerable.Range(1, c.K).All(i => mapa.BlockedCell(c.X + i, c.Y));
			ParedeSoDeCatalogoDeTeste = true;
			try
			{
				(bool, bool, bool, bool, bool, string Detalhe) furado = Rodada(c);
				ValCheca($"(defeito injetado: parede so de mapa de arquivo) {nome}, de pe no mesmo lugar: os cinco ATRAVESSAM -- era o jogo em "
						+ "todo planeta gerado", dePe && Nenhum(furado), $"parede de pe {dePe}; {furado.Detalhe}");
			}
			finally { ParedeSoDeCatalogoDeTeste = false; }
		}

		// ---- O CAMPO ABERTO PRIMEIRO: sem ele as recusas de baixo nao provam nada. E onde o bloco de base vai subir.
		(int X, int Y, int K)? campo = Aberto();
		BlocoDef? tijolo = _blocos?.Todos.FirstOrDefault(b => b.Classe == ClasseDeBloco.Parede);
		if (campo is { } aberto && tijolo != null)
		{
			(bool, bool, bool, bool, bool, string Detalhe) livre = Rodada(aberto);
			ValCheca("O CAMINHO LEGITIMO: em campo aberto do mesmo planeta os cinco ANDAM -- a parede nao e uma recusa geral de quem esta em "
					+ "planeta gerado", Nenhum(livre), livre.Detalhe);

			// O PASSO CURTO DO SOCO so anda COM mapa: em planeta gerado ele nem acontecia.
			ServerPlayer perto = ValGente("PertoDoPlanetaDaBancada", "Human", palco, bp: 50_000, kiMin: 5_000_000);
			ServerPlayer vizinho = ValGente("VizinhoDoPlanetaDaBancada", "Human", palco + new Vec2(T, 0), bp: 50_000);
			Vec2 saida = Pe(aberto.X, aberto.Y);
			ValMover(perto, zona, saida);
			ValMover(vizinho, zona, saida + new Vec2(44, 0));
			perto.Facing = Facing.East;
			perto.AlvoId = vizinho.Id;
			Aproximar(perto, longo: false);
			float comMapa = perto.Pos.X - saida.X;
			perto.Pos = saida;
			ParedeSoDeCatalogoDeTeste = true;
			try { Aproximar(perto, longo: false); }
			finally { ParedeSoDeCatalogoDeTeste = false; }
			float semMapa = perto.Pos.X - saida.X;
			ValCheca("**O PASSO CURTO DO SOCO EXISTE EM PLANETA GERADO**: a 44 px do alvo (longe demais pro punho, perto demais pra investida) o "
					+ "corpo anda os 12 px que faltam -- com o defeito injetado ele fica plantado e soca o vazio",
					Math.Abs(comMapa - 12f) < 0.5f && Math.Abs(semMapa) < 0.01f, $"com o mapa andou {comMapa:0.#} px; sem, {semMapa:0.#}");
			perto.AlvoId = 0;
			ValMover(perto, terra, palco);
			ValMover(vizinho, terra, palco);

			// O BLOCO DE BASE, erguido pelo comando de producao no segundo quadro do campo.
			obreiro.Pos = Pe(aberto.X, aberto.Y + 1);
			List<string> obra = Ouvir(() => ComandoDeBloco(obreiro, Protocol.BlocoErguer, aberto.X + 1, aberto.Y, tijolo.Numero, ""));
			bool subiu = BlocoEm(zona, aberto.X + 1, aberto.Y) != null && mapa.BlockedCell(aberto.X + 1, aberto.Y);
			ValCheca("(montagem) uma parede de BASE sobe no meio do campo aberto, pelo comando de producao, e barra no mapa do planeta", subiu, Ultimos(obra));
			try { if (subiu) Provar("O BLOCO DE BASE", aberto); }
			finally { Ouvir(() => ComandoDeBloco(obreiro, Protocol.BlocoDesmanchar, aberto.X + 1, aberto.Y, 0, "")); }
		}
		else ValCheca("(montagem) ha quatro quadros de campo aberto neste mundo, e o catalogo de blocos tem parede", false, $"campo {campo != null}, tijolo {tijolo != null}");

		Provar("A MONTANHA", Corredor(Montanha, 10));
		Provar("A ARVORE", Corredor(Arvore, 3));
		(int X, int Y, int K)? pedra = Corredor(Rocha, 8);
		Provar("A ROCHA GRANDE", pedra);

		// ---- OS SALTOS DO EMBATE (`Cruzar`): o ponto de cada cruzamento e SORTEADO num anel de 6 tiles, entao a prova e
		// por contagem -- e em volta da ROCHA GRANDE, que o estrago do proprio embate nao derruba (`DerrubarCelula`).
		if (pedra is not { } p0) return;
		// O MEIO: tres de chao em fila (o par que nao acha lugar em dez tentativas fica ali, meio tile pra cada lado), e
		// o mais cercado de pedra que houver por perto.
		(int X, int Y) meio = default;
		int cerco = 0;
		for (int dy = -5; dy <= 5; dy++)
			for (int dx = -5; dx <= p0.K + 6; dx++)
			{
				int mx = p0.X + dx, my = p0.Y + dy;
				if (!mapa.ServeDeChao(mx - 1, my) || !mapa.ServeDeChao(mx, my) || !mapa.ServeDeChao(mx + 1, my)) continue;
				int n = 0;
				for (int ry = -6; ry <= 6; ry++)
					for (int rx = -6; rx <= 6; rx++)
						if (rx * rx + ry * ry is >= 7 and <= 36 && Rocha(mx + rx, my + ry) && mapa.BlockedCell(mx + rx, my + ry)) n++;
				if (n > cerco) { cerco = n; meio = (mx, my); }
			}
		ValCheca("(montagem) ha um chao de tres quadros com rocha grande no anel de 6 tiles em volta (o raio dos saltos do embate)",
				cerco >= 6, $"{cerco} celula(s) de pedra no anel de ({meio.X},{meio.Y})");
		if (cerco < 6) return;

		ServerPlayer um = ValGente("CruzaDoPlanetaDaBancada", "Human", palco, bp: BpQueNaoRachaOChao);
		ServerPlayer outro = ValGente("CruzaTambemDoPlanetaDaBancada", "Human", palco + new Vec2(T, 0), bp: BpQueNaoRachaOChao);
		Vec2 centro = Pe(meio.X, meio.Y);
		ValMover(um, zona, centro);
		ValMover(outro, zona, centro);
		var encontro = new Embate { A = um, B = outro };
		try
		{
			// QUANTOS CRUZAMENTOS PUSERAM ALGUEM ONDE O CORPO NAO CABE (`pisa`) -- ou com os pes no meio de uma PAREDE (`parede`).
			(int Pisa, int Parede) Contar(int vezes)
			{
				int pisa = 0, parede = 0;
				for (int i = 0; i < vezes; i++)
				{
					encontro.Meio = centro;            // sempre do mesmo meio: cada cruzamento e um sorteio novo do mesmo anel
					Cruzar(encontro);
					if (MoveRules.Occupied(mapa, um.Pos) || MoveRules.Occupied(mapa, outro.Pos)) pisa++;
					if (mapa.BlockedAt(um.Pos + new Vec2(0, MoveRules.FeetOffsetY)) || mapa.BlockedAt(outro.Pos + new Vec2(0, MoveRules.FeetOffsetY))) parede++;
				}
				return (pisa, parede);
			}
			const int Cruzamentos = 200;
			(int Pisa, int Parede) certo = Contar(Cruzamentos);
			(int Pisa, int Parede) furado;
			ParedeSoDeCatalogoDeTeste = true;
			try { furado = Contar(Cruzamentos); }
			finally { ParedeSoDeCatalogoDeTeste = false; }
			ValCheca($"**OS SALTOS DO EMBATE NAO CAEM DENTRO DE PAREDE** (`Cruzar`, {Cruzamentos} cruzamentos sorteados em volta da rocha grande): "
					+ "com o mapa do planeta, nenhum poe um corpo onde ele nao cabe; com o defeito injetado, o par atravessa a pedra",
					certo.Pisa == 0 && certo.Parede == 0 && furado.Parede > 0,
					$"com o mapa: {certo.Pisa} onde nao cabe, {certo.Parede} em parede; com o defeito: {furado.Parede} em parede (de {Cruzamentos})");
		}
		finally
		{
			foreach (ServerPlayer q in new[] { um, outro })
			{
				_invisiveis.Remove(q.Id);
				ValMover(q, terra, palco);
			}
		}
	}
}
