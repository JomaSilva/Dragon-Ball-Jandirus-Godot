using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// OS CARGOS DO MUNDO. Quem e o Guardiao da Terra, quem e o Kaio do Norte, quem e o Kaioshin.
///
/// SAO DO SERVIDOR, NAO DO PERSONAGEM: um cargo tem UM dono no mundo inteiro, e por isso ele
/// mora aqui e nao no save de ninguem. O que o save do personagem guarda e so a CHAVE do cargo
/// que ele carrega -- e mesmo essa e reconferida contra a lista do mundo ao entrar, senao dois
/// jogadores voltariam de logout se dizendo Kaioshin.
///
/// A CHAVE E A CONTA, e isso e uma licao do original: la o sistema comparava ora por `key`, ora
/// por `name`, ora por `signature`, e o resultado era rank que nao transferia e trono que nunca
/// vagava. Aqui e SEMPRE a conta -- o unico identificador que nao muda e nao se repete.
///
/// (2026-10-26) E O PERSONAGEM TAMBEM. O dono: *"faca os ranks serem ligados ao personagem e nao a conta assim n tem como ter varios personagens da mesma conta caindo no mesmo rank"* -- e,
/// perguntado, *"Sim, cada um o seu"* (personagens da mesma conta podem ter cargos DIFERENTES, nunca o mesmo) e *"O 1o que entrar com
/// ele"*. A conta continua a chave de `_tronos` (e por ela que as portas, os arquivos e as bancadas falam do dono), e ao lado dela mora o
/// PERSONAGEM -- o slot (`_personagensDosTronos`). O trono sem personagem (o de antes desta data, que era da conta inteira) vale pra
/// qualquer personagem da conta ate o primeiro deles ser visto no jogo, e ai se prende a ele (<see cref="VagaDe(ServerPlayer)"/>).
/// </summary>
public partial class GameServer
{
	/// <summary>chave do cargo -> conta do dono. Vazio = trono vago.</summary>
	private readonly Dictionary<string, string> _tronos = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// (2026-10-26) vaga -> o PERSONAGEM que a carrega: a conta (de quando ele sentou), o slot e o nome. So vale enquanto a conta for a
	/// mesma do `_tronos` -- quem escreve o trono direto (as bancadas, o arquivo de antes) escreve so a conta, e um personagem velho preso a
	/// vaga nao pode passar pra outra conta. Ver <see cref="SlotDoTrono"/>.
	/// </summary>
	private readonly Dictionary<string, (string Conta, int Slot, string Nome)> _personagensDosTronos = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Ao lado dos saves de conta. Um arquivo de texto de uma dezena de linhas -- cargo e
	/// coisa RARA, e um formato que se le num editor vale mais aqui que economia de bytes.
	/// </summary>
	// ============================ PELO `_store`, E NAO PELO CAMINHO CRAVADO ============================
	// Este arquivo (e mais tres: `mestres.txt`, `herdeiros.txt`, `titulo.txt`) montava o caminho com
	// `GlobalizePath("user://saves")` escrito na mao, enquanto os oito `.json` do mundo perguntam ao
	// `_store.Pasta`. Sao a mesma pasta hoje -- e por isso a divergencia nunca deu sintoma.
	//
	// Ela vira defeito no dia em que a pasta se mover: uma bancada com pasta propria, ou o dono
	// rodando dois servidores na mesma maquina. Ai estes quatro continuariam apontando pro lugar
	// antigo enquanto todo o resto do mundo mudaria de casa -- e a LIMPEZA TOTAL, que apaga a pasta do
	// `_store`, deixaria os tronos, o discipulado e a sucessao inteiros para tras. E exatamente o
	// "apagou de menos" que este trabalho existe pra evitar, escrito em quatro linhas de infra.
	//
	// O `??` continua cravando o caminho pra o caso de nao haver `_store`: nao ha, hoje, mas devolver
	// vazio faria o `Path.Combine` gravar na pasta de trabalho do processo.
	// ==============================================================================================
	private string ArquivoDeCargos =>
		System.IO.Path.Combine(_store?.Pasta ?? ProjectSettings.GlobalizePath("user://saves"), "cargos.txt");

	private void CarregarCargos()
	{
		_tronos.Clear();
		_personagensDosTronos.Clear();
		CarregarAusencias();   // (2026-10-19) a ausencia de cada dono, que vaga o trono de quem sumiu -- ver `GameServer.CargoLivre.cs`
		CarregarAprendizes();   // (2026-10-19) os vinculos de aprendiz de Kaioshin -- ver `GameServer.AprendizDeKaioshin.cs`
		if (!System.IO.File.Exists(ArquivoDeCargos)) return;
		foreach (string linha in System.IO.File.ReadAllLines(ArquivoDeCargos))
		{
			string[] p = linha.Split('\t');
			if (p.Length < 2 || p[1].Length == 0) continue;
			_tronos[p[0]] = p[1];
			// (2026-10-26) a terceira e a quarta colunas sao o PERSONAGEM (o slot e o nome); a linha de antes, so com a conta, e o trono da
			// conta inteira esperando o primeiro personagem dela entrar
			if (p.Length >= 3 && int.TryParse(p[2], out int slot))
				_personagensDosTronos[p[0]] = (p[1], slot, p.Length >= 4 ? p[3] : "");
		}
		GD.Print($"[server] cargos ocupados: {_tronos.Count} de {Cargos.Todos.Length}");
	}

	private void SalvarCargos()
	{
		try
		{
			// (2026-10-26) ...e o PERSONAGEM, quando a vaga ja tem um: `vaga \t conta \t slot \t nome`
			System.IO.File.WriteAllLines(ArquivoDeCargos,
				_tronos.Select(kv => SlotDoTrono(kv.Key, out int slot)
					? $"{kv.Key}\t{kv.Value}\t{slot}\t{_personagensDosTronos[kv.Key].Nome}"
					: $"{kv.Key}\t{kv.Value}"));
		}
		catch (Exception e) { GD.PushWarning($"[server] nao gravei os cargos: {e.Message}"); }
	}

	/// <summary>
	/// Qual cargo esta conta carrega ("" = nenhum). A CHAVE DO CARGO, e nao a da vaga (2026-10-16): quem esta na 3a vaga do
	/// Kaioshin responde `kaioshin`, e por isso os leitores de cargo do servidor (o kit, os verbs, o vacuo...) nao mudaram.
	///
	/// (2026-10-26) PELO PERSONAGEM DA CONTA QUE ESTA NO JOGO: o cargo e do personagem agora, e uma conta joga um personagem por vez -- entao
	/// "o cargo desta conta" e o do personagem dela que esta aqui (<see cref="CargoDe(ServerPlayer)"/>). Com a conta fora do jogo, o primeiro
	/// trono dela (o que o servidor sabe sem corpo). E por isso que as chamadas `CargoDe(pl.Conta)` espalhadas pelo servidor nao mudaram.
	/// </summary>
	private string CargoDe(string conta) =>
		OnlinePorConta(conta) is { } pl ? CargoDe(pl) : CargoDaVaga(PrimeiraVagaDaConta(conta));

	/// <summary>(2026-10-26) O cargo DESTE personagem ("" = nenhum). Ver <see cref="VagaDe(ServerPlayer)"/>.</summary>
	private string CargoDe(ServerPlayer pl) => CargoDaVaga(VagaDe(pl));

	// =====================================================================
	// AS VAGAS (2026-10-16) -- o cargo de varias almas
	// =====================================================================
	/// <summary>
	/// A CHAVE DE `_tronos` DA VAGA <paramref name="i"/> (0-based) do cargo. A primeira e a propria chave do cargo -- o arquivo de
	/// sempre e os cargos de uma vaga so nao mudam em nada --, e as outras sao `chave#2` ... `chave#N` (`RankDef.Vagas`; hoje so
	/// o Kaioshin tem 5). Cada vaga e um trono inteiro: tem dono, deveres, renome e destituicao proprios.
	/// </summary>
	private static string Vaga(RankDef r, int i) => i == 0 ? r.Chave : $"{r.Chave}#{i + 1}";

	/// <summary>Todas as vagas do cargo, da primeira a ultima.</summary>
	private static IEnumerable<string> VagasDe(RankDef r)
	{
		int n = VagasIgnoradasDeTeste ? 1 : Math.Max(1, r.Vagas);
		for (int i = 0; i < n; i++) yield return Vaga(r, i);
	}

	/// <summary>O cargo de uma chave de vaga: `kaioshin#3` -> `kaioshin`.</summary>
	private static string CargoDaVaga(string vaga)
	{
		int i = vaga.IndexOf('#');
		return i < 0 ? vaga : vaga[..i];
	}

	/// <summary>
	/// A VAGA que esta conta ocupa ("" = nenhuma) -- a chave crua de `_tronos`. Quem tira alguem de um trono tira DAQUI. (2026-10-26) Pelo
	/// personagem da conta que esta no jogo, como o <see cref="CargoDe(string)"/>; fora do jogo, a primeira.
	/// </summary>
	private string VagaDaConta(string conta) =>
		OnlinePorConta(conta) is { } pl ? VagaDe(pl) : PrimeiraVagaDaConta(conta);

	/// <summary>A primeira vaga da conta, de qualquer personagem ("" = nenhuma) -- o `VagaDaConta` de antes de o cargo ser do personagem.</summary>
	private string PrimeiraVagaDaConta(string conta) =>
		_tronos.FirstOrDefault(kv => string.Equals(kv.Value, conta, StringComparison.OrdinalIgnoreCase)).Key ?? "";

	// =====================================================================
	// (2026-10-26) O PERSONAGEM NO TRONO -- o cargo e de quem sentou, e nao da conta inteira
	// =====================================================================

	/// <summary>
	/// O SLOT do personagem que carrega esta vaga. Falso quando a vaga nao tem personagem (vazia, ou o trono de antes, so da conta) ou
	/// quando o personagem guardado e de OUTRA conta (o trono foi reescrito por cima so com a conta).
	/// </summary>
	private bool SlotDoTrono(string vaga, out int slot)
	{
		slot = -1;
		if (!_tronos.TryGetValue(vaga, out string? conta) || conta.Length == 0
			|| !_personagensDosTronos.TryGetValue(vaga, out (string Conta, int Slot, string Nome) p)
			|| !string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase))
			return false;
		slot = p.Slot;
		return true;
	}

	/// <summary>SENTA o personagem na vaga: a conta no `_tronos` (como sempre) e ele ao lado. Quem chama grava (`SalvarCargos`).</summary>
	private void Ocupar(string vaga, ServerPlayer pl)
	{
		_tronos[vaga] = pl.Conta;
		_personagensDosTronos[vaga] = (pl.Conta, pl.Slot, pl.Name);
	}

	/// <summary>Esvazia a vaga -- o dono e o personagem dele.</summary>
	private void Desocupar(string vaga)
	{
		_tronos.Remove(vaga);
		_personagensDosTronos.Remove(vaga);
	}

	/// <summary>
	/// A VAGA DESTE PERSONAGEM ("" = nenhuma): a presa ao slot dele; ou, se nao houver, o trono da conta SEM personagem -- o de antes de
	/// 2026-10-26 --, que se prende a ele AGORA. E o *"O 1o que entrar com ele"* do dono: o primeiro personagem da conta que o servidor ve no
	/// jogo e quem pergunta primeiro (a ficha do login pergunta o cargo). Um corpo sem slot (os forjados de bancada) usa o trono sem prende-lo.
	/// </summary>
	private string VagaDe(ServerPlayer pl)
	{
		if (pl.Conta.Length == 0) return "";
		string? semPersonagem = null;
		foreach ((string vaga, string conta) in _tronos)
		{
			if (!string.Equals(conta, pl.Conta, StringComparison.OrdinalIgnoreCase)) continue;
			if (SlotDoTrono(vaga, out int slot)) { if (slot == pl.Slot) return vaga; }
			else semPersonagem ??= vaga;
		}
		if (semPersonagem == null) return "";
		if (pl.Slot >= 0)
		{
			_personagensDosTronos[semPersonagem] = (pl.Conta, pl.Slot, pl.Name);
			SalvarCargos();
			GD.Print($"[server] cargos: o trono '{semPersonagem}' da conta {pl.Conta} (de quando o cargo era da conta) ficou com {pl.Name}, o primeiro a entrar");
		}
		return semPersonagem;
	}

	/// <summary>
	/// O PERSONAGEM QUE CARREGA ESTA VAGA, se ele esta no jogo agora (nulo = vaga vazia, ou o dono fora). O irmao de conta que esta jogando
	/// NAO e ele. A vaga sem personagem (o trono de antes, so da conta) e de qualquer personagem da conta, como era.
	/// </summary>
	private ServerPlayer? OnlineDoTrono(string vaga)
	{
		if (vaga.Length == 0 || !_tronos.TryGetValue(vaga, out string? conta) || conta.Length == 0) return null;
		bool temSlot = SlotDoTrono(vaga, out int slot);
		return _players.Values.FirstOrDefault(p => EhPessoa(p) && string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase)
												   && (!temSlot || p.Slot == slot));
	}

	/// <summary>Este personagem carrega esta vaga?</summary>
	private bool OcupaOTrono(string vaga, ServerPlayer pl) =>
		vaga.Length > 0 && string.Equals(VagaDe(pl), vaga, StringComparison.OrdinalIgnoreCase);

	/// <summary>A conta tem ALGUM personagem neste cargo (em qualquer vaga dele -- as cinco do Kaioshin inclusas)?</summary>
	private bool ContaTemOCargo(string conta, string cargo) =>
		conta.Length > 0 && Cargos.Get(cargo) is { } r
		&& VagasDe(r).Any(v => _tronos.TryGetValue(v, out string? c) && string.Equals(c, conta, StringComparison.OrdinalIgnoreCase));

	/// <summary>O cargo do personagem (conta, slot), dentro ou fora do jogo: a vaga presa a ele, ou o trono da conta sem personagem.</summary>
	private string CargoDoPersonagem(string conta, int slot)
	{
		string? semPersonagem = null;
		foreach ((string vaga, string c) in _tronos)
		{
			if (!string.Equals(c, conta, StringComparison.OrdinalIgnoreCase)) continue;
			if (SlotDoTrono(vaga, out int s)) { if (s == slot) return CargoDaVaga(vaga); }
			else semPersonagem ??= vaga;
		}
		return semPersonagem != null ? CargoDaVaga(semPersonagem) : "";
	}

	/// <summary>O cargo do personagem desta ASSINATURA, com a conta dele (a assinatura e um hash de conta+slot). "" = nenhum.</summary>
	private string CargoDaAssinatura(string conta, string sig)
	{
		for (int s = 0; s < AccountStore.Slots; s++)
			if (ServerPlayer.AssinaturaDe(conta, s) == sig) return CargoDoPersonagem(conta, s);
		return "";
	}

	/// <summary>O NOME de quem carrega a vaga: o do personagem no jogo; fora dele, o que sentou nela; sem os dois, a conta (o de antes).</summary>
	private string NomeDoTrono(string vaga)
	{
		if (OnlineDoTrono(vaga) is { } vivo) return vivo.Name;
		if (SlotDoTrono(vaga, out _) && _personagensDosTronos[vaga].Nome.Length > 0) return _personagensDosTronos[vaga].Nome;
		return _tronos.TryGetValue(vaga, out string? conta) ? conta : "";
	}

	/// <summary>A primeira vaga ocupada do cargo ("" = vago).</summary>
	private string PrimeiraVagaOcupada(RankDef r) =>
		VagasDe(r).FirstOrDefault(v => _tronos.TryGetValue(v, out string? d) && d.Length > 0) ?? "";

	/// <summary>
	/// O PERSONAGEM APAGADO DEIXA O TRONO: a vaga presa a ele (a assinatura e `hash(conta, slot)` -- a do slot que vagou) e destituida.
	/// Senao o proximo personagem criado no mesmo slot nasceria com o cargo do que saiu. Chamado do `EsquecerAAssinaturaNoMundo`, na hora em
	/// que o personagem e apagado (e de novo na criacao, pelo mesmo funil).
	/// </summary>
	private void VagarOsTronosDaAssinatura(string sig)
	{
		foreach (string vaga in _tronos.Keys.ToList())
			if (SlotDoTrono(vaga, out int slot) && ServerPlayer.AssinaturaDe(_tronos[vaga], slot) == sig)
				Destronar(CargoDaVaga(vaga), "o personagem que o carregava saiu do mundo", _tronos[vaga]);
	}

	/// <summary>As contas que carregam este cargo agora, na ordem das vagas.</summary>
	private List<string> DonosDe(RankDef r)
	{
		var donos = new List<string>();
		foreach (string v in VagasDe(r))
			if (_tronos.TryGetValue(v, out string? d) && d.Length > 0) donos.Add(d);
		return donos;
	}

	/// <summary>A primeira vaga livre do cargo, ou nulo com todas ocupadas.</summary>
	private string? VagaLivre(RankDef r)
	{
		foreach (string v in VagasDe(r))
			if (!_tronos.TryGetValue(v, out string? d) || d.Length == 0) return v;
		return null;
	}

	/// <summary>Quantos jogadores carregam o cargo <paramref name="chave"/> -- o que o povoamento pergunta (`cadaDonoTiraUm`).</summary>
	private int QuantosDonos(string chave) => Cargos.Get(chave) is { } r ? DonosDe(r).Count : 0;

	/// <summary>DEFEITO INJETADO (`--kaioshinteste`): todo cargo tem UMA vaga -- o "um dono por cargo" de antes. Falso em jogo.</summary>
	internal static bool VagasIgnoradasDeTeste;

	private FichaDeRank Ficha(ServerPlayer pl) => new()
	{
		Raca = pl.Race,
		Classe = pl.Class,
		BpBase = pl.Ficha.BP,
		Karma = pl.Karma,
		CargoAtual = CargoDe(pl),

		// OS QUATRO CAMPOS NOVOS. Sem preencher aqui, todo requisito que depende deles passava
		// de graca (bool false e double 0 sao os defaults, e a comparacao e ">=") -- os cargos de
		// Kaio, que o DM gateia por God Ki desperto, abririam pra qualquer um.
		RacaMae = pl.Ficha.Genoma?.MajorityRace ?? pl.Race,
		GodKiDesperto = pl.Ficha.godki?.awakened == true,
		GodKiMaestria = pl.Ficha.godki?.mastery ?? 0,
		Zeni = pl.Ficha.Zeni,

		// ============================ OS DOIS CAMPOS QUE A CONQUISTA TROUXE ============================
		// Quatro cargos carregavam a pendencia *"o port nao tem reputacao de planeta"* e ela caducou --
		// o livro-caixa existe desde a cadeia de sagas e ganhou duas fontes novas (matar habitante,
		// conquistar). O que faltava era CHEGAR aqui: regra escrita nao e regra ligada.
		//
		// O mapa e montado por planeta com povo, que sao os unicos que o DM cita nos requisitos. Nao ha
		// atalho lendo "a reputacao do jogador": ela e por planeta, e um escalar responderia a pergunta
		// errada com um numero plausivel.
		ReputacaoDePlaneta = ReputacaoDe(pl.Conta),
		PlanetasDominados = QuantosDomina(pl.Assinatura),

		// (2026-10-19) OS DOIS CAMPOS DOS CARGOS QUE SAIRAM DO ADMIN: o planeta pre-feito que o King of Acronia pede (Arconia), e o
		// Vegeta condenado que abre o trono vago do Rei -- ver `GameServer.CargoLivre.cs`.
		PlanetasQueDomina = PreFeitosQueDomina(pl.Assinatura),
		VegetaCondenado = VegetaCondenado,
	};

	/// <summary>
	/// REIVINDICAR. Tres portas, nesta ordem, e a ordem decide a mensagem que o jogador le:
	/// o trono precisa estar VAGO, a alma nao pode carregar outro cargo, e os requisitos tem
	/// que estar cumpridos.
	///
	/// (2026-10-19) E A CONCESSAO, E NAO MAIS A PORTA DO JOGADOR: o pacote `C2S.Cargo` chama a PROVA DO ESPIRITO DO CARGO
	/// (`PedirAProvaDoCargo`, `GameServer.ProvaDoCargo.cs`), que passa por aqui DUAS vezes -- com `conceder` falso pra conferir
	/// tudo (as mesmas mensagens) antes de erguer o espirito, e de verdade quando ele cai. Verdadeiro = passou nas portas.
	/// </summary>
	private bool ReivindicarCargo(ServerPlayer pl, string chave, bool conceder = true)
	{
		RankDef? r = Cargos.Get(chave);
		if (r == null) { Avisar(pl, "esse cargo nao existe."); return false; }

		// `if(!usr.client || !usr.signature || usr.KO) return` (`RankQuests.dm:272`), e o morto so reivindica cargo
		// do Outro Mundo: `if(usr.dead && !(key in RQ_SKY))` (`:299`, `:320` -- o `RQ_SKY` e o `DoOutroMundo`
		// daqui). O port nao perguntava nenhum dos dois: nocauteado no chao, ou fantasma no Alem, o cliente mandava
		// o pacote e saia Guardiao da Terra, com o kit na hora.
		if (!CaidoAgeDeTeste)
		{
			if (pl.Ficha.KO) { Avisar(pl, "nao da pra reivindicar cargo nenhum caido."); return false; }
			if (pl.Ficha.dead && !r.DoOutroMundo)
			{ Avisar(pl, $"um morto so reivindica cargo do Outro Mundo -- {r.Nome} e do mundo dos vivos."); return false; }
		}

		// (2026-10-16) POR VAGA: o cargo de varias vagas (o Kaioshin, `RankDef.Vagas`) so recusa com TODAS ocupadas.
		if (string.Equals(CargoDe(pl), r.Chave, StringComparison.OrdinalIgnoreCase)) { Avisar(pl, $"voce JA e {r.Nome}."); return false; }
		// (2026-10-26) O CARGO E DO PERSONAGEM, e a mesma conta nao poe dois no mesmo -- nem com as vagas do Kaioshin sobrando. O dono: *"assim
		// n tem como ter varios personagens da mesma conta caindo no mesmo rank"*.
		if (ContaTemOCargo(pl.Conta, r.Chave))
		{
			Avisar(pl, $"outro personagem da sua conta ja e {r.Nome} -- um cargo nao se repete na mesma conta.");
			return false;
		}
		if (VagaLivre(r) is not { } vaga)
		{
			Avisar(pl, r.Vagas > 1 && !VagasIgnoradasDeTeste ? $"as {r.Vagas} vagas de {r.Nome} estao ocupadas." : $"{r.Nome} ja tem dono.");
			return false;
		}

		// UMA ALMA, UM TRONO. A escada e a excecao: subir de Kaio a Grande Kaio LARGA o cargo
		// anterior em vez de acumular -- senao o mundo perderia um Kaio cardeal a cada promocao.
		string atual = CargoDe(pl);
		bool subindo = atual.Length > 0 && r.Degraus.Contains(atual, StringComparer.OrdinalIgnoreCase);
		if (atual.Length > 0 && !subindo)
		{
			Avisar(pl, $"voce ja carrega {Cargos.Get(atual)?.Nome ?? atual}. Uma alma, um trono.");
			return false;
		}

		// ============================ O PEDAGIO DA ASCENSAO E O RENOME ============================
		// `RQ_PROMO_QUESTS 3` (`RankQuests.dm:53`, cobrado em `:292`): so sobe quem SERVIU no cargo de
		// agora. Enquanto o motor de tarefas nao existia, esta linha era uma pendencia escrita no
		// Grand Kai e no Kaioshin (*"o port nao tem o motor de tarefas"*) e a escada dos Kaios subia so
		// com o requisito -- mais barata que o original, e a divida mais cara do `Ranks.cs`.
		//
		// O RENOME E DE QUEM CARREGA O CARGO AGORA, e nao do trono: ver `RenomeDe`. Herdar o servico de
		// quem veio antes seria comprar promocao com o trabalho alheio.
		// ======================================================================================
		if (subindo && !MissoesDeCargo.PodeAscender(RenomeDe(VagaDe(pl))))
		{
			int tem = RenomeDe(VagaDe(pl));
			Avisar(pl, $"ainda falta RENOME para almejar algo maior: {tem}/{MissoesDeCargo.TarefasParaAscender} "
					 + $"tarefas cumpridas como {Cargos.Get(atual)?.Nome ?? atual}. Sirva ao cargo primeiro "
					 + "(verb Rank Duty).");
			return false;
		}

		if (Cargos.OqueFalta(r, Ficha(pl)) is { } falta)
		{
			Avisar(pl, $"{r.Nome} exige: {falta}.");
			return false;
		}

		if (!conceder) return true;

		if (subindo) Desocupar(VagaDe(pl));
		Ocupar(vaga, pl);
		SalvarCargos();

		// (2026-10-19) A CAMPANHA: `if(key == "president") M.zenni -= RQ_PRESIDENT_CAMPAIGN //a campanha cobra ao VENCER`
		// (`RankQuests.dm:405`). O requisito so conferia o bolso; quem cobrava era o fim da prova, que nao existia -- e o
		// Presidente saia de graca. A porta de cima ja garantiu que o dinheiro esta la.
		if (r.Campanha > 0 && !ProvaDoCargo.CampanhaDeGracaDeTeste)
		{
			pl.Ficha.Zeni -= r.Campanha;
			MandarCarteira(pl);
			Avisar(pl, $"a campanha custou {r.Campanha:N0} zeni.");
		}

		GD.Print($"[server] {pl.Name} ({pl.Conta}) assumiu o cargo '{r.Nome}'"
				 + (subindo ? $" (largou {atual})" : ""));

		// ============================ O KIT CHEGA AGORA, E NAO NO TIQUE SEGUINTE ============================
		// **ESTA LINHA FALTAVA, e quem a encomendou foi a bancada de producao** (`--cargovivo`,
		// `GameServer.CargoVivoTeste.cs:21`): *"`ReivindicarCargo` e `Outorgar` NAO chamam
		// `ReconciliarDadiva`"*. As duas escreviam o trono e paravam -- quem reivindicava um cargo
		// recebia o kit **de zero a um segundo depois**, quando o `TickDosCargos` passasse.
		//
		// A reconciliacao ja era idempotente e o tique ja a chamava, entao a janela nunca virou bug de
		// jogo permanente -- ela virava a pior forma de latencia que existe: o mundo anunciava
		// "VOCE E O NOVO EREMITA TARTARUGA", o jogador abria a aba de skills e o Kamehameha nao estava
		// la. Um segundo depois estava. Nao ha como distinguir isso de "o jogo esta quebrado".
		//
		// **ANTES do `AnunciarCargo`**, e a ordem e o ponto: o anuncio manda `MandarCargos` pra todo
		// mundo, e a reconciliacao manda `MandarSkills` pro dono. Nesta ordem, o pacote de skills chega
		// ANTES do "voce e o novo X" -- o jogador le o titulo com o kit ja na mao.
		// =================================================================================================
		ReconciliarDadiva(pl);

		AnunciarCargo(r, pl, subindo ? atual : "");
		AparaOPovoDoCargo(r.Chave);   // (2026-10-16) cada Kaioshin jogador tira um Supremo Kaioh NPC do Ceu
		return true;
	}

	/// <summary>
	/// PoE ALGUEM NO TRONO IGNORANDO OS REQUISITOS. E o `Give (Rank)` do original, e a unica
	/// porta de entrada que a reivindicacao nao cobre.
	///
	/// Existe porque cargo, hoje, so se consegue cumprindo requisito -- e isso deixa sem conserto
	/// o caso em que ele TRAVOU: o dono abandonou o jogo, ou o requisito mudou depois de alguem ja
	/// ter assumido. Sem admin, o trono fica preso pra sempre.
	///
	/// O QUE ELE NAO IGNORA: "uma alma, um trono" continua valendo dos dois lados -- quem recebe
	/// larga o que tinha, e quem estava no trono sai dele.
	/// </summary>
	private void Outorgar(RankDef r, ServerPlayer pl)
	{
		string vagaVelha = VagaDe(pl);
		string largou = CargoDaVaga(vagaVelha);
		if (vagaVelha.Length > 0) Desocupar(vagaVelha);
		// (2026-10-26) ...e o OUTRO personagem da mesma conta que estivesse neste cargo sai dele: o cargo nao se repete na conta, e o admin
		// escolheu ESTE personagem
		foreach (string v in VagasDe(r).ToList())
			if (_tronos.TryGetValue(v, out string? c) && string.Equals(c, pl.Conta, StringComparison.OrdinalIgnoreCase)) Desocupar(v);

		// (2026-10-16) A VAGA: a livre, se o cargo tiver (o Kaioshin, `RankDef.Vagas`); com todas ocupadas, a PRIMEIRA troca de dono.
		string vaga = VagaLivre(r) ?? r.Chave;

		// O EX-DONO E GUARDADO PRA DEPOIS DO `Ocupar`: a reconciliacao dele so responde certo
		// com o trono JA trocado, senao ela leria o cargo velho e devolveria o mesmo kit.
		// (2026-10-26) E E O PERSONAGEM que estava na vaga, e nao todo personagem da conta dele.
		ServerPlayer? velho = OnlineDoTrono(vaga);
		if (velho != null) Avisar(velho, $"voce nao e mais {r.Nome}.");

		Ocupar(vaga, pl);
		SalvarCargos();
		GD.Print($"[server] {pl.Name} ({pl.Conta}) recebeu o cargo '{r.Nome}' por outorga de admin");

		// A MESMA JANELA DE 1 s DO `ReivindicarCargo`, fechada dos DOIS lados -- ver o bloco de la. O
		// `Entronizar` (`GameServer.CargoPortas.cs:59-66`) ja fazia exatamente isto para as portas
		// novas, com o argumento escrito: *"sem esta linha ele so o perderia no proximo tique de
		// reconciliacao -- e num duelo pelo titulo isso e uma janela em que dois 'Deuses' existem"*. A
		// outorga de admin tinha a mesma janela e nao tinha a linha.
		if (velho != null && velho != pl) ReconciliarDadiva(velho);
		ReconciliarDadiva(pl);

		AnunciarCargo(r, pl, largou);
		AparaOPovoDoCargo(r.Chave);
	}

	/// <summary>
	/// O MUNDO INTEIRO FICA SABENDO. Cargo e posicao social: se ninguem soube, nao houve.
	/// Um lugar so porque a reivindicacao e a outorga precisam anunciar igual.
	/// </summary>
	private void AnunciarCargo(RankDef r, ServerPlayer pl, string largou)
	{
		foreach (ServerPlayer o in _players.Values)
		{
			// `falante: null` -- e o servidor anunciando pro mundo inteiro, e nao alguem falando
			// perto de voce: ninguem fica mais familiar por ter lido a outorga de um cargo.
			Mandar(o, Protocol.Fala.Sistema, "",
				o == pl ? $"VOCE E O NOVO {r.Nome.ToUpperInvariant()}."
						: $"{pl.Name} assumiu o cargo de {r.Nome}.",
				falante: null);

			// E A LISTA VAI JUNTO. O cliente so pede a lista de cargos quando ela esta VAZIA -- ou
			// seja, quem ja tinha aberto a aba continuaria lendo o dono antigo ate relogar. A
			// reivindicacao escapava disso por acidente (o handler do pedido responde logo depois);
			// a outorga de admin nao tinha ninguem pra responder por ela.
			MandarCargos(o);
		}
		if (largou.Length > 0) Avisar(pl, $"voce largou {Cargos.Get(largou)?.Nome ?? largou}.");
	}

	/// <summary>A lista de cargos e quem os ocupa -- e o que a aba mostra.</summary>
	private void MandarCargos(ServerPlayer pl)
	{
		var w = Protocol.Begin(Protocol.S2C.Cargos);
		w.Put((byte)Cargos.Todos.Length);
		FichaDeRank f = Ficha(pl);
		foreach (RankDef r in Cargos.Todos)
		{
			w.Put(r.Chave);
			// (2026-10-16) OS DONOS, separados por virgula: o cargo de varias vagas tem ate cinco (ver `RankDef.Vagas`)
			List<string> donos = DonosDe(r);
			// (2026-10-26) o NOME pela VAGA: o personagem que sentou nela, e nao "algum personagem da conta"
			w.Put(string.Join(", ", VagasDe(r).Where(v => _tronos.TryGetValue(v, out string? d) && d.Length > 0).Select(NomeDoTrono)));
			// o que falta PRA MIM: e a unica parte que muda de jogador pra jogador
			// (2026-10-19) ...ou que o espirito dele esta sendo enfrentado agora -- e o botao "Reivindicar" some (`ProvaQueOcupa`)
			w.Put(Cargos.OqueFalta(r, f) ?? ProvaQueOcupa(r, pl) ?? "");

			// ============================ E O QUE O CARGO DA, QUE NUNCA SAIU DAQUI ============================
			// O painel mandava chave, dono e o que falta -- e mais nada. **Ninguem no jogo conseguia
			// descobrir o que um cargo entrega antes de toma-lo**, e a informacao existia em DOIS
			// lugares: a `RankDef.Desc` (texto, escrita pra ser lida) e a `DadivaDeCargo` (a tabela de
			// verdade, a que o `ReconciliarDadiva` executa).
			//
			// Sao os dois campos, e nao um: a `Desc` diz o que o cargo E ("a escola que forma
			// protetores"), a dadiva diz o que ele DA. Um sem o outro deixa metade da escolha no escuro.
			// ==========================================================================================
			w.Put(r.Desc);
			w.Put(OQueOCargoEntrega(r.Chave));

			// (2026-10-16) AS VAGAS E QUANTAS ESTAO LIVRES, no FIM do cargo: e o que deixa a aba oferecer "Reivindicar" num cargo que
			// ja tem dono (o Kaioshin com dois de cinco) -- o "dono vazio = vago" de antes so sabia responder pra uma vaga.
			int vagas = VagasDe(r).Count();
			w.Put((byte)vagas);
			w.Put((byte)Math.Max(0, vagas - donos.Count));
		}

		// A BANCADA ESCUTA AQUI, e o lugar e o ponto todo: e ESTE writer que vira os bytes do fio.
		// Uma bancada que chamasse `OQueOCargoEntrega` direto leria a TABELA -- e a pergunta do dono
		// ("o painel ainda anuncia o Mafuba?") e sobre o que CHEGA no cliente, que passa por um
		// `Put`/`GetString` com limite de tamanho no meio. Nulo em jogo. Ver `GameServer.SeloTeste.cs`.
		EscutaDeCargos?.Add((pl.Id, w));
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// ============================ O QUE ESTE CARGO ENTREGA, EM UMA LINHA HONESTA ============================
	/// A lista sai da <see cref="DadivaDeCargo"/> -- **a mesma tabela que o `ReconciliarDadiva`
	/// executa de verdade** --, e nao da `RankDef.Concede`, que e texto escrito a mao descrevendo o
	/// DM. A diferenca nao e estilo: `Concede` promete pelo original (o Guardiao "também cria
	/// Esferas"), e a dadiva diz o que ESTE servidor poe no livro. Uma tela alimentada pela promessa
	/// mentiria; alimentada pela tabela, ela acompanha sozinha o dia em que a tabela mudar.
	///
	/// **E ELA SEPARA O QUE FUNCIONA DO QUE E BOTAO MUDO**, pelo mesmo censo que o
	/// <see cref="ContarOQueOCargoDeu"/> ja usa (`CensoDeSkills.SistemaQueFalta`). Esconder a metade
	/// muda faria o painel prometer uma Genkidama que nao dispara -- e mostrar tudo junto, sem marcar,
	/// faria o jogador chamar de defeito o que e divida declarada. No dia em que a Genkidama for
	/// portada, ela troca de lado sozinha.
	/// ====================================================================================================
	/// </summary>
	private string OQueOCargoEntrega(string chave)
	{
		string[] kit = DadivaDeCargo.De(chave);
		if (_skills == null || kit.Length == 0) return "";

		List<string> prontas = [], mudas = [];
		foreach (string path in kit)
		{
			Skill? s = _skills.Get(path);
			if (s == null || s.Nome.Length == 0) continue;
			(CensoDeSkills.SistemaQueFalta(s) == null ? prontas : mudas).Add(s.Nome);
		}

		string texto = prontas.Count > 0 ? string.Join(", ", prontas) : "";
		// (2026-10-23) O RITUAL DO KAIOSHIN NAO E SKILL DE LIVRO (e do trono, como a travessia do juiz), e e o que o kit ganhou no lugar
		// do Mistico (o dono): entra na linha pelo nome, na frente -- e quem escolhe o cargo descobre o ritual aqui.
		if (string.Equals(chave, RitualDoKaioshin.Cargo, StringComparison.OrdinalIgnoreCase))
			texto = RitualDoKaioshin.Nome + (texto.Length > 0 ? ", " + texto : "");
		if (mudas.Count > 0)
			texto += (texto.Length > 0 ? " | " : "") + "ainda mudo neste servidor: " + string.Join(", ", mudas);
		return texto;
	}

	/// <summary>
	/// O NOME de quem ocupa, e nao a conta. A conta e a chave interna; mostrar "roboA" onde
	/// deveria estar "Goku" entrega o login de quem joga.
	/// </summary>
	private string NomeDoDono(string? conta)
	{
		if (string.IsNullOrEmpty(conta)) return "";
		ServerPlayer? vivo = _players.Values.FirstOrDefault(
			p => string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase));
		if (vivo != null) return vivo.Name;

		// offline: nao vamos abrir save pra desenhar uma lista. A conta serve de rotulo ate
		// a pessoa entrar -- e cargo e de conta, nao de personagem.
		return conta;
	}
}
