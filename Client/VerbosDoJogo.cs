namespace Jandirus.Client;

/// <summary>
/// OS VERBS QUE TODO PERSONAGEM TEM -- as abas "Other" e "Admin" do menu.
///
/// ============================ O QUE ENTROU E O QUE NAO ============================
/// O original tem 91 verbs em "Other" e ~90 em "Admin". O dono pediu "os principais e mais
/// importantes... nao precisa colocar verb que muda variavel tipo o que muda ganhos de BP".
///
/// A regra que usei pra cortar: **so entra verb com mecanica pronta**. Um botao que nao faz nada e
/// pior que um botao que nao existe, porque ele promete -- e este projeto ja tropecou nisso mais de
/// uma vez. Ficaram de fora, por enquanto, os que dependem de sistema nao portado (casamento,
/// faccao, torneio, biografia, musica em streaming, itens) e os de balanceamento.
/// ==================================================================================
///
/// OS DE ADMIN AGEM SOBRE O ALVO MARCADO, e nao sobre um nome digitado. O BYOND tinha `input()`
/// bloqueante pra pedir o nome; aqui ja existe um gesto melhor e que o jogador ja usa -- duplo
/// clique marca alguem. Menos codigo, menos caixa de dialogo, e funciona com nome repetido.
///
/// A ABA ADMIN SO EXISTE PRA QUEM E ADMIN, e quem decide isso e o servidor
/// (<see cref="Jandirus.Net.Protocol.Poder.Admin"/>, ver `MenuJogo.Abas`). Esconder o botao e
/// conveniencia; a permissao e conferida DE NOVO no servidor, sempre.
/// </summary>
public static class VerbosDoJogo
{
	private static bool _registrados;

	private static GameClient? C => GameClient.Instance;

	/// <summary>Ha alguem marcado? E o que os verbs de admin exigem.</summary>
	private static bool TemAlvo() => (C?.AlvoId ?? 0) != 0;

	private static void NoAlvo(string cmd)
	{
		int id = C?.AlvoId ?? 0;
		if (id == 0) { Chat.Sistema("marque alguem antes (duplo clique nele)."); return; }
		C?.SendVerbo(cmd, id.ToString());
	}

	/// <summary>Chamado quando o personagem entra no mundo. Idempotente.</summary>
	public static void Registrar()
	{
		if (_registrados) return;
		_registrados = true;

		// =====================================================================
		// OTHER
		// =====================================================================
		Verbos.Registrar(new Verbo("Training Session", Verbos.Outros,
			"Comeca uma sessao de treino: marca o seu poder de agora pra medir o ganho depois.",
			() => C?.SendVerbo("sessao_nova")));

		Verbos.Registrar(new Verbo("View Session", Verbos.Outros,
			"Quanto de poder voce ganhou desde que a sessao comecou.",
			() => C?.SendVerbo("sessao_ver")));

		// O `knockbackon` do original. Existe pra treinar com alguem sem arremessa-lo a cada golpe.
		Verbos.Registrar(new Verbo("Toggle Knockback", Verbos.Outros,
			"Liga e desliga o arremesso dos SEUS golpes.",
			() => C?.SendVerbo("knockback")));

		Verbos.Registrar(new Verbo("Goto Spawn", Verbos.Outros,
			"Volta ao ponto de partida. A saida pra quem ficou preso.",
			() => C?.SendVerbo("spawn")));

		Verbos.Registrar(new Verbo("Clear Buffs", Verbos.Outros,
			"Derruba todos os efeitos temporarios de cima de voce.",
			() => C?.SendVerbo("limpar_buffs")));

		// OS TRES ABAIXO NAO SAO NOVOS NO SERVIDOR -- os canais ja existiam e ninguem os
		// chamava. Eram sistemas inteiros sem porta de entrada.
		Verbos.Registrar(new Verbo("Ranks", Verbos.Outros,
			"Os cargos do mundo: quem ocupa cada um e o que falta pra voce.",
			() => C?.SendCargo("")));

		Verbos.Registrar(new Verbo("Tech Catalog", Verbos.Outros,
			"O catalogo de construcoes, com o custo e o motivo de cada nao.",
			() => C?.SendTech("lista")));

		Verbos.Registrar(new Verbo("Bolt", Verbos.Outros,
			"Aparafusa (ou solta) a construcao ao seu lado. Sem aparafusar, ela nao funciona.",
			() => C?.SendTech("aparafusar")));

		Verbos.Registrar(new Verbo("Study", Verbos.Outros,
			"Debruca-se sobre a bancada de pesquisa. E o unico jeito de ganhar tecnologia.",
			() => C?.SendTech("estudar")));

		Verbos.Registrar(new Verbo("Who", Verbos.Outros,
			"Quem esta no mundo agora, e onde.",
			() => C?.SendVerbo("quem")));

		// O BANCO. Ele existe nos mapas do original desde sempre (dezessete deles, um por cidade)
		// e ate agora era celula de tilemap -- desenho parado. Agora e uma construcao de verdade,
		// e estes tres verbos sao o que ele FAZ. Sem argumento = tudo, que e o uso de sempre:
		// chegar, guardar, sair. Ver `GameServer.Banco.cs`.
		Verbos.Registrar(new Verbo("Bank: Balance", Verbos.Outros,
			"Quanto voce tem no bolso e quanto tem guardado. Precisa estar num banco.",
			() => C?.SendVerbo("banco_ver")));

		Verbos.Registrar(new Verbo("Bank: Deposit All", Verbos.Outros,
			"Guarda todo o zeni do bolso. O que esta no banco nao se perde ao morrer.",
			() => C?.SendVerbo("banco_depositar")));

		Verbos.Registrar(new Verbo("Bank: Withdraw All", Verbos.Outros,
			"Tira do cofre tudo o que voce guardou.",
			() => C?.SendVerbo("banco_sacar")));

		// =====================================================================
		// ADMIN -- o servidor confere de novo. Ver GameServer.Admin.cs.
		//
		// SO OS QUE NAO PEDEM TEXTO moram aqui. Os que precisam de um nome ou de um numero
		// (anunciar, promover, banir, quantos marcos) vivem no PAINEL da aba Admin, que tem campo
		// de digitacao -- no BYOND esses eram justamente os que abriam um `input()`.
		//
		// Ficaram de fora do port, com motivo: os que so mexem em variavel de balanceamento
		// (`Gravity_Cap`, `Change_Ascension`, `Set_KO_Time_Mult`, `Global_EXP_Rate`), que o dono
		// pediu pra nao trazer; e os que dependem de sistema nao portado (torneio, conquista de
		// planetas, esferas, fusao, magia, dungeons).
		//
		// O CLIMA SAIU DESSA LISTA: ele existe agora (ver `Core.World.Clima`), e forcar um tipo
		// e escolher a forca moram no PAINEL da aba Admin, porque pedem dois argumentos.
		// =====================================================================

		// ---------------------------------------------------------- corpo do alvo
		Verbos.Registrar(new Verbo("Heal", Verbos.Admin,
			"Cura o alvo marcado por completo -- membros inclusive. Sem alvo, cura voce.",
			() => C?.SendVerbo("admin_curar", (C?.AlvoId ?? 0).ToString())));

		// `MassRevive()` (Admin.dm:294) aplicado a um so: de pe, inteiro, Ki cheio.
		Verbos.Registrar(new Verbo("Revive Target", Verbos.Admin,
			"Poe o alvo marcado de pe, mesmo morto. Sem alvo, revive voce.",
			() => C?.SendVerbo("admin_reviver", (C?.AlvoId ?? 0).ToString())));

		Verbos.Registrar(new Verbo("Knock Out Target", Verbos.Admin,
			"Poe o alvo marcado no chao.",
			() => NoAlvo("admin_kb"), TemAlvo));

		// o `Kill()` do original (Admin.dm:350)
		Verbos.Registrar(new Verbo("Kill Target", Verbos.Admin,
			"Mata o alvo marcado. Ele renasce pelo caminho normal.",
			() => NoAlvo("admin_matar"), TemAlvo));

		// `World_Heal()` (Admin.dm:410) + `MassRevive()`
		Verbos.Registrar(new Verbo("Heal Everyone", Verbos.Admin,
			"Restaura todo mundo do servidor: vida, membros e Ki.",
			() => C?.SendVerbo("admin_curar_todos")));

		// `KO_All_in_View()` (Admin.dm:394) / `MassKO()` (MapSave.dm:2)
		Verbos.Registrar(new Verbo("Knock Out Everyone Here", Verbos.Admin,
			"Poe no chao todo mundo da SUA zona. Voce fica de pe.",
			() => C?.SendVerbo("admin_kb_todos")));

		// ---------------------------------------------------------- ir e trazer
		Verbos.Registrar(new Verbo("Go To Target", Verbos.Admin,
			"Vai ate o alvo marcado.",
			() => NoAlvo("admin_ir"), TemAlvo));

		Verbos.Registrar(new Verbo("Bring Target", Verbos.Admin,
			"Traz o alvo marcado ate voce.",
			() => NoAlvo("admin_trazer"), TemAlvo));

		// `MassSummon()` (Admin.dm:305)
		Verbos.Registrar(new Verbo("Bring Everyone", Verbos.Admin,
			"Traz TODO MUNDO do servidor pra onde voce esta.",
			() => C?.SendVerbo("admin_trazer_todos")));

		// `Return_Mob_To_Spawn()` (SpawnPoints.dm:37) / `Send_Spawn()` (Admin.dm:30)
		Verbos.Registrar(new Verbo("Send Target To Spawn", Verbos.Admin,
			"Manda o alvo marcado de volta ao ponto de partida.",
			() => NoAlvo("admin_spawn_alvo"), TemAlvo));

		// ---------------------------------------------------------- enxergar
		// O `Assess()` (Assess.dm) -- o verb de admin que mais importa: e o unico jeito de ler o BP
		// alheio sem scouter, e sem ele nao ha como julgar uma denuncia de trapaca.
		Verbos.Registrar(new Verbo("Assess Target", Verbos.Admin,
			"A ficha COMPLETA do alvo marcado, sem sigilo: BP, forma, maestrias, corpo, conta.",
			() => C?.SendVerbo("admin_ficha", (C?.AlvoId ?? 0).ToString())));

		// `AssessAll()` (Admin.dm:400) + `BP_Lists()` (Stat Balance.dm:23)
		Verbos.Registrar(new Verbo("Assess All", Verbos.Admin,
			"Todo mundo do servidor, do mais forte pro mais fraco.",
			() => C?.SendVerbo("admin_fichas")));

		Verbos.Registrar(new Verbo("Who (admin)", Verbos.Admin,
			"A lista de quem esta no mundo, com a zona de cada um.",
			() => C?.SendVerbo("admin_quem")));

		// `Races()` (Admin.dm:286)
		Verbos.Registrar(new Verbo("Races", Verbos.Admin,
			"Quantos de cada raca estao jogando agora.",
			() => C?.SendVerbo("admin_racas")));

		// `AllIPs()` (Admin.dm:254) -- e assim que se descobre que duas contas sao a mesma pessoa
		Verbos.Registrar(new Verbo("IPs", Verbos.Admin,
			"De que endereco cada um esta conectado.",
			() => C?.SendVerbo("admin_ips")));

		// `Galaxy_Status()` (ProceduralSpace.dm:1704)
		Verbos.Registrar(new Verbo("Galaxy Status", Verbos.Admin,
			"A seed do universo, as zonas com gente e os planetas da sua chunk.",
			() => C?.SendVerbo("admin_galaxia")));

		// ---------------------------------------------------------- punir
		// `Boot()` (Admin.dm:450)
		Verbos.Registrar(new Verbo("Boot Target", Verbos.Admin,
			"Desconecta o alvo marcado. Ele pode voltar.",
			() => NoAlvo("admin_expulsar"), TemAlvo));

		// `Mute()`/`UnMute()` (Admin.dm:459-472). Alterna: o mesmo botao cala e descala.
		Verbos.Registrar(new Verbo("Mute Target", Verbos.Admin,
			"Cala (ou descala) o alvo marcado. Vale pra conta, nao so pro personagem.",
			() => NoAlvo("admin_calar"), TemAlvo));

		// `UNMUTEALL()` (Admin.dm:275)
		Verbos.Registrar(new Verbo("Unmute All", Verbos.Admin,
			"Devolve a fala a todo mundo que estava calado.",
			() => C?.SendVerbo("admin_descalar")));

		// ---------------------------------------------------------- dar
		// `Reward()` (Rewards.dm:11) -- um marco por clique
		Verbos.Registrar(new Verbo("Give Milestone", Verbos.Admin,
			"Da um marco de habilidade ao alvo marcado. Sem alvo, a voce.",
			() => C?.SendVerbo("admin_marco", $"{C?.AlvoId ?? 0}|1")));

		// `Global_Points()` -- "Global - Give all Milestones." (Stat Balance.dm:13)
		Verbos.Registrar(new Verbo("Give Milestone to Everyone", Verbos.Admin,
			"Da um marco de habilidade a cada jogador do servidor.",
			() => C?.SendVerbo("admin_marco_todos", "1")));

		// `Edit_Masteries()` (Mastery Master.dm:183). So a forma ATUAL: maestria custa tres horas
		// dentro dela, e "dominar tudo" apagaria a progressao inteira de alguem num clique.
		Verbos.Registrar(new Verbo("Master Current Form", Verbos.Admin,
			"Poe em 100% a maestria da forma em que o alvo esta AGORA.",
			() => C?.SendVerbo("admin_dominar", (C?.AlvoId ?? 0).ToString())));

		// ---------------------------------------------------------- mundo e servidor
		// `Invisible()` (Admin.dm:332) -- usa a mesma invisibilidade da tecnica, sem segundo caminho
		Verbos.Registrar(new Verbo("Toggle Invisible", Verbos.Admin,
			"Some da vista de todo mundo (e volta).",
			() => C?.SendVerbo("admin_invisivel")));

		// `Restaurar_Planeta()` (Planets.dm:258), na escala que este port tem: a zona atual
		Verbos.Registrar(new Verbo("Restore Scenery", Verbos.Admin,
			"Refaz as paredes e o chao que os golpes derrubaram nesta zona.",
			() => C?.SendVerbo("admin_consertar_cenario")));

		// `SaveAll()` (Admin.dm:178) / `Force_All_Backup_Save()` (Misc.dm:437)
		Verbos.Registrar(new Verbo("Save All", Verbos.Admin,
			"Grava agora todos os personagens, o mundo e os cargos.",
			() => C?.SendVerbo("admin_salvar")));

		// A FICHA DO CEU no chat. O painel da aba mostra o TIPO, que muda devagar; os numeros
		// vivos (forca, cinza, quanto a nuvem encobre) so fazem sentido lidos no instante em que
		// se pede -- por isso saem no chat e nao numa pagina que se remonta sozinha.
		Verbos.Registrar(new Verbo("Weather Report", Verbos.Admin,
			"O ceu desta zona agora: o que esta caindo, com que forca, e o que pode cair aqui.",
			() => C?.SendVerbo("admin_clima_ficha")));

		// Forcar um clima pede DOIS argumentos (qual e quao forte) e mora no painel da aba, junto
		// do seletor. Soltar nao pede nenhum, entao cabe aqui -- e e a saida rapida de quem
		// deixou uma nevasca ligada.
		Verbos.Registrar(new Verbo("Release Weather", Verbos.Admin,
			"Solta o ceu desta zona: o clima volta a ser sorteado pelo relogio do mundo.",
			() => C?.SendVerbo("admin_clima_natural")));
	}

	/// <summary>Zera o registro -- o personagem trocou (volta ao menu de slots).</summary>
	public static void Limpar() => _registrados = false;
}
