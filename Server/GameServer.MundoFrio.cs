using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O MUNDO GERADO EM QUE ALGUEM PISA TEM QUE EXISTIR NO SERVIDOR -- por qualquer porta, e nao so pelo pouso.
///
/// ============================ O ACHADO (2026-10-10, investigando a gravidade) ============================
/// O personagem do dono esta salvo em `Verdejante-113`, um mundo sorteado. Os tres logins seguintes ao
/// relato dele tem a mesma forma no log:
///
///     [server] Kentaro entrou (id 109) em Verdejante-113#14007899197994498188
///     [planeta] Planeta: Jardim, 223x223 tiles, gravidade 4x gerado em 14,3 ms      <- o CLIENTE gerou
///     (nenhum "[server] nascendo Verdejante-113")                                    <- o servidor, nao
///
/// Mundo gerado nao vai pro disco: ele e funcao pura da seed, e o servidor o refaz quando precisa. So
/// que "quando precisa" era UM lugar -- o `PousarEmProcedural`, que roda quando um corpo em ORBITA
/// encosta no disco. Quem entra por qualquer outra porta num mundo que o servidor ainda nao tem (o
/// LOGIN depois de um reinicio; a saida de uma nave, de um selo ou de uma mente pra um mundo que a poda
/// ja soltou) fica num mundo FRIO:
///
///   * `MapaDaZona` devolve nulo -- o passo e validado so por velocidade, a IA nao tem chao e a borda do
///     mapa nao da a volta;
///   * `DecolarDeProcedural` nao acha o mundo e a decolagem cai no "este lugar nao fica no mapa do
///     universo" -- **ninguem sai do planeta** ate alguem pousar nele vindo de orbita;
///   * tudo o que nasce JUNTO do mundo (a colheita do `TickDasGeracoes`) simplesmente nao nasce.
///
/// E a mesma familia do defeito da gravidade (`GameServer.GravidadeDoChao.cs`): a regra morava num
/// chamador so, e as outras portas a esqueciam.
/// ======================================================================================================
///
/// ============================ POR QUE NINGUEM ENCOMENDAVA: FALTAVA O CAMINHO DE VOLTA ============================
/// `Encomendar` pede o corpo celeste do mundo (`PlanetaNoEspaco`: e pra la que a decolagem devolve), e
/// quem chega por pouso o tem na mao -- e o disco em que acabou de encostar. Quem chega por login tem so
/// a `ZoneKey`. A volta da zona pro corpo agora existe (<see cref="CorpoGerado.DaZona"/>), conferida
/// pela seed.
/// ================================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada --gravidadeteste): ninguem acorda o mundo gerado em que se entra sem pousar --
	/// ele fica FRIO (sem colisao, e sem ter de onde decolar), que e o que acontecia.
	/// </summary>
	internal bool MundoFrioFicaFrioDeTeste;

	/// <summary>Zonas que ja ouviram "isto nao e um corpo deste universo": o aviso sai uma vez por zona, e nao por passo.</summary>
	private readonly HashSet<ulong> _mundosSemCorpo = [];

	/// <summary>
	/// ALGUEM PISOU NESTA ZONA: se ela e um mundo sorteado que o servidor ainda nao tem, ele comeca a nascer.
	///
	/// Chamado do `AoPisarNoMundo`, que e por onde passam as DUAS portas de entrada num lugar (o `Entrar`
	/// e o `MoveToZone`) -- um gancho em cada verbo que teleporta seria a lista que alguem esquece.
	///
	/// NAO ESPERA NADA: a geracao roda fora do tique (ver <see cref="Encomendar"/>) e o `TickDasGeracoes`
	/// a colhe em seguida. Por alguns milissegundos o corpo ainda esta no mundo frio de antes; depois o
	/// chao existe pra ele e pra todo mundo que estiver la.
	///
	/// AS TRES RECUSAS:
	///   * mundo ja vivo ou ja nascendo -- e o caso de todo pouso, que encomenda antes de entrar;
	///   * mundo DESTRUIDO -- cadaver nao tem chao pra gerar (quem acorda num e levado embora por
	///     `OndeEsteCorpoPodeAcordar`);
	///   * zona que nao e um corpo deste universo (save de outra seed de mundo, zona de bancada): nao ha
	///     de onde tirar o lugar dela no ceu, e ela fica como estava.
	/// </summary>
	private void AcordarOMundoGerado(ZoneKey zona)
	{
		if (MundoFrioFicaFrioDeTeste || !EhZonaProcedural(zona)) return;
		if (_zonasGeradas.ContainsKey(zona.Hash) || _gerando.ContainsKey(zona.Hash)) return;
		if (ZonaMorta(zona)) return;

		if (CorpoGerado.DaZona(SeedDoUniverso, zona) is not { } achado)
		{
			if (_mundosSemCorpo.Add(zona.Hash))
				GD.Print($"[server] '{zona}' nao e um corpo deste universo -- a zona fica sem mundo no servidor");
			return;
		}

		Encomendar(zona, achado.Corpo);
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada --gravidadeteste): o mundo que nasce com gente dentro nasce NU -- sem a camada
	/// das construcoes e das bases, que so a chegada de um corpo escrevia no mapa.
	/// </summary>
	internal bool MundoAcordadoNasceNuDeTeste;

	/// <summary>
	/// O MUNDO ACABOU DE NASCER E JA TEM GENTE DENTRO: ele se veste AGORA. Chamado da colheita
	/// (`TickDasGeracoes`).
	///
	/// ============================ QUEM VESTIA O MAPA ERA A CHEGADA ============================
	/// O terreno que a thread devolve e so o da seed. O que alguem pos em cima -- construcao densa, nave
	/// parada, parede e porta de base -- vira colisao em duas chamadas que moram no caminho de quem
	/// CHEGA: o `MandarObras` (`AplicarColisaoDasObras`) e o `MandarBlocos` (`AssentarBase`). No pouso a
	/// ordem fecha sozinha: o mundo nasce, e so entao o `MoveToZone` poe o corpo nele e veste o mapa.
	///
	/// Quem LOGA num mundo frio inverteu a ordem: as duas chamadas do `Entrar` rodaram antes de haver mapa
	/// (e nao escreveram nada), e o mundo nasce depois, com o corpo ja dentro. Sem este passo o servidor
	/// deixaria atravessar a parede da base que o cliente desenha -- as duas pontas discordando sobre onde
	/// ha parede, que e o que o `MoveRules` existe pra impedir.
	/// =========================================================================================
	///
	/// SO COM GENTE DENTRO: num mundo vazio (todo pouso) quem veste continua sendo a chegada, e fazer o
	/// trabalho duas vezes seria so custo.
	///
	/// AS OUTRAS CAMADAS JA TEM QUEM AS REPONHA, e por isso nao estao aqui: o cenario derrubado e as rebrotas
	/// voltam no `AoColherOMundo` (`GameServer.Lenha.cs`), que roda na mesma colheita, logo antes; as rochas
	/// de minerio perguntam pelo mapa a cada tique ate ele existir (`ReceberNoPlaneta`, em
	/// `GameServer.Rochas.cs`) -- pergunta que, num mundo frio, so passou a ter resposta com o
	/// <see cref="AcordarOMundoGerado"/>.
	/// </summary>
	private void VestirOMundoQueNasceu(ZoneKey zona)
	{
		if (MundoAcordadoNasceNuDeTeste) return;
		if (!_zones.TryGetValue(zona.Hash, out List<ServerPlayer>? gente) || gente.Count == 0) return;

		AplicarColisaoDasObras(zona);
		if (_bases.TryGetValue(zona.Hash, out BaseDaZona? daZona)) AssentarBase(daZona);
	}
}
