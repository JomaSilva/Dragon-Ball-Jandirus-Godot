using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A QUEDA PELA NUVEM -- o `Enter()` das nuvens do original, do lado do servidor.
///
/// ============================ O PEDIDO DO DONO, LITERAL ============================
/// *"as NUVENS q tem no CAMINHO DA SERPENTE no outro mundo, se um jogador ir nelas SEM ESTAR COM
/// FLY ATIVADO, ele vai automaticamente ser JOGADO NO MAPA DO INFERNO. e no mapa do LOOKOUT se o
/// jogador cair na nuvem do mapa sem fly, ele CAI DE VOLTA PRA TERRA"*.
///
/// A METADE QUE NAO E ESTE ARQUIVO: qual celula e nuvem (o plano `.nuvem`, gravado pelo conversor),
/// quem passa por cima (<see cref="ClasseDeNuvem.Travessia"/>) e pra onde se cai
/// (<see cref="ClasseDeNuvem.DestinoDaQueda"/>) moram no Core, porque o CLIENTE le as duas primeiras
/// pra nao deixar o jogador andar por cima da nuvem que barra. Aqui mora so o que e DECISAO DO
/// SERVIDOR: quem cai, quando, e como chega inteiro do outro lado.
/// ==================================================================================
///
/// ============================ POR QUE ELE E IRMAO DO `GameServer.Passagens.cs` ============================
/// Porque e a mesma familia de acontecimento -- "pisou aqui, foi parar ali" -- e ele reusa as tres
/// pecas que aquele arquivo ja tinha resolvido, em vez de inventar as suas:
///
///   * o <c>MoveToZone</c>, que e o unico jeito certo de mudar alguem de zona;
///   * a CARENCIA (<c>_acabouDeAtravessar</c>), que impede o corpo de ricochetear entre dois mapas;
///   * e a ordem no tique, logo depois das passagens.
///
/// **O QUE ELE NAO REUSA E A `Passagem`**, e isso esta medido no cabecalho do
/// <see cref="ClasseDeNuvem.DestinoDaQueda"/>: uma passagem e uma LISTA POR CELULA varrida a cada
/// tique, e sao 422.388 celulas de nuvem que derruba. O destino da nuvem e da ZONA, e cabe em uma
/// linha.
/// ============================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// ============================ AS SONDAS -- POR ONDE A BANCADA TROCA A REGRA DA NUVEM ============================
	/// Cada campo aqui e uma das perguntas que o <see cref="TickDasNuvens"/> e o <see cref="Cair"/>
	/// fazem, e o valor de cada um **E** a funcao de producao. Em jogo isto nao muda nada: o caminho
	/// saudavel chama exatamente `ClasseDeNuvem.Travessia`, `ModoDeTravessiaDe`,
	/// `ClasseDeNuvem.DestinoDaQueda` e `ZoneCollision.PontoLivrePerto`.
	///
	/// **ELAS EXISTEM PRA A BANCADA PODER REPROVAR**, e o padrao nao e novo: e o mesmo
	/// <see cref="SondasDoVacuo"/> do `GameServer.Vacuo.cs`, escrito pelo mesmo motivo e lido com o
	/// mesmo cuidado (o campo inteiro e trocado e devolvido a `null` no `finally`, porque devolver
	/// nulo REMONTA os padroes -- guardar e repor campo a campo deixa um campo esquecido mutante num
	/// servidor vivo).
	///
	/// ============================ POR QUE A NUVEM PRECISA DELAS ============================
	/// Porque os defeitos que este sistema pode ter sao TODOS de codigo, e nenhum estado de mundo os
	/// encena: *"a metade do voo caiu"* (o voador tambem cai), *"a nuvem parou de derrubar"*, *"o
	/// destino trocou de zona"* e *"o funil de pouso saiu do caminho"* (o corpo chega dentro de
	/// pedra). Nao ha mapa que produza nenhum dos quatro -- so ha o codigo mutante rodando as MESMAS
	/// provas, que e o que a `--nuvemviva` faz.
	/// ==============================================================================================================
	/// </summary>
	internal sealed class SondasDaNuvem
	{
		/// <summary>Como este corpo esta atravessando agora? Ver `GameServer.Nado.cs`.</summary>
		public Func<ServerPlayer, ModoDeTravessia> Modo = ModoDeTravessiaDe;

		/// <summary>O `Enter()` da nuvem -- so `isflying` passa. Ver <see cref="ClasseDeNuvem.Travessia"/>.</summary>
		public Func<ModoDeTravessia, bool, TravessiaDaNuvem> Travessia = ClasseDeNuvem.Travessia;

		/// <summary>Pra onde esta zona derruba. Ver <see cref="ClasseDeNuvem.DestinoDaQueda"/>.</summary>
		public Func<string, (string Zona, int Bx, int By)?> Destino = ClasseDeNuvem.DestinoDaQueda;

		/// <summary>
		/// ESTA ZONA DERRUBA, OU SO BARRA? -- lida do PLANO gravado, e nao do nome.
		///
		/// E a linha que mantem o Ceu, o Reino dos Deuses e os `Outside` fora deste laco inteiro, e
		/// por isso ela merece sonda propria: sem uma, a bancada nao consegue mostrar que e ELA quem
		/// protege aquelas tres zonas -- o defeito *"toda nuvem passou a derrubar"* nao teria por onde
		/// entrar, e a familia do contra-exemplo ficaria verde sem saber ficar vermelha.
		/// </summary>
		public Func<ZoneCollision, bool> ZonaDerruba = m => m.NuvemDerruba;

		/// <summary>
		/// O FUNIL DE POUSO ESTA NO CAMINHO? Falso e o defeito *"quem cai chega dentro de parede"* --
		/// o pedido do dono em uma frase (*"nao pode ficar preso nem cair dentro de parede"*).
		/// </summary>
		public bool UsarFunilDePouso = true;
	}

	/// <summary>
	/// AS SONDAS DESTE SERVIDOR. Nulo em jogo, e ai a propriedade remonta os padroes de producao --
	/// mesma escolha do <see cref="Sondas"/> do vacuo, pelo mesmo motivo escrito la.
	/// </summary>
	private SondasDaNuvem? _sondasDaNuvem;
	private SondasDaNuvem SondasNuvem => _sondasDaNuvem ??= new SondasDaNuvem();

	/// <summary>
	/// QUEM ESTA EM CIMA DE UMA NUVEM SEM VOAR, CAI. Roda no tique cheio, logo depois das passagens.
	///
	/// ============================ O CUSTO E O NUMERO DE JOGADORES, E NAO O DE NUVENS ============================
	/// Mesma propriedade do `TickDasPassagens`, e aqui ela e o que torna a coisa possivel: sao quase
	/// meio milhao de celulas de nuvem no jogo, e nenhuma delas e visitada. A pergunta e feita a
	/// partir do CORPO -- quatro consultas de bit na caixa dos pes --, e so pra quem esta numa zona
	/// que tem plano de nuvem (35 dos 40 andares nem tem o arquivo, e ai `EhNuvem` e um teste de
	/// referencia nula).
	/// ==========================================================================================================
	/// </summary>
	private void TickDasNuvens()
	{
		long agora = NowMs();

		// A LISTA E COPIADA porque cair MEXE em `_players` por dentro (o `MoveToZone` troca a lista
		// da zona), e iterar uma colecao que muda no meio estoura. Mesma razao do `TickDasPassagens`.
		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			// ============================ O NOCAUTEADO NAO CAI, E ISSO E UMA ESCOLHA ============================
			// O `Enter()` do DM nao pergunta por nocaute -- la um corpo KO nao anda, entao a situacao
			// nao existe. Aqui ela existe (um corpo desmaiado pode ser EMPURRADO por um soco), e a
			// decisao e a mesma que o `TickDasPassagens` ja tomou pro mesmo caso, com as mesmas
			// palavras: *"caido e caido, e um corpo desmaiado empurrado pra cima de uma boca de
			// caverna nao 'atravessa' nada"*.
			//
			// Divergir aqui seria pior que a divergencia: um corpo KO empurrado pra cima da nuvem do
			// Templo cairia na Terra, de onde ninguem o veria voltar -- e quem o empurrou nao teria
			// como saber que foi isso que aconteceu.
			if (pl.Ficha.KO) continue;

			// A CARENCIA E COMPARTILHADA COM AS PASSAGENS, de proposito. Quem acabou de atravessar uma
			// passagem pode chegar EM CIMA de nuvem (a volta do Ceu cai no z6, que e todo nuvem), e
			// duas carencias separadas deixariam a queda disparar no mesmo tique da chegada -- o
			// jogador atravessaria e sumiria, sem ver onde chegou. Um relogio so, um ricochete a menos.
			if (_acabouDeAtravessar.TryGetValue(pl.Id, out long livre) && agora < livre) continue;

			if (MapaDaZonaOuCatalogo(pl.Zone) is not { TemNuvem: true } mapa) continue;

			SondasDaNuvem sn = SondasNuvem;

			// A ZONA SO BARRA? Entao nao ha o que despachar: o `ZoneCollision.Bloqueia` ja impediu o
			// corpo de entrar, nas DUAS pontas. Sair aqui cedo e o que mantem o Ceu e o Reino dos
			// Deuses fora deste laco inteiro. (Pela sonda -- ver `SondasDaNuvem.ZonaDerruba`.)
			if (!sn.ZonaDerruba(mapa)) continue;

			// PELA CAIXA DOS PES e nao pelo centro -- ver `MoveRules.NaNuvem`.
			if (!MoveRules.NaNuvem(mapa, pl.Pos)) continue;

			// ============================ E AQUI SE PERGUNTA AO **MESMO** FUNIL DO NADO ============================
			// `ModoDeTravessiaDe` e o dono unico de "como este corpo esta atravessando agora" (ver
			// `GameServer.Nado.cs`), e usa-lo aqui e o que garante que a nuvem e a agua concordem sobre
			// o que e "estar no ar". Escrever `pl.Altitude > 0` na mao seria uma segunda opiniao, e ela
			// erraria exatamente onde aquele funil ja acertou: quem esta subindo nos primeiros 32 px
			// ainda consulta o mapa, e um corpo decolando de cima da nuvem cairia no Inferno no meio
			// da decolagem.
			// (As duas perguntas passam pelas <see cref="SondasDaNuvem"/>. Em jogo `SondasNuvem` E
			// `ClasseDeNuvem.Travessia` e `ModoDeTravessiaDe` -- ver o cabecalho delas.)
			// (o `true` e o `zonaDerruba` -- o `if` de cima ja garantiu que esta zona derruba)
			if (sn.Travessia(sn.Modo(pl), true) != TravessiaDaNuvem.Derruba) continue;

			Cair(pl);
		}
	}

	/// <summary>
	/// A QUEDA. Manda o corpo pro destino da zona -- e o PONTO FINAL nao e a coordenada do DM, e o
	/// que o <see cref="ZoneCollision.PontoLivrePerto"/> devolver perto dela.
	///
	/// ============================ O PEDIDO DO DONO SOBRE ISTO E EXPLICITO ============================
	/// *"Quem cai pela nuvem nao pode ficar preso nem cair dentro de parede -- use o funil de pouso
	/// que ja existe"*. O funil e o `PontoLivrePerto`, e ele varre anel por anel recusando parede,
	/// beirada, agua e -- desde esta tarefa -- nuvem. Ele nunca desiste: 64 aneis, e no pior caso
	/// devolve um ponto a trinta tiles em vez de deixar alguem dentro de pedra.
	///
	/// **E ELE E CHAMADO AQUI E NAO DENTRO DO `MoveToZone`**, porque o `MoveToZone` e usado por quem
	/// ja sabe onde quer por o corpo (o berco, o admin, a nave). Chama-lo aqui e o mesmo que o
	/// `GameServer.Alem` faz com a mesa do Enma, e pelo mesmo motivo.
	/// ================================================================================================
	/// </summary>
	private void Cair(ServerPlayer pl)
	{
		string origem = pl.Zone.Name;
		SondasDaNuvem sn = SondasNuvem;
		if (sn.Destino(origem) is not { } destino) return;

		// A ZONA DE DESTINO PRECISA EXISTIR. Uma queda pra zona inexistente poria o corpo no vazio --
		// pior que nao cair. Mesma recusa que o `CarregarPassagens` faz com passagem orfa, e aqui ela
		// e ainda mais barata: sao duas zonas, conferidas no boot pela bancada.
		ZoneEntry? chegada = _catalogo?.Get(destino.Zona);
		if (chegada == null)
		{
			GD.PushWarning($"[server] nuvem de {origem} aponta pra zona inexistente `{destino.Zona}`");
			return;
		}

		// A CARENCIA VEM ANTES DA MUDANCA, como no `Atravessar`: sem ela um corpo que caisse em cima
		// de outra nuvem seria despachado de novo no mesmo tique.
		_acabouDeAtravessar[pl.Id] = NowMs() + MsDeCarenciaDePassagem;

		// A ALTURA DO MAPA DE DESTINO, e nao a do de origem: a conversao do Y inverte o eixo do BYOND
		// (`altura - by`), entao usar a altura errada poria a chegada espelhada na vertical. Hoje
		// todas as zonas envolvidas sao 500x500 e a conta daria igual -- e e exatamente por isso que
		// ela esta escrita certa agora, enquanto ninguem consegue notar a diferenca.
		Vec2 desejado = ClasseDeNuvem.EmPixel(destino.Bx, destino.By, chegada.H);
		// `UsarFunilDePouso` e SEMPRE verdadeiro em jogo -- ver `SondasDaNuvem`. O falso e o defeito
		// *"quem cai chega dentro de parede"*, e ele so existe pra a bancada poder ficar vermelha.
		Vec2 onde = sn.UsarFunilDePouso && chegada.Mapa is { } mc ? mc.PontoLivrePerto(desejado) : desejado;

		// O VOO CAI JUNTO, e a palavra do dono e "CAI": chegar do outro lado ainda planando faria a
		// queda parecer um teleporte. Quem nao voava (a unica gente que chega aqui) tambem nao tem
		// altitude pra zerar, entao isto e uma afirmacao barata -- e ela protege o caso do
		// arremessado, que chega no ar por um soco e nao por vontade.
		pl.Altitude = 0f;
		pl.Nadando = false;

		Avisar(pl, ClasseDeNuvem.AvisoDaQueda(origem));
		GD.Print($"[server] {pl.Name}: caiu pela nuvem {origem} -> {destino.Zona}");

		MoveToZone(pl.Id, ZoneKey.Premade(destino.Zona), onde);
	}
}
