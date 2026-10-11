using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O PESO DO CHAO DE QUALQUER ZONA -- a pergunta que o `AplicarGravidade` faz, respondida num lugar so.
///
/// ============================ O RELATO DO DONO (2026-10-10) ============================
/// *"parece q tem um bug com gravidade do planeta, eu morri e revivi e usei o verb pra me mandar pro
/// spawn, esse planeta era 4x gravidade mas ele consta como 1x dps q eu morri e voltei"*.
///
/// O LOG DA SESSAO DELE CONTA A HISTORIA INTEIRA, e sao duas voltas pelo MESMO verb com dois fins:
///
///     02:00  admin_reviver, admin_spawn
///            [refugio] Kentaro: 'Earth' nao existe mais -- PertoDoNatal -> 'Verdejante-113'
///            [server] nascendo Verdejante-113: Jardim, 223x223 tiles, gravidade 4x (fora do tique)
///            [server] Kentaro pousou em Verdejante-113 (Jardim, 223x223 tiles, gravidade 4x)      <- 4x
///     02:10  [server] Kentaro MORREU e foi pro Outro Mundo
///     02:11  admin_reviver, admin_spawn
///            [refugio] Kentaro: 'Earth' nao existe mais -- PertoDoNatal -> 'Verdejante-113'
///            (nenhum "pousou em")                                                                <- 1x
///
/// Na primeira o mundo estava FRIO: o berco pos o corpo em orbita, o `TickDoEspaco` pousou e o
/// `PousarEmProcedural` acertou a gravidade. Na segunda o mundo ja estava VIVO no servidor, o
/// `PousarNo` pegou o atalho ("o mundo gerado ja esta carregado: entra direto") e o corpo entrou por
/// um `MoveToZone` cru.
/// ======================================================================================
///
/// ============================ A CAUSA: O FUNIL TINHA UM PONTO CEGO, REMENDADO NUM CHAMADOR ============================
/// `AplicarGravidade` e o funil por onde TODA troca de zona passa (login, passagem, porta, nave, admin,
/// mente, leme), e ele lia o peso do chao de UMA fonte: o `planetas.json`, que so tem os mapas feitos a
/// mao. Um mundo sorteado nao tem linha la, e `CatalogoDePlanetas.De` devolve pra zona desconhecida a
/// ficha padrao -- gravidade 1, que e o `Planetgrav = 1` de antes do `switch` do DM. Resposta VALIDA,
/// e por isso calada.
///
/// O conserto morava num chamador so: o `PousarEmProcedural` chamava um `AplicarGravidadeGerada` logo
/// DEPOIS do `MoveToZone`, pra desfazer o 1x que o funil acabara de escrever. Toda outra porta de entrada
/// num mundo gerado ficava com o 1x: o atalho do berco (o `spawn` do admin, o `Renascer` do Enma e das
/// esferas, o duelo de cargo, o selo), o `admin_ir`/`admin_trazer`, a volta do transe, a saida de uma
/// nave ou de dentro de um Majin, o teleporte de tecnica -- e o LOGIN de quem deslogou la.
///
/// E a regra escrita e esquecida num chamador, o defeito que mais se repetiu neste port. O proprio
/// `AplicarGravidadeGerada` trazia a nota: *"a duplicacao morre no dia em que o `AplicarGravidade`
/// consultar a gravidade da zona gerada"*. Morreu hoje: o funil pergunta AQUI, e o remendo foi apagado.
///
/// NO DM ISTO NAO TEM COMO ACONTECER, e vale saber por que: la o `Grav()` (`Gravity.dm:80-121`) RELE o
/// planeta a cada volta do laco de stats -- `Planetgrav = 1`, o `switch(Planet)`, e em seguida
/// `pspace_planet_grav(Planet)` pros gerados. E estado lido, nao evento. Aqui a gravidade e escrita no
/// evento de trocar de zona, entao o funil desse evento tem que saber responder por todo tipo de chao.
/// ====================================================================================================================
///
/// ============================ "CONSTA 1x" NAO ERA SO O MOSTRADOR ============================
/// A pilula "Nx grav" da aba Stats e o `Esmagamento.Gravidade(ficha)` do servidor
/// (`Planetgrav + gravmult`, no pacote `S2C.Atributos`). O numero da tela ERA o do jogo: com 1x na ficha
/// o treino rendia como na Terra (`GravGain`, `MultiplicadorDeGanho`), o esmagamento nao cobrava, o
/// peso vestido nao esmagava e o Planet Destroy pedia o BP de um planeta leve (`10000 x Planetgrav`).
/// ==========================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada --gravidadeteste): o chao volta a ser lido SO do `planetas.json`, e o mundo
	/// gerado cai na ficha padrao (1x) por toda porta que nao seja o pouso -- o relato do dono, letra por letra.
	/// </summary>
	internal bool GravidadeSoDoCatalogoDeTeste;

	/// <summary>
	/// A FICHA DO CHAO DESTA ZONA: a do `planetas.json` pros lugares feitos a mao, a do mundo SORTEADO
	/// pros gerados.
	///
	/// A DO GERADO E FUNCAO PURA DA SEED (<see cref="MundoProcedural.GravidadeDaSeed"/>, a escada do
	/// `ProceduralSpace.dm:448-452`), e isso e o que a faz valer por QUALQUER porta: ela nao pergunta se o
	/// mundo esta carregado no servidor, entao responde igual pra quem pousou, pra quem foi puxado por um
	/// admin e pra quem acabou de logar num planeta que o servidor ainda nem gerou. E o mesmo numero que a
	/// carta estelar mostra antes do pouso e que o cliente usa pra desenhar o relevo
	/// (`PlanetaProcedural.Nascer`) -- uma fonte, tres leitores.
	///
	/// O ESPACO FICA DE FORA de proposito: ele tambem e `KindProcedural` (<see cref="EhZonaProcedural"/>
	/// explica), mas nao e um mundo sorteado -- a ficha dele e a "Space" do catalogo, gravidade ZERO, e
	/// ler a seed do universo como se fosse a de um planeta daria um peso qualquer ao vacuo.
	///
	/// O NOME E O QUE O JOGADOR LE na frase "o chao de X puxa...": o batizado, quando alguem batizou.
	/// </summary>
	private FichaDePlaneta FichaDoChao(ZoneKey zona) =>
		EhZonaProcedural(zona) && !GravidadeSoDoCatalogoDeTeste
			? new FichaDePlaneta { Nome = NomeDoMundo(zona), Gravidade = MundoProcedural.GravidadeDaSeed(zona.Seed) }
			: _planetas?.De(zona.Name) ?? new FichaDePlaneta { Nome = zona.Name };

	// =====================================================================
	// NINGUEM NASCE ESMAGADO -- TAMBEM NO REFUGIO
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada --gravidadeteste): so o berco de NASCIMENTO aclimata -- quem perdeu o planeta
	/// natal acorda esmagado no refugio, que e o que acontecia.
	/// </summary>
	internal bool RefugioNaoAclimataDeTeste;

	/// <summary>
	/// ESTE CHAO E ONDE ESTE CORPO NASCE E RENASCE? E a pergunta do "spawn planet" do `race.dm:130-131`
	/// (`GravMastered = max(GravMastered, PlanetGravity(spawnPlanet))`), feita pelo `AplicarGravidade`.
	///
	/// ============================ O REFUGIO E O BERCO DE QUEM PERDEU O BERCO ============================
	/// A pergunta era so "a zona tem o nome do meu berco?". Com o berco DESTRUIDO ela responde nao pra
	/// sempre -- e o corpo passa a nascer e renascer no refugio (`GameServer.Refugio.cs`) sem nunca se
	/// acostumar a ele. O refugio foi desenhado contando com isso: o sorteio dele recusa mundo acima de
	/// 15g "pesado demais pra um recem-nascido", e a reserva (quando so ha mundo pesado por perto) esta
	/// escrita assim em `Refugios.MundosPertoDe`: *"mundo pesado e melhor que lugar nenhum, e o `max` do
	/// `race.dm:130-131` acostuma o corpo a gravidade do berco, entao ele sobrevive"*. A tela do refugio
	/// diz o mesmo ao jogador. So que o `max` nunca rodava la: um Humano (maestria 1) cujo refugio puxa
	/// 4x acordava a 4 vezes o limite dele -- PRESO no chao, perdendo 3 de vida por segundo.
	///
	/// Ficou escondido por dois motivos, e os dois acabaram hoje: quase ninguem tinha perdido o planeta
	/// natal, e -- pelo defeito da gravidade deste mesmo arquivo -- quem RENASCIA num refugio ja vivo no
	/// servidor entrava com 1x. Com o chao pesando o que pesa por toda porta, o refugiado fraco passaria
	/// a morrer no proprio ponto de partida a cada volta.
	/// ==================================================================================================
	///
	/// SO O REFUGIO DELE, e so enquanto o berco estiver morto: o mundo que o `DestinoDe` escolhe pra este
	/// personagem (<see cref="ZonaDoRefugioDe"/>). Visitar o refugio de outro, ou qualquer outro mundo
	/// pesado, continua nao acostumando ninguem a nada.
	/// </summary>
	private bool EhOChaoDeNascer(ServerPlayer pl) =>
		string.Equals(pl.Zone.Name, pl.Berco.Planeta, StringComparison.OrdinalIgnoreCase)
		|| (!RefugioNaoAclimataDeTeste && ZonaDoRefugioDe(pl) is { } refugio && refugio.Equals(pl.Zone));

	/// <summary>
	/// O MUNDO DE REFUGIO DESTE PERSONAGEM -- o da vizinhanca de casa (a opcao B2 do `GameServer.Refugio.cs`),
	/// ou nulo quando ele nao e refugiado de vizinhanca.
	///
	/// E A MESMA DECISAO DO <see cref="DestinoDe"/>, SEM OS EFEITOS DELE (ele conta ao jogador o que
	/// aconteceu, e por isso nao pode ser perguntado a cada troca de zona): berco vivo -> nao ha refugio;
	/// dominio marcado como ponto de renascimento -> e pra la que ele volta, e territorio conquistado NAO
	/// aclimata (seria maestria de presente pra quem fincasse a bandeira num mundo de 80x); senao, o mundo
	/// que a vizinhanca sorteia com a semente dele. As duas contas sao as de la -- `VizinhancaDeRefugio` e
	/// `Arredores.Sorteia` --, e a bancada confere que as duas respostas sao o mesmo mundo.
	///
	/// CUSTO: quem nao perdeu o berco sai na primeira linha. O refugiado paga a varredura de nove celulas
	/// de sistema (microssegundos), e so quando troca de zona.
	/// </summary>
	private ZoneKey? ZonaDoRefugioDe(ServerPlayer pl)
	{
		if (!PerdeuOBerco(pl)) return null;

		if (_dominios.Exists(d => d.EhOSpawn
								  && string.Equals(d.Assinatura, pl.Assinatura, StringComparison.Ordinal)
								  && !PlanetaDoDominioMorreu(d) && CorpoDoDominio(d) != null))
			return null;

		// A MESMA SEMENTE do `DestinoDoBerco`: a do personagem, e a do berco quando ele nao tem (corpo sem save).
		ulong semente = pl.SeedDoBerco != 0 ? pl.SeedDoBerco : pl.Berco.Seed;
		return VizinhancaDeRefugio(AncoraDoRefugio(pl.Berco)).Sorteia(semente) is { } m ? Espaco.ZonaDe(m.Corpo) : null;
	}
}
