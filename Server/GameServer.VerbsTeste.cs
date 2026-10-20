using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Magic;
using Jandirus.Core.Social;
using Jandirus.Core.Stats;
using Jandirus.Core.Verbos;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DOS VERBS POR REQUISITO E DO RADAR DOURADO (`--verbsteste`).
///
/// O dono (2026-10-10), com a foto da aba World de um Saiyajin: *"tem um bug na terceira imagem q aparece em
/// world falando q te um sinal dourado 2 celulas dali mas nem tenho radar, e seria melhor dar uma limpa em
/// verbs q sao exclusivas de raças e deveriam so aparecer se o player tem os requisitos se nao fica mt
/// poluido, como esses verbs de clan dragao, split form (coisa q n tenho mas tem os verbs), verb de fusao
/// namek etc."*
///
/// HOSPEDANDO, assim que o host entra (o primeiro tique dos fatos com um jogador): o que ela mede do radar e
/// dos fatos e o que SAI NO FIO pra um jogador de verdade, e isso pede um corpo com `Peer`. Os personagens de
/// cada raca sao corpos forjados.
///
///   1. O RADAR DOURADO: sem o Dragon Radar a frase nem viaja; com ele, mede de onde o corpo ESTA NO CEU (o
///      planeta, em terra firme) e nao do pixel do mapa; e sai so quando muda.
///   2. A REGRA PURA: `Requisito` (E, Ou, o texto) e a tabela bem formada.
///   3. OS PERSONAGENS: humano novato, Saiyajin, Namekuseijin com e sem Cla do Dragao, com e sem Split Form,
///      com radar, Majin com e sem Buu Absorb, bio-androide, os dois androides, os cargos. O que cada um VE
///      sai impresso (e a tabela inteira tambem -- e ela que vai pro relatorio).
///   4. A TABELA CONTRA O SERVIDOR: cada verb com requisito e apertado, pelo funil de producao, por TODO
///      personagem que nao o cumpre -- e o servidor tem que recusar. E a prova de que esconder nao tirou
///      nada de ninguem e de que nenhum verb escondido estava sem validacao.
///   5. A ABSORCAO SEM PORTAO: o verb que nao conferia quem apertava (achado por esta bancada).
///   6. OS FATOS: cada bit nasce pela porta do sistema dele, liga o botao, e some quando o motivo acaba; e o
///      pacote sai so quando muda.
///   7. NINGUEM FICOU SEM PLATEIA: toda linha da tabela foi vista por pelo menos um personagem.
///
/// Os defeitos injetados: `Requisito.MostrarTudoDeTeste` (o botao que mostra tudo -- o menu de antes),
/// `SinalSemRadarDeTeste`, `RadarMedeDoChaoDeTeste`, `AbsorcaoSemPortaoDeTeste`, `FatosCongeladosDeTeste`.
///
///     Godot --headless --path . --host --rede 8190 --verbsteste --raca Human --conta bancada_verbs --nome Novato
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// A flag da bancada, lida AQUI e nao no `GameServer.cs` (o molde do `--radarteste`): quem a dispara e o tique
	/// dos fatos -- ver <see cref="DispararABancadaDosVerbs"/>.
	/// </summary>
	private static readonly bool VerbsDeTeste = Array.IndexOf(OS.GetCmdlineArgs(), "--verbsteste") >= 0;

	private bool _verbsTestou;

	/// <summary>
	/// A BANCADA RODA UMA VEZ, no primeiro tique dos fatos em que ha um jogador com tela (o host): o que ela mede
	/// do radar e dos fatos e o que SAI NO FIO, e isso pede um `Peer`.
	/// </summary>
	private void DispararABancadaDosVerbs()
	{
		if (_verbsTestou || Jogadores.FirstOrDefault() is not { } host) return;
		_verbsTestou = true;
		GD.Print("[server] BANCADA: verbs por requisito e radar dourado (--verbsteste)");
		RodarBancadaDosVerbs(host);
	}

	/// <summary>O id da obra que a bancada poe no chao (o mainframe da tecla E) -- fora da faixa das de jogo e das outras bancadas.</summary>
	private const int IdDoMainframeDaBancada = 986_190;

	/// <summary>
	/// O QUE UM CORPO TEM, DO JEITO QUE O SERVIDOR SABE -- a ponta de la do <see cref="IPosse"/>. So a
	/// bancada a usa: e a resposta CERTA com que a tabela e comparada (o cliente responde as mesmas
	/// perguntas com o que recebeu pelo fio).
	/// </summary>
	private sealed class PosseDoServidor(GameServer servidor, ServerPlayer pl) : IPosse
	{
		public string Raca => pl.Race;
		public string Classe => pl.Class;
		public bool SabeTecnica(string verbo) => servidor.SabeTecnica(pl, verbo);
		public bool SabeSkill(string typepath) => pl.Livro?.Sabe(typepath) == true;
		public bool TemItem(string id) => pl.Mochila.Quantos(id) > 0;
		public bool TemCargo(string chave) =>
			pl.Conta.Length > 0 && string.Equals(servidor.CargoDe(pl.Conta), chave, StringComparison.OrdinalIgnoreCase);
		public Fatos Fatos => servidor.FatosDe(pl);
		public Lugar Lugar => servidor.LugarDe(pl);
		public Jandirus.Core.Races.SkillRacial SkillsRaciais => SkillsDoCorpo(pl);
		public Jandirus.Core.Races.SkillRacial SkillsDoSangue => Jandirus.Core.Races.SkillsRaciais.DoSangue(pl.Ficha);
	}

	private IPosse PosseDe(ServerPlayer pl) => new PosseDoServidor(this, pl);

	/// <summary>Onde este corpo esta, nos tres tracos que a tabela pergunta. O cliente tira o mesmo da zona e do `S2C.Esferas`.</summary>
	private Lugar LugarDe(ServerPlayer pl)
	{
		Lugar l = Lugar.Nenhum;
		if (Espaco.EhPlaneta(pl.Zone)) l |= Lugar.Planeta;
		if (Espaco.EhEspaco(pl.Zone)) l |= Lugar.Espaco;
		foreach (SetDeEsferas s in _sets)
			if (s.Zona.Hash == pl.Zone.Hash) { l |= Lugar.ComEstatua; break; }
		return l;
	}

	/// <summary>A frase do radar que VIAJOU num pacote `S2C.SuperEsferas` -- lida com a mesma ordem de campos do cliente.</summary>
	private static string SinalNoPacote(NetDataWriter w)
	{
		var r = new NetDataReader(w.CopyData());
		r.GetByte();   // o opcode
		int n = r.GetByte();
		for (int i = 0; i < n; i++) { r.GetByte(); r.GetFloat(); r.GetFloat(); r.GetString(24); r.GetBool(); }
		r.GetByte();   // quantas sao minhas
		return r.GetString(120);
	}

	private void RodarBancadaDosVerbs(ServerPlayer pl)
	{
		GD.Print("\n[verbs] ================ QUEM VE QUE VERB + O RADAR DOURADO (dono, 2026-10-10) ================");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[verbs]   OK    {nome}"); }
			else { falhou++; GD.PrintErr($"[verbs]   FALHA {nome}   [{detalhe}]"); }
		}

		// ============================ O MUNDO VOLTA COMO ESTAVA ============================
		ZoneKey zonaDeAntes = pl.Zone;
		Vec2 posDeAntes = pl.Pos;
		int radarDeAntes = pl.Mochila.Quantos(CatalogoDeItens.Radar);
		var tronosDeAntes = new Dictionary<string, string>(_tronos);
		List<Dominio> dominiosDeAntes = [.. _dominios];
		var mestresDeAntes = new Dictionary<string, string>(_mestreDe);
		var nomesDeMestreDeAntes = new Dictionary<string, string>(_nomePorAssinatura);
		List<int> setsDeAntes = [.. _sets.Select(s => s.Id)];
		string desafianteDeAntes = _desafianteEsperando;
		bool emDueloDeAntes = _duelo.EmDuelo;
		(long Titulo, long Ultimo, int Adiamentos, long Semana) dueloDeAntes =
			(_duelo.TituloDesde, _duelo.UltimoDuelo, _duelo.Adiamentos, _duelo.SemanaDosAdiamentos);
		var vistas = new HashSet<string>(StringComparer.Ordinal);   // as linhas que ALGUEM viu (familia 7)

		const int T = ZoneCollision.TileSize;
		IReadOnlyList<VerbDeclarado> tabela = RequisitosDosVerbos.Todos;

		try
		{
			_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
			ZoneKey terra = ZonaDaBancadaDeProjetil;
			Vec2 estacionamento = CorredorLivre(24);
			Vec2 palco = CorredorLivre(24);

			// UM PERSONAGEM DE BANCADA: corpo forjado, com conta propria (a assinatura sai dela) e a raca
			// escrita nos dois lugares que o servidor le.
			int quantos = 0;
			ServerPlayer Gente(string nome, string raca, string classe = "Normal", string[]? skills = null,
							   (string Path, int Nivel)[]? degraus = null)
			{
				ServerPlayer c = ForjarComSkills(nome, estacionamento + new Vec2(quantos % 20 * T, 0), 5_000, skills, degraus);
				c.Conta = $"bancada_verbs_{(char)('a' + quantos / 26)}{(char)('a' + quantos % 26)}";
				c.Race = raca;
				c.Ficha.Race = raca;
				c.Ficha.ParentRace = raca;
				c.Class = classe;
				c.Ficha.Class = classe;
				quantos++;
				return c;
			}

			// O PARCEIRO: quem fica na frente de quem aperta (a danca e a absorcao perguntam "ha alguem ai?"
			// ANTES de perguntar a raca -- sem ninguem na frente a recusa seria a do alvo, nao a do requisito).
			ServerPlayer parceiro = Gente("Parceiro", "Human");
			parceiro.Pos = palco + new Vec2(T * 0.9f, 0);
			parceiro.Facing = Facing.West;

			List<string> Apertar(ServerPlayer quem, VerbDeclarado linha)
			{
				Vec2 volta = quem.Pos;
				ZoneKey zonaDele = quem.Zone;
				if (quem.Zone.Hash == parceiro.Zone.Hash)
				{
					quem.Pos = palco;
					quem.Facing = Facing.East;
					quem.AlvoId = parceiro.Id;
				}
				try
				{
					return linha.EhHabilidade
						? Ouvir(() => UsarHabilidade(quem, linha.Comando))
						: Ouvir(() => Verbo(quem, linha.Comando, linha.Arg));
				}
				finally { quem.Pos = volta; quem.Zone = zonaDele; quem.AlvoId = 0; }
			}

			bool Recusou(List<string> falas, VerbDeclarado linha) =>
				linha.Recusa.Split('|').Any(trecho => Disse(falas, trecho));

			bool Ve(ServerPlayer quem, VerbDeclarado linha)
			{
				bool ve = linha.Requisito.Atende(PosseDe(quem));
				if (ve && !Requisito.MostrarTudoDeTeste) vistas.Add(linha.Chave);
				return ve;
			}

			bool VeChave(ServerPlayer quem, string chave) => Ve(quem, RequisitosDosVerbos.Linha(chave)!);

			// =====================================================================
			// 1) O RADAR DOURADO
			// =====================================================================
			GD.Print("[verbs] -- 1) o radar dourado: so com o Dragon Radar, e medido de onde o corpo esta NO CEU --");
			ZoneKey espaco = ZonaDoEspaco;
			Vec2 naSuper = OndeEstaASuper(1);

			ServerPlayer radarista = Gente("Radarista", "Saiyan");
			radarista.Zone = espaco;
			radarista.Pos = naSuper;
			Checa("(montagem) o Saiyajin esta na MESMA celula de uma Super Esfera, no espaco, e NAO tem radar",
				  SuperEsferas.CelulasEntre(radarista.Pos, OndeEstaASuper(1)) == 0
				  && radarista.Mochila.Quantos(CatalogoDeItens.Radar) == 0);

			string semRadar = SinalNoPacote(MontarPacoteDeSupers(radarista));
			Checa("**SEM O DRAGON RADAR A FRASE NEM VIAJA**: o pacote das Super sai com o sinal VAZIO, a um passo da esfera",
				  semRadar.Length == 0 && SinalDouradoDe(radarista).Length == 0, $"viajou '{semRadar}'");

			SinalSemRadarDeTeste = true;
			try
			{
				string vazou = SinalNoPacote(MontarPacoteDeSupers(radarista));
				Checa("(defeito injetado: o sinal sai pra quem nao tem radar) a prova de cima REPROVA -- e a frase que o dono leu",
					  vazou.StartsWith("RADAR:", StringComparison.Ordinal), $"viajou '{vazou}'");
			}
			finally { SinalSemRadarDeTeste = false; }

			radarista.Mochila.Guardar(CatalogoDeItens.Radar);
			string comRadar = SinalNoPacote(MontarPacoteDeSupers(radarista));
			Checa("...e COM o radar na mochila ela viaja, dizendo que a esfera esta NESTA celula",
				  comRadar.StartsWith("RADAR:", StringComparison.Ordinal) && comRadar.Contains("NESTA", StringComparison.Ordinal),
				  $"viajou '{comRadar}'");

			// ---- de onde se mede: em terra firme, do PLANETA ----
			PlanetaNoEspaco vegeta = Espaco.PreFeitos().First(p => p.Nome == "Vegeta");
			int maisPertoDeVegeta = Enumerable.Range(1, SuperEsferas.Total)
				.Min(n => SuperEsferas.CelulasEntre(vegeta.Pos, OndeEstaASuper(n)));
			Checa($"(montagem) Vegeta fica longe de TODAS as sete (a mais proxima a {maisPertoDeVegeta} celulas; o radar alcanca "
				  + $"{SuperEsferas.CelulasDeSinal}) -- elas nascem a ate {SuperEsferas.CelulasNoMaximo} celulas da Terra",
				  maisPertoDeVegeta > SuperEsferas.CelulasDeSinal);

			// O corpo esta em Vegeta, e o PIXEL dele no mapa de Vegeta e -- por construcao -- o mesmo numero da
			// posicao de uma Super Esfera no espaco. Quem medir do pixel jura que esta em cima dela.
			radarista.Zone = ZoneKey.Premade("Vegeta");
			radarista.Pos = OndeEstaASuper(1);
			string emVegeta = SinalDouradoDe(radarista);
			Checa("**EM TERRA FIRME O RADAR MEDE DE ONDE O PLANETA ESTA**: com radar, em Vegeta, nao ha sinal nenhum",
				  emVegeta.Length == 0, $"'{emVegeta}'");

			RadarMedeDoChaoDeTeste = true;
			try
			{
				string doChao = SinalDouradoDe(radarista);
				Checa("(defeito injetado: mede do pixel do mapa) a prova de cima REPROVA -- Vegeta vira a celula da esfera",
					  doChao.Contains("NESTA", StringComparison.Ordinal), $"'{doChao}'");
			}
			finally { RadarMedeDoChaoDeTeste = false; }

			var comSinal = new List<string>();
			bool terraFirmeBate = true;
			foreach (PlanetaNoEspaco mundo in Espaco.PreFeitos())
			{
				bool devia = Enumerable.Range(1, SuperEsferas.Total)
					.Any(n => SuperEsferas.CelulasEntre(mundo.Pos, OndeEstaASuper(n)) <= SuperEsferas.CelulasDeSinal);
				radarista.Zone = ZoneKey.Premade(mundo.Nome);
				radarista.Pos = new Vec2(250 * T, 250 * T);
				bool tem = SinalDouradoDe(radarista).Length > 0;
				terraFirmeBate &= tem == devia;
				if (tem) comSinal.Add(mundo.Nome);
			}
			Checa("...e em cada um dos sete mundos do mapa ha sinal SE E SO SE uma Super esta a ate 2 celulas do planeta "
				  + $"(nesta semente: {(comSinal.Count == 0 ? "nenhum" : string.Join(", ", comSinal))})", terraFirmeBate);

			radarista.Zone = ZoneKey.Premade("Heaven");
			Checa("...e onde o corpo nao tem lugar no ceu (o Paraiso) nao ha leitura, com radar e tudo",
				  CorpoDaZona(radarista.Zone) == null && SinalDouradoDe(radarista).Length == 0);

			// ---- o pacote sai so quando a frase MUDA (o tique de 1 Hz) ----
			pl.Mochila.Tirar(CatalogoDeItens.Radar, 99);
			pl.Zone = espaco;
			pl.Pos = naSuper;
			_sinalMandado[pl.Id] = "";
			List<(int Quem, string Sinal)> fio = [];
			EscutaDoSinalDourado = fio;
			try
			{
				ConferirOsRadares();
				Checa("O TIQUE: sem radar e sem nada mudar, nenhum pacote sai", fio.Count == 0, $"{fio.Count} pacote(s)");

				pl.Mochila.Guardar(CatalogoDeItens.Radar);
				ConferirOsRadares();
				Checa("...o radar ENTRA na mochila e, no tique seguinte, UM pacote sai com a frase",
					  fio.Count == 1 && fio[0].Quem == pl.Id && fio[0].Sinal.StartsWith("RADAR:", StringComparison.Ordinal),
					  $"{fio.Count} pacote(s): {string.Join(" | ", fio.Select(f => f.Sinal))}");

				ConferirOsRadares();
				ConferirOsRadares();
				Checa("...e parado, com a frase igual, ele NAO sai de novo", fio.Count == 1, $"{fio.Count} pacote(s)");

				pl.Mochila.Tirar(CatalogoDeItens.Radar, 99);
				ConferirOsRadares();
				Checa("...o radar SAI da mochila e o pacote seguinte leva a frase VAZIA (a linha some da tela de quem o perdeu)",
					  fio.Count == 2 && fio[1].Sinal.Length == 0, $"{fio.Count} pacote(s)");
			}
			finally { EscutaDoSinalDourado = null; }
			pl.Zone = zonaDeAntes;
			pl.Pos = posDeAntes;

			// =====================================================================
			// 2) A REGRA PURA
			// =====================================================================
			GD.Print("[verbs] -- 2) a regra pura: o requisito e a tabela --");
			var humano = new PosseEscrita { Raca = "Human", Classe = "Normal", Lugar = Lugar.Planeta };
			var namekDoCla = new PosseEscrita { Raca = "Namekian", Classe = "Dragon clan", Lugar = Lugar.Planeta };
			var namekGuerreiro = new PosseEscrita { Raca = "Namekian", Classe = "Warrior clan", Lugar = Lugar.Planeta };
			Checa("`E` exige as duas metades: o Cla do Dragao e Namekuseijin E da classe -- o guerreiro de Namek nao e, o humano nao e",
				  RequisitosDosVerbos.ClaDoDragao.Atende(namekDoCla) && !RequisitosDosVerbos.ClaDoDragao.Atende(namekGuerreiro)
				  && !RequisitosDosVerbos.ClaDoDragao.Atende(humano));
			Requisito ou = Requisito.Raca("Namekian").Ou(Requisito.Item(CatalogoDeItens.Radar));
			var humanoComRadar = new PosseEscrita { Raca = "Human" };
			humanoComRadar.Itens.Add(CatalogoDeItens.Radar);
			Checa("`Ou` aceita qualquer das duas: o Namekuseijin sem radar e o humano com radar passam, o humano sem nada nao",
				  ou.Atende(namekGuerreiro) && ou.Atende(humanoComRadar) && !ou.Atende(humano));
			Checa("o texto do requisito se le (e ele que vai pra tabela do relatorio), e um `ou` dentro de um `e` ganha parenteses",
				  RequisitosDosVerbos.ClaDoDragao.Texto == "raça Namekian e classe Dragon clan"
				  && ou.E(Requisito.Classe("X")).Texto == "(raça Namekian ou item Dragon_Radar) e classe X",
				  $"'{RequisitosDosVerbos.ClaDoDragao.Texto}' | '{ou.E(Requisito.Classe("X")).Texto}'");
			Checa("**VERB QUE A TABELA NAO CONHECE NAO TEM REQUISITO**: aparece pra todo mundo (o \"Minerar\" de outro sistema nao precisa saber da tabela)",
				  RequisitosDosVerbos.DoVerbo("minerar").Livre && RequisitosDosVerbos.DoVerbo("minerar").Atende(humano)
				  && RequisitosDosVerbos.De("hab:nadar").Livre && RequisitosDosVerbos.De("").Livre);
			Checa($"a tabela tem {tabela.Count} linhas, sem chave repetida, cada uma com rotulo, aba e requisito de verdade",
				  tabela.Count >= 50 && tabela.Select(v => v.Chave).Distinct().Count() == tabela.Count
				  && tabela.All(v => v.Rotulo.Length > 0 && v.Onde.Length > 0 && !v.Requisito.Livre));
			Checa("...e a chave de um botao de verbo carrega o argumento quando ele muda o gesto (`db_estatua supremo`)",
				  RequisitosDosVerbos.Linha("verbo:db_estatua supremo") is { Comando: "db_estatua", Arg: "supremo", EhVerbo: true }
				  && RequisitosDosVerbos.Linha("hab:fus_namek") is { Comando: "fus_namek", Arg: "", EhHabilidade: true });

			GD.Print("[verbs]   A TABELA (verb -> requisito -> onde aparece):");
			foreach (VerbDeclarado v in tabela)
				GD.Print($"[verbs]     TABELA | {v.Rotulo} | {v.Requisito.Texto} | {v.Onde} | {v.Chave}");

			// =====================================================================
			// 3) OS PERSONAGENS
			// =====================================================================
			GD.Print("[verbs] -- 3) os personagens: quem ve o que --");
			ServerPlayer novato = Gente("HumanoNovato", "Human");
			ServerPlayer saiyajin = Gente("SaiyajinNovato", "Saiyan", "Low-Class");
			// (2026-10-13) A FUSAO NAMEKUSEIJIN PEDE A SKILL DELA (`Fusion- Namek Style`): estes dois a aprenderam. O
			// Namekuseijin que NAO a aprendeu e medido mais abaixo, na cena da absorcao (o `NamekB`).
			ServerPlayer guerreiro = Gente("NamekGuerreiro", "Namekian", "Warrior clan", skills: [RequisitosDosVerbos.SkillDaFusaoNamek]);
			ServerPlayer doCla = Gente("NamekDoCla", "Namekian", "Dragon clan",   // (a estatua tambem pede a skill dela, 2026-10-13)
									   skills: [Jandirus.Core.Skills.AcendedoresDoPort.EstatuaDoDragao, RequisitosDosVerbos.SkillDaFusaoNamek]);
			ServerPlayer divisor = Gente("HumanoComSplitForm", "Human", skills: ["/datum/skill/general/splitform"]);
			ServerPlayer comRadarNaMao = Gente("HumanoComRadar", "Human");
			comRadarNaMao.Mochila.Guardar(CatalogoDeItens.Radar);
			ServerPlayer dancarino = Gente("HumanoQueDanca", "Human", skills: ["/datum/skill/rank/Fusion_Dance"]);
			ServerPlayer majin = Gente("MajinComBuuAbsorb", "Majin", skills: [Jandirus.Core.Combat.AbsorcaoMajin.Skill]);
			ServerPlayer majinCru = Gente("MajinSemASkill", "Majin");
			ServerPlayer bio = Gente("BioAndroide", Jandirus.Core.Races.BioAndroids.Raca);
			ServerPlayer bioSaiyajin = Gente("BioDeDnaSaiyajin", Jandirus.Core.Races.BioAndroids.Raca);
			bioSaiyajin.Ficha.canSSJ = true;   // o `canSSJ` que o DNA Saiyajin da ao bio de laboratorio: a escada sem a raca
			// (2026-10-17) O BIO QUE SAIU DE UM TANQUE veste a larva quando quer; e a LARVA se enterra (o adendo do dono, `GameServer.Larva.cs`).
			ServerPlayer bioDeLab = Gente("BioDeLaboratorio", Jandirus.Core.Races.BioAndroids.Raca);
			bioDeLab.Ficha.bio_lab_born = true;
			bioDeLab.Ficha.bio_stage = Jandirus.Core.Races.BioAndroids.Imperfeito;
			// (2026-10-17) O SANGUE QUE DA AS SKILLS DO DNA (`Races.SkillsRaciais`): quatro dos cinco kits de raca -- o quinto, o Namekuseijin, e do
			// casulo da secao 6, e assim toda linha da escolha tem quem a veja.
			bioDeLab.Ficha.bio_racas_dna = ["Saiyan", "Human", "Halfbreed", "Icer"];
			ServerPlayer larvaDeBio = Gente("LarvaDeBio", Jandirus.Core.Races.BioAndroids.Raca);
			larvaDeBio.Ficha.bio_lab_born = true;
			larvaDeBio.Ficha.bio_stage = Jandirus.Core.Races.BioAndroids.Larva;
			ServerPlayer coletor = Gente("AndroideDeAbsorcao", "Android");
			coletor.Ficha.AndroideAbsorcao = true;
			ServerPlayer infinito = Gente("AndroideInfinito", "Android");
			infinito.Ficha.AndroideInfinito = true;
			// (2026-10-18) O NEO TUFFLE: o Tsufurujin que passou pela maquina -- ve o laboratorio dele e o "Possuir" (`GameServer.NeoTuffle.cs`).
			ServerPlayer neoTuffle = Gente("NeoTuffle", "Tsujin");
			neoTuffle.Ficha.NeoTuffle = true;
			ServerPlayer divino = Gente("HumanoComGodKi", "Human");
			divino.Ficha.godki = new GodKiState { awakened = true, mastery = 10 };
			// (Write_Teachings vem da COMPRA da Ki Unlocked; Focus_Skill, do nivel 10 da Basic Ki Awareness)
			const string consciencia = "/datum/skill/mind/Basic_Ki_Awareness";
			ServerPlayer mentalista = Gente("HumanoDaMente", "Human", skills: ["/datum/skill/mind/Ki_Unlocked", consciencia],
											degraus: [(consciencia, 10)]);
			// (o `Power_Control` vem do DEGRAU 5 de Basic Ki Control -- e ele que abre a barra de poder e o relaxar)
			ServerPlayer controlado = Gente("HumanoComControleDeKi", "Human", skills: ["/datum/skill/mind/Ki_Unlocked", SkillKiControl],
											degraus: [(SkillKiControl, 5)]);
			// (v0.4.0) OS SANGUES COM ATIVA RACIAL (o humano e o Namekuseijin ja estao no elenco): o Lendario, o Primal, o Lendario Primal,
			// o Frost Demon e o mutante -- as treze linhas da aba Skills tem quem as veja.
			ServerPlayer lendario = Gente("SaiyajinLendario", "Saiyan", "Legendary");
			ServerPlayer primal = Gente("SaiyajinPrimal", "Saiyan", "Normal Primal Saiyan");
			ServerPlayer lendarioPrimal = Gente("LendarioPrimal", "Saiyan", "Legendary Primal Saiyan");
			ServerPlayer frost = Gente("FrostDemon", "Icer", "Frost Demon");
			ServerPlayer mutante = Gente("FrostMutante", "Icer", "Mutant Frost Demon");
			ServerPlayer noEspaco = Gente("HumanoNoEspaco", "Human");
			noEspaco.Zone = espaco;
			noEspaco.Pos = naSuper;

			// OS CARGOS, pelo livro de tronos (por CONTA, como o servidor confere).
			ServerPlayer anciao = Gente("GrandeAnciao", "Namekian", "Dragon clan");
			ServerPlayer rei = Gente("ReiDeVegeta", "Saiyan", "Elite");
			ServerPlayer presidente = Gente("Presidente", "Human");
			ServerPlayer guardiao = Gente("Guardiao", "Namekian", "Dragon clan", skills: ["/datum/skill/rank/Permission"]);
			ServerPlayer korin = Gente("MestreKorin", "Human", skills: ["/datum/skill/rank/Permission"]);
			// (2026-10-19) O KING YEMMA: a travessia pro Outro Mundo e do trono dele (`GameServer.TravessiaDoJuiz.cs`).
			ServerPlayer juiz = Gente("KingYemma", "Human");
			_tronos["elder"] = anciao.Conta;
			_tronos["kov"] = rei.Conta;
			_tronos["president"] = presidente.Conta;
			_tronos["guardian"] = guardiao.Conta;
			_tronos["korin"] = korin.Conta;
			_tronos[TravessiaDoJuiz.Cargo] = juiz.Conta;
			// O TRONO DO DEUS TEM DONO (fora do elenco): com ele vago o desafio recusaria por "nao ha Deus", e o
			// que a tabela promete ver recusado e a FALTA DO GOD KI.
			ServerPlayer deus = Gente("DeusDaDestruicao", "Human");
			_tronos[CargoEmDisputa] = deus.Conta;

			(string Nome, ServerPlayer Corpo)[] elenco =
			[
				("humano novato", novato), ("Saiyajin novato", saiyajin), ("Namekuseijin guerreiro", guerreiro),
				("Saiyajin Lendário", lendario), ("Saiyajin Primal", primal), ("Lendário Primal", lendarioPrimal),
				("Frost Demon", frost), ("Frost Demon mutante", mutante),
				("Namekuseijin do Clã do Dragão", doCla), ("humano com Split Form", divisor),
				("humano com Dragon Radar", comRadarNaMao), ("humano que sabe a Dança", dancarino),
				("Majin com Buu Absorb", majin), ("Majin sem a skill", majinCru), ("bio-androide", bio),
				("bio-androide de DNA Saiyajin", bioSaiyajin), ("androide de absorção", coletor), ("androide de energia infinita", infinito),
				("bio-androide de laboratório", bioDeLab), ("larva de bio-androide", larvaDeBio),
				("humano com God Ki", divino), ("humano com o estudo da Mente", mentalista), ("humano com controle de ki", controlado),
				("humano no espaço", noEspaco), ("Grande Ancião de Namek", anciao), ("Rei de Vegeta", rei),
				("Presidente da Terra", presidente), ("Guardião da Terra", guardiao), ("Mestre Korin", korin),
				("Neo Tuffle", neoTuffle), ("King Yemma", juiz),
			];

			Checa("(montagem) a Terra desta bancada NAO tem Estatua do Dragao, e os personagens estao nela (menos o do espaco)",
				  !_sets.Any(s => s.Zona.Hash == terra.Hash) && novato.Zone.Hash == terra.Hash
				  && LugarDe(novato) == Lugar.Planeta && LugarDe(noEspaco) == Lugar.Espaco);

			foreach ((string nome, ServerPlayer corpo) in elenco)
			{
				List<string> ve = [.. tabela.Where(v => Ve(corpo, v)).Select(v => v.Rotulo)];
				GD.Print($"[verbs]     VE | {nome} | {ve.Count} de {tabela.Count} | {string.Join("; ", ve)}");
			}

			// ---- o caso do dono, palavra por palavra ----
			// (2026-10-17) A LARVA (o adendo do dono): o bio de LABORATORIO ve a forma de larva, so a LARVA ve o enterrar, e o bio que nao saiu de
			// um tanque nao ve nenhum dos dois.
			Checa("A LARVA: o bio de LABORATORIO ve a \"Forma de larva\", so a LARVA ve o \"Enterrar-se\", e o bio que nao saiu de um tanque nao ve "
				  + "nenhum dos dois",
				  VeChave(bioDeLab, "hab:larva") && !VeChave(bioDeLab, "hab:enterrar") && VeChave(larvaDeBio, "hab:enterrar")
				  && VeChave(larvaDeBio, "hab:larva") && !VeChave(bio, "hab:larva") && !VeChave(bio, "hab:enterrar")
				  && (FatosDe(bioDeLab) & Fatos.BioDeLaboratorio) != 0 && (FatosDe(larvaDeBio) & Fatos.EmLarva) != 0
				  && (FatosDe(bioDeLab) & Fatos.EmLarva) == 0);
			// (2026-10-17) AS SKILLS DO DNA (o adendo do dono): o bio de laboratorio de sangue Saiyajin + Humano + meio-Saiyajin + Frost Demon ve os
			// DEZESSETE botoes da escolha (as skills dos quatro kits), e ninguem mais do elenco ve um.
			string[] doDna = [.. Jandirus.Core.Races.SkillsRaciais.Todas.Select(s => RequisitosDosVerbos.PrefixoDeHabilidade + s.IdDaEscolha)];
			Checa("AS SKILLS DO DNA: o bio de laboratorio de sangue Saiyajin + Humano + meio-Saiyajin + Frost Demon ve os DEZESSETE botoes da escolha "
				  + "(os dos quatro kits) e nenhum do Namekuseijin, que ele nao carrega -- e ninguem mais do elenco ve um",
				  doDna.Count(ch => VeChave(bioDeLab, ch)) == 17 && (FatosDe(bioDeLab) & Fatos.SkillDoDnaPorEscolher) != 0
				  && !VeChave(bioDeLab, RequisitosDosVerbos.PrefixoDeHabilidade + Jandirus.Core.Races.SkillsRaciais.PrefixoDaEscolha
											   + Jandirus.Core.Races.SkillRacial.Resiliencia)
				  && elenco.Where(e => e.Corpo != bioDeLab).All(e => doDna.All(ch => !VeChave(e.Corpo, ch))),
				  string.Join(", ", doDna.Where(ch => VeChave(bioDeLab, ch))));

			string[] doClaDoDragao = ["verbo:db_estatua", "verbo:db_estatua supremo", "verbo:db_reviver"];
			string[] doSplitForm = [.. tabela.Where(v => v.Chave.StartsWith("verbo:splitform_", StringComparison.Ordinal)).Select(v => v.Chave)];
			Checa("**O SAIYAJIN DA FOTO**: sem radar ele nao ve a linha do RADAR nem o botao; nao e do Cla do Dragao e nao ve "
				  + "Erguer Estatua, Erguer + SUPREMO nem Despertar as esferas",
				  !VeChave(saiyajin, RequisitosDosVerbos.LinhaDoRadar) && !VeChave(saiyajin, "verbo:db_radar")
				  && doClaDoDragao.All(c => !VeChave(saiyajin, c)));
			Checa("...num mundo SEM estatua ele tambem nao ve Ver/Pegar/Largar/INVOCAR (nao ha esfera nenhuma pra pegar ali)",
				  new[] { "verbo:db_ver", "verbo:db_pegar", "verbo:db_largar", "verbo:db_invocar" }.All(c => !VeChave(saiyajin, c)));
			// (as duas linhas do cartao "O pedido ao dragão" -- a segunda mora na janela de desejos, de onde o servidor a manda)
			Checa("...nem \"Derrubar a estátua\" (nao ha o que derrubar) nem \"Pedidos por invocação\" -- e o Namekuseijin do Cla, que ainda nao ergueu nada, tambem nao",
				  !VeChave(saiyajin, "verbo:db_menu foco derrubar") && !VeChave(saiyajin, "verbo:db_refazer")
				  && !VeChave(doCla, "verbo:db_menu foco derrubar") && !VeChave(doCla, "verbo:db_refazer"));
			Checa($"...os {doSplitForm.Length} verbs do Split Form nao aparecem pra quem nao o tem, e a fusao Namekuseijin nao aparece pra um Saiyajin",
				  doSplitForm.Length == 5 && doSplitForm.All(c => !VeChave(saiyajin, c) && !VeChave(novato, c))
				  && !VeChave(saiyajin, "hab:fus_namek") && !VeChave(novato, "hab:fus_namek"));
			Checa("...e o que E dele continua la: o Saiyajin ve Voltar ao normal, os graus do SSJ e a linha de sucessao de Vegeta",
				  VeChave(saiyajin, "hab:oozaru") && VeChave(saiyajin, "verbo:graus") && VeChave(saiyajin, "verbo:cargo_herdeiros"));

			Checa("O NAMEKUSEIJIN DO CLA DO DRAGAO ve Erguer Estatua e Erguer + SUPREMO (num mundo sem estatua), a fusao Namekuseijin e Regenerar",
				  VeChave(doCla, "verbo:db_estatua") && VeChave(doCla, "verbo:db_estatua supremo")
				  && VeChave(doCla, "hab:fus_namek") && VeChave(doCla, "hab:regenerar"));
			Checa("...e o Namekuseijin de OUTRA classe ve a fusao (aprendeu a skill dela) e o Regenerar (e da raca), mas NAO os verbs do Cla",
				  VeChave(guerreiro, "hab:fus_namek") && VeChave(guerreiro, "hab:regenerar") && doClaDoDragao.All(c => !VeChave(guerreiro, c)));
			Checa("QUEM TEM SPLIT FORM ve as cinco ordens das copias -- e so ele, no elenco inteiro",
				  doSplitForm.All(c => VeChave(divisor, c))
				  && elenco.Where(e => e.Corpo != divisor).All(e => doSplitForm.All(c => !VeChave(e.Corpo, c))));
			Checa("QUEM TEM O DRAGON RADAR ve o botao Radar e a linha do sinal -- e so ele",
				  VeChave(comRadarNaMao, "verbo:db_radar") && VeChave(comRadarNaMao, RequisitosDosVerbos.LinhaDoRadar)
				  && elenco.Where(e => e.Corpo != comRadarNaMao).All(e => !VeChave(e.Corpo, "verbo:db_radar")));
			Checa("A DANCA DA FUSAO so pra quem a aprendeu", VeChave(dancarino, "hab:fus_danca") && !VeChave(novato, "hab:fus_danca"));
			Checa("ABSORVER: o bio-androide pela raca, o Majin SO depois de comprar Buu Absorb; humano, Saiyajin e androide nao",
				  VeChave(bio, "hab:absorver") && VeChave(majin, "hab:absorver") && !VeChave(majinCru, "hab:absorver")
				  && !VeChave(novato, "hab:absorver") && !VeChave(saiyajin, "hab:absorver") && !VeChave(coletor, "hab:absorver")
				  && VeChave(majin, "hab:expelir") && !VeChave(bio, "hab:expelir"));
			Checa("OS GRAUS DO SUPER SAIYAJIN sao de quem TEM a escada: o Saiyajin pela raca e o bio-androide de DNA Saiyajin pelo `canSSJ` "
				  + "-- o bio comum, o Namekuseijin e o humano nao",
				  VeChave(saiyajin, "verbo:graus") && VeChave(bioSaiyajin, "verbo:graus") && Tem(bioSaiyajin, Fatos.EscadaSuperSaiyajin)
				  && !VeChave(bio, "verbo:graus") && !VeChave(guerreiro, "verbo:graus") && !VeChave(novato, "verbo:graus"));
			Checa("OS COLETORES so pro androide de ABSORCAO -- o de energia infinita, que e da mesma raca, nao os ve",
				  VeChave(coletor, "hab:absorver_ki") && !VeChave(infinito, "hab:absorver_ki") && !VeChave(novato, "hab:absorver_ki"));
			Checa("DESAFIAR O DEUS DA DESTRUICAO so com o God Ki desperto", VeChave(divino, "verbo:cargo_desafiar") && !VeChave(novato, "verbo:cargo_desafiar"));
			// O KIT DO TITULO (`god_apply_powers`, `GodOfDestruction.dm:119-133`): os verbs sao de quem carrega o cargo AGORA.
			string[] doKitDoDeus =
			[
				Jandirus.Core.Ranks.KitDoDeus.IdDaFury, Jandirus.Core.Ranks.KitDoDeus.VerbHakai, Jandirus.Core.Ranks.KitDoDeus.VerbFlick,
				Jandirus.Core.Ranks.KitDoDeus.VerbFuria, Jandirus.Core.Ranks.KitDoDeus.VerbEsfera, Jandirus.Core.Ranks.KitDoDeus.VerbAnular,
			];
			Checa("O KIT DO DEUS DA DESTRUICAO (Fury, Hakai, Power Flick, os dois tiros, a anulacao) so pra quem carrega o titulo",
				  doKitDoDeus.All(id => VeChave(deus, RequisitosDosVerbos.PrefixoDeHabilidade + id)
										&& !VeChave(novato, RequisitosDosVerbos.PrefixoDeHabilidade + id)
										&& !VeChave(divino, RequisitosDosVerbos.PrefixoDeHabilidade + id)));
			string[] emprestados =
			[
				RequisitosDosVerbos.PrefixoDeHabilidade + Jandirus.Core.Ranks.KitDoDeus.VerbAuraEmprestada,
				RequisitosDosVerbos.PrefixoDeHabilidade + Jandirus.Core.Ranks.KitDoDeus.VerbInfusaoEmprestada,
			];
			bool semOInstinto = emprestados.All(c => !Ve(deus, RequisitosDosVerbos.Linha(c)!) && !Ve(novato, RequisitosDosVerbos.Linha(c)!));
			deus.UltraInstinct.Aprendida = true;
			bool comOInstinto = Tem(deus, Fatos.KitDaDestruicaoEmprestado) && emprestados.All(c => VeChave(deus, c));
			deus.UltraInstinct.Aprendida = false;
			Checa("A DESTRUICAO EMPRESTADA (a aura e a infusao) so pro Deus que trilha o INSTINTO: o Deus sem ele nao ve os dois botoes",
				  semOInstinto && comOInstinto, $"sem o Instinto escondidos: {semOInstinto}; com ele visiveis: {comOInstinto}");
			Checa("O ESTUDO DA MENTE (focar, escrever) so pra quem tem o verb do degrau", VeChave(mentalista, "hab:Focus_Skill")
				  && VeChave(mentalista, "hab:Write_Teachings") && !VeChave(novato, "hab:Focus_Skill") && !VeChave(novato, "hab:Write_Teachings"));
			Checa("RELAXAR so pra quem sabe controlar o proprio poder (dono, 2026-10-12; no DM e de todo corpo): quem tem o degrau 5 de "
				  + "Basic Ki Control ve o verb, e o novato e quem so estuda a Mente nao",
				  VeChave(controlado, "hab:prontidao") && !VeChave(novato, "hab:prontidao") && !VeChave(mentalista, "hab:prontidao"));
			Checa("AS SUPER ESFERAS so no espaco: quem esta la ve os tres botoes, quem esta em terra nao ve nenhum",
				  new[] { "verbo:sdb_status", "verbo:sdb_reivindicar", "verbo:sdb_invocar" }
					  .All(c => VeChave(noEspaco, c) && !VeChave(novato, c)));
			Checa("OS CARGOS: cada um ve o seu e nao o do vizinho (Anciao nomeia, Rei escolhe herdeiro, Presidente deposita a verba)",
				  VeChave(anciao, "verbo:cargo_nomear") && !VeChave(rei, "verbo:cargo_nomear")
				  && VeChave(rei, "verbo:cargo_herdeiro") && !VeChave(presidente, "verbo:cargo_herdeiro")
				  && VeChave(presidente, "verbo:cargo_verba") && !VeChave(anciao, "verbo:cargo_verba")
				  && VeChave(presidente, "verbo:cargo_deveres") && !VeChave(novato, "verbo:cargo_deveres"));
			Checa("A CHAVE DA SALA DO TEMPO e de quem SABE faze-la: o Guardiao e o Mestre Korin autorizam; SOLTAR e so do Guardiao",
				  VeChave(guardiao, "verbo:sala_autorizar") && VeChave(korin, "verbo:sala_autorizar")
				  && VeChave(guardiao, "verbo:sala_soltar") && !VeChave(korin, "verbo:sala_soltar")
				  && !VeChave(novato, "verbo:sala_autorizar"));

			// O que sobra pro humano novato sao as oito linhas do mainframe de androides -- que e maquina DE humano
			// e so aparece na tecla E, com a maquina na frente -- e, desde a v0.4.0, as TRES ATIVAS DO SANGUE HUMANO (Second Wind,
			// Sheer Willpower e Max Power), na aba Skills. Nenhuma outra. (Eram seis ate 2026-10-10: as duas conversoes ganharam a
			// gemea paga em recursos, e as gemeas pedem a mesma raca.)
			List<VerbDeclarado> doNovato = [.. tabela.Where(v => Ve(novato, v))];
			List<VerbDeclarado> doSangue = [.. doNovato.Where(v => v.Chave.StartsWith("hab:rc_", StringComparison.Ordinal))];
			List<VerbDeclarado> daMaquina = [.. doNovato.Except(doSangue)];
			Checa($"O HUMANO NOVATO nao ve NENHUMA outra linha com requisito ({tabela.Count} na tabela): so as {doSangue.Count} ativas do "
				  + $"sangue humano (aba Skills) e as {daMaquina.Count} do mainframe de androides, na tecla E",
				  doSangue.Count == 3 && doSangue.All(v => v.Onde == "Skills")
				  && daMaquina.Count == 8 && daMaquina.All(v => v.Onde.StartsWith("Tecla E", StringComparison.Ordinal)),
				  $"ve {doNovato.Count}: {string.Join("; ", doNovato.Select(v => v.Rotulo))}");
			Checa("...e as oito da maquina NAO aparecem pra quem nao e humano: Saiyajin, Namekuseijin, Majin, bio-androide e o proprio androide (ja convertido)",
				  daMaquina.All(v => !Ve(saiyajin, v) && !Ve(guerreiro, v) && !Ve(majin, v) && !Ve(bio, v) && !Ve(coletor, v)));
			Checa("...nem as tres do sangue: elas sao do kit HUMANO (o androide ja convertido tambem nao as ve -- *\"Only available when they are not an android\"*)",
				  doSangue.All(v => !Ve(saiyajin, v) && !Ve(guerreiro, v) && !Ve(majin, v) && !Ve(bio, v) && !Ve(coletor, v)));

			Requisito.MostrarTudoDeTeste = true;
			try
			{
				int comOBotao = tabela.Count(v => Ve(novato, v));
				Checa($"(defeito injetado: o botao que mostra tudo) a prova de cima REPROVA -- o novato ve as {comOBotao} linhas, que e o menu de antes",
					  comOBotao == tabela.Count, $"{comOBotao} de {tabela.Count}");
			}
			finally { Requisito.MostrarTudoDeTeste = false; }

			// =====================================================================
			// 4) A TABELA CONTRA O SERVIDOR
			// =====================================================================
			GD.Print("[verbs] -- 4) ninguem que a tabela esconde usa o verb por fora do menu --");
			List<VerbDeclarado> cobraveis = [.. tabela.Where(v => v.Recusa.Length > 0)];
			int apertos = 0;
			var furos = new List<string>();
			foreach (VerbDeclarado linha in cobraveis)
				foreach ((string nome, ServerPlayer corpo) in elenco)
				{
					if (Ve(corpo, linha)) continue;
					apertos++;
					List<string> falas = Apertar(corpo, linha);
					if (!Recusou(falas, linha)) furos.Add($"{linha.Chave} por {nome}: '{Ultimos(falas)}'");
				}
			Checa($"**O SERVIDOR RECUSA TODO MUNDO QUE A TABELA ESCONDE**: {cobraveis.Count} verbs, apertados pelo funil de producao "
				  + $"por quem NAO cumpre o requisito ({apertos} apertos), e todos ouviram a recusa",
				  furos.Count == 0 && apertos > 500, $"{furos.Count} furo(s): {string.Join(" || ", furos)}");
			Checa($"...e as {tabela.Count - cobraveis.Count} linhas sem recusa a cobrar sao recorte de LUGAR, de sentido ou pagina de menu (nao viajam), e estao nomeadas",
				  tabela.Where(v => v.Recusa.Length == 0).Select(v => v.Chave).Order().SequenceEqual(
					  // (o `sdb_invocar` saiu daqui em 2026-10-10: o servidor passou a recusar quem o manda de terra firme)
					  new[] { RequisitosDosVerbos.LinhaDoRadar, "verbo:cargo_herdeiros", "verbo:graus", "verbo:sdb_status",
							  "verbo:lab_menu labs", "verbo:and_menu androide",
							  "verbo:tuffle_menu tuffle" }.Order()),   // (2026-10-18) a pagina da conversao do Neo Tuffle, irma das duas de cima
				  string.Join(", ", tabela.Where(v => v.Recusa.Length == 0).Select(v => v.Chave)));

			// ---- a tecla E: com o mainframe NA FRENTE, e a raca que recusa (e nao a falta da maquina) ----
			var mainframe = new Obra
			{
				Id = IdDoMainframeDaBancada, Tipo = "Android_Creation_Mainframe", Aparafusada = true,
				X = palco.X, Y = palco.Y, DonoConta = saiyajin.Conta, DonoNome = saiyajin.Name,
			};
			mainframe.PorZona(terra);
			_noChao.Add(mainframe);
			try
			{
				Vec2 volta = saiyajin.Pos;
				saiyajin.Pos = palco;
				saiyajin.Ficha.techskill = 99;
				saiyajin.Ficha.Zeni = 10_000_000;
				List<string> instalar = Ouvir(() => Verbo(saiyajin, "lab_androide", ""));
				mainframe.Lab = 1;   // (palco) o Android Lab pronto, pra medir a conversao
				List<string> converter = Ouvir(() => Verbo(saiyajin, "androide_absorcao", ""));
				// ...NEM PELA GEMEA PAGA EM RECURSOS (dono, 2026-10-10): a moeda no argumento nao e porta dos fundos.
				saiyajin.Ficha.Recursos = 10_000_000;
				List<string> comRecursos = Ouvir(() => Verbo(saiyajin, "androide_absorcao", Jandirus.Core.Economia.Recurso.PalavraDeRecursos));
				Checa("A TECLA E, NO MAINFRAME: um Saiyajin DONO da maquina, com tecnologia, zeni e recursos de sobra, nao instala o laboratorio "
					  + "(\"pede um humano\") nem se converte num Android Lab pronto, em zeni ou em recursos (\"fisiologia humana\") -- as oito linhas que o menu lhe esconde",
					  Disse(instalar, "pede um humano") && Disse(converter, "fisiologia humana") && Disse(comRecursos, "fisiologia humana")
					  && saiyajin.Race == "Saiyan" && !saiyajin.Ficha.AndroideAbsorcao
					  && saiyajin.Ficha.Zeni == 10_000_000 && saiyajin.Ficha.Recursos == 10_000_000,
					  $"{Ultimos(instalar)} || {Ultimos(converter)} || {Ultimos(comRecursos)}");
				saiyajin.Ficha.Recursos = 0;
				saiyajin.Pos = volta;
			}
			finally { _noChao.Remove(mainframe); }

			// O CONTRA-EXEMPLO DA REGUA: com o botao que mostra tudo, "quem nao ve" fica vazio e a regua nao
			// aperta nada -- ela so prova alguma coisa porque a tabela ESCONDE.
			Requisito.MostrarTudoDeTeste = true;
			try
			{
				int escondidos = cobraveis.Sum(linha => elenco.Count(e => !Ve(e.Corpo, linha)));
				Checa("(defeito injetado: o botao que mostra tudo) a regua de cima REPROVA -- ninguem esta escondido de nada, e nao ha aperto a fazer",
					  escondidos == 0, $"{escondidos}");
			}
			finally { Requisito.MostrarTudoDeTeste = false; }

			// E O OUTRO LADO, pra quem TEM: o servidor nao lhe da a recusa do requisito (da outra, ou atende).
			Checa("QUEM TEM O REQUISITO nao ouve a recusa dele: o do radar varre o horizonte, o Mestre Korin chega ao pedido da chave, "
				  + "o androide de absorcao abre os coletores e o Namekuseijin passa do portao do Regenerar",
				  !Recusou(Apertar(comRadarNaMao, RequisitosDosVerbos.Linha("verbo:db_radar")!), RequisitosDosVerbos.Linha("verbo:db_radar")!)
				  && !Recusou(Apertar(korin, RequisitosDosVerbos.Linha("verbo:sala_autorizar")!), RequisitosDosVerbos.Linha("verbo:sala_autorizar")!)
				  && !Recusou(Apertar(coletor, RequisitosDosVerbos.Linha("hab:absorver_ki")!), RequisitosDosVerbos.Linha("hab:absorver_ki")!)
				  && !Recusou(Apertar(doCla, RequisitosDosVerbos.Linha("hab:regenerar")!), RequisitosDosVerbos.Linha("hab:regenerar")!));
			coletor.Ficha.ki_absorb_stance = false;

			// =====================================================================
			// 5) A ABSORCAO SEM PORTAO
			// =====================================================================
			GD.Print("[verbs] -- 5) o verb que nao conferia quem apertava: a absorcao do bio-androide --");
			ServerPlayer Vitima(string nome)
			{
				ServerPlayer v = Gente(nome, "Human");
				v.Pos = palco + new Vec2(T * 0.9f, 0);
				v.Ficha.KO = true;
				return v;
			}
			parceiro.Pos = estacionamento + new Vec2(22 * T, 0);   // sai da frente: a vitima e outra

			ServerPlayer v1 = Vitima("VitimaDoHumano");
			novato.Pos = palco;
			novato.AlvoId = v1.Id;
			double bpAntes = novato.Ficha.AbsorbBP;
			List<string> doHumano = Ouvir(() => UsarHabilidade(novato, "absorver"));
			Checa("**UM HUMANO NAO ABSORVE NINGUEM**: com um nocauteado ao lado ele aperta, o servidor recusa, a vitima continua viva e nenhum poder entra",
				  Disse(doHumano, "nao tem como absorver") && !v1.Ficha.dead && novato.Ficha.AbsorbBP == bpAntes
				  && !novato.Ficha.AbsorbDeterminesBP, Ultimos(doHumano));

			AbsorcaoSemPortaoDeTeste = true;
			try
			{
				Ouvir(() => AbsorverAteOFimBio(novato));   // (2026-10-17) a execucao leva 5 s: o relogio anda ate ela se consumar
				Checa("(defeito injetado: sem o portao) a prova de cima REPROVA -- o humano CONSOME a vitima, que era o que qualquer cliente mexido fazia",
					  v1.Ficha.dead && novato.Ficha.AbsorbBP > bpAntes, $"morta {v1.Ficha.dead}, AbsorbBP {novato.Ficha.AbsorbBP:0}");
			}
			finally { AbsorcaoSemPortaoDeTeste = false; }
			novato.Ficha.AbsorbBP = bpAntes;
			novato.Ficha.AbsorbDeterminesBP = false;
			_prontoAbsorver.Remove(novato.Id);
			novato.Pos = estacionamento;
			novato.AlvoId = 0;
			v1.Pos = estacionamento + new Vec2(23 * T, 0);

			ServerPlayer v2 = Vitima("VitimaDoBio");
			bio.Pos = palco;
			bio.AlvoId = v2.Id;
			Ouvir(() => AbsorverAteOFimBio(bio));
			Checa("...e o BIO-ANDROIDE continua absorvendo: ao fim dos 5 s da execucao a vitima morre e o poder dela entra nele",
				  v2.Ficha.dead && bio.Ficha.AbsorbBP > 0,
				  $"morta {v2.Ficha.dead}, AbsorbBP {bio.Ficha.AbsorbBP:0}");
			bio.Pos = estacionamento;
			bio.AlvoId = 0;
			v2.Pos = estacionamento + new Vec2(23 * T, 0);
			parceiro.Pos = palco + new Vec2(T * 0.9f, 0);

			// =====================================================================
			// 6) OS FATOS
			// =====================================================================
			GD.Print("[verbs] -- 6) os fatos: cada um nasce pela porta do sistema dele, liga o botao e some quando o motivo acaba --");
			bool Tem(ServerPlayer quem, Fatos f) => (FatosDe(quem) & f) != 0;
			long agora = NowMs();

			// ---- (2026-10-17) A LARVA: vestir -> o enterrar aparece; amadurecer -> some ----
			{
				ServerPlayer casulo = Gente("CasuloDaBancada", Jandirus.Core.Races.BioAndroids.Raca);
				casulo.Ficha.bio_lab_born = true;
				casulo.Ficha.bio_stage = Jandirus.Core.Races.BioAndroids.SemiPerfeito;
				Checa("(antes) o bio de laboratorio fora da larva nao ve o enterrar", !Tem(casulo, Fatos.EmLarva) && !VeChave(casulo, "hab:enterrar"));
				// (2026-10-17) O NUCLEO: os botoes da escolha acendem pro bio de laboratorio que ainda nao escolheu, e somem com a escolha.
				Checa("O BIO DE LABORATORIO SEM NUCLEO ve os botoes da escolha (`Fatos.NucleoPorEscolher`)",
					  Tem(casulo, Fatos.NucleoPorEscolher) && VeChave(casulo, "hab:nucleo"));
				List<string> escolheu = Ouvir(() => UsarHabilidade(casulo, "nucleo:Perna direita"));
				Checa("...ESCOLHEU (pelo verb de producao): os botoes somem -- e uma vez so",
					  !Tem(casulo, Fatos.NucleoPorEscolher) && !VeChave(casulo, "hab:nucleo") && casulo.Ficha.bio_nucleo == "Perna direita", Ultimos(escolheu));
				// (2026-10-17) AS SKILLS DO DNA: um bio de sangue SO Namekuseijin -- tres skills no sangue, menos que as seis vagas. Cada escolha tira o
				// botao dela (e a ativa escolhida ganha o seu); a terceira ESGOTA o sangue e o fato apaga, com tres vagas ainda livres.
				casulo.Ficha.bio_racas_dna = ["Namekian"];
				static string Dna(Jandirus.Core.Races.SkillRacial s) =>
					RequisitosDosVerbos.PrefixoDeHabilidade + Jandirus.Core.Races.SkillsRaciais.PrefixoDaEscolha + s;
				Checa("O BIO DE SANGUE NAMEKUSEIJIN ve os TRES botoes da escolha do DNA (`Fatos.SkillDoDnaPorEscolher`), e nao o da ativa Spirit Disruption",
					  Tem(casulo, Fatos.SkillDoDnaPorEscolher) && VeChave(casulo, Dna(Jandirus.Core.Races.SkillRacial.Resiliencia))
					  && VeChave(casulo, Dna(Jandirus.Core.Races.SkillRacial.Disrupcao)) && VeChave(casulo, Dna(Jandirus.Core.Races.SkillRacial.Vitalidade))
					  && !VeChave(casulo, "hab:" + Jandirus.Core.Races.Passivas.Disrupcao));
				List<string> umaDoDna = Ouvir(() => UsarHabilidade(casulo, Jandirus.Core.Races.SkillsRaciais.PrefixoDaEscolha + Jandirus.Core.Races.SkillRacial.Disrupcao));
				Checa("...ESCOLHEU A SPIRIT DISRUPTION (pelo verb de producao): o botao da escolha some, o da ATIVA aparece, e os outros dois ficam",
					  !VeChave(casulo, Dna(Jandirus.Core.Races.SkillRacial.Disrupcao)) && VeChave(casulo, "hab:" + Jandirus.Core.Races.Passivas.Disrupcao)
					  && VeChave(casulo, Dna(Jandirus.Core.Races.SkillRacial.Resiliencia)) && VeChave(casulo, Dna(Jandirus.Core.Races.SkillRacial.Vitalidade)),
					  Ultimos(umaDoDna));
				UsarHabilidade(casulo, Jandirus.Core.Races.SkillsRaciais.PrefixoDaEscolha + Jandirus.Core.Races.SkillRacial.Resiliencia);
				UsarHabilidade(casulo, Jandirus.Core.Races.SkillsRaciais.PrefixoDaEscolha + Jandirus.Core.Races.SkillRacial.Vitalidade);
				Checa("...e a terceira ESGOTA o sangue: o fato apaga e nenhum botao de DNA sobra -- com tres das seis vagas ainda livres",
					  !Tem(casulo, Fatos.SkillDoDnaPorEscolher) && doDna.All(ch => !VeChave(casulo, ch))
					  && Jandirus.Core.Races.SkillsRaciais.Quantas(Jandirus.Core.Races.SkillsRaciais.EscolhidasDoBio(casulo.Ficha)) == 3);
				List<string> vestiu = Ouvir(() => UsarHabilidade(casulo, "larva"));
				Checa("O BIO VESTE A LARVA (pelo verb de producao) e o ENTERRAR aparece (`Fatos.EmLarva`)",
					  Tem(casulo, Fatos.EmLarva) && VeChave(casulo, "hab:enterrar"), Ultimos(vestiu));
				UsarHabilidade(casulo, "enterrar");
				// O RELOGIO VOLTA DEPOIS: o resto desta secao marca prazos pelo `agora` guardado la em cima (o pedido de filho, por exemplo).
				long relogioDeAntes = AdiantoDoRelogioDeTeste;
				AdiantoDoRelogioDeTeste += (long)(Jandirus.Core.Races.BioAndroids.SegundosEnterradoPraAmadurecer * 1000) + 1_000;
				TickDaLarva();
				AdiantoDoRelogioDeTeste = relogioDeAntes;
				Checa("...e AMADURECEU (5 min enterrada): o enterrar some, e o degrau e o de antes",
					  !Tem(casulo, Fatos.EmLarva) && !VeChave(casulo, "hab:enterrar")
					  && casulo.Ficha.bio_stage == Jandirus.Core.Races.BioAndroids.SemiPerfeito);
			}

			// ---- o discipulado: convite -> vinculo -> fim ----
			ServerPlayer mestre = Gente("Mestre", "Human");
			ServerPlayer aluno = Gente("Aluno", "Human");
			mestre.Ficha.BP = 50_000;
			aluno.Ficha.BP = 5_000;
			mestre.Pos = palco + new Vec2(0, 6 * T);
			aluno.Pos = mestre.Pos + new Vec2(T * 0.9f, 0);
			mestre.Facing = Facing.East;
			aluno.Facing = Facing.West;
			mestre.AlvoId = aluno.Id;
			Checa("(antes) o aluno nao tem convite: nao ve Aceitar/Recusar; o mestre nao tem aluno: nao ve Ajudar a transformar nem Dispensar",
				  !Tem(aluno, Fatos.ConviteDeMestre) && !VeChave(aluno, "hab:mst_aceitar") && !VeChave(aluno, "hab:mst_recusar")
				  && !VeChave(mestre, "hab:mst_ensinar") && !VeChave(mestre, "hab:mst_dispensar") && !VeChave(aluno, "hab:mst_largar"));
			List<string> convite = Ouvir(() => UsarHabilidade(mestre, "mst_convidar"));
			Checa("O MESTRE CONVIDA (pelo verb de producao) e o ALUNO passa a ver Aceitar e Recusar",
				  aluno.PedidoDoMestre != null && Tem(aluno, Fatos.ConviteDeMestre)
				  && VeChave(aluno, "hab:mst_aceitar") && VeChave(aluno, "hab:mst_recusar"), Ultimos(convite));

			PedidoDeMestre? guardado = aluno.PedidoDoMestre;
			if (guardado != null) aluno.PedidoDoMestre = guardado with { Ate = agora - 1 };
			Checa("...o convite VENCE pelo relogio e os dois botoes somem sozinhos (ninguem precisa avisar: o fato e LIDO, com o prazo)",
				  !Tem(aluno, Fatos.ConviteDeMestre) && !VeChave(aluno, "hab:mst_aceitar"));
			aluno.PedidoDoMestre = guardado;

			List<string> aceite = Ouvir(() => UsarHabilidade(aluno, "mst_aceitar"));
			Checa("...o aluno ACEITA: ele passa a ver Deixar meu mestre, o mestre passa a ver Ajudar a transformar e Dispensar aluno, e o Aceitar some",
				  Tem(aluno, Fatos.TemMestre) && Tem(mestre, Fatos.TemAluno) && !Tem(aluno, Fatos.ConviteDeMestre)
				  && VeChave(aluno, "hab:mst_largar") && VeChave(mestre, "hab:mst_ensinar") && VeChave(mestre, "hab:mst_dispensar")
				  && !VeChave(aluno, "hab:mst_aceitar") && !VeChave(mestre, "hab:mst_largar"), Ultimos(aceite));
			// A ABA DO DISCIPULADO (dono, 2026-10-11) nasce do MESMO vinculo: a aba (o pedido do painel) pros dois, e os
			// dois gestos do cartao de um aluno so pra quem guia.
			Checa("...e a aba Alunos/Mestre passa a existir pros DOIS (o verb do painel); os gestos do cartao de um aluno (ajudar a transformar, ensinar uma skill ou uma tecnica, dispensar) so pro mestre",
				  VeChave(aluno, "verbo:alunos") && VeChave(mestre, "verbo:alunos")
				  && VeChave(mestre, "verbo:alunos_ensinar") && VeChave(mestre, "verbo:alunos_dispensar")
				  && VeChave(mestre, "verbo:alunos_licao") && VeChave(mestre, "verbo:alunos_tecnica")
				  && !VeChave(aluno, "verbo:alunos_ensinar") && !VeChave(aluno, "verbo:alunos_dispensar")
				  && !VeChave(aluno, "verbo:alunos_licao") && !VeChave(aluno, "verbo:alunos_tecnica"));
			Ouvir(() => UsarHabilidade(aluno, "mst_largar"));
			Checa("...e ele DEIXA o mestre: os tres botoes somem dos dois lados -- e a aba Alunos/Mestre tambem",
				  !Tem(aluno, Fatos.TemMestre) && !Tem(mestre, Fatos.TemAluno)
				  && !VeChave(aluno, "hab:mst_largar") && !VeChave(mestre, "hab:mst_ensinar")
				  && !VeChave(aluno, "verbo:alunos") && !VeChave(mestre, "verbo:alunos") && !VeChave(mestre, "verbo:alunos_dispensar"));

			// ---- a licao de skill ----
			aluno.PedidoDeLicao = new PedidoDeLicao(mestre.Assinatura, mestre.Name, "/datum/skill/general/splitform", agora + 20_000);
			Checa("UMA LICAO NA MESA liga Aceitar a licao / Recusar a licao -- e o Recusar de producao a tira de la",
				  Tem(aluno, Fatos.LicaoOferecida) && VeChave(aluno, "hab:ens_licao_sim") && VeChave(aluno, "hab:ens_licao_nao")
				  && !Recusou(Ouvir(() => UsarHabilidade(aluno, "ens_licao_nao")), RequisitosDosVerbos.Linha("hab:ens_licao_nao")!)
				  && !Tem(aluno, Fatos.LicaoOferecida) && !VeChave(aluno, "hab:ens_licao_sim"));

			// ---- (2026-10-17) os filhos: o amor correspondido acende "Ter um filho"; o pedido na mesa, as duas respostas ----
			ServerPlayer amante = Gente("Amante", "Human");
			ServerPlayer amado = Gente("Amado", "Saiyan");
			bool antesDoAmor = !VeChave(amante, "hab:filho_pedir") && !VeChave(amado, "hab:filho_pedir");
			foreach ((ServerPlayer de, ServerPlayer por) in new[] { (amante, amado), (amado, amante) })
			{
				de.Social.Fotografar(por.Assinatura, por.Name, por.Race, por.Genero, por.Idade, agora);
				de.Social.Ficha(por.Assinatura)!.Familiaridade = Convivio.FamiliaridadeExigida(Relacao.Amor);
				de.Social.Declarar(por.Assinatura, Relacao.Amor);
			}
			amado.PedidoDeFilho = new PedidoDeFilho(amante.Assinatura, amante.Name, agora + 20_000);
			Checa("O AMOR CORRESPONDIDO acende 'Ter um filho' pros dois (sem amor, pra ninguem); o PEDIDO NA MESA acende 'Aceitar o filho' e 'Recusar o filho' -- e o Recusar de producao o tira de la",
				  antesDoAmor && Tem(amante, Fatos.TemUmAmor) && VeChave(amante, "hab:filho_pedir") && VeChave(amado, "hab:filho_pedir")
				  && VeChave(amado, "hab:filho_sim") && VeChave(amado, "hab:filho_nao")
				  && !Recusou(Ouvir(() => UsarHabilidade(amado, "filho_nao")), RequisitosDosVerbos.Linha("hab:filho_nao")!)
				  && !Tem(amado, Fatos.PedidoDeFilho) && !VeChave(amado, "hab:filho_sim"));

			// ---- a fusao: a Danca e a absorcao Namekuseijin ----
			ServerPlayer d1 = Gente("DancarinoA", "Human", skills: ["/datum/skill/rank/Fusion_Dance"]);
			ServerPlayer d2 = Gente("DancarinoB", "Human", skills: ["/datum/skill/rank/Fusion_Dance"]);
			d1.Pos = palco + new Vec2(0, 9 * T);
			d2.Pos = d1.Pos + new Vec2(T * 0.9f, 0);
			d1.Facing = Facing.East;
			d2.Facing = Facing.West;
			d1.AlvoId = d2.Id;
			List<string> chamada = Ouvir(() => UsarHabilidade(d1, "fus_danca"));
			Checa("A DANCA: o convidado (e so ele) passa a ver Aceitar a fusao e Recusar a fusao -- e NAO o aceite da absorcao",
				  Tem(d2, Fatos.ConviteDeFusao) && VeChave(d2, "hab:fus_sim") && VeChave(d2, "hab:fus_nao")
				  && !VeChave(d2, "hab:fus_namek_sim") && !VeChave(d1, "hab:fus_sim"), Ultimos(chamada));
			Ouvir(() => UsarHabilidade(d2, "fus_nao"));
			Checa("...recusou: os botoes somem", !Tem(d2, Fatos.ConviteDeFusao) && !VeChave(d2, "hab:fus_sim") && !VeChave(d2, "hab:fus_nao"));

			ServerPlayer n1 = Gente("NamekA", "Namekian", "Warrior clan", skills: [RequisitosDosVerbos.SkillDaFusaoNamek]);
			ServerPlayer n2 = Gente("NamekB", "Namekian", "Warrior clan");
			n1.Pos = palco + new Vec2(0, 12 * T);
			n2.Pos = n1.Pos + new Vec2(T * 0.9f, 0);
			n1.Facing = Facing.East;
			n2.Facing = Facing.West;
			n1.AlvoId = n2.Id;
			n2.AlvoId = n1.Id;
			List<string> semASkill = Ouvir(() => UsarHabilidade(n2, "fus_namek"));
			Checa("(2026-10-13) A FUSAO NAMEKUSEIJIN E DE QUEM APRENDEU A SKILL DELA: o Namekuseijin que nao aprendeu a Fusion- Namek Style nao ve o botao, e o servidor recusa o gesto dizendo onde ela esta -- nenhum convite sai",
				  VeChave(n1, "hab:fus_namek") && !VeChave(n2, "hab:fus_namek") && Disse(semASkill, "Fusion- Namek Style") && !Tem(n1, Fatos.ConviteDeAbsorcao),
				  Ultimos(semASkill));
			n2.AlvoId = 0;
			List<string> oferta = Ouvir(() => UsarHabilidade(n1, "fus_namek"));
			Checa("A ABSORCAO NAMEKUSEIJIN: o convidado passa a ver Aceitar a ABSORCAO e Recusar -- e NAO o aceite da fusao comum",
				  Tem(n2, Fatos.ConviteDeAbsorcao) && VeChave(n2, "hab:fus_namek_sim") && VeChave(n2, "hab:fus_nao")
				  && !VeChave(n2, "hab:fus_sim"), Ultimos(oferta));
			Ouvir(() => UsarHabilidade(n2, "fus_nao"));
			Checa("...recusou: somem", !Tem(n2, Fatos.ConviteDeAbsorcao) && !VeChave(n2, "hab:fus_namek_sim"));

			var fusao = new FusaoAtiva { Dono = d1, Passageiro = d2, Tipo = TipoDeFusao.Danca };
			_fundidos[d1.Id] = fusao;
			_fundidos[d2.Id] = fusao;
			Checa("FUNDIDO E NO VOLANTE: quem dirige ve Passar o controle; o passageiro nao",
				  Tem(d1, Fatos.DirigeUmaFusao) && VeChave(d1, "hab:fus_passar") && !VeChave(d2, "hab:fus_passar"));
			_fundidos.Remove(d1.Id);
			_fundidos.Remove(d2.Id);
			Checa("...separou: some", !VeChave(d1, "hab:fus_passar"));

			// ---- as ofertas que alguem faz a voce ----
			ServerPlayer kai = Gente("GrandKai", "Kai");
			ServerPlayer velho = Gente("Velho", "Human");
			kai.Pos = palco + new Vec2(0, 15 * T);
			velho.Pos = kai.Pos + new Vec2(T * 0.9f, 0);
			kai.AlvoId = velho.Id;
			Ouvir(() => OferecerJuventudeG8(kai, "20"));
			Checa("A OFERTA DE JUVENTUDE (pelo verb de quem oferece): o alvo passa a ver Accept Youth / Decline Youth",
				  Tem(velho, Fatos.OfertaDeJuventude) && VeChave(velho, "verbo:juventude_aceitar") && VeChave(velho, "verbo:juventude_recusar")
				  && !VeChave(kai, "verbo:juventude_aceitar"));
			Ouvir(() => Verbo(velho, "juventude_recusar", ""));
			Checa("...recusou: somem", !Tem(velho, Fatos.OfertaDeJuventude) && !VeChave(velho, "verbo:juventude_aceitar"));

			Ouvir(() => OferecerPotencialG11(kai));
			Checa("A OFERTA DE DESPERTAR O POTENCIAL: o alvo passa a ver Accept Potential / Decline Potential",
				  Tem(velho, Fatos.OfertaDePotencial) && VeChave(velho, "verbo:potencial_aceitar") && VeChave(velho, "verbo:potencial_recusar"));
			Ouvir(() => Verbo(velho, "potencial_recusar", ""));
			Checa("...recusou: somem", !Tem(velho, Fatos.OfertaDePotencial) && !VeChave(velho, "verbo:potencial_aceitar"));

			_genkidamaG12[kai.Id] = new EstadoDaGenkidamaG12 { Fase = 1 };
			_ofertasDeGenkidamaG12[velho.Id] = kai.Id;
			Checa("UMA GENKIDAMA CRESCENDO pedindo a sua energia: voce passa a ver Give Power / Refuse",
				  Tem(velho, Fatos.PedidoDeGenkidama) && VeChave(velho, "verbo:genkidama_doar") && VeChave(velho, "verbo:genkidama_negar"));
			_genkidamaG12[kai.Id].Fase = 2;
			Checa("...a bomba saiu da fase de crescer: o pedido deixou de valer e os botoes somem (a MESMA conta do verb, que recusaria)",
				  !Tem(velho, Fatos.PedidoDeGenkidama) && !VeChave(velho, "verbo:genkidama_doar")
				  && Recusou(Ouvir(() => Verbo(velho, "genkidama_doar", "")), RequisitosDosVerbos.Linha("verbo:genkidama_doar")!));
			_genkidamaG12.Remove(kai.Id);
			_ofertasDeGenkidamaG12.Remove(velho.Id);

			ServerPlayer novico = Gente("Novico", "Namekian", "Warrior clan");
			novico.Pos = anciao.Pos + new Vec2(T, 0);   // (palco) no tile vizinho: nomear e presencial (`view(1)`, `NamekRanks.dm:56`)
			List<string> nomeacao = Ouvir(() => VerboNomearAnciao(anciao, novico.Id.ToString()));
			Checa("O GRANDE ANCIAO NOMEIA (pelo verb dele): o Namekuseijin convidado passa a ver Accept/Decline Elder Seat -- e o guerreiro de Namek que ninguem convidou, nao",
				  Tem(novico, Fatos.OfertaDeAssento) && VeChave(novico, "verbo:cargo_nomear_aceitar") && VeChave(novico, "verbo:cargo_nomear_recusar")
				  && !VeChave(guerreiro, "verbo:cargo_nomear_aceitar"), Ultimos(nomeacao));
			Ouvir(() => Verbo(novico, "cargo_nomear_recusar", ""));
			Checa("...recusou: somem", !Tem(novico, Fatos.OfertaDeAssento) && !VeChave(novico, "verbo:cargo_nomear_aceitar"));

			// (2026-10-20) O APRENDIZ DE KAIOSHIN -- as quatro linhas do `ag/aprendiz` (o convite pelo verb e o aceite estao provados na
			// `--aprendizteste`; aqui e a plateia da familia 7): o Kaioshin no trono ve Name/Dismiss Apprentice, e quem tem o convite na mesa
			// ve Accept/Decline Apprenticeship.
			ServerPlayer kaioshin = Gente("Kaioshin", "Kai");
			ServerPlayer convidado = Gente("ConvidadoDoKaioshin", "Human");
			_tronos[Jandirus.Core.Ranks.AprendizDeKaioshin.CargoDoMestre] = kaioshin.Conta;
			_ofertasDeAprendiz[convidado.Conta] = (kaioshin.Conta, kaioshin.Name, NowMs() + 60_000);
			Checa("O APRENDIZ DE KAIOSHIN: o Kaioshin no trono ve Name/Dismiss Apprentice, quem tem o convite na mesa ve Accept/Decline "
				  + "Apprenticeship -- e o novato, nenhum dos quatro",
				  VeChave(kaioshin, "verbo:aprendiz_nomear") && VeChave(kaioshin, "verbo:aprendiz_dispensar")
				  && Tem(convidado, Fatos.OfertaDeAprendiz) && VeChave(convidado, "verbo:aprendiz_aceitar") && VeChave(convidado, "verbo:aprendiz_recusar")
				  && !VeChave(novato, "verbo:aprendiz_nomear") && !VeChave(novato, "verbo:aprendiz_aceitar"));
			_ofertasDeAprendiz.Remove(convidado.Conta);
			_tronos.Remove(Jandirus.Core.Ranks.AprendizDeKaioshin.CargoDoMestre);

			Checa("(antes) o Deus da Destruicao sem desafio pendente nao ve Accept Challenge / Postpone Challenge",
				  !Tem(deus, Fatos.DesafioAoTitulo) && !VeChave(deus, "verbo:cargo_duelo_aceitar"));
			_duelo.TituloDesde = _duelo.UltimoDuelo = 0;   // (palco) fora da carencia de 3 dias e do intervalo de 2: a prova e do God Ki, nao do calendario
			_duelo.EmDuelo = false;
			List<string> desafio = Ouvir(() => Verbo(divino, "cargo_desafiar", ""));
			Checa("QUEM TEM GOD KI DESAFIA (pelo verb): com o desafio esperando resposta o Deus passa a ver os dois -- e o desafiante, nao",
				  Tem(deus, Fatos.DesafioAoTitulo) && VeChave(deus, "verbo:cargo_duelo_aceitar") && VeChave(deus, "verbo:cargo_duelo_adiar")
				  && !VeChave(divino, "verbo:cargo_duelo_aceitar"), Ultimos(desafio));
			_desafianteEsperando = "";          // (palco) o desafio some sem resposta: adiar pelo verb gastaria um adiamento da semana no arquivo do titulo
			_duelo.EmDuelo = emDueloDeAntes;
			Checa("...o desafio saiu da mesa: somem", !Tem(deus, Fatos.DesafioAoTitulo) && !VeChave(deus, "verbo:cargo_duelo_aceitar"));

			// ---- as copias do Split Form ----
			novato.Ficha.splitformCount = 1;
			Checa("COPIAS DE PE sem a tecnica (o Majin que expeliu quem a sabia): as cinco ordens continuam ao alcance",
				  Tem(novato, Fatos.TemCopias) && doSplitForm.All(c => VeChave(novato, c)));
			novato.Ficha.splitformCount = 0;

			// ---- a arma de fogo (v0.2.1) ----
			string gatilho = RequisitosDosVerbos.ChaveDoVerbo(Jandirus.Core.Tech.ArmasDeFogo.VerboAtirar);
			bool semArma = !Tem(novato, Fatos.TemArmaDeFogo) && !VeChave(novato, gatilho);
			// (com o estado no id, como fica depois do primeiro tiro: a pergunta e pela arma, e nao pelo id cru)
			string pistolaUsada = Jandirus.Core.Tech.ArmasDeFogo.Escrever(Jandirus.Core.Tech.ArmasDeFogo.Pistola,
				new Jandirus.Core.Tech.ArmasDeFogo.Estado(12, 30, 1, false, false));
			novato.Mochila.Guardar(pistolaUsada);
			Checa("UMA ARMA DE FOGO NA MOCHILA (com o estado dela no id): o gatilho de tecla aparece -- antes nao aparecia, e some com ela",
				  semArma && Tem(novato, Fatos.TemArmaDeFogo) && VeChave(novato, gatilho));
			novato.Mochila.Tirar(pistolaUsada);
			Checa("...sem a arma: some", !Tem(novato, Fatos.TemArmaDeFogo) && !VeChave(novato, gatilho));

			// ---- o dominio e a estatua ----
			string[] deQuemDominaAqui = ["verbo:conq_tributo", "verbo:conq_spawn", "verbo:conq_abandonar sim"];
			Checa("(antes) ninguem do elenco domina nada: Meus dominios, Coletar tributo, Renascer aqui e Abandonar nao aparecem pra ninguem",
				  elenco.All(e => !VeChave(e.Corpo, "verbo:conq_dominios") && deQuemDominaAqui.All(c => !VeChave(e.Corpo, c))));
			var dominio = new Dominio
			{
				PreFeito = true, Planeta = terra.Name, Assinatura = doCla.Assinatura, Conta = doCla.Conta, Nome = doCla.Name,
				Desde = TempoDoMundo, Coletado = TempoDoMundo, Visto = TempoDoMundo, Fx = doCla.Pos.X, Fy = doCla.Pos.Y,
			};
			_dominios.Add(dominio);
			Checa("A BANDEIRA DA TERRA E DELE: o dono ve os quatro; o vizinho que pisa no mesmo chao nao ve nenhum",
				  Tem(doCla, Fatos.DominaEstePlaneta) && Tem(doCla, Fatos.DominaAlgum)
				  && VeChave(doCla, "verbo:conq_dominios") && deQuemDominaAqui.All(c => VeChave(doCla, c))
				  && !VeChave(guerreiro, "verbo:conq_dominios") && deQuemDominaAqui.All(c => !VeChave(guerreiro, c)));
			doCla.Zone = ZoneKey.Premade("Namek");
			Checa("...e em OUTRO planeta o dono continua vendo Meus dominios, mas nao Coletar tributo/Renascer aqui/Abandonar (sao do planeta em que se esta)",
				  Tem(doCla, Fatos.DominaAlgum) && !Tem(doCla, Fatos.DominaEstePlaneta)
				  && VeChave(doCla, "verbo:conq_dominios") && deQuemDominaAqui.All(c => !VeChave(doCla, c)));
			doCla.Zone = terra;
			_dominios.Remove(dominio);

			// A ESTATUA, pelo verb de producao: na Terra quem ergue e o Guardiao (o trono e por conta).
			_tronos["guardian"] = doCla.Conta;
			List<string> erguer = Ouvir(() => Verbo(doCla, "db_estatua", ""));
			SetDeEsferas? estatua = _sets.Find(s => s.Zona.Hash == terra.Hash);
			Checa("O NAMEKUSEIJIN DO CLA ERGUE A ESTATUA (pelo verb): ele passa a ver Despertar as esferas e DEIXA de ver Erguer (ja ha uma aqui)",
				  estatua != null && Tem(doCla, Fatos.CriouAEstatuaDaqui) && VeChave(doCla, "verbo:db_reviver")
				  && !VeChave(doCla, "verbo:db_estatua") && !VeChave(doCla, "verbo:db_estatua supremo"), Ultimos(erguer));
			Checa("...e TODO MUNDO neste mundo passa a ver Ver as esferas daqui / Pegar / Largar / INVOCAR -- mas Despertar e so de quem ergueu",
				  new[] { "verbo:db_ver", "verbo:db_pegar", "verbo:db_largar", "verbo:db_invocar" }.All(c => VeChave(saiyajin, c) && VeChave(novato, c))
				  && !VeChave(saiyajin, "verbo:db_reviver") && !VeChave(guerreiro, "verbo:db_reviver")
				  && !VeChave(noEspaco, "verbo:db_ver"));
			Checa("...\"Derrubar a estátua\" tambem passa a aparecer pra TODO MUNDO neste mundo (derrubar e de qualquer um ao lado dela), e "
				  + "\"Pedidos por invocação\" so pra quem a ergueu -- o mesmo fato de Despertar as esferas",
				  VeChave(saiyajin, "verbo:db_menu foco derrubar") && VeChave(novato, "verbo:db_menu foco derrubar")
				  && !VeChave(noEspaco, "verbo:db_menu foco derrubar")
				  && VeChave(doCla, "verbo:db_refazer") && !VeChave(saiyajin, "verbo:db_refazer") && !VeChave(guerreiro, "verbo:db_refazer"));
			_tronos["guardian"] = guardiao.Conta;

			// ---- os fatos VIAJAM, e so quando mudam ----
			List<(int Quem, Fatos Fatos)> fioDosFatos = [];
			EscutaDeFatos = fioDosFatos;
			try
			{
				_fatosMandados.Remove(pl.Id);
				TickDosFatos();
				Checa("O FIO: a primeira leitura de uma tela SEMPRE viaja (mesmo sem fato nenhum -- o cliente pode ter o do personagem anterior)",
					  fioDosFatos.Count(f => f.Quem == pl.Id) == 1, $"{fioDosFatos.Count} pacote(s)");

				TickDosFatos();
				TickDosFatos();
				Checa("...e parado, sem nada mudar, nenhum pacote sai", fioDosFatos.Count(f => f.Quem == pl.Id) == 1,
					  $"{fioDosFatos.Count(f => f.Quem == pl.Id)} pacote(s)");

				_ofertasDeJuventudeG8[pl.Conta] = ("bancada", 20, NowMs() + 60_000);
				TickDosFatos();
				Checa("...uma oferta cai na mesa do jogador e o pacote seguinte leva o bit dela",
					  fioDosFatos.Count(f => f.Quem == pl.Id) == 2 && (fioDosFatos[^1].Fatos & Fatos.OfertaDeJuventude) != 0,
					  $"{fioDosFatos.Count(f => f.Quem == pl.Id)} pacote(s)");

				_ofertasDeJuventudeG8.Remove(pl.Conta);
				TickDosFatos();
				Checa("...a oferta sai e o pacote seguinte leva o bit apagado",
					  fioDosFatos.Count(f => f.Quem == pl.Id) == 3 && (fioDosFatos[^1].Fatos & Fatos.OfertaDeJuventude) == 0);

				FatosCongeladosDeTeste = true;
				try
				{
					_ofertasDeJuventudeG8[pl.Conta] = ("bancada", 20, NowMs() + 60_000);
					TickDosFatos();
					Checa("(defeito injetado: os fatos nao saem quando mudam) a prova de cima REPROVA -- a oferta esta na mesa e nenhum pacote sai",
						  fioDosFatos.Count(f => f.Quem == pl.Id) == 3, $"{fioDosFatos.Count(f => f.Quem == pl.Id)} pacote(s)");
				}
				finally { FatosCongeladosDeTeste = false; _ofertasDeJuventudeG8.Remove(pl.Conta); }
				TickDosFatos();
			}
			finally { EscutaDeFatos = null; }

			// =====================================================================
			// 7) NINGUEM FICOU SEM PLATEIA
			// =====================================================================
			List<string> semPlateia = [.. tabela.Where(v => !vistas.Contains(v.Chave)).Select(v => v.Chave)];
			Checa($"TODA linha da tabela foi vista por pelo menos um personagem desta bancada ({vistas.Count} de {tabela.Count}) -- "
				  + "nenhum requisito ficou impossivel de cumprir, e nenhum ficou sem prova",
				  semPlateia.Count == 0, string.Join(", ", semPlateia));
		}
		catch (Exception e)
		{
			Checa("a bancada correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			GD.PrintErr(e.ToString());
		}
		finally
		{
			// ---- o mundo volta: flags, fio, livros e corpos ----
			Requisito.MostrarTudoDeTeste = false;
			SinalSemRadarDeTeste = RadarMedeDoChaoDeTeste = AbsorcaoSemPortaoDeTeste = FatosCongeladosDeTeste = false;
			EscutaDoSinalDourado = null;
			EscutaDeFatos = null;

			pl.Zone = zonaDeAntes;
			pl.Pos = posDeAntes;
			int radarAgora = pl.Mochila.Quantos(CatalogoDeItens.Radar);
			if (radarAgora > radarDeAntes) pl.Mochila.Tirar(CatalogoDeItens.Radar, radarAgora - radarDeAntes);
			else if (radarAgora < radarDeAntes) pl.Mochila.Guardar(CatalogoDeItens.Radar, radarDeAntes - radarAgora);
			_sinalMandado.Remove(pl.Id);
			_fatosMandados.Remove(pl.Id);

			_tronos.Clear();
			foreach ((string chave, string conta) in tronosDeAntes) _tronos[chave] = conta;
			_dominios.Clear();
			_dominios.AddRange(dominiosDeAntes);
			_desafianteEsperando = desafianteDeAntes;
			_duelo.EmDuelo = emDueloDeAntes;
			(_duelo.TituloDesde, _duelo.UltimoDuelo, _duelo.Adiamentos, _duelo.SemanaDosAdiamentos) = dueloDeAntes;

			// O discipulado e as esferas GRAVAM pelas portas de producao que a bancada usou: a memoria volta e
			// o arquivo e regravado com ela (a pasta e a de saves desviada da bancada, mas o estado tem que bater).
			_mestreDe.Clear();
			foreach ((string a, string m) in mestresDeAntes) _mestreDe[a] = m;
			_nomePorAssinatura.Clear();
			foreach ((string a, string n) in nomesDeMestreDeAntes) _nomePorAssinatura[a] = n;
			SalvarMestres();

			foreach (SetDeEsferas s in _sets.Where(s => !setsDeAntes.Contains(s.Id)).ToList())
			{
				_esferas.RemoveAll(e => e.Set == s.Id);
				_sets.Remove(s);
				FecharAInvocacao(s.Id, "");
			}
			SalvarEsferas();

			foreach (string conta in _ofertasDeJuventudeG8.Keys.Where(k => k.StartsWith("bancada_verbs_", StringComparison.Ordinal)).ToList())
				_ofertasDeJuventudeG8.Remove(conta);
			foreach (string conta in _ofertasDePotencialG11.Keys.Where(k => k.StartsWith("bancada_verbs_", StringComparison.Ordinal)).ToList())
				_ofertasDePotencialG11.Remove(conta);
			foreach (string conta in _convitesDeAnciao.Keys.Where(k => k.StartsWith("bancada_verbs_", StringComparison.Ordinal)).ToList())
				_convitesDeAnciao.Remove(conta);
			foreach (int id in _pedidosDeFusao.Keys.Where(k => k >= IdBaseDeProjetil).ToList()) _pedidosDeFusao.Remove(id);
			foreach (int id in _fundidos.Keys.Where(k => k >= IdBaseDeProjetil).ToList()) _fundidos.Remove(id);
			foreach (int id in _genkidamaG12.Keys.Where(k => k >= IdBaseDeProjetil).ToList()) _genkidamaG12.Remove(id);
			foreach (int id in _ofertasDeGenkidamaG12.Keys.Where(k => k >= IdBaseDeProjetil).ToList()) _ofertasDeGenkidamaG12.Remove(id);
			foreach (int id in _prontoAbsorver.Keys.Where(k => k >= IdBaseDeProjetil).ToList()) _prontoAbsorver.Remove(id);
			_noChao.RemoveAll(o => o.Id == IdDoMainframeDaBancada);

			// OS CORPOS FORJADOS SAEM DE TODA LISTA DE ZONA: a bancada trocou a `Zone` de alguns NA MAO (o
			// radarista rodou os sete mundos, um humano ficou no espaco), e a faxina comum tira cada corpo so da
			// lista da zona em que ele DIZ estar -- o resto ficaria de fantasma na Terra.
			foreach (ServerPlayer c in _players.Values)
				if (c.Id >= IdBaseDeProjetil)
					foreach (List<ServerPlayer> lista in _zones.Values) lista.Remove(c);
			LimparTudoDaBancada();

			GD.Print($"[verbs] ================ {ok} OK, {falhou} FALHA(S) ================\n");
		}
	}

	// =====================================================================
	// AS FIXTURES DA BANCADA DE TELA (`--diagverbs`, `Client/RoboDeVerbs.cs`)
	// =====================================================================
	// O robo do cliente roda no MESMO processo do servidor (`--host`) e chama estes metodos direto. Nenhum e
	// alcancavel pelo fio: nao ha verbo nem opcode que chegue aqui.

	/// <summary>SO PRA BANCADA (`--diagverbs`): poe (ou tira) o Dragon Radar da mochila do jogador, pela porta da mochila.</summary>
	internal bool RadarNaMochilaDeTeste(int id, bool dar)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		if (dar) return Guardar(pl, CatalogoDeItens.Radar);
		pl.Mochila.Tirar(CatalogoDeItens.Radar, pl.Mochila.Quantos(CatalogoDeItens.Radar));
		MandarMochila(pl);
		return true;
	}

	/// <summary>
	/// SO PRA BANCADA (`--diagverbs`): roda o CICLO das Super Esferas ate alguma ficar ao alcance do radar de quem
	/// esta na TERRA (a semente do mundo e sorteada, e em mais da metade delas nao ha sinal nenhum la). Devolve o
	/// ciclo de antes, pra <see cref="CicloDasSupersDeTeste"/> devolver; -1 se nenhum dos proximos serviu.
	/// </summary>
	internal int SinalNaTerraDeTeste()
	{
		int antes = _cicloDasSupers;
		Vec2 terra = Espaco.PreFeitos().First(p => p.Nome == "Earth").Pos;
		for (int ciclo = antes; ciclo < antes + 64; ciclo++)
			for (int n = 1; n <= SuperEsferas.Total; n++)
				if (SuperEsferas.CelulasEntre(terra, SuperEsferas.PosicaoDa(SeedDoUniverso, n, ciclo)) <= SuperEsferas.CelulasDeSinal)
				{
					_cicloDasSupers = ciclo;
					return antes;
				}
		return -1;
	}

	/// <summary>SO PRA BANCADA (`--diagverbs`): devolve o ciclo das Super Esferas que <see cref="SinalNaTerraDeTeste"/> trocou.</summary>
	internal void CicloDasSupersDeTeste(int ciclo) => _cicloDasSupers = ciclo;

	/// <summary>SO PRA BANCADA (`--diagverbs`): uma oferta de juventude na mesa do jogador -- o que o `Restore_Youth` de outro deixaria la.</summary>
	internal bool OfertaDeJuventudeDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Conta.Length == 0) return false;
		_ofertasDeJuventudeG8[pl.Conta] = ("bancada", 20, NowMs() + PrazoDaOfertaDeJuventudeMsG8);
		return true;
	}

	/// <summary>SO PRA BANCADA (`--diagverbs`): um mainframe de androides aparafusado aos pes do jogador, e dele -- ou some com ele.</summary>
	internal bool MainframeDeTeste(int id, bool por)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		_noChao.RemoveAll(o => o.Id == IdDoMainframeDaBancada);
		if (por)
		{
			var m = new Obra
			{
				Id = IdDoMainframeDaBancada, Tipo = "Android_Creation_Mainframe", Aparafusada = true,
				X = pl.Pos.X, Y = pl.Pos.Y, DonoConta = pl.Conta, DonoNome = pl.Name,
			};
			m.PorZona(pl.Zone);
			_noChao.Add(m);
		}
		MandarObras(pl.Zone);
		return true;
	}
}
